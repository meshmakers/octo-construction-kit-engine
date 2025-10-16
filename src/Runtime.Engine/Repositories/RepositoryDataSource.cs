using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Repositories.Query;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Base class for a data source for a repository
/// </summary>
public abstract class RepositoryDataSource : IRepositoryDataSource
{
    /// <inheritdoc />
    public ILinkedBinaryDataSource BinaryDataSource { get; }

    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="tenantId"></param>
    /// <param name="linkedBinaryDataSource">The corresponding linked binary data source</param>
    protected RepositoryDataSource(string tenantId, ILinkedBinaryDataSource linkedBinaryDataSource)
    {
        BinaryDataSource = linkedBinaryDataSource;
        TenantId = tenantId;
    }

    /// <inheritdoc />
    public string TenantId { get; }

    /// <inheritdoc />
    public abstract IDataSourceCollection<OctoObjectId, TEntity> GetRtCollection<TEntity>(CkTypeGraph ckTypeGraph)
        where TEntity : RtEntity, new();

    /// <inheritdoc />
    public abstract IDataSourceCollection<OctoObjectId, RtAssociation> RtAssociations { get; }

    /// <inheritdoc />
    public Task<IMultipleOriginResultSet<RtAssociation>> GetRtAssociationsAsync(IOctoSession session,
        IEnumerable<RtEntityId> rtEntityIds, GraphDirections direction, int? skip = null,
        int? take = null)
    {
        return GetRtAssociationsInternalAsync(session, rtEntityIds.ToList(), direction, null, skip, take);
    }


    /// <inheritdoc />
    public Task<IMultipleOriginResultSet<RtAssociation>> GetRtAssociationsAsync(IOctoSession session,
        IEnumerable<RtEntityId> rtEntityIds, GraphDirections direction, RtCkId<CkAssociationRoleId> roleId,
        int? skip = null,
        int? take = null)
    {
        return GetRtAssociationsInternalAsync(session, rtEntityIds.ToList(), direction, roleId, skip, take);
    }

    private async Task<IMultipleOriginResultSet<RtAssociation>> GetRtAssociationsInternalAsync(IOctoSession session,
        ICollection<RtEntityId> rtEntityIds, GraphDirections direction, RtCkId<CkAssociationRoleId>? roleId,
        int? skip = null,
        int? take = null)
    {
        var associations = new Dictionary<RtEntityId, List<RtAssociation>>();
        foreach (var rtEntityId in rtEntityIds)
        {
            associations.Add(rtEntityId, []);
        }
        var queryable = await RtAssociations.AsQueryableAsync(session).ConfigureAwait(false);

        if (direction == GraphDirections.Any || direction == GraphDirections.Inbound)
        {
            foreach (var rtAssociation in queryable.Where(x =>
                         rtEntityIds.Any(rtEntityId => rtEntityId.RtId == x.TargetRtId &&
                                                       rtEntityId.CkTypeId == x.TargetCkTypeId &&
                                                       roleId == null || x.AssociationRoleId == roleId)))
            {
                // Add the association to the dictionary if it does not already exist
                var rtEntityId = new RtEntityId(rtAssociation.TargetCkTypeId, rtAssociation.TargetRtId);
                if (!associations.ContainsKey(rtEntityId))
                {
                    associations.Add(rtEntityId, [rtAssociation]);
                }
                else
                {
                    associations[rtEntityId].Add(rtAssociation);
                }
            }
        }

        if (direction == GraphDirections.Any || direction == GraphDirections.Outbound)
        {
            foreach (var rtAssociation in queryable.Where(x => rtEntityIds.Any(rtEntityId =>
                             rtEntityId.RtId == x.OriginRtId &&
                             rtEntityId.CkTypeId == x.OriginCkTypeId &&
                             roleId == null || x.AssociationRoleId == roleId)))
            {
                // Add the association to the dictionary if it does not already exist
                var rtEntityId = new RtEntityId(rtAssociation.OriginCkTypeId, rtAssociation.OriginRtId);
                if (!associations.ContainsKey(rtEntityId))
                {
                    associations.Add(rtEntityId, [rtAssociation]);
                }
                else
                {
                    associations[rtEntityId].Add(rtAssociation);
                }
            }
        }


        var multipleResult =
            associations.ToDictionary(kvp => kvp.Key,
                kvp => new ResultSet<RtAssociation>(kvp.Value, kvp.Value.Count, null, null));
        if (skip.HasValue)
        {
            foreach (var resultSetValue in multipleResult.Values)
            {
                resultSetValue.Skip(skip.Value);
            }
        }

        if (take.HasValue)
        {
            foreach (var resultSetValue in multipleResult.Values)
            {
                resultSetValue.Take(take.Value);
            }
        }

        return new AssociationMultipleOriginResultSet(multipleResult);
    }


