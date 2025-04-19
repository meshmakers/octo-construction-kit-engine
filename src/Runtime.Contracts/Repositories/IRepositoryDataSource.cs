using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
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
    public string TenantId { get; }

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
    ///     Gets associations for a runtime entity.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtId">Object id of the runtime entity</param>
    /// <param name="direction">Direction of associations to get</param>
    /// <returns></returns>
    Task<IReadOnlyList<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, OctoObjectId rtId,
        GraphDirections direction);

    /// <summary>
    ///     Gets associations for multiple runtime entities.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtIds">Object id of runtime entities</param>
    /// <param name="direction">Direction of associations to get</param>
    /// <returns></returns>
    Task<IReadOnlyList<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, IEnumerable<OctoObjectId> rtIds,
        GraphDirections direction);

    /// <summary>
    ///     Gets associations for a runtime entity of a specific role
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtId">Object id of the runtime entity</param>
    /// <param name="direction">Direction of associations to get</param>
    /// <param name="roleId">The construction kit role to get</param>
    /// <returns></returns>
    Task<IReadOnlyList<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, OctoObjectId rtId,
        GraphDirections direction, CkId<CkAssociationRoleId> roleId);

    /// <summary>
    ///     Gets associations for a runtime entity of a specific role
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtIds">Object id of runtime entities</param>
    /// <param name="direction">Direction of associations to get</param>
    /// <param name="roleId">The construction kit role to get</param>
    /// <returns></returns>
    Task<IReadOnlyList<RtAssociation>> GetRtAssociationsAsync(IOctoSession session, IEnumerable<OctoObjectId> rtIds,
        GraphDirections direction, CkId<CkAssociationRoleId> roleId);

    /// <summary>
    ///     Returns the current multiplicity of a runtime association, that means the number of associations that exist for a give runtime entity
    ///     and role
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="rtEntityId">Object id of the runtime entity</param>
    /// <param name="ckRoleId">Construction kit role id of the association</param>
    /// <param name="direction">Direction of associations to get</param>
    /// <returns></returns>
    Task<CurrentMultiplicity> GetCurrentRtAssociationMultiplicityAsync(IOctoSession session, RtEntityId rtEntityId,
        CkId<CkAssociationRoleId> ckRoleId, GraphDirections direction);

    /// <summary>
    ///     Gets an association by its origin, target and role id.
    /// </summary>
    /// <param name="session">The session object</param>
    /// <param name="originRtEntityId">Runtime id of the origin entity</param>
    /// <param name="targetRtEntityId">Runtime id of the target entity</param>
    /// <param name="ckRoleId">Construction kit role id of the association</param>
    /// <returns></returns>
    Task<RtAssociation?> GetRtAssociationOrDefaultAsync(IOctoSession session, RtEntityId originRtEntityId,
        RtEntityId targetRtEntityId,
        CkId<CkAssociationRoleId> ckRoleId);

    /// <summary>
    ///     Creates an instance of a runtime association
    /// </summary>
    /// <param name="originRtEntityId">Runtime id of the origin entity</param>
    /// <param name="ckRoleId">Construction kit role id of the association</param>
    /// <param name="targetRtEntityId">Runtime id of the target entity</param>
    /// <returns>A transient version of a role, need to be stored.</returns>
    RtAssociation CreateTransientRtAssociation(RtEntityId originRtEntityId, CkId<CkAssociationRoleId> ckRoleId,
        RtEntityId targetRtEntityId);


    #region Large Binaries

    /// <summary>
    /// Uploads a large binary file to the repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="filename">Filename of the file</param>
    /// <param name="contentType">Content type of the file</param>
    /// <param name="binaryType">Binary type of the file</param>
    /// <param name="stream">Binary stream of the file</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns></returns>
    Task<OctoObjectId> UploadLargeBinaryAsync(IOctoSession session, string filename, string contentType, BinaryType binaryType, Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a large binary file in the repository based on the large binary id
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="largeBinaryId">Object id of the large binary</param>
    /// <param name="filename">Filename of the file</param>
    /// <param name="contentType">Content type of the file</param>
    /// <param name="binaryType">Binary type of the file</param>
    /// <param name="stream">Stream of the file</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns></returns>
    Task ReplaceLargeBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId, string filename, string contentType, BinaryType binaryType,
        Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a large binary file in the repository based on the filename and binary type
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="filename">Filename of the file</param>
    /// <param name="contentType">Content type of the file</param>
    /// <param name="binaryType">Binary type of the file</param>
    /// <param name="stream">Stream of the file</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns>Object id of the large binary</returns>
    Task<OctoObjectId> ReplaceLargeBinaryAsync(IOctoSession session, string filename, string contentType, BinaryType binaryType,
        Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a large binary file from the repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="largeBinaryId">Object id of the large binary</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns></returns>
    Task DeleteLargeBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a large binary file from the repository based on the large binary id
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="largeBinaryId">Object id of the large binary</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns>Handler for the download stream</returns>
    Task<IDownloadStreamHandler> DownloadLargeBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a large binary file from the repository based on the large binary id
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="largeBinaryId">Object id of the large binary</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Binary info of the file including size, content type, etc.</returns>
    Task<IBinaryInfo?> GetLargeBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a large binary file from the repository based on the filename and binary type
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="fileName">Filename of the file</param>
    /// <param name="binaryType">Binary type of the file</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Binary info of the file including size, content type, etc.</returns>
    Task<IBinaryInfo?> GetLargeBinaryAsync(IOctoSession session, string fileName, BinaryType binaryType,
        CancellationToken cancellationToken = default);

    #endregion Large Binaries
}