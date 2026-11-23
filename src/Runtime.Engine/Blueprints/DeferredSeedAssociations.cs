using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// One association edge of a blueprint seed that could not be written because an end did not exist yet (AB#9651).
/// </summary>
/// <param name="Blueprint">The blueprint whose seed declared the edge (<c>Name-Version</c>).</param>
/// <param name="Role">The association role (semantic versioned full name).</param>
/// <param name="OriginCkTypeId">The CK type of the origin (semantic versioned full name).</param>
/// <param name="OriginRtId">The origin entity.</param>
/// <param name="TargetCkTypeId">The CK type of the target (semantic versioned full name).</param>
/// <param name="TargetRtId">The target entity.</param>
/// <param name="RecordedAt">When the seed import dropped the edge (UTC).</param>
internal sealed record DeferredSeedAssociation(
    string Blueprint,
    string Role,
    string OriginCkTypeId,
    string OriginRtId,
    string TargetCkTypeId,
    string TargetRtId,
    DateTime RecordedAt)
{
    /// <summary>Identity of the edge, independent of the blueprint and the time it was recorded.</summary>
    public (string Role, string Origin, string Target) Key => (Role, OriginRtId, TargetRtId);

    /// <summary>The blueprint name without version.</summary>
    public string BlueprintName => new BlueprintId(Blueprint).Name;

    public string Describe() => $"{Role} {OriginCkTypeId}@{OriginRtId} -> {TargetCkTypeId}@{TargetRtId}";
}

/// <summary>What one pass over the deferred seed edges of a tenant did (AB#9651).</summary>
internal sealed class DeferredSeedAssociationOutcome
{
    /// <summary>Edges of the blueprint just applied that wait for an end; with a bounded sample.</summary>
    public int DeferredForCurrent { get; set; }

    public List<string> DeferredSamples { get; } = [];

    /// <summary>Edges the seed dropped that are not kept (they carry attributes or are identity-owned).</summary>
    public int NotDeferrable { get; set; }

    /// <summary>Edges written in this pass, per blueprint that declared them.</summary>
    public Dictionary<string, int> AppliedByBlueprint { get; } = new(StringComparer.Ordinal);

    public int Applied => AppliedByBlueprint.Values.Sum();

    /// <summary>Edges given up in this pass (blueprint no longer installed, to-one role taken, store full).</summary>
    public int Dropped { get; set; }

    public List<string> DroppedSamples { get; } = [];
}

/// <summary>
/// AB#9651 - the association edges of a blueprint seed that the import dropped because an end did not exist yet
/// (AB#5005), kept per tenant and written once both ends exist. Typical case: a blueprint whose pipelines point at the
/// Mesh Adapter that the service-managed System.Communication blueprint seeds when Communication is enabled - by the
/// platform after the install (AB#9578) or by hand later. Every blueprint seed import (in whichever service it runs)
/// records the edges it dropped and then writes the deferred edges whose ends now exist.
/// </summary>
/// <remarks>
/// <para>
/// The edges are kept in the tenant's own database as the <c>System/TenantConfiguration</c> entity with the well-known
/// name <see cref="ConfigurationKey" /> (JSON in <c>ConfigurationValue</c>), so they survive restarts and travel with
/// the tenant. No CK model change.
/// </para>
/// <para>
/// A deferred edge is written only when both ends exist with the recorded CK types and the blueprint that declared it is
/// still installed in the tenant. It is never written over another edge of a to-one role (the tenant's later choice
/// wins). Edges of an identity association role and edges that carry attributes are never kept (they stay reported as
/// skipped). Edges of a blueprint that is no longer installed are dropped once they are older than
/// <see cref="UninstalledGracePeriod" />.
/// </para>
/// </remarks>
internal sealed class DeferredSeedAssociations
{
    /// <summary>Well-known name of the <c>System/TenantConfiguration</c> entity that holds the deferred edges.</summary>
    internal const string ConfigurationKey = "Octo.Blueprints.DeferredSeedAssociations";

