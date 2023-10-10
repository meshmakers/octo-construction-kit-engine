using System.Collections.Concurrent;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

internal class LocalDataSourceCollection<TDocument> : IDataSourceCollection<TDocument> where TDocument : RtEntity, new()
{
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    private readonly string _tenantId;
    private readonly ICkCacheService _ckCacheService;
    public string FilePath { get; }
    public IRtSerializer RtSerializer { get; }

    private readonly ConcurrentDictionary<OctoObjectId, RtEntity> _rtEntities;
    private bool _isLoaded;

    public LocalDataSourceCollection(string tenantId, string filePath, ICkCacheService ckCacheService, IRtSerializer rtSerializer)
    {
        _tenantId = tenantId;
        _ckCacheService = ckCacheService;
        FilePath = filePath;
        RtSerializer = rtSerializer;

        _rtEntities = new ConcurrentDictionary<OctoObjectId, RtEntity>();
        _isLoaded = false;
    }


    public async Task InsertAsync(IOctoSession session, TDocument document)
    {
        await LoadAsync().ConfigureAwait(false);

        if (!_rtEntities.TryAdd(document.RtId, document))
        {
            throw RuntimeRepositoryException.EntityAlreadyAdded(_tenantId, document.RtId);
        }

        await SaveAsync().ConfigureAwait(false);
    }

    private async Task SaveAsync()
    {
        try
        {
            await _semaphoreSlim.WaitAsync().ConfigureAwait(false);
            if (!_isLoaded)
            {
                return;
            }

            var entities = new ConcurrentBag<RtEntityDto>();

            Parallel.ForEach(_rtEntities.Values, (modelRtEntity, token) =>
            {
                var ckTypeGraph = _ckCacheService.GetCkType(_tenantId, modelRtEntity.CkTypeId);

                var rtEntityDto = new RtEntityDto
                {
                    RtId = modelRtEntity.RtId,
                    CkTypeId = modelRtEntity.CkTypeId,
                    RtChangedDateTime = modelRtEntity.RtChangedDateTime,
                    RtCreationDateTime = modelRtEntity.RtCreationDateTime,
                    RtWellKnownName = modelRtEntity.RtWellKnownName,
                };
                
                foreach (var modelRtAttribute in modelRtEntity.Attributes)
                {
                    if (ckTypeGraph.AllAttributesByName.TryGetValue(modelRtAttribute.Key, out var ckTypeAttributeGraph))
                    {
                        rtEntityDto.Attributes.Add(new RtAttributeDto
                        {
                            Id = ckTypeAttributeGraph.CkAttributeId,
                            Value = modelRtAttribute.Value
                        });
                    }
                }
                
                entities.Add(rtEntityDto);
            });
            
            var rtModelRoot = new RtModelRootDto
            {
                Dependencies = _ckCacheService.GetCkDependencies(_tenantId).ToList(),
                Entities = entities.ToList()
            };

            await using var streamWriter = new StreamWriter(FilePath);
            await RtSerializer.SerializeAsync(streamWriter, rtModelRoot).ConfigureAwait(false);
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            await _semaphoreSlim.WaitAsync().ConfigureAwait(false);

            if (_isLoaded)
            {
                return;
            }

            if (!File.Exists(FilePath))
            {
                _isLoaded = true;
                return;
            }

            var operationResult = new OperationResult();
            await using var fileStream = File.OpenRead(FilePath);
            var existingDocuments = await RtSerializer.DeserializeAsync(fileStream, FilePath, operationResult).ConfigureAwait(false);
            if (operationResult.HasErrors)
            {
                throw new Exception($"Error while loading file {FilePath}");
            }

            Parallel.ForEach(existingDocuments.Entities, (modelRtEntity, _) =>
            {
                var ckTypeGraph = _ckCacheService.GetCkType(_tenantId, modelRtEntity.CkTypeId);

                var entity = new RtEntity
                {
                    RtId = modelRtEntity.RtId,
                    CkTypeId = modelRtEntity.CkTypeId,
                    RtChangedDateTime = modelRtEntity.RtChangedDateTime,
                    RtCreationDateTime = modelRtEntity.RtCreationDateTime,
                    RtWellKnownName = modelRtEntity.RtWellKnownName,
                };

                foreach (var modelRtAttribute in modelRtEntity.Attributes)
                {
                    if (ckTypeGraph.AllAttributes.TryGetValue(modelRtAttribute.Id, out var ckTypeAttributeGraph))
                    {
                        entity.SetAttributeValue(ckTypeAttributeGraph.AttributeName, ckTypeAttributeGraph.ValueType,
                            modelRtAttribute.Value);
                    }
                }

                _rtEntities.TryAdd(modelRtEntity.RtId, entity);
            });

            _isLoaded = true;
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }
}