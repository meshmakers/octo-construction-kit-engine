using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     Interface for the data source of a runtime repository
/// </summary>
public interface IRepositoryDataSource
{
    /// <summary>
    ///     Returns the corresponding tenant id
    /// </summary>
    string TenantId { get; }

    /// <summary>
    /// Returns the binary data source for the repository
    /// </summary>
    ILinkedBinaryDataSource BinaryDataSource { get; }

    /// <summary>
    ///     Returns the associations collection
    /// </summary>
    IDataSourceCollection<OctoObjectId, RtAssociation> RtAssociations { get; }

    /// <summary>
    ///     Returns the data source access object for the given entity type
    /// </summary>
    /// <param name="ckTypeGraph">Construction kit type graph object</param>
    /// <typeparam name="TEntity">The type of entity derived from &lt;see cref="RtEntity"/&gt;</typeparam>
    /// <returns></returns>
    IDataSourceCollection<OctoObjectId, TEntity> GetRtCollection<TEntity>(CkTypeGraph ckTypeGraph)
        where TEntity : RtEntity, new();

    /// <summary>
    ///     Gets associations for multiple runtime entities.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtEntityIds">Runtime entity identifiers to get associations for</param>
    /// <param name="associationQueryOptions">Options of the association query</param>
    /// <returns></returns>
    Task<IMultipleOriginResultSet<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, IEnumerable<RtEntityId> rtEntityIds,
        RtAssociationQueryOptions associationQueryOptions);

    /// <summary>
    ///     Gets associations for a runtime entity of a specific role
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtEntityIds">Runtime entity identifiers to get associations for</param>
    /// <param name="roleId">The construction kit role to get</param>
    /// <param name="associationQueryOptions">Options of the association query</param>
    /// <returns></returns>
    Task<IMultipleOriginResultSet<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, IEnumerable<RtEntityId> rtEntityIds,
        RtCkId<CkAssociationRoleId> roleId, RtAssociationQueryOptions associationQueryOptions);

    /// <summary>
    ///     Returns the current multiplicity of a runtime association, that means the number of associations that exist for a give runtime entity
    ///     and role
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="entityRoleIdDirectionPairs">Direction pairs of runtime entity and role id</param>
    /// <returns></returns>
    Task<IReadOnlyList<RtAssociationsMultiplicityResult>> GetRtAssociationsMultiplicityAsync(IOctoSession session,
        IEnumerable<RtEntityRoleIdDirectionPair> entityRoleIdDirectionPairs);

    /// <summary>
    ///     Gets an association by its origin, target and role id.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="originRtEntityId">Runtime id of the origin entity</param>
    /// <param name="targetRtEntityId">Runtime id of the target entity</param>
    /// <param name="ckRoleId">Construction kit role id of the association</param>
    /// <returns>An association object or null if not found</returns>
    Task<RtAssociation?> GetRtAssociationOrDefaultAsync(IOctoSession session, RtEntityId originRtEntityId,
        RtEntityId targetRtEntityId,
        RtCkId<CkAssociationRoleId> ckRoleId);

    /// <summary>
    /// Gets associations by origin, target and role id for multiple pairs.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtOriginTargetPair">Pairs of origin and target runtime entity identifiers</param>
    /// <param name="associationQueryOptions">Options of the association query</param>
    /// <returns>The list of associations</returns>
    Task<IReadOnlyList<RtAssociation>> GetRtAssociationsAsync(IOctoSession session,
        IEnumerable<RtOriginTargetPair> rtOriginTargetPair, RtAssociationQueryOptions associationQueryOptions);

    /// <summary>
    ///     Creates an instance of a runtime association
    /// </summary>
    /// <param name="originRtEntityId">Runtime id of the origin entity</param>
    /// <param name="ckRoleId">Construction kit role id of the association</param>
    /// <param name="targetRtEntityId">Runtime id of the target entity</param>
    /// <returns>A transient version of a role, need to be stored.</returns>
    RtAssociation CreateTransientRtAssociation(RtEntityId originRtEntityId, RtCkId<CkAssociationRoleId> ckRoleId,
        RtEntityId targetRtEntityId);

    /// <summary>
    /// Uploads a large binary file to the repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="filename">Filename of the file</param>
    /// <param name="contentType">Content type of the file</param>
    /// <param name="expiryDateTime">Expiry date time of the file</param>
    /// <param name="stream">Binary stream of the file</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns></returns>
    Task<OctoObjectId> UploadTemporaryBinaryAsync(IOctoSession session, string filename, string contentType, DateTime expiryDateTime, Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a large binary file in the repository based on the filename and binary type
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="filename">Filename of the file</param>
    /// <param name="contentType">Content type of the file</param>
    /// <param name="stream">Stream of the file</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns>Object id of the large binary</returns>
    Task<OctoObjectId> ReplaceTemporaryLargeBinaryAsync(IOctoSession session, string filename, string contentType, Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a large binary file from the repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="largeBinaryId">Object id of the large binary</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns></returns>
    Task DeleteTemporaryLargeBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a large binary file from the repository based on the large binary id
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="largeBinaryId">Object id of the large binary</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns>Handler for the download stream</returns>
    Task<IDownloadStreamHandler> DownloadBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default);


    /// <summary>
    /// Gets a large binary file from the repository based on the object id
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="binaryId">Binary id of the file</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Binary info of the file including size, content type, etc.</returns>
    Task<IBinaryInfo?> GetTemporaryBinaryAsync(IOctoSession session, OctoObjectId binaryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a large binary file from the repository based on the filename and binary type
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fileName">Filename of the file</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Binary info of the file including size, content type, etc.</returns>
    Task<IBinaryInfo?> GetTemporaryBinaryAsync(IOctoSession session, string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all expired temporary large binaries from the repository.
    /// This method is typically called by a background job to clean up expired files.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="expiryDateTime">Expiry date time to filter expired binaries</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns></returns>
    Task DeleteExpiredTemporaryLargeBinariesAsync(IOctoSession session, DateTime expiryDateTime, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes all temporary large binaries from the repository.
    /// This method is typically called by a background job to clean up all temporary files.
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns></returns>
    Task DeleteAllTemporaryLargeBinariesAsync(IOctoSession session, CancellationToken cancellationToken);
}