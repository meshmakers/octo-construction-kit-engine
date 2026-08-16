using System.Collections;
using System.Collections.Concurrent;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;

namespace Meshmakers.Octo.Runtime.Engine.Exchange;

internal class ImportRtModelCommand(
    ILogger<ImportRtModelCommand> logger,
    ICkCacheService cacheService,
    IRtYamlSerializer rtYamlSerializer,
    IRtJsonSerializer rtJsonSerializer,
    IRtImportAuditTrail rtImportAuditTrail,
    RtImportOptions importOptions)
    : IImportRtModelCommand
{
    private readonly ConcurrentDictionary<OctoObjectId, byte> _entityImportIds = new();
    private readonly ConcurrentQueue<RtAssociation> _importAssociationQueue = new();

    private readonly ConcurrentQueue<RtEntity> _importEntityQueue = new();

    private readonly ConcurrentQueue<(OctoObjectId RtId, RtCkId<CkTypeId> CkTypeId, IReadOnlyList<string> MissingCkAttributeIds)>
        _mandatoryViolations = new();

    private readonly IRtSerializer _rtYamlSerializer = rtYamlSerializer;
    private int _associationsCount;

    public async Task ImportTextAsync(IRuntimeRepository runtimeRepository, string jsonText,
        ImportStrategy importStrategy, CancellationToken? cancellationToken = null)
    {
        logger.LogInformation("Importing RT entities using text started");

        var session = await runtimeRepository.GetSessionAsync().ConfigureAwait(false);
        try
        {
            session.StartTransaction();

            OperationResult operationResult = new();
            var rtModelRoot = await _rtYamlSerializer.DeserializeAsync(jsonText, "-", operationResult)
                .ConfigureAwait(false);
            ValidateCkModels(runtimeRepository.TenantId, rtModelRoot.Dependencies);
            await ImportEntityAsync(session, rtModelRoot.Entities, runtimeRepository, importStrategy)
                .ConfigureAwait(false);

            await session.CommitTransactionAsync().ConfigureAwait(false);

            logger.LogInformation("{Count} entities, {AssociationsCount} associations imported", _entityImportIds.Count,
                _associationsCount);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Import of RT model failed");
            throw;
        }
    }

    public async Task ImportModelAsync(IRuntimeRepository runtimeRepository, RtModelRootTcDto rtModelRoot,
        ImportStrategy importStrategy, CancellationToken? cancellationToken = null)
    {
        logger.LogInformation("Importing RT entities using text started");

        if (!cacheService.IsTenantLoaded(runtimeRepository.TenantId))
        {
            await runtimeRepository.LoadCacheForTenantAsync(cacheService).ConfigureAwait(false);
        }

        var session = await runtimeRepository.GetSessionAsync().ConfigureAwait(false);
        try
        {
            session.StartTransaction();

            ValidateCkModels(runtimeRepository.TenantId, rtModelRoot.Dependencies);
            await ImportEntityAsync(session, rtModelRoot.Entities, runtimeRepository, importStrategy)
                .ConfigureAwait(false);

            await session.CommitTransactionAsync().ConfigureAwait(false);

            logger.LogInformation("{Count} entities, {AssociationsCount} associations imported", _entityImportIds.Count,
                _associationsCount);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Import of RT model failed");
            throw;
        }
    }

    public async Task ImportAsync(IRuntimeRepository runtimeRepository, string filePath, string contentType,
        ImportStrategy importStrategy, CancellationToken? cancellationToken = null)
    {
        logger.LogInformation("Importing RT entities using file started");

        if (!cacheService.IsTenantLoaded(runtimeRepository.TenantId))
        {
            await runtimeRepository.LoadCacheForTenantAsync(cacheService).ConfigureAwait(false);
        }

        var session = await runtimeRepository.GetSessionAsync().ConfigureAwait(false);
        try
        {
            session.StartTransaction();
#if NETSTANDARD2_0
            using (var stream = File.OpenRead(filePath))
#else
            await using (var stream = File.OpenRead(filePath))
#endif
            {
                if (contentType.ToLower() == ExchangeMimeTypes.MimeTypeYaml)
                {
                    OperationResult operationResult = new();
                    var rtModelRootDto = await _rtYamlSerializer.DeserializeAsync(stream, filePath, operationResult)
                        .ConfigureAwait(false);
                    ValidateCkModels(runtimeRepository.TenantId, rtModelRootDto.Dependencies);
                    await ImportEntityAsync(session, rtModelRootDto.Entities, runtimeRepository, importStrategy)
                        .ConfigureAwait(false);
                }
                else
                {
                    var rtDeserializeStream = await rtJsonSerializer.DeserializeStreamAsync(stream, cancellationToken)
                        .ConfigureAwait(false);
                    rtDeserializeStream.BulkDeserialized += async (_, args) =>
                    {
                        await ImportEntityAsync(session, args.DeserializedEntities, runtimeRepository, importStrategy)
                            .ConfigureAwait(false);

                        args.IsHandled = true;
                    };
                    ValidateCkModels(runtimeRepository.TenantId, rtDeserializeStream.Dependencies.ToList());
                    await rtDeserializeStream.ReadAsync().ConfigureAwait(false);
                }
            }

            await session.CommitTransactionAsync().ConfigureAwait(false);

            logger.LogInformation("{Count} entities, {AssociationsCount} associations imported", _entityImportIds.Count,
                _associationsCount);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Import of RT model failed");
            throw;
        }
    }

    private void ValidateCkModels(string tenantId, ICollection<CkModelIdVersionRange> ckModelIdRanges)
    {
        var unsatisfiedRanges = cacheService.EnsureModelIdRanges(tenantId, ckModelIdRanges);
        if (unsatisfiedRanges.Any())
        {
            throw ExchangeException.CkModelsMissing(tenantId, unsatisfiedRanges);
        }
    }

    private async Task ImportEntityAsync(IOctoSession session, IEnumerable<RtEntityTcDto> modelRtEntities,
        IRuntimeRepository runtimeRepository, ImportStrategy importStrategy)
    {
        // Materialize once so the runtime-state preserve pass and the import loop below both
        // operate on the same DTO instances (the preserve pass rewrites seed attribute values
        // in place) and a lazy/single-pass IEnumerable can't be enumerated twice.
        var entities = modelRtEntities as IReadOnlyList<RtEntityTcDto> ?? modelRtEntities.ToList();

        // On Upsert the DB layer runs a full ReplaceOne, which would overwrite every attribute
        // on the existing document — including CK attributes flagged isRuntimeState that
        // services/operators own at runtime (deployment/communication status, sync counters,
        // stream-data Archive.Status, …). Preserve those from the existing entity before the
        // import so no Upsert caller (blueprint apply, plain RT ImportRt, CK-model migration)
        // can silently trample them. Insert can't overwrite (it errors on an existing id), so
        // there is nothing to preserve there.
        if (importStrategy == ImportStrategy.Upsert)
        {
            await PreserveRuntimeStateAttributesAsync(session, entities, runtimeRepository).ConfigureAwait(false);
        }

#if NETSTANDARD2_0
        Parallel.ForEach(entities, modelRtEntity =>
#else
        await Parallel.ForEachAsync(entities, async (modelRtEntity, token) =>
#endif
        {
            var ckTypeGraph = cacheService.GetRtCkType(runtimeRepository.TenantId, modelRtEntity.CkTypeId);

#if NETSTANDARD2_0
            var createTask = runtimeRepository.CreateTransientRtEntityByRtCkIdAsync(modelRtEntity.CkTypeId);
            createTask.Wait();
            var rtEntity = createTask.Result;
#else
            var rtEntity = await runtimeRepository.CreateTransientRtEntityByRtCkIdAsync(modelRtEntity.CkTypeId)
                .ConfigureAwait(false);
#endif
            rtEntity.RtId = modelRtEntity.RtId;
            rtEntity.RtChangedDateTime = modelRtEntity.RtChangedDateTime ?? DateTime.UtcNow;
            rtEntity.RtCreationDateTime = modelRtEntity.RtCreationDateTime ?? DateTime.UtcNow;
            rtEntity.RtWellKnownName = modelRtEntity.RtWellKnownName;
            rtEntity.RtState = modelRtEntity.RtState;

            // Atomic check-and-insert across Parallel.ForEachAsync workers; HashSet is not
            // thread-safe and a separate Contains + Add racily hits its internal resize.
            if (!_entityImportIds.TryAdd(rtEntity.RtId, 0))
            {
                logger.LogError("'{RtEntityRtId}' already imported", rtEntity.RtId);
            }

#if !NETSTANDARD2_0
            token.ThrowIfCancellationRequested();
#endif
            AssignAttributes(runtimeRepository, modelRtEntity, ckTypeGraph, rtEntity, "type", ckTypeGraph.CkTypeId);

            // AB#4772: the bulk import path bypasses the entity rule engine, so an entity missing
            // a mandatory attribute would be stored in a state its CK type forbids (AB#4771:
            // seeded rollups without Archive.Columns broke the non-null GraphQL field for the
            // whole archives list). Collect violations here; they are reported (and, in strict
            // mode, rejected) before anything is written.
            var missingMandatory = FindMissingMandatoryAttributes(ckTypeGraph.AllAttributes.Values, rtEntity);
            if (missingMandatory.Count > 0)
            {
                _mandatoryViolations.Enqueue((rtEntity.RtId, modelRtEntity.CkTypeId,
                    missingMandatory.Select(a => a.CkAttributeId.ToString()).ToList()));
            }

            _importEntityQueue.Enqueue(rtEntity);

            if (modelRtEntity.Associations is { Count: > 0 })
            {
                foreach (var association in modelRtEntity.Associations)
                {
                    var ckAssociationRoleGraph =
                        cacheService.GetRtCkAssociationRole(runtimeRepository.TenantId, association.RoleId);

                    var rtAssociation = new RtAssociation
                    {
                        AssociationRoleId = association.RoleId,
                        RtState = rtEntity.RtState, // We take over the state of the entity.
                        OriginRtId = rtEntity.RtId,
                        OriginCkTypeId = rtEntity.CkTypeId!,
                        TargetRtId = association.TargetRtId,
                        TargetCkTypeId = association.TargetCkTypeId,
                        TargetCkAttributeIds = association.TargetCkAttributeIds
                    };

                    AssignAttributes(runtimeRepository, association, ckAssociationRoleGraph, rtAssociation,
                        "association", ckAssociationRoleGraph.CkRoleId);

                    _importAssociationQueue.Enqueue(rtAssociation);
                    Interlocked.Increment(ref _associationsCount);
                }
            }
#if NETSTANDARD2_0
        });
#else
        }).ConfigureAwait(false);
#endif
        logger.LogInformation("{EntityCount} entities (total imports of {Count}) imported", _importEntityQueue.Count,
            _entityImportIds.Count);
        await ReportMandatoryViolationsAsync(runtimeRepository.TenantId).ConfigureAwait(false);
        await ImportToDatabase(session, runtimeRepository, importStrategy).ConfigureAwait(false);
    }

    /// <summary>
    /// The exact predicate the entity rule engine applies on API inserts (see
    /// <c>EntityRuleEngine.SetDefaultValuesOnInsert</c>): an attribute is a violation when it is
    /// not optional, carries no value on the entity, and neither default values nor an
    /// auto-increment reference exist to ever fill it. Attributes WITH defaults/auto-increment
    /// are deliberately not flagged — that keeps parity with what an API insert would accept,
    /// even though the import path applies neither (stage-2 scope decision on AB#4772).
    /// Exposed as the testable seam of the validation.
    /// </summary>
    internal static List<CkTypeAttributeGraph> FindMissingMandatoryAttributes(
        IEnumerable<CkTypeAttributeGraph> attributeGraphs, RtTypeWithAttributes rtType)
    {
        List<CkTypeAttributeGraph>? missing = null;
        foreach (var attribute in attributeGraphs)
        {
            if (attribute.IsOptional)
            {
                continue;
            }

            if (rtType.Attributes.TryGetValue(attribute.AttributeName, out var value) && value != null)
            {
                continue;
            }

            if (attribute.DefaultValues != null || !string.IsNullOrWhiteSpace(attribute.AutoIncrementReference))
            {
                continue;
            }

            (missing ??= []).Add(attribute);
        }

        return missing ?? [];
    }

    /// <summary>
    /// Stage-2 rollout of the AB#4772 hardening: logs every collected mandatory-attribute
    /// violation as a warning and publishes one audit event per entity (tenant event log via the
    /// host's <c>IAuditEventSink</c>). When <see cref="RtImportOptions.StrictMandatoryValidation"/>
    /// is on, throws BEFORE anything is written so the import fails atomically. An audit-trail
    /// failure never blocks the import in non-strict mode.
    /// </summary>
    private async Task ReportMandatoryViolationsAsync(string tenantId)
    {
        if (_mandatoryViolations.IsEmpty)
        {
            return;
        }

        var violations = _mandatoryViolations.ToArray();
        foreach (var (rtId, ckTypeId, missing) in violations)
        {
            logger.LogWarning(
                "Imported entity '{CkTypeId}@{RtId}' is missing mandatory attribute(s) {MissingAttributes} " +
                "(no default value or auto-increment reference). The stored entity violates its CK type; " +
                "non-null GraphQL fields on these attributes will fail (AB#4772).",
                ckTypeId, rtId, string.Join(", ", missing));

            try
            {
                await rtImportAuditTrail.RecordMissingMandatoryAttributesAsync(tenantId, ckTypeId, rtId, missing)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogWarning(e,
                    "Failed to publish the missing-mandatory-attributes audit event for '{CkTypeId}@{RtId}'",
                    ckTypeId, rtId);
            }
        }

        if (importOptions.StrictMandatoryValidation)
        {
            var summary = string.Join("; ", violations
                .Take(20)
                .Select(v => $"{v.CkTypeId}@{v.RtId}: {string.Join(", ", v.MissingCkAttributeIds)}"));
            if (violations.Length > 20)
            {
                summary += $"; … {violations.Length - 20} more";
            }

            throw ExchangeException.MandatoryAttributesMissing(violations.Length, summary);
        }
    }

    private void AssignAttributes<TKey>(IRuntimeRepository runtimeRepository,
        RtTypeWithAttributesTcDto rtTypeWithAttributesDto,
        CkTypeWithAttributesGraph ckTypeWithAttributesGraph, RtTypeWithAttributes rtTypeWithAttributes,
        string elementType, CkId<TKey> ckId)
        where TKey : IComparable<TKey>, ICkElementId
    {
        foreach (var modelAttribute in rtTypeWithAttributesDto.Attributes)
        {
            var typeAttributeGraph =
                ckTypeWithAttributesGraph.AllAttributes.Values.FirstOrDefault(a =>
                    a.CkAttributeId.Equals(modelAttribute.Id));
            if (typeAttributeGraph == null)
            {
                logger.LogError("'{ModelAttributeId}' does not exit on type '{CkTypeId}'", modelAttribute.Id,
                    ckId);
                throw ExchangeException.AttributeNotFound(modelAttribute.Id, elementType, ckId);
            }

            if (typeAttributeGraph.ValueType == AttributeValueTypesDto.Record)
            {
                if (modelAttribute.Value is RtRecordTcDto rtRecordDto)
                {
                    var ckRecordGraph = cacheService.GetRtCkRecord(runtimeRepository.TenantId, rtRecordDto.CkRecordId);

                    var rtRecord = new RtRecord { CkRecordId = ckRecordGraph.CkRecordId.ToRtCkId() };
                    AssignAttributes(runtimeRepository, rtRecordDto, ckRecordGraph, rtRecord, elementType, ckId);

                    rtTypeWithAttributes.SetAttributeValue(typeAttributeGraph.AttributeName,
                        typeAttributeGraph.ValueType, rtRecord);
                }

                continue;
            }

            if (typeAttributeGraph.ValueType == AttributeValueTypesDto.RecordArray)
            {
                var rtRecords = new List<RtRecord>();
                if (modelAttribute.Value is IEnumerable rtRecordDtoList)
                {
                    foreach (RtRecordTcDto record in rtRecordDtoList)
                    {
                        var ckRecordGraph = cacheService.GetRtCkRecord(runtimeRepository.TenantId, record.CkRecordId);

                        var rtRecord = new RtRecord { CkRecordId = ckRecordGraph.CkRecordId.ToRtCkId() };
                        AssignAttributes(runtimeRepository, record, ckRecordGraph, rtRecord, elementType, ckId);

                        rtRecords.Add(rtRecord);
                    }
                }

                rtTypeWithAttributes.SetAttributeValue(typeAttributeGraph.AttributeName, typeAttributeGraph.ValueType,
                    rtRecords);
                continue;
            }

            if (typeAttributeGraph.ValueType == AttributeValueTypesDto.Enum)
            {
                if (typeAttributeGraph.ValueCkEnumId == null)
                {
                    logger.LogError(
                        "'{ModelAttributeId}' defines unknown enum '{CkEnumId}' at type '{CkTypeId}'",
                        modelAttribute.Id,
                        typeAttributeGraph.ValueCkEnumId, ckId);
                    throw ExchangeException.CkEnumIdNotDefined(typeAttributeGraph);
                }

                var ckEnumGraph = cacheService.GetCkEnum(runtimeRepository.TenantId, typeAttributeGraph.ValueCkEnumId);

                if (modelAttribute.Value == null)
                {
                    rtTypeWithAttributes.SetAttributeValue(typeAttributeGraph.AttributeName,
                        typeAttributeGraph.ValueType,
                        null);
                    continue;
                }

                var value = ckEnumGraph.Values.FirstOrDefault(x => x.Key.Equals(modelAttribute.Value) ||
                                                                   x.Key.ToString().Equals(modelAttribute.Value) ||
                                                                   String.Compare(x.Name,
                                                                       modelAttribute.Value?.ToString(),
                                                                       StringComparison.OrdinalIgnoreCase) == 0);
                if (value == null)
                {
                    logger.LogError(
                        "'{ModelAttributeId}' defines unknown enum value '{EnumValue}' at type '{CkTypeId}'",
                        modelAttribute.Id,
                        modelAttribute.Value, ckId);
                    throw ExchangeException.CkEnumWithOutOfRange(typeAttributeGraph, modelAttribute.Value);
                }

                rtTypeWithAttributes.SetAttributeValue(typeAttributeGraph.AttributeName, typeAttributeGraph.ValueType,
                    value.Key);
                continue;
            }

            rtTypeWithAttributes.SetAttributeValue(typeAttributeGraph.AttributeName, typeAttributeGraph.ValueType,
                modelAttribute.Value);
        }
    }

    private async Task ImportToDatabase(IOctoSession session, IRuntimeRepository runtimeRepository,
        ImportStrategy importStrategy)
    {
        logger.LogInformation("Importing {Count} to database", _importEntityQueue.Count);

        try
        {
            var importEntities = new List<RtEntity>();
            var importAssociations = new List<RtAssociation>();

            var entityMax = _importEntityQueue.Count;
            var associationsMax = _importAssociationQueue.Count;

            for (var i = 0; i < entityMax; i++)
            {
                if (_importEntityQueue.TryDequeue(out var tmp))
                {
                    importEntities.Add(tmp);
                }
                else
                {
                    break;
                }
            }

            for (var i = 0; i < associationsMax; i++)
            {
                if (_importAssociationQueue.TryDequeue(out var tmp))
                {
                    importAssociations.Add(tmp);
                }
                else
                {
                    break;
                }
            }

            var bulkInsertStrategy = importStrategy == ImportStrategy.Insert
                ? BulkInsertStrategies.InsertOnly
                : BulkInsertStrategies.Upsert;

            if (importEntities.Any())
            {
                logger.LogInformation("Adding entities...");
                await runtimeRepository.BulkInsertRtEntitiesAsync(session, importEntities,
                    new BulkOperationOptions { InsertStrategy = bulkInsertStrategy }).ConfigureAwait(false);
            }

            if (importAssociations.Any())
            {
                logger.LogInformation("Adding associations...");
                await runtimeRepository.BulkRtAssociationsAsync(session, importAssociations,
                    new BulkOperationOptions { InsertStrategy = bulkInsertStrategy }).ConfigureAwait(false);
            }


            logger.LogInformation("Add to database completed");
        }
        catch (Exception e)
        {
            throw ExchangeException.BulkImportError(e);
        }
    }

    /// <summary>
    /// For every entity in the import model that already exists in the tenant repo, replaces the
    /// incoming values for CK-attributes flagged <c>isRuntimeState</c> (see
    /// <see cref="ConstructionKit.Contracts.DataTransferObjects.CkAttributeDto.IsRuntimeState"/>)
    /// with the existing runtime value. Runs only for <see cref="ImportStrategy.Upsert"/> (an Insert
    /// cannot overwrite an existing entity). Fresh tenants and brand-new entities are silent no-ops.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why this exists: an Upsert import maps to a full <c>ReplaceOne</c> in the MongoDB layer —
    /// every attribute on the existing document is overwritten by the import's values, even for
    /// attributes that carry runtime state services/operators own (deployment/communication status,
    /// last-error fields, sync sequence numbers, stream-data <c>Archive.Status</c>). Without this,
    /// re-importing a seed/YAML that carries those attributes resets them to whatever default the
    /// import file holds — e.g. a "Deployed" adapter flipping back to "Undeployed" on a blueprint
    /// version bump, or an activated stream-data archive silently reverting to Disabled on a
    /// blueprint re-apply or a plain <c>ImportRt -r</c> (AB#4582 / AB#4589).
    /// </para>
    /// <para>
    /// This lives at the single import choke point so every Upsert caller — blueprint seed apply,
    /// the plain <c>ImportRt</c> CLI path, and CK-model migration entity writes — gets the guarantee
    /// automatically. Preservation only rewrites incoming values in place for attributes the model
    /// already declares AND the existing entity already has a value for; additive attributes (new in
    /// this CK bump) fall through to the imported value on a pre-existing entity, and entities that
    /// don't exist yet are untouched.
    /// </para>
    /// </remarks>
    private async Task PreserveRuntimeStateAttributesAsync(IOctoSession session,
        IReadOnlyList<RtEntityTcDto> modelRtEntities, IRuntimeRepository runtimeRepository)
    {
        // Group by CkTypeId so we can batch one repo query per type instead of per entity.
        // Entities with an empty rtId can't be looked up — they are always treated as new.
        var entitiesByType = modelRtEntities
            .Where(e => !e.RtId.Equals(OctoObjectId.Empty))
            .GroupBy(e => e.CkTypeId)
            .ToList();

        if (entitiesByType.Count == 0)
        {
            return;
        }

        var totalPreserved = 0;

        foreach (var typeGroup in entitiesByType)
        {
            // Look up the CK type to find which attributes are flagged. If the type isn't in
            // the cache the import would fail downstream anyway — let it surface there with
            // the existing error path instead of swallowing it here.
            if (!cacheService.TryGetRtCkType(runtimeRepository.TenantId, typeGroup.Key, out var ckTypeGraph))
            {
                continue;
            }

            // Materialize the flagged-attribute set once per type.
            var flaggedAttributes = ckTypeGraph!.AllAttributes.Values
                .Where(a => a.IsRuntimeState)
                .ToList();

            if (flaggedAttributes.Count == 0)
            {
                // Cheap fast path: this type has no runtime-state attrs, nothing to preserve.
                continue;
            }

            var modelEntities = typeGroup.ToList();
            var rtIds = modelEntities.Select(e => e.RtId).ToList();

            IResultSet<RtEntity> existingEntities;
            try
            {
                existingEntities = await runtimeRepository.GetRtEntitiesByIdAsync(
                    session, typeGroup.Key, rtIds, RtEntityQueryOptions.Create()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Don't fail the whole import — if we can't look up existing entities the
                // import will still proceed with the imported values (i.e. the old behaviour).
                logger.LogWarning(ex,
                    "Failed to look up existing entities of type {CkTypeId} for runtime-state preservation; falling back to imported values",
                    typeGroup.Key);
                continue;
            }

            var existingByRtId = existingEntities.Items.ToDictionary(e => e.RtId);

            foreach (var modelEntity in modelEntities)
            {
                if (!existingByRtId.TryGetValue(modelEntity.RtId, out var existing))
                {
                    // New entity — let the imported value land as-is.
                    continue;
                }

                var preservedForEntity = PreserveAttributesForEntity(modelEntity, existing, flaggedAttributes);
                if (preservedForEntity > 0)
                {
                    totalPreserved += preservedForEntity;
                    logger.LogDebug(
                        "Preserved {PreservedCount} runtime-state attribute(s) on existing entity {RtId} (type {CkTypeId}) during RT import",
                        preservedForEntity, modelEntity.RtId, typeGroup.Key);
                }
            }
        }

        if (totalPreserved > 0)
        {
            logger.LogInformation(
                "RT import preserved {TotalPreserved} runtime-state attribute value(s) for tenant {TenantId}",
                totalPreserved, runtimeRepository.TenantId);
        }
    }

    /// <summary>
    /// Per-entity preserve loop. For each <paramref name="flaggedAttributes"/> entry that has a
    /// value on <paramref name="existing"/> AND a value on <paramref name="modelEntity"/>, copies
    /// the existing value over the imported value in-place on <paramref name="modelEntity"/>. Returns
    /// the count of attributes preserved. Pure function so it can be unit-tested without mocking
    /// the repository / cache surface.
    /// </summary>
    internal static int PreserveAttributesForEntity(
        RtEntityTcDto modelEntity,
        RtEntity existing,
        IReadOnlyList<CkTypeAttributeGraph> flaggedAttributes)
    {
        var preserved = 0;
        foreach (var flaggedAttr in flaggedAttributes)
        {
            var modelAttr = modelEntity.Attributes.FirstOrDefault(a =>
                a.Id.Equals(flaggedAttr.CkAttributeId));
            if (modelAttr == null)
            {
                // Import model doesn't carry this attribute (e.g. additive CK bump) — nothing to overwrite.
                continue;
            }

            if (!existing.Attributes.TryGetValue(flaggedAttr.AttributeName, out var existingValue))
            {
                // Existing entity doesn't have a value for this attr (e.g. the attr was just added
                // in this CK bump on a pre-existing entity); fall through to the imported value so the
                // new attr lands with its imported default.
                continue;
            }

            modelAttr.Value = existingValue;
            preserved++;
        }
        return preserved;
    }
}