    /// <summary>The most edges a tenant keeps; further ones are reported and dropped.</summary>
    internal const int MaxEntries = 1000;

    /// <summary>
    /// How long an edge of a blueprint without installation row is kept (the row is written after the seed).
    /// </summary>
    internal static readonly TimeSpan UninstalledGracePeriod = TimeSpan.FromMinutes(15);

    /// <summary>How often a pass runs before its failure is reported (write conflicts with a concurrent pass).</summary>
    internal const int MaxAttempts = 3;

    private const int SampleCap = 10;
    private const string ConfigurationValueAttribute = "ConfigurationValue";

    private static readonly RtCkId<CkTypeId> TenantConfigurationCkTypeId = new("System", "TenantConfiguration");

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IRuntimeRepository _repository;
    private readonly ICkCacheService _ckCacheService;
    private readonly ILogger _logger;
    private readonly Func<DateTime> _utcNow;

    public DeferredSeedAssociations(IRuntimeRepository repository, ICkCacheService ckCacheService, ILogger logger,
        Func<DateTime>? utcNow = null)
    {
        _repository = repository;
        _ckCacheService = ckCacheService;
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Records the edges <paramref name="skipped" /> the seed of <paramref name="blueprint" /> dropped, forgets the kept
    /// edges of that blueprint that its current seed no longer declares, then writes every deferred edge of the tenant
    /// whose ends exist now and saves what is left. The read, the writes and the save run in one transaction of the
    /// tenant database; a write conflict with a concurrent pass (another service applying a seed at the same time) aborts
    /// it and the pass is run again on the fresh state (<see cref="MaxAttempts" /> attempts).
    /// </summary>
    /// <param name="blueprint">The blueprint whose seed was just imported.</param>
    /// <param name="skipped">The edges that import dropped (<see cref="IImportRtModelCommand.SkippedAssociations" />).</param>
    /// <param name="importedEntityIds">
    /// The entities that import wrote. A kept edge of <paramref name="blueprint" /> (same name, any version) whose origin
    /// is among them and that the import did not drop again is no longer pending: the seed wrote it or no longer declares it.
    /// </param>
    /// <param name="isInstalledAsync">Tells whether a blueprint (by name) is installed in the tenant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DeferredSeedAssociationOutcome> ProcessAsync(BlueprintId blueprint,
        IReadOnlyList<RtImportSkippedAssociation>? skipped, IReadOnlyCollection<OctoObjectId>? importedEntityIds,
        Func<string, Task<bool>> isInstalledAsync, CancellationToken cancellationToken)
    {
        return await InTransactionAsync(async session =>
        {
            var outcome = new DeferredSeedAssociationOutcome();
            var (configurations, stored, invalid) = await LoadAsync(session).ConfigureAwait(false);
            var entries = new List<DeferredSeedAssociation>(stored);
            ReportInvalid(invalid, outcome);
            var changed = invalid.Count > 0 || configurations.Count > 1;
            changed |= DropRedeclared(entries, blueprint, skipped ?? [], importedEntityIds ?? []) > 0;
            changed |= Merge(entries, blueprint, skipped ?? [], _utcNow(), outcome);

            if (entries.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                changed |= await DropUninstalledAsync(entries, blueprint, isInstalledAsync, outcome).ConfigureAwait(false);
                changed |= await ApplyAsync(session, entries, outcome).ConfigureAwait(false);
            }

            var current = blueprint.FullName;
            foreach (var entry in entries.Where(e => e.Blueprint == current))
            {
                outcome.DeferredForCurrent++;
                if (outcome.DeferredSamples.Count < SampleCap)
                {
                    outcome.DeferredSamples.Add(entry.Describe());
                }
            }

            if (changed)
            {
                await SaveAsync(session, configurations, entries).ConfigureAwait(false);
            }

            return outcome;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes the deferred edges of a blueprint (uninstall). Returns how many were removed.</summary>
    public async Task<int> RemoveBlueprintAsync(string blueprintName, CancellationToken cancellationToken = default)
    {
        return await InTransactionAsync(async session =>
        {
            var (configurations, stored, invalid) = await LoadAsync(session).ConfigureAwait(false);
            if (configurations.Count == 0)
            {
                return 0;
            }

            var kept = stored.Where(e => !string.Equals(e.BlueprintName, blueprintName, StringComparison.Ordinal))
                .ToList();
            var removed = stored.Count - kept.Count;
            if (removed > 0 || invalid.Count > 0 || configurations.Count > 1)
            {
                await SaveAsync(session, configurations, kept).ConfigureAwait(false);
            }

            return removed;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <paramref name="pass" /> in a transaction of the tenant database and commits it; any failure aborts it and
    /// the pass runs again with a new session, up to <see cref="MaxAttempts" /> times (the last failure is thrown).
    /// </summary>
    private async Task<T> InTransactionAsync<T>(Func<IOctoSession, Task<T>> pass, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var session = await _repository.GetSessionAsync().ConfigureAwait(false);
            session.StartTransaction();
            try
            {
                var result = await pass(session).ConfigureAwait(false);
                await session.CommitTransactionAsync().ConfigureAwait(false);
                return result;
            }
            catch (Exception e) when (attempt < MaxAttempts && !cancellationToken.IsCancellationRequested)
            {
                await TryAbortAsync(session).ConfigureAwait(false);
                _logger.LogInformation(e,
                    "Deferred seed association pass in tenant '{TenantId}' failed (attempt {Attempt} of {MaxAttempts}), probably a concurrent pass; running it again",
                    _repository.TenantId, attempt, MaxAttempts);
            }
            catch
            {
                await TryAbortAsync(session).ConfigureAwait(false);
                throw;
            }
        }
    }

    private static async Task TryAbortAsync(IOctoSession session)
    {
        try
        {
            await session.AbortTransactionAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The transaction may already be aborted by the server (write conflict); nothing else to undo.
        }
    }

    /// <summary>
    /// Removes the kept edges of <paramref name="blueprint" /> (by name, any version) whose origin is among
    /// <paramref name="importedEntityIds" /> and which <paramref name="skipped" /> does not hold again: the seed just
    /// imported that origin and either wrote the edge or no longer declares it. Pure; returns how many were removed.
    /// </summary>
    internal static int DropRedeclared(List<DeferredSeedAssociation> entries, BlueprintId blueprint,
        IReadOnlyList<RtImportSkippedAssociation> skipped, IReadOnlyCollection<OctoObjectId> importedEntityIds)
    {
        if (importedEntityIds.Count == 0)
        {
            return 0;
        }

        var imported = importedEntityIds.Select(id => id.ToString()).ToHashSet(StringComparer.Ordinal);
        var stillSkipped = skipped
            .Select(e => (e.RoleId.SemanticVersionedFullName, e.OriginRtId.ToString(), e.TargetRtId.ToString()))
            .ToHashSet();
        return entries.RemoveAll(e =>
            string.Equals(e.BlueprintName, blueprint.Name, StringComparison.Ordinal) &&
            imported.Contains(e.OriginRtId) &&
            !stillSkipped.Contains(e.Key));
    }

    /// <summary>
    /// Adds the deferrable edges of <paramref name="skipped" /> to <paramref name="entries" /> (an edge already kept is
    /// taken over by the blueprint that declared it again). Pure; returns true when <paramref name="entries" /> changed.
    /// </summary>
    internal static bool Merge(List<DeferredSeedAssociation> entries, BlueprintId blueprint,
        IReadOnlyList<RtImportSkippedAssociation> skipped, DateTime now, DeferredSeedAssociationOutcome outcome)
    {
        var changed = false;
        var index = entries.Select((e, i) => (e.Key, i)).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.First().i);
        foreach (var edge in skipped)
        {
            if (edge.HasAttributes || IdentityOwnedWrites.IsIdentityOwnedAssociation(edge.RoleId))
            {
                outcome.NotDeferrable++;
                continue;
            }

            var entry = new DeferredSeedAssociation(blueprint.FullName, edge.RoleId.SemanticVersionedFullName,
                edge.OriginCkTypeId.SemanticVersionedFullName, edge.OriginRtId.ToString(),
                edge.TargetCkTypeId.SemanticVersionedFullName, edge.TargetRtId.ToString(), now);
            if (index.TryGetValue(entry.Key, out var at))
            {
                entries[at] = entry;
                changed = true;
                continue;
            }

            if (entries.Count >= MaxEntries)
            {
                outcome.Dropped++;
                if (outcome.DroppedSamples.Count < SampleCap)
                {
                    outcome.DroppedSamples.Add($"{entry.Describe()} (more than {MaxEntries} deferred edges)");
                }

                continue;
            }

            index[entry.Key] = entries.Count;
            entries.Add(entry);
            changed = true;
        }

        return changed;
    }

    private async Task<bool> DropUninstalledAsync(List<DeferredSeedAssociation> entries, BlueprintId current,
        Func<string, Task<bool>> isInstalledAsync, DeferredSeedAssociationOutcome outcome)
    {
        var installed = new Dictionary<string, bool>(StringComparer.Ordinal) { [current.Name] = true };
        var cutoff = _utcNow() - UninstalledGracePeriod;
        var removed = 0;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (entry.RecordedAt > cutoff)
            {
                continue;
            }

            var name = entry.BlueprintName;
            if (!installed.TryGetValue(name, out var isInstalled))
            {
                isInstalled = await isInstalledAsync(name).ConfigureAwait(false);
                installed[name] = isInstalled;
            }

            if (isInstalled)
            {
                continue;
            }

            entries.RemoveAt(i);
            removed++;
            outcome.Dropped++;
            if (outcome.DroppedSamples.Count < SampleCap)
            {
                outcome.DroppedSamples.Add($"{entry.Describe()} (blueprint {name} is not installed)");
            }
        }

        return removed > 0;
    }

    private async Task<bool> ApplyAsync(IOctoSession session, List<DeferredSeedAssociation> entries,
        DeferredSeedAssociationOutcome outcome)
    {
        var parsed = new List<(DeferredSeedAssociation Entry, RtAssociation Edge)>();
        var unreadable = new List<DeferredSeedAssociation>();
        foreach (var entry in entries)
        {
            if (TryParse(entry, out var edge))
            {
                parsed.Add((entry, edge));
            }
            else
            {
                unreadable.Add(entry);
            }
        }

        foreach (var entry in unreadable)
        {
            entries.Remove(entry);
            outcome.Dropped++;
            if (outcome.DroppedSamples.Count < SampleCap)
            {
                outcome.DroppedSamples.Add($"{entry.Describe()} (unreadable)");
            }
        }

        var existing = await FindExistingAsync(session, parsed.Select(p => p.Edge)).ConfigureAwait(false);
        var ready = parsed
            .Where(p => existing.Contains((p.Edge.OriginCkTypeId, p.Edge.OriginRtId)) &&
                        existing.Contains((p.Edge.TargetCkTypeId, p.Edge.TargetRtId)))
            .ToList();
        if (ready.Count == 0)
        {
            return unreadable.Count > 0;
        }

        var taken = await FindTakenToOneAsync(session, ready.Select(p => p.Edge).ToList()).ConfigureAwait(false);
        var (toWrite, blocked) = DecideWrites(ready, taken);
        foreach (var item in ready)
        {
            entries.Remove(item.Entry);
        }

        foreach (var (entry, otherTarget) in blocked)
        {
            outcome.Dropped++;
            if (outcome.DroppedSamples.Count < SampleCap)
            {
                outcome.DroppedSamples.Add($"{entry.Describe()} (the origin is linked to {otherTarget} on this to-one role)");
            }
        }

        if (toWrite.Count > 0)
        {
            await _repository.BulkRtAssociationsAsync(session, toWrite.Select(p => p.Edge),
                    new BulkOperationOptions { InsertStrategy = BulkInsertStrategies.Upsert })
                .ConfigureAwait(false);
            foreach (var item in toWrite)
            {
                outcome.AppliedByBlueprint[item.Entry.Blueprint] =
                    outcome.AppliedByBlueprint.GetValueOrDefault(item.Entry.Blueprint) + 1;
            }

            _logger.LogInformation(
                "Wrote {Count} deferred seed association edge(s) in tenant '{TenantId}' now that both ends exist: {Blueprints}",
                toWrite.Count, _repository.TenantId,
                string.Join(", ", outcome.AppliedByBlueprint.Select(kv => $"{kv.Key} ({kv.Value})")));
        }

        return true;
    }

    /// <summary>
    /// Pure decision over the deferred edges whose ends exist: an edge whose origin already carries the same target on a
    /// to-one role is already there (neither written nor reported); one whose origin carries another target there is
    /// blocked (the tenant's later choice wins); every other edge is written.
    /// </summary>
    /// <param name="ready">The deferred edges whose ends exist.</param>
    /// <param name="takenToOne">The current target of an origin on a to-one role.</param>
    internal static (List<(DeferredSeedAssociation Entry, RtAssociation Edge)> ToWrite,
        List<(DeferredSeedAssociation Entry, OctoObjectId OtherTarget)> Blocked)
        DecideWrites(IReadOnlyList<(DeferredSeedAssociation Entry, RtAssociation Edge)> ready,
            IReadOnlyDictionary<(OctoObjectId Origin, RtCkId<CkAssociationRoleId> Role), OctoObjectId> takenToOne)
    {
        var toWrite = new List<(DeferredSeedAssociation, RtAssociation)>();
        var blocked = new List<(DeferredSeedAssociation, OctoObjectId)>();
        foreach (var item in ready)
        {
            if (item.Edge.AssociationRoleId != null &&
                takenToOne.TryGetValue((item.Edge.OriginRtId, item.Edge.AssociationRoleId), out var current))
            {
                if (current != item.Edge.TargetRtId)
                {
                    blocked.Add((item.Entry, current));
                }

                continue;
            }

            toWrite.Add(item);
        }

        return (toWrite, blocked);
    }

    private async Task<HashSet<(RtCkId<CkTypeId> CkTypeId, OctoObjectId RtId)>> FindExistingAsync(
        IOctoSession session, IEnumerable<RtAssociation> edges)
    {
        var byType = new Dictionary<RtCkId<CkTypeId>, HashSet<OctoObjectId>>();
        foreach (var edge in edges)
        {
            Add(edge.OriginCkTypeId, edge.OriginRtId);
            Add(edge.TargetCkTypeId, edge.TargetRtId);
        }

        var existing = new HashSet<(RtCkId<CkTypeId>, OctoObjectId)>();
        foreach (var (ckTypeId, rtIds) in byType)
        {
            var resultSet = await _repository
                .GetRtEntitiesByIdAsync(session, ckTypeId, rtIds.ToList(), RtEntityQueryOptions.Create())
                .ConfigureAwait(false);
            foreach (var entity in resultSet.Items)
            {
                existing.Add((ckTypeId, entity.RtId));
            }
        }

        return existing;

        void Add(RtCkId<CkTypeId> ckTypeId, OctoObjectId rtId)
        {
            if (!byType.TryGetValue(ckTypeId, out var set))
            {
                set = [];
                byType[ckTypeId] = set;
            }

            set.Add(rtId);
        }
    }

    /// <summary>
    /// The current target of every origin among <paramref name="edges" /> on a role that is to-one, or whose multiplicity
    /// cannot be resolved from the CK cache (fail closed: such an edge is never written next to another target).
    /// </summary>
    private async Task<Dictionary<(OctoObjectId Origin, RtCkId<CkAssociationRoleId> Role), OctoObjectId>>
        FindTakenToOneAsync(IOctoSession session, IReadOnlyList<RtAssociation> edges)
    {
        var taken = new Dictionary<(OctoObjectId, RtCkId<CkAssociationRoleId>), OctoObjectId>();
        foreach (var roleGroup in edges.Where(e => IsToOne(e) != false).GroupBy(e => e.AssociationRoleId!))
        {
            var origins = roleGroup.Select(e => new RtEntityId(e.OriginCkTypeId, e.OriginRtId)).Distinct().ToList();
            var resultSet = await _repository.GetRtAssociationsAsync(session, origins,
                    RtAssociationExtendedQueryOptions.Create(GraphDirections.Outbound, roleGroup.Key))
                .ConfigureAwait(false);
            foreach (var edge in resultSet.Values.SelectMany(r => r.Items))
            {
                taken.TryAdd((edge.OriginRtId, roleGroup.Key), edge.TargetRtId);
            }
        }

        return taken;
    }

    /// <summary>
    /// True for a to-one outbound role of the origin type, false for a to-many one, <c>null</c> when the origin type or
    /// the role cannot be resolved (CK cache not loaded, model upgraded since the edge was recorded). Roles are compared
    /// version-less.
    /// </summary>
    private bool? IsToOne(RtAssociation edge)
    {
        if (edge.AssociationRoleId == null ||
            !_ckCacheService.TryGetRtCkType(_repository.TenantId, edge.OriginCkTypeId, out var ckTypeGraph) ||
            ckTypeGraph == null)
        {
            return null;
        }

        var role = VersionLess(edge.AssociationRoleId);
        var association = ckTypeGraph.Associations.Out.All
            .FirstOrDefault(a => VersionLess(a.CkRoleId.ToRtCkId()) == role);
        if (association == null)
        {
            return null;
        }

        return association.Multiplicity is MultiplicitiesDto.One or MultiplicitiesDto.ZeroOrOne;
    }

    /// <summary>Model name and role id of an association role, without the model version and the role version.</summary>
    internal static (string Model, string Element) VersionLess(RtCkId<CkAssociationRoleId> id)
    {
        var model = id.ModelId.ToString() ?? string.Empty;
        var cut = model.IndexOfAny(['-', '@']);
        return (cut < 0 ? model : model[..cut], id.ElementId.RoleId);
    }

    internal static bool TryParse(DeferredSeedAssociation entry, out RtAssociation edge)
    {
        edge = null!;
        try
        {
            if (!OctoObjectId.TryParse(entry.OriginRtId, out var origin) ||
                !OctoObjectId.TryParse(entry.TargetRtId, out var target))
            {
                return false;
            }

            var role = new RtCkId<CkAssociationRoleId>(entry.Role);
            if (IdentityOwnedWrites.IsIdentityOwnedAssociation(role))
            {
                return false;
            }

            edge = new RtAssociation
            {
                AssociationRoleId = role,
                RtState = RtState.Active,
                OriginRtId = origin,
                OriginCkTypeId = new RtCkId<CkTypeId>(entry.OriginCkTypeId),
                TargetRtId = target,
                TargetCkTypeId = new RtCkId<CkTypeId>(entry.TargetCkTypeId)
            };
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<(List<RtEntity> Configurations, List<DeferredSeedAssociation> Entries,
        List<string> Invalid)> LoadAsync(IOctoSession session)
    {
        var options = RtEntityQueryOptions.Create();
        options.Field(nameof(RtEntity.RtWellKnownName), FieldFilterOperator.Equals, ConfigurationKey);
        var resultSet = await _repository
            .GetRtEntitiesByTypeAsync(session, TenantConfigurationCkTypeId, options)
            .ConfigureAwait(false);
        // Two passes that both found no store insert one each; every entity with the name is read and merged.
        var configurations = (resultSet?.Items ?? [])
            .Where(e => string.Equals(e.RtWellKnownName, ConfigurationKey, StringComparison.Ordinal))
            .ToList();
        var entries = new List<DeferredSeedAssociation>();
        var invalid = new List<string>();
        var keys = new HashSet<(string, string, string)>();
        foreach (var configuration in configurations)
        {
            var json = configuration.GetAttributeStringValueOrDefault(ConfigurationValueAttribute);
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            List<DeferredSeedAssociation?>? loaded;
            try
            {
                loaded = JsonSerializer.Deserialize<List<DeferredSeedAssociation?>>(json, JsonOptions);
            }
            catch (JsonException e)
            {
                _logger.LogWarning(e,
                    "The deferred seed association edges of tenant '{TenantId}' could not be read and are discarded",
                    _repository.TenantId);
                invalid.Add("unreadable store");
                continue;
            }

            foreach (var entry in loaded ?? [])
            {
                if (!IsValid(entry))
                {
                    invalid.Add(entry == null ? "empty entry" : $"{entry.Describe()} (invalid entry)");
                    continue;
                }

                if (keys.Add(entry!.Key))
                {
                    entries.Add(entry);
                }
            }
        }

        return (configurations, entries, invalid);
    }

    /// <summary>
    /// True when every field of <paramref name="entry" /> is set, the blueprint id parses and the recording time is
    /// known. Invalid entries (a manual write, a future format) are dropped instead of failing every later pass.
    /// </summary>
    internal static bool IsValid(DeferredSeedAssociation? entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.Blueprint) || string.IsNullOrWhiteSpace(entry.Role) ||
            string.IsNullOrWhiteSpace(entry.OriginCkTypeId) || string.IsNullOrWhiteSpace(entry.OriginRtId) ||
            string.IsNullOrWhiteSpace(entry.TargetCkTypeId) || string.IsNullOrWhiteSpace(entry.TargetRtId) ||
            entry.RecordedAt == default)
        {
            return false;
        }

        try
        {
            var id = new BlueprintId(entry.Blueprint);
            return !id.IsEmpty && !string.IsNullOrEmpty(id.Name);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void ReportInvalid(IReadOnlyList<string> invalid, DeferredSeedAssociationOutcome outcome)
    {
        foreach (var text in invalid)
        {
            outcome.Dropped++;
            if (outcome.DroppedSamples.Count < SampleCap)
            {
                outcome.DroppedSamples.Add(text);
            }
        }
    }

    /// <summary>Writes <paramref name="entries" /> into the first store entity and erases every other one.</summary>
    private async Task SaveAsync(IOctoSession session, IReadOnlyList<RtEntity> configurations,
        IReadOnlyList<DeferredSeedAssociation> entries)
    {
        var keep = entries.Count > 0 ? configurations.FirstOrDefault() : null;
        foreach (var other in configurations.Where(c => !ReferenceEquals(c, keep)))
        {
            await _repository.DeleteOneRtEntityByRtIdAsync(session, TenantConfigurationCkTypeId, other.RtId,
                DeleteOptions.Erase).ConfigureAwait(false);
        }

        if (entries.Count == 0)
        {
            return;
        }

        var entity = await _repository.CreateTransientRtEntityByRtCkIdAsync(TenantConfigurationCkTypeId)
            .ConfigureAwait(false);
        entity.RtWellKnownName = ConfigurationKey;
        entity.SetAttributeValue(ConfigurationValueAttribute, AttributeValueTypesDto.String,
            JsonSerializer.Serialize(entries, JsonOptions));
        if (keep != null)
        {
            await _repository.ReplaceOneRtEntityByIdAsync(session, TenantConfigurationCkTypeId, keep.RtId, entity)
                .ConfigureAwait(false);
        }
        else
        {
            await _repository.InsertOneRtEntityAsync(session, TenantConfigurationCkTypeId, entity)
                .ConfigureAwait(false);
        }
    }
}
