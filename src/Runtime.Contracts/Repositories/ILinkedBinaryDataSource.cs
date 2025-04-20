using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Interface for handling large binary files in the repository.
/// </summary>
public interface ILinkedBinaryDataSource
{
    /// <summary>
    /// Uploads a large binary file to the repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="rtEntityId">Runtime entity id of the associated entity</param>
    /// <param name="filename">Filename of the file</param>
    /// <param name="contentType">Content type of the file</param>
    /// <param name="stream">Binary stream of the file</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns></returns>
    Task<OctoObjectId> UploadFileSystemBinaryAsync(IOctoSession session, RtEntityId rtEntityId, string filename, string contentType, Stream stream,
        CancellationToken cancellationToken = default);

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
    /// Replaces a large binary file in the repository based on the large binary id
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="largeBinaryId">Object id of the large binary</param>
    /// <param name="filename">Filename of the file</param>
    /// <param name="contentType">Content type of the file</param>
    /// <param name="stream">Stream of the file</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns></returns>
    Task ReplaceFileSystemBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId, string filename, string contentType,
        Stream stream, CancellationToken cancellationToken = default);

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
    /// Deletes all large binary files from the repository based on the runtime entity id
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="rtEntityId">Runtime entity id of the associated entity</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <returns></returns>
    Task DeleteAllFileSystemBinariesAsync(IOctoSession session, RtEntityId rtEntityId, CancellationToken cancellationToken = default);

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
    /// Gets a large binary file from the repository based on the large binary id
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="largeBinaryId">Object id of the large binary</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Binary info of the file including size, content type, etc.</returns>
    Task<IBinaryInfo?> GetFileSystemBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId, CancellationToken cancellationToken = default);

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
}