    /// <inheritdoc />
    public abstract Task<IReadOnlyList<RtAssociationsMultiplicityResult>> GetRtAssociationsMultiplicityAsync(
        IOctoSession session, IEnumerable<RtEntityRoleIdDirectionPair> entityRoleIdDirectionPairs);

    /// <inheritdoc />
    public async Task<RtAssociation?> GetRtAssociationOrDefaultAsync(IOctoSession session, RtEntityId originRtEntityId,
        RtEntityId targetRtEntityId, RtCkId<CkAssociationRoleId> ckRoleId)
    {
        var queryable = await RtAssociations.AsQueryableAsync(session).ConfigureAwait(false);
        return queryable
            .FirstOrDefault(a => a.OriginRtId == originRtEntityId.RtId && a.OriginCkTypeId == originRtEntityId.CkTypeId
                                                                       && a.TargetRtId == targetRtEntityId.RtId &&
                                                                       a.TargetCkTypeId == targetRtEntityId.CkTypeId
                                                                       && a.AssociationRoleId == ckRoleId);
    }

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<RtAssociation>> GetRtAssociationsAsync(IOctoSession session,
        IEnumerable<RtOriginTargetPair> rtOriginTargetPair);

    /// <inheritdoc />
    public RtAssociation CreateTransientRtAssociation(RtEntityId originRtEntityId, RtCkId<CkAssociationRoleId> ckRoleId,
        RtEntityId targetRtEntityId)
    {
        return new RtAssociation
        {
            AssociationId = OctoObjectId.GenerateNewId(),
            AssociationRoleId = ckRoleId,
            OriginCkTypeId = originRtEntityId.CkTypeId,
            OriginRtId = originRtEntityId.RtId,
            TargetCkTypeId = targetRtEntityId.CkTypeId,
            TargetRtId = targetRtEntityId.RtId
        };
    }

    /// <inheritdoc />
    public Task<OctoObjectId> UploadTemporaryBinaryAsync(IOctoSession session, string filename, string contentType,
        DateTime expiryDateTime,
        Stream stream, CancellationToken cancellationToken = default)
    {
        return BinaryDataSource.UploadTemporaryBinaryAsync(session, filename, contentType, expiryDateTime, stream,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OctoObjectId> ReplaceTemporaryLargeBinaryAsync(IOctoSession session, string filename,
        string contentType, Stream stream,
        CancellationToken cancellationToken = default)
    {
        return BinaryDataSource.ReplaceTemporaryLargeBinaryAsync(session, filename, contentType, stream,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteTemporaryLargeBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default)
    {
        await BinaryDataSource.DeleteTemporaryLargeBinaryAsync(session, largeBinaryId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IDownloadStreamHandler> DownloadBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default)
    {
        return BinaryDataSource.DownloadBinaryAsync(session, largeBinaryId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IBinaryInfo?> GetTemporaryBinaryAsync(IOctoSession session, OctoObjectId binaryId,
        CancellationToken cancellationToken = default)
    {
        return BinaryDataSource.GetTemporaryBinaryAsync(session, binaryId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IBinaryInfo?> GetTemporaryBinaryAsync(IOctoSession session, string fileName,
        CancellationToken cancellationToken = default)
    {
        return BinaryDataSource.GetTemporaryBinaryAsync(session, fileName, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteExpiredTemporaryLargeBinariesAsync(IOctoSession session, DateTime expiryDateTime, CancellationToken cancellationToken)
    {
        return BinaryDataSource.DeleteExpiredTemporaryLargeBinariesAsync(session, expiryDateTime, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAllTemporaryLargeBinariesAsync(IOctoSession session, CancellationToken cancellationToken)
    {
        return BinaryDataSource.DeleteAllTemporaryLargeBinariesAsync(session, cancellationToken);
    }
}