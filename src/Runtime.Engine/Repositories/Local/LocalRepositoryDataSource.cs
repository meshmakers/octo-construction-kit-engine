using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Local;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

internal class LocalRepositoryDataSource : RepositoryDataSource, ILocalRepositoryDataSource
{
    private readonly ICkCacheService _ckCacheService;
    private readonly IRtRepositorySerializer _rtSerializer;
    private readonly string _directoryPath;

    public LocalRepositoryDataSource(string tenantId, string directoryPath, ICkCacheService ckCacheService,
        IRtRepositorySerializer rtSerializer)
        : base(tenantId)
    {
        _ckCacheService = ckCacheService;
        _rtSerializer = rtSerializer;
        _directoryPath = directoryPath;

        RtAssociations = new LocalDataSourceCollection<OctoObjectId, RtAssociation, RtAssociationDto>(TenantId,
            Path.Combine(_directoryPath, "associations.json"),
            new RtAssociationDataSourceMapper(TenantId, _ckCacheService, _rtSerializer));
    }

    public override IDataSourceCollection<OctoObjectId, TEntity> GetRtCollection<TEntity>(CkId<CkTypeId> ckTypeId)
    {
        var suffix = ckTypeId.SemanticVersionedFullName.Replace("/", "_");

        var filePath = Path.Combine(_directoryPath, suffix + ".json");

        var dataSourceMapper = new RtEntityDataSourceMapper<TEntity>(TenantId, _ckCacheService, _rtSerializer);

        return new LocalDataSourceCollection<OctoObjectId, TEntity, RtEntityDto>(TenantId, filePath, dataSourceMapper);
    }

    public override IDataSourceCollection<OctoObjectId, RtAssociation> RtAssociations { get; }

    public override async Task<CurrentMultiplicity> GetCurrentRtAssociationMultiplicityAsync(IOctoSession session, RtEntityId rtEntityId,
        CkId<CkAssociationRoleId> ckRoleId, GraphDirections direction)
    {
        long counter = 0;
        var queryable = await RtAssociations.AsQueryableAsync().ConfigureAwait(false);

        if (direction == GraphDirections.Inbound || direction == GraphDirections.Any)
        {
            var r = queryable.Count(a =>
                a.TargetRtId == rtEntityId.RtId && a.TargetCkTypeId == rtEntityId.CkTypeId && a.AssociationRoleId == ckRoleId);
            counter = Math.Max(r, counter);
        }

        if (direction == GraphDirections.Outbound || direction == GraphDirections.Any)
        {
            var r = queryable.Count(a =>
                a.OriginRtId == rtEntityId.RtId && a.OriginCkTypeId == rtEntityId.CkTypeId && a.AssociationRoleId == ckRoleId);
            counter = Math.Max(r, counter);
        }

        if (counter >= 2)
        {
            return CurrentMultiplicity.Many;
        }

        if (counter == 1)
        {
            return CurrentMultiplicity.One;
        }

        return CurrentMultiplicity.Zero;
    }
}