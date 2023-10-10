using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Local;
using Meshmakers.Octo.Runtime.Contracts.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

internal class LocalRepositoryDataSource : RepositoryDataSource, ILocalRepositoryDataSource
{
    private readonly string _tenantId;
    private readonly ICkCacheService _ckCacheService;
    private readonly IRtSerializer _rtSerializer;
    public string DirectoryPath { get; }

    public LocalRepositoryDataSource(string tenantId, string directoryPath, ICkCacheService ckCacheService, IRtSerializer rtSerializer)
    {
        _tenantId = tenantId;
        _ckCacheService = ckCacheService;
        _rtSerializer = rtSerializer;
        DirectoryPath = directoryPath;
    }
    
    public override IDataSourceCollection<TEntity> GetRtCollection<TEntity>(CkId<CkTypeId> ckTypeId) 
    {
        var suffix = ckTypeId.SemanticVersionedFullName.Replace("/", "_");

        var filePath = Path.Combine(DirectoryPath, suffix + ".json");

        return new LocalDataSourceCollection<TEntity>(_tenantId, filePath, _ckCacheService, _rtSerializer);
    }
}