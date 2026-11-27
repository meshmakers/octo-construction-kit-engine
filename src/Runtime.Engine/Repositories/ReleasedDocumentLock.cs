using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     The lock of released documents (design note <c>docs/concept-number-ranges-and-released-documents.md</c>, D3, D8,
///     D10). A document is released when its stored state value is one of the <c>ReleasedValues</c> of the release
///     configuration (<c>System/AutoIncrement.Release</c>) of a number range one of its attributes is numbered from. A
///     released document never changes an attribute, its number, its state back out of the released values or an
///     outbound association, and it is never deleted or archived; only the <c>ChangeableAttributes</c> of the range may
///     change (never a number attribute, and the state only to another released value).
///     <para>
///         The lock is not a hook: it is called by the write paths themselves and has no switch. <see cref="BulkRtMutation" />
///         calls <see cref="CheckMutationAsync" /> after the rule validation and before anything of the batch is written,
///         whatever the options (also with <c>DisablePreDocumentModifications</c>) and whatever the identity (also a system
///         session); every insert, update, replace, delete, archive and association change of a repository, by id, by
///         filter and in <c>ApplyChanges</c>, reaches it there. The bulk import (<c>BulkInsertRtEntitiesAsync</c>: RT
///         import, blueprint seed, re-apply) calls <see cref="FilterImportAsync" />, the association import
///         <see cref="FilterAssociationImportAsync" />, the delete of associations by id
///         <see cref="CheckAssociationDeleteAsync" />.
///     </para>
///     <para>
///         A write that leaves every locked attribute at its stored value changes nothing and passes (e.g. a form that
///         saves the unchanged values together with a changeable one). An import of a released document with the stored
///         content is a no-op: the entity is not written. A refusal refuses the whole batch (D3) with one
///         <see cref="ReleasedDocumentLockedException" /> that names every refused document (message 168).
///     </para>
///     The lock reads only when a write targets an entity of a type with an auto-increment attribute: one read of the
///     stored documents per type and batch and, only when one of them is stored, one read of the number ranges, in the
///     session (and so the transaction) of the write. A range whose release configuration cannot be read locks every stored document of its
///     types (fail closed).
/// </summary>
internal static partial class ReleasedDocumentLock
{
    /// <summary>Version-less id of <c>System/AutoIncrement</c>, the number ranges.</summary>
    internal static readonly RtCkId<CkTypeId> AutoIncrementCkTypeId = new("System", "AutoIncrement");

    internal const string OperationInsert = "insert";
    internal const string OperationUpdate = "update";
    internal const string OperationReplace = "replace";
    internal const string OperationDelete = "delete";
    internal const string OperationAssociation = "association change";
    internal const string OperationImport = "import";

    /// <summary>Pseudo attribute name of a delete or archive, and of a write of the entity state.</summary>
    internal const string RtStateChange = "rtState";

    /// <summary>Pseudo attribute name of an insert that would overwrite a stored document.</summary>
    internal const string RtCreationDateTimeChange = "rtCreationDateTime";

    /// <summary>
    ///     The blueprint bookkeeping of <c>System/Entity</c> (which blueprint version wrote the entity, when, and whether
    ///     it is locked to it). Not business data: a re-apply that only stamps a released document anew is the no-op of
    ///     D10, and the stamp of a tenant-owned entity may be refreshed.
    /// </summary>
    internal static readonly IReadOnlySet<string> BlueprintBookkeepingAttributes =
        new HashSet<string>(["RtBlueprintSource", "RtBlueprintLocked", "RtBlueprintAppliedAt"], StringComparer.Ordinal);

    /// <summary>
    ///     Refuses the batch of one <see cref="BulkRtMutation" /> when it changes a released document; called after the
    ///     rule validation and before anything is written.
    /// </summary>
    /// <param name="session">Session (and transaction) of the write</param>
    /// <param name="repositoryDataSource">The data source the batch is written to</param>
    /// <param name="ckCacheService">The CK cache</param>
    /// <param name="entities">The validated entity changes</param>
    /// <param name="associations">The validated association changes, the cascade of deleted entities included</param>
    /// <param name="options">The options of the batch</param>
    /// <returns>The stornos the batch releases, for the <c>Storno</c> entries of the audit trail</returns>
    public static async Task<IReadOnlyList<StornoRelease>> CheckMutationAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService, EntityRuleEngineResult<RtEntity> entities, GraphRuleEngineResult associations,
        BulkRtMutationOptions options)
    {
        var targets = new List<RtEntityId>();
        targets.AddRange(entities.RtEntitiesToDelete);
        targets.AddRange(entities.RtEntitiesToUpdate.Keys);
        targets.AddRange(entities.RtEntitiesToReplace.Keys);
        targets.AddRange(entities.RtEntitiesToInsert.Where(e => e.RtId != OctoObjectId.Empty)
            .Select(e => new RtEntityId(e.GetRtCkTypeId(), e.RtId)));
        targets.AddRange(associations.RtAssociationsToCreate.Concat(associations.RtAssociationsToDelete)
            .Select(a => new RtEntityId(a.OriginCkTypeId, a.OriginRtId)));

        // D9: audit entries are append-only and created only by the engine.
        DocumentAuditEntryLock.CheckMutation(entities);

        // D11: a numbered range keeps its release configuration.
        var rangeWrites = new List<NumberRangeReleaseLock.RangeWrite>();
        rangeWrites.AddRange(entities.RtEntitiesToDelete.Select(id =>
            new NumberRangeReleaseLock.RangeWrite(id, OperationDelete, _ => null)));
        rangeWrites.AddRange(entities.RtEntitiesToUpdate.Select(u =>
            new NumberRangeReleaseLock.RangeWrite(u.Key, OperationUpdate,
                stored => NumberRangeReleaseLock.Overlay(stored, u.Value))));
        rangeWrites.AddRange(entities.RtEntitiesToReplace.Select(r =>
            new NumberRangeReleaseLock.RangeWrite(r.Key, OperationReplace, _ => r.Value)));
        rangeWrites.AddRange(entities.RtEntitiesToInsert.Where(e => e.RtId != OctoObjectId.Empty).Select(e =>
            new NumberRangeReleaseLock.RangeWrite(new RtEntityId(e.GetRtCkTypeId(), e.RtId), OperationInsert,
                _ => e)));
        await NumberRangeReleaseLock.CheckAsync(session, repositoryDataSource, ckCacheService, rangeWrites)
            .ConfigureAwait(false);

        // D6, D7: the storno rules.
        var stornoReleases = await CheckStornosAsync(session, repositoryDataSource, ckCacheService, entities,
            associations.RtAssociationsToCreate, associations.RtAssociationsToDelete).ConfigureAwait(false);

        var scope = new LockScope(session, repositoryDataSource, ckCacheService);
        if (!await scope.LoadAsync(targets).ConfigureAwait(false))
        {
            return stornoReleases;
        }

        var refusals = new Refusals();
        foreach (var target in entities.RtEntitiesToDelete)
        {
            if (scope.TryGetReleased(target.RtId, out var released))
            {
                refusals.Add(released, OperationDelete, [RtStateChange]);
            }
        }

        foreach (var (target, partial) in entities.RtEntitiesToUpdate)
        {
            if (!scope.TryGetReleased(target.RtId, out var released))
            {
                continue;
            }

            entities.RecordArrayItemUpdates.TryGetValue(target, out var itemUpdates);
            var changes = released.PartialChanges(partial, itemUpdates);
            refusals.AddIfForbidden(released, OperationUpdate, changes, partial);
        }

        foreach (var (target, replacement) in entities.RtEntitiesToReplace)
        {
            if (scope.TryGetReleased(target.RtId, out var released))
            {
                // The number modifier carries the stored number over into a replace without one (unless disabled).
                var changes = released.FullChanges(replacement,
                    carryOverNumbers: !options.DisablePreDocumentModifications);
                refusals.AddIfForbidden(released, OperationReplace, changes, replacement);
            }
        }

        foreach (var inserted in entities.RtEntitiesToInsert)
        {
            // An insert never changes a stored document; one with the id of a released document is refused.
            if (inserted.RtId != OctoObjectId.Empty && scope.TryGetReleased(inserted.RtId, out var released))
            {
                var changes = released.FullChanges(inserted, carryOverNumbers: false);
                refusals.Add(released, OperationInsert, changes.Count > 0 ? changes : [RtCreationDateTimeChange]);
            }
        }

        foreach (var association in associations.RtAssociationsToCreate.Concat(associations.RtAssociationsToDelete))
        {
            if (scope.TryGetReleased(association.OriginRtId, out var released))
            {
                refusals.Add(released, OperationAssociation, [DescribeAssociation(association)]);
            }
        }

        refusals.ThrowIfAny();
        return stornoReleases;
    }

    /// <summary>
    ///     The entities of a bulk import (RT import, blueprint seed, re-apply) that are written: an entity with the id of a
    ///     released document is refused when it changes a locked attribute (D10), left out when it carries the stored
    ///     content (a no-op: no write), and written when it changes only changeable attributes. Every other entity is
    ///     written as given.
    /// </summary>
    public static async Task<IReadOnlyList<RtEntity>> FilterImportAsync(IOctoSession session,
        IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService, IReadOnlyList<RtEntity> entities)
    {
        // D9: an import never creates or overwrites an audit entry.
        entities = await DocumentAuditEntryLock.FilterImportAsync(session, repositoryDataSource, ckCacheService, entities)
            .ConfigureAwait(false);

        var targets = entities.Where(e => e.RtId != OctoObjectId.Empty)
            .Select(e => new RtEntityId(e.GetRtCkTypeId(), e.RtId)).ToList();

        // D11: an import (seed, re-apply) of a numbered range keeps its release configuration.
        await NumberRangeReleaseLock.CheckAsync(session, repositoryDataSource, ckCacheService,
            entities.Where(e => e.RtId != OctoObjectId.Empty).Select(e =>
                new NumberRangeReleaseLock.RangeWrite(new RtEntityId(e.GetRtCkTypeId(), e.RtId), OperationImport,
                    _ => e)).ToList()).ConfigureAwait(false);

        var scope = new LockScope(session, repositoryDataSource, ckCacheService);
        if (!await scope.LoadAsync(targets).ConfigureAwait(false))
        {
            return entities;
        }

        var written = new List<RtEntity>(entities.Count);
        var refusals = new Refusals();
        foreach (var entity in entities)
        {
            if (entity.RtId == OctoObjectId.Empty || !scope.TryGetReleased(entity.RtId, out var released))
            {
                written.Add(entity);
                continue;
            }

            // The import writes the entity as it is: no modifier carries a number over.
            var changes = released.FullChanges(entity, carryOverNumbers: false);
            if (changes.Count == 0)
            {
                continue;
            }

            if (!refusals.AddIfForbidden(released, OperationImport, changes, entity))
            {
                written.Add(entity);
            }
        }

        refusals.ThrowIfAny();
        return written;
    }

    /// <summary>
    ///     The associations of a bulk import that are written: a new outbound association of a released document is
    ///     refused; one it already has is left out (a no-op). Every other association is written as given.
    /// </summary>
    public static async Task<IReadOnlyList<RtAssociation>> FilterAssociationImportAsync(IOctoSession session,
        IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService,
        IReadOnlyList<RtAssociation> associations)
    {
        var scope = new LockScope(session, repositoryDataSource, ckCacheService);
        if (!await scope.LoadAsync(associations.Select(a => new RtEntityId(a.OriginCkTypeId, a.OriginRtId)))
                .ConfigureAwait(false))
        {
            return associations;
        }

        var releasedOrigins = associations.Select(a => a.OriginRtId)
            .Where(id => scope.TryGetReleased(id, out _)).Distinct().ToList();
        if (releasedOrigins.Count == 0)
        {
            return associations;
        }

        var existing = await repositoryDataSource.RtAssociations
            .FindManyAsync(session, a => releasedOrigins.Contains(a.OriginRtId)).ConfigureAwait(false);

        var written = new List<RtAssociation>(associations.Count);
        var refusals = new Refusals();
        foreach (var association in associations)
        {
            if (!scope.TryGetReleased(association.OriginRtId, out var released))
            {
                written.Add(association);
                continue;
            }

            var isStored = existing.Any(e => e.OriginRtId == association.OriginRtId &&
                                             e.TargetRtId == association.TargetRtId &&
                                             SameRole(e.AssociationRoleId, association.AssociationRoleId));
            if (!isStored)
            {
                refusals.Add(released, OperationAssociation, [DescribeAssociation(association)]);
            }
        }

        refusals.ThrowIfAny();
        return written;
    }

    /// <summary>Refuses the delete of associations by id when one of them is an outbound association of a released document.</summary>
    public static async Task CheckAssociationDeleteAsync(IOctoSession session,
        IRepositoryDataSource repositoryDataSource, ICkCacheService ckCacheService,
        IReadOnlyCollection<OctoObjectId> associationIds)
    {
        if (associationIds.Count == 0 || !HasAnyNumberedType(repositoryDataSource.TenantId, ckCacheService))
        {
            return;
        }

        var ids = associationIds.Distinct().ToList();
        var associations = await repositoryDataSource.RtAssociations
            .FindManyAsync(session, a => ids.Contains(a.AssociationId)).ConfigureAwait(false);
        var scope = new LockScope(session, repositoryDataSource, ckCacheService);
        if (!await scope.LoadAsync(associations.Select(a => new RtEntityId(a.OriginCkTypeId, a.OriginRtId)))
                .ConfigureAwait(false))
        {
            return;
        }

        var refusals = new Refusals();
        foreach (var association in associations)
        {
            if (scope.TryGetReleased(association.OriginRtId, out var released))
            {
                refusals.Add(released, OperationAssociation, [DescribeAssociation(association)]);
            }
        }

        refusals.ThrowIfAny();
    }

    private static bool HasAnyNumberedType(string tenantId, ICkCacheService ckCacheService) =>
        ckCacheService.GetCkTypes(tenantId).Any(t =>
            t.AllAttributes.Values.Any(a => !string.IsNullOrEmpty(a.AutoIncrementReference)));

    private static bool SameRole(RtCkId<CkAssociationRoleId>? a, RtCkId<CkAssociationRoleId>? b) =>
        string.Equals(a?.FullName, b?.FullName, StringComparison.Ordinal);

    private static string DescribeAssociation(RtAssociation association) =>
        $"association '{association.AssociationRoleId?.FullName}' to " +
        $"'{association.TargetCkTypeId?.FullName}@{association.TargetRtId}'";

    private static bool HasValue(object? value) => value switch
    {
        null => false,
        string text => !string.IsNullOrWhiteSpace(text),
        _ => true
    };

    /// <summary>A release configuration of a number range on a document type, or the error that makes it unreadable.</summary>
    internal sealed record RangeRelease(
        string Reference,
        NumberRangeRelease? Release,
        CkTypeAttributeGraph? StateAttribute,
        CkEnumGraph? StateEnum,
        string? Error);

    /// <summary>The number ranges with a release configuration of one document type and its number attributes.</summary>
    internal sealed record TypeLock(CkTypeGraph CkTypeGraph, IReadOnlyList<RangeRelease> Ranges,
        IReadOnlySet<string> NumberAttributes);

    /// <summary>What one write may read: the number ranges and the stored documents it targets.</summary>
    internal sealed class LockScope(IOctoSession session, IRepositoryDataSource repositoryDataSource,
        ICkCacheService ckCacheService)
    {
        private readonly Dictionary<string, RtEntity> _rangesByReference = new(StringComparer.Ordinal);
        private readonly Dictionary<OctoObjectId, RtEntity> _stored = new();
        private readonly Dictionary<OctoObjectId, string> _addressedTypes = new();
        private readonly Dictionary<string, TypeLock?> _typeLocks = new(StringComparer.Ordinal);

        private string TenantId => repositoryDataSource.TenantId;

        /// <summary>
        ///     Reads the stored documents of <paramref name="targets" /> that may be locked and, only when one is stored,
        ///     their number ranges (so a batch of new documents reads no range). False when nothing can be locked (no
        ///     stored target in a collection that holds a numbered type, no range with a release configuration).
        ///     <para>
        ///         A whole inheritance tree shares one collection and the writes match on the id alone, so a write that
        ///         names a sibling type (numbered or not) reaches the stored document all the same. The numbered types and
        ///         their ranges are therefore taken from the subtree of the collection root, every target in such a
        ///         collection is read, and <see cref="TryGetReleased" /> judges by the stored type.
        ///     </para>
        /// </summary>
        public async Task<bool> LoadAsync(IEnumerable<RtEntityId> targets)
        {
            var groups = new Dictionary<string, (CkTypeGraph Graph, HashSet<OctoObjectId> RtIds)>(StringComparer.Ordinal);
            var unnumbered = new HashSet<string>(StringComparer.Ordinal);
            var references = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in targets)
            {
                if (target.RtId == OctoObjectId.Empty || target.CkTypeId == null)
                {
                    continue;
                }

                var typeName = target.CkTypeId.FullName;
                if (unnumbered.Contains(typeName))
                {
                    continue;
                }

                if (!groups.TryGetValue(typeName, out var group))
                {
                    if (!ckCacheService.TryGetRtCkType(TenantId, target.CkTypeId, out var graph) ||
                        !CollectCollectionReferences(graph, references, out var collectionGraph))
                    {
                        unnumbered.Add(typeName);
                        continue;
                    }

                    group = (collectionGraph, []);
                    groups[typeName] = group;
                }

                group.RtIds.Add(target.RtId);
                _addressedTypes.TryAdd(target.RtId, typeName);
            }

            foreach (var (graph, rtIds) in groups.Values)
            {
                var ids = rtIds.ToList();
                var stored = await repositoryDataSource.GetRtCollection<RtEntity>(graph)
                    .FindManyAsync(session, e => ids.Contains(e.RtId)).ConfigureAwait(false);
                foreach (var entity in stored)
                {
                    _stored.TryAdd(entity.RtId, entity);
                }
            }

            return _stored.Count > 0 && await LoadRangesAsync(references).ConfigureAwait(false);
        }

        /// <summary>The stored entity of <paramref name="rtId" /> read by <see cref="LoadAsync" />, or null.</summary>
        public RtEntity? Stored(OctoObjectId rtId) => _stored.GetValueOrDefault(rtId);

        /// <summary>True when <paramref name="ckTypeId" /> or one of its derived types has an auto-increment attribute.</summary>
        public bool IsNumberedType(RtCkId<CkTypeId>? ckTypeId) =>
            ckTypeId != null && ckCacheService.TryGetRtCkType(TenantId, ckTypeId, out var graph) &&
            CollectSubtreeReferences(graph, []);

        /// <summary>Reads the number ranges of <paramref name="ckTypeIds" /> that are not read yet.</summary>
        public async Task EnsureRangesAsync(IEnumerable<RtCkId<CkTypeId>> ckTypeIds)
        {
            var references = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ckTypeId in ckTypeIds)
            {
                if (ckCacheService.TryGetRtCkType(TenantId, ckTypeId, out var graph))
                {
                    CollectSubtreeReferences(graph, references);
                }
            }

            references.ExceptWith(_rangesByReference.Keys);
            if (references.Count > 0)
            {
                await LoadRangesAsync(references).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     True when <paramref name="entity" />, a document of <paramref name="ckTypeId" /> as a write leaves it, is in
        ///     one of the released values of a readable release configuration of its ranges.
        /// </summary>
        public bool IsReleasedState(RtEntity entity, RtCkId<CkTypeId> ckTypeId)
        {
            var typeLock = GetTypeLock(ckTypeId);
            return typeLock != null && typeLock.Ranges.Any(r => r.Release != null &&
                r.Release.IsReleased(entity.GetAttributeValueOrDefault(r.StateAttribute!.AttributeName),
                    r.StateAttribute, r.StateEnum));
        }

        /// <summary>True when <paramref name="ckTypeId" /> is <paramref name="configured" /> or derived from it.</summary>
        public bool IsTypeOrDerived(RtCkId<CkTypeId> ckTypeId, string configured)
        {
            if (string.Equals(ckTypeId.FullName, new RtCkId<CkTypeId>(configured).FullName, StringComparison.Ordinal))
            {
                return true;
            }

            return ckCacheService.TryGetRtCkType(TenantId, new RtCkId<CkTypeId>(configured), out var graph) &&
                   graph.GetAllDerivedTypes(true).Any(t =>
                       string.Equals(t.ToRtCkId().FullName, ckTypeId.FullName, StringComparison.Ordinal));
        }

        /// <summary>True with the released document when <paramref name="rtId" /> is a stored released document.</summary>
        public bool TryGetReleased(OctoObjectId rtId, out ReleasedDocument released)
        {
            released = null!;
            if (!_stored.TryGetValue(rtId, out var stored))
            {
                return false;
            }

            var typeName = stored.CkTypeId?.FullName ?? _addressedTypes.GetValueOrDefault(rtId);
            var typeLock = typeName == null ? null : GetTypeLock(stored.CkTypeId ?? new RtCkId<CkTypeId>(typeName));
            if (typeLock == null)
            {
                return false;
            }

            var releasedBy = new List<RangeRelease>();
            foreach (var range in typeLock.Ranges)
            {
                if (range.Error != null)
                {
                    // Fail closed: a configuration that cannot be read cannot tell a draft from a released document.
                    releasedBy.Add(range);
                    continue;
                }

                if (range.Release!.IsReleased(stored.GetAttributeValueOrDefault(range.StateAttribute!.AttributeName),
                        range.StateAttribute, range.StateEnum))
                {
                    releasedBy.Add(range);
                }
            }

            if (releasedBy.Count == 0)
            {
                return false;
            }

            released = new ReleasedDocument(stored, typeLock, releasedBy, ckCacheService, TenantId);
            return true;
        }

        /// <summary>
        ///     Collects the number ranges of every type stored in the collection of <paramref name="graph" /> (the subtree
        ///     of its collection root); <paramref name="collectionGraph" /> is the root, read without a type filter. A root
        ///     the CK cache cannot resolve counts every numbered type of the tenant (fail closed).
        /// </summary>
        private bool CollectCollectionReferences(CkTypeGraph graph, HashSet<string> references,
            out CkTypeGraph collectionGraph)
        {
            collectionGraph = graph;
            var rootCkTypeId = graph.IsCollectionRoot ? graph.CkTypeId : graph.DefiningCollectionRootCkTypeId;
            if (rootCkTypeId != null && string.Equals(rootCkTypeId.FullName, graph.CkTypeId.FullName,
                    StringComparison.Ordinal))
            {
                return CollectSubtreeReferences(graph, references);
            }

            if (rootCkTypeId != null && ckCacheService.TryGetCkType(TenantId, rootCkTypeId, out var rootGraph) &&
                rootGraph != null)
            {
                collectionGraph = rootGraph;
                return CollectSubtreeReferences(rootGraph, references);
            }

            var found = false;
            foreach (var type in ckCacheService.GetCkTypes(TenantId))
            {
                foreach (var attribute in type.AllAttributes.Values)
                {
                    if (!string.IsNullOrEmpty(attribute.AutoIncrementReference))
                    {
                        references.Add(attribute.AutoIncrementReference);
                        found = true;
                    }
                }
            }

            return found;
        }

        private bool CollectSubtreeReferences(CkTypeGraph graph, HashSet<string> references)
        {
            var found = false;
            foreach (var typeId in graph.GetAllDerivedTypes(true))
            {
                if (!ckCacheService.TryGetCkType(TenantId, typeId, out var type))
                {
                    continue;
                }

                foreach (var attribute in type.AllAttributes.Values)
                {
                    if (!string.IsNullOrEmpty(attribute.AutoIncrementReference))
                    {
                        references.Add(attribute.AutoIncrementReference);
                        found = true;
                    }
                }
            }

            return found;
        }

        private async Task<bool> LoadRangesAsync(HashSet<string> references)
        {
            if (!ckCacheService.TryGetRtCkType(TenantId, AutoIncrementCkTypeId, out var autoIncrementGraph))
            {
                return false;
            }

            var names = references.ToList();
            var ranges = await repositoryDataSource.GetRtCollection<RtEntity>(autoIncrementGraph)
                .FindManyAsync(session, e => names.Contains(e.RtWellKnownName!)).ConfigureAwait(false);
            foreach (var range in ranges)
            {
                if (range.RtWellKnownName != null &&
                    range.GetAttributeValueOrDefault(NumberRangeRelease.ReleaseAttribute) != null &&
                    _rangesByReference.TryAdd(range.RtWellKnownName, range))
                {
                    _typeLocks.Clear();
                }
            }

            return _rangesByReference.Count > 0;
        }

        private TypeLock? GetTypeLock(RtCkId<CkTypeId> ckTypeId)
        {
            if (_typeLocks.TryGetValue(ckTypeId.FullName, out var cached))
            {
                return cached;
            }

            TypeLock? typeLock = null;
            if (ckCacheService.TryGetRtCkType(TenantId, ckTypeId, out var graph))
            {
                var numbered = graph.AllAttributes.Values
                    .Where(a => !string.IsNullOrEmpty(a.AutoIncrementReference)).ToList();
                var ranges = new List<RangeRelease>();
                foreach (var reference in numbered.Select(a => a.AutoIncrementReference!).Distinct())
                {
                    if (!_rangesByReference.TryGetValue(reference, out var range))
                    {
                        continue;
                    }

                    ranges.Add(ReadRange(reference, range, graph));
                }

                if (ranges.Count > 0)
                {
                    typeLock = new TypeLock(graph, ranges,
                        numbered.Select(a => a.AttributeName).ToHashSet(StringComparer.Ordinal));
                }
            }

            _typeLocks[ckTypeId.FullName] = typeLock;
            return typeLock;
        }

        private RangeRelease ReadRange(string reference, RtEntity range, CkTypeGraph graph)
        {
            if (!NumberRangeRelease.TryCreate(range, out var release, out var error))
            {
                return new RangeRelease(reference, null, null, null, error);
            }

            if (!graph.AllAttributesByName.TryGetValue(release!.StateAttribute, out var stateAttribute))
            {
                return new RangeRelease(reference, null, null, null,
                    $"{NumberRangeRelease.ReleaseAttribute}.{NumberRangeRelease.StateAttributeAttribute} " +
                    $"'{release.StateAttribute}' is not an attribute of '{graph.CkTypeId}'");
            }

            CkEnumGraph? stateEnum = null;
            if (stateAttribute.ValueCkEnumId != null)
            {
                ckCacheService.TryGetCkEnum(TenantId, stateAttribute.ValueCkEnumId, out stateEnum);
            }

            return new RangeRelease(reference, release, stateAttribute, stateEnum, null);
        }
    }

    /// <summary>A stored document in a released state, with the rules of the ranges that release it.</summary>
    internal sealed class ReleasedDocument
    {
        private readonly ICkCacheService _ckCacheService;
        private readonly string _tenantId;
        private readonly IReadOnlyList<RangeRelease> _releasedBy;
        private readonly TypeLock _typeLock;

        public ReleasedDocument(RtEntity stored, TypeLock typeLock, IReadOnlyList<RangeRelease> releasedBy,
            ICkCacheService ckCacheService, string tenantId)
        {
            Stored = stored;
            _typeLock = typeLock;
            _releasedBy = releasedBy;
            _ckCacheService = ckCacheService;
            _tenantId = tenantId;

            // Changeable only what every releasing range allows; never a number, nothing with an unreadable range.
            IEnumerable<string>? changeable = null;
            foreach (var range in releasedBy)
            {
                var allowed = range.Release?.ChangeableAttributes ?? [];
                changeable = changeable == null ? allowed : changeable.Intersect(allowed, StringComparer.Ordinal);
            }

            Changeable = (changeable ?? []).Where(a => !typeLock.NumberAttributes.Contains(a))
                .ToHashSet(StringComparer.Ordinal);
        }

        public RtEntity Stored { get; }

        public IReadOnlySet<string> Changeable { get; }

        /// <summary>The storno types the releasing ranges configure (<c>Release.StornoCkTypeId</c>).</summary>
        public IReadOnlyList<string> StornoCkTypeIds =>
            _releasedBy.Select(r => r.Release?.StornoCkTypeId).OfType<string>().Distinct(StringComparer.Ordinal)
                .ToList();

        public RtEntityId Id => new(Stored.CkTypeId ?? _typeLock.CkTypeGraph.CkTypeId.ToRtCkId(), Stored.RtId);

        public string NumberRange => string.Join(", ", _releasedBy.Select(r => r.Reference));

        public string State
        {
            get
            {
                var range = _releasedBy.FirstOrDefault(r => r.Error == null);
                if (range == null)
                {
                    return $"unknown: {_releasedBy[0].Error}";
                }

                var value = Stored.GetAttributeValueOrDefault(range.StateAttribute!.AttributeName);
                if (value is not string && range.StateEnum != null && value != null)
                {
                    try
                    {
                        var key = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                        return range.StateEnum.Values.FirstOrDefault(v => v.Key == key)?.Name ?? value.ToString()!;
                    }
                    catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
                    {
                        return value.ToString()!;
                    }
                }

                return value?.ToString() ?? string.Empty;
            }
        }

        /// <summary>The attributes a partial update changes (record-array item updates and the entity state included).</summary>
        public List<string> PartialChanges(RtEntity partial, IReadOnlyList<RtRecordArrayItemUpdate>? itemUpdates)
        {
            var changes = new List<string>();
            foreach (var (name, value) in partial.Attributes)
            {
                if (BlueprintBookkeepingAttributes.Contains(name))
                {
                    continue;
                }

                // A member path ("Record.Member") is a change of its root attribute; it is not compared.
                if (name.Contains('.') || !IsUnchanged(name, value))
                {
                    AddOnce(changes, RootOf(name));
                }
            }

            foreach (var itemUpdate in itemUpdates ?? [])
            {
                AddOnce(changes, RootOf(itemUpdate.AttributeName));
            }

            if (partial.RtState.HasValue && partial.RtState != Stored.RtState || partial.RtArchivedDateTime.HasValue)
            {
                AddOnce(changes, RtStateChange);
            }

            return changes;
        }

        /// <summary>
        ///     The attributes a full write (replace, import) changes: every attribute of the stored document or of the
        ///     payload whose value differs. With <paramref name="carryOverNumbers" /> a number attribute the payload
        ///     leaves empty keeps the stored number.
        /// </summary>
        public List<string> FullChanges(RtEntity payload, bool carryOverNumbers)
        {
            var changes = new List<string>();
            foreach (var name in Stored.Attributes.Keys.Union(payload.Attributes.Keys, StringComparer.Ordinal))
            {
                if (BlueprintBookkeepingAttributes.Contains(name))
                {
                    continue;
                }

                var written = payload.GetAttributeValueOrDefault(name);
                if (carryOverNumbers && _typeLock.NumberAttributes.Contains(name) && !HasValue(written))
                {
                    continue;
                }

                if (!IsUnchanged(name, written))
                {
                    AddOnce(changes, RootOf(name));
                }
            }

            if (payload.RtState.HasValue && payload.RtState != Stored.RtState)
            {
                AddOnce(changes, RtStateChange);
            }

            return changes;
        }

        /// <summary>The changes a released document does not allow.</summary>
        public List<string> Forbidden(IEnumerable<string> changes, RtEntity written)
        {
            var forbidden = new List<string>();
            foreach (var change in changes)
            {
                if (_typeLock.NumberAttributes.Contains(change) || !Changeable.Contains(change) ||
                    LeavesTheReleasedState(change, written))
                {
                    forbidden.Add(change);
                }
            }

            return forbidden;
        }

        private bool LeavesTheReleasedState(string attributeName, RtEntity written)
        {
            foreach (var range in _releasedBy)
            {
                if (range.Release != null &&
                    string.Equals(range.StateAttribute!.AttributeName, attributeName, StringComparison.Ordinal) &&
                    !range.Release.IsReleased(written.GetAttributeValueOrDefault(attributeName), range.StateAttribute,
                        range.StateEnum))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsUnchanged(string name, object? written)
        {
            _typeLock.CkTypeGraph.AllAttributesByName.TryGetValue(name, out var attribute);
            CkEnumGraph? attributeEnum = null;
            if (attribute?.ValueCkEnumId != null)
            {
                _ckCacheService.TryGetCkEnum(_tenantId, attribute.ValueCkEnumId, out attributeEnum);
            }

            return RtAttributeValueEquality.AreEqual(written, Stored.GetAttributeValueOrDefault(name), attribute,
                attributeEnum);
        }

        private static string RootOf(string name)
        {
            var dot = name.IndexOf('.');
            return dot < 0 ? name : name[..dot];
        }

        private static void AddOnce(List<string> changes, string name)
        {
            if (!changes.Contains(name, StringComparer.Ordinal))
            {
                changes.Add(name);
            }
        }
    }

    /// <summary>The refused writes of one batch.</summary>
    private sealed class Refusals
    {
        private readonly OperationResult _result = new();
        private readonly List<ReleasedDocumentRefusal> _refusals = [];

        public void Add(ReleasedDocument released, string operation, IReadOnlyList<string> changes)
        {
            var id = released.Id;
            var ckTypeId = id.CkTypeId.FullName;
            _refusals.Add(new ReleasedDocumentRefusal(id, operation, changes, released.State, released.NumberRange));
            _result.AddMessage(MessageCodes.RtReleasedDocumentLocked($"{ckTypeId}@{id.RtId}", id.RtId, ckTypeId,
                released.State, released.NumberRange, operation, string.Join(", ", changes),
                string.Join(", ", released.Changeable.Order(StringComparer.Ordinal))));
        }

        /// <summary>Adds a refusal when <paramref name="changes" /> holds a forbidden change; true when it did.</summary>
        public bool AddIfForbidden(ReleasedDocument released, string operation, IReadOnlyList<string> changes,
            RtEntity written)
        {
            var forbidden = released.Forbidden(changes, written);
            if (forbidden.Count == 0)
            {
                return false;
            }

            Add(released, operation, forbidden);
            return true;
        }

        public void ThrowIfAny()
        {
            if (_refusals.Count > 0)
            {
                throw new ReleasedDocumentLockedException(_result, _refusals);
            }
        }
    }
}
