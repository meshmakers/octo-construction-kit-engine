using System.Collections;
using System.Collections.Concurrent;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
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
    IRtJsonSerializer rtJsonSerializer)
    : IImportRtModelCommand
{
    private readonly HashSet<OctoObjectId> _entityImportIds = [];
    private readonly ConcurrentQueue<RtAssociation> _importAssociationQueue = new();

    private readonly ConcurrentQueue<RtEntity> _importEntityQueue = new();
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
#if NETSTANDARD2_0
        Parallel.ForEach(modelRtEntities, modelRtEntity =>
#else
        await Parallel.ForEachAsync(modelRtEntities, async (modelRtEntity, token) =>
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

            if (_entityImportIds.Contains(rtEntity.RtId))
            {
                logger.LogError("'{RtEntityRtId}' already imported", rtEntity.RtId);
            }

            lock (_entityImportIds)
            {
                _entityImportIds.Add(rtEntity.RtId);
            }

#if !NETSTANDARD2_0
            token.ThrowIfCancellationRequested();
#endif
            AssignAttributes(runtimeRepository, modelRtEntity, ckTypeGraph, rtEntity, "type", ckTypeGraph.CkTypeId);

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
        await ImportToDatabase(session, runtimeRepository, importStrategy).ConfigureAwait(false);
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
}