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
        IEnumerable<RtEntityId> rtEntityIds, RtAssociationExtendedQueryOptions associationExtendedQueryOptions)
    {
        return GetRtAssociationsInternalAsync(session, rtEntityIds.ToList(), associationExtendedQueryOptions);
    }


    private async Task<IMultipleOriginResultSet<RtAssociation>> GetRtAssociationsInternalAsync(IOctoSession session,
        ICollection<RtEntityId> rtEntityIds,
        RtAssociationExtendedQueryOptions options)
    {
        var associations = new Dictionary<RtEntityId, List<RtAssociation>>();
        foreach (var rtEntityId in rtEntityIds)
        {
            associations.Add(rtEntityId, []);
        }

        var queryable = await RtAssociations.AsQueryableAsync(session).ConfigureAwait(false);
        bool includeArchived = options.GlobalFilter?.IncludeArchived ?? false;
        var roleId = options.RoleId;
        var relatedRtCkTypeId = options.RelatedRtCkTypeId;
        var relatedRtId = options.RelatedRtId;

        if (options.Direction == GraphDirections.Any ||
            options.Direction == GraphDirections.Inbound)
        {
            foreach (var rtAssociation in queryable.Where(x =>
                             (includeArchived || x.RtState != RtState.Archived) &&
                             (roleId == null || x.AssociationRoleId == roleId) &&
                             (relatedRtCkTypeId == null || x.OriginCkTypeId == relatedRtCkTypeId) &&
                             (relatedRtId == null || x.OriginRtId == relatedRtId) &&
                             rtEntityIds.Any(rtEntityId => rtEntityId.RtId == x.TargetRtId &&
                                                           rtEntityId.CkTypeId == x.TargetCkTypeId
                             )))
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

        if (options.Direction == GraphDirections.Any ||
            options.Direction == GraphDirections.Outbound)
        {
            foreach (var rtAssociation in queryable.Where(x =>
                         (includeArchived || x.RtState != RtState.Archived) &&
                         (roleId == null || x.AssociationRoleId == roleId) &&
                         (relatedRtCkTypeId == null || x.TargetCkTypeId == relatedRtCkTypeId) &&
                         (relatedRtId == null || x.TargetRtId == relatedRtId) &&
                         rtEntityIds.Any(rtEntityId => rtEntityId.RtId == x.OriginRtId &&
                                                       rtEntityId.CkTypeId == x.OriginCkTypeId
                         )))
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
        if (options.Skip.HasValue)
        {
            foreach (var resultSetValue in multipleResult.Values)
            {
                resultSetValue.Skip(options.Skip.Value);
            }
        }

        if (options.Take.HasValue)
        {
            foreach (var resultSetValue in multipleResult.Values)
            {
                resultSetValue.Take(options.Take.Value);
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
        IEnumerable<RtOriginTargetPair> rtOriginTargetPair, RtAssociationBaseQueryOptions associationQueryOptions);

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
    public Task DeleteExpiredTemporaryLargeBinariesAsync(IOctoSession session, DateTime expiryDateTime,
        CancellationToken cancellationToken)
    {
        return BinaryDataSource.DeleteExpiredTemporaryLargeBinariesAsync(session, expiryDateTime, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAllTemporaryLargeBinariesAsync(IOctoSession session, CancellationToken cancellationToken)
    {
        return BinaryDataSource.DeleteAllTemporaryLargeBinariesAsync(session, cancellationToken);
    }
}