using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Local;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

internal class LocalRepositoryDataSource : RepositoryDataSource, ILocalRepositoryDataSource
{
    private readonly ICkCacheService _ckCacheService;
    private readonly string _directoryPath;
    private readonly IRtRepositorySerializer _rtSerializer;


    public LocalRepositoryDataSource(string tenantId, string directoryPath, ICkCacheService ckCacheService,
        IRtRepositorySerializer rtSerializer)
        : base(tenantId, new LocalLinkedBinaryDataSource(tenantId, directoryPath, rtSerializer))
    {
        _ckCacheService = ckCacheService;
        _rtSerializer = rtSerializer;
        _directoryPath = directoryPath;

        RtAssociations = new LocalDataSourceCollection<OctoObjectId, RtAssociation, RtAssociationTcDto>(TenantId,
            Path.Combine(_directoryPath, "associations.json"),
            new RtAssociationDataSourceMapper(TenantId, _ckCacheService, _rtSerializer));
    }

    public override IDataSourceCollection<OctoObjectId, TEntity> GetRtCollection<TEntity>(CkTypeGraph ckTypeGraph)
    {
        var suffix = ckTypeGraph.CkTypeId.SemanticVersionedFullName.Replace("/", "_");

        var filePath = Path.Combine(_directoryPath, suffix + ".json");

        var dataSourceMapper = new RtEntityDataSourceMapper<TEntity>(TenantId, _ckCacheService, _rtSerializer);

        return new LocalDataSourceCollection<OctoObjectId, TEntity, RtEntityTcDto>(TenantId, filePath, dataSourceMapper);
    }

    public override IDataSourceCollection<OctoObjectId, RtAssociation> RtAssociations { get; }

    public override async Task<IReadOnlyList<RtAssociationsMultiplicityResult>> GetRtAssociationsMultiplicityAsync(
        IOctoSession session, IEnumerable<RtEntityRoleIdDirectionPair> entityRoleIdDirectionPairs)
    {
        var queryable = await RtAssociations.AsQueryableAsync(session).ConfigureAwait(false);

        var results = new List<RtAssociationsMultiplicityResult>();

        foreach (var pair in entityRoleIdDirectionPairs)
        {
            var count = 0;
            if (pair.Direction == GraphDirections.Inbound || pair.Direction == GraphDirections.Any)
            {
                count += queryable.Count(a =>
                    a.TargetRtId == pair.RtEntityId.RtId && a.TargetCkTypeId == pair.RtEntityId.CkTypeId &&
                    a.AssociationRoleId == pair.CkRoleId);
            }

            if (pair.Direction == GraphDirections.Outbound || pair.Direction == GraphDirections.Any)
            {
                count += queryable.Count(a =>
                    a.OriginRtId == pair.RtEntityId.RtId && a.OriginCkTypeId == pair.RtEntityId.CkTypeId &&
                    a.AssociationRoleId == pair.CkRoleId);
            }

            CurrentMultiplicity multiplicity = count switch
            {
                >= 2 => CurrentMultiplicity.Many,
                1 => CurrentMultiplicity.One,
                _ => CurrentMultiplicity.Zero
            };

            results.Add(new RtAssociationsMultiplicityResult(pair, multiplicity));
        }

        return results;
    }

    public override async Task<IReadOnlyList<RtAssociation>> GetRtAssociationsAsync(IOctoSession session,
        IEnumerable<RtOriginTargetPair> rtOriginTargetPair, RtAssociationQueryOptions associationQueryOptions)
    {
        var queryable = await RtAssociations.AsQueryableAsync(session).ConfigureAwait(false);
        bool includeArchived = associationQueryOptions.GlobalFilter?.IncludeArchived ?? false;
        var associations = new List<RtAssociation>();
        foreach (var pair in rtOriginTargetPair)
        {
            var association = queryable.FirstOrDefault(a =>
                includeArchived || a.RtState != RtState.Deleted &&
                a.OriginRtId == pair.Origin.RtId && a.OriginCkTypeId == pair.Origin.CkTypeId &&
                a.TargetRtId == pair.Target.RtId && a.TargetCkTypeId == pair.Target.CkTypeId &&
                a.AssociationRoleId == pair.AssociationRoleId);

            if (association != null)
            {
                associations.Add(association);
            }
        }

        return associations;
    }
}