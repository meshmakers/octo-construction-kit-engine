using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Basic implementation of the <see cref="ILinkedBinaryDataSource"/> interface.
/// </summary>
public abstract class LinkedBinaryDataSource : ILinkedBinaryDataSource
{
    /// <inheritdoc />
    public Task<OctoObjectId> UploadFileSystemBinaryAsync(IOctoSession session, RtEntityId rtEntityId, string filename,
        string contentType,
        Stream stream, CancellationToken cancellationToken = default)
    {
        return UploadLargeBinaryAsync(session, filename, contentType, BinaryType.FileSystem, rtEntityId, null, stream,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OctoObjectId> UploadTemporaryBinaryAsync(IOctoSession session, string filename, string contentType,
        DateTime expiryDateTime,
        Stream stream, CancellationToken cancellationToken = default)
    {
        return UploadLargeBinaryAsync(session, filename, contentType, BinaryType.Temporary, null, expiryDateTime,
            stream, cancellationToken);
    }

    /// <inheritdoc />
    public Task ReplaceFileSystemBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId, string filename,
        string contentType,
        Stream stream, CancellationToken cancellationToken = default)
    {
        return ReplaceLargeBinaryAsync(session, filename, contentType, BinaryType.FileSystem, largeBinaryId, stream,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OctoObjectId> ReplaceTemporaryLargeBinaryAsync(IOctoSession session, string filename,
        string contentType, Stream stream,
        CancellationToken cancellationToken = default)
    {
        return ReplaceLargeBinaryAsync(session, filename, contentType, BinaryType.Temporary, null, stream,
            cancellationToken);
    }

    /// <inheritdoc />
    public abstract Task DeleteAllFileSystemBinariesAsync(IOctoSession session, RtEntityId rtEntityId,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task DeleteTemporaryLargeBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IDownloadStreamHandler> DownloadBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IBinaryInfo?> GetFileSystemBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public Task<IBinaryInfo?> GetTemporaryBinaryAsync(IOctoSession session, OctoObjectId binaryId,
        CancellationToken cancellationToken = default)
    {
        return GetFileSystemBinaryAsync(session, binaryId, cancellationToken);
    }

    /// <inheritdoc />
    public abstract Task<IBinaryInfo?> GetTemporaryBinaryAsync(IOctoSession session, string fileName,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task DeleteExpiredTemporaryLargeBinariesAsync(IOctoSession session, DateTime expiryDateTime,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    public abstract Task
        DeleteAllTemporaryLargeBinariesAsync(IOctoSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Uploads a large binary file to the repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="filename">Filename of the file</param>
    /// <param name="contentType">Content type of the file</param>
    /// <param name="binaryType">Binary type of the file</param>
    /// <param name="rtEntityId">Associated runtime entity id</param>
    /// <param name="expiryDateTime">>Expiry date time of the file</param>
    /// <param name="stream">Stream of the file</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns></returns>
    protected abstract Task<OctoObjectId> UploadLargeBinaryAsync(IOctoSession session,
        string filename, string contentType, BinaryType binaryType, RtEntityId? rtEntityId, DateTime? expiryDateTime,
        Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a large binary file in the repository
    /// </summary>
    /// <param name="session">Session object for transaction handling</param>
    /// <param name="filename">Filename of the file</param>
    /// <param name="contentType">Content type of the file</param>
    /// <param name="binaryType">Binary type of the file</param>
    /// <param name="binaryId">Binary id of the file</param>
    /// <param name="stream">Stream of the file</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns></returns>
    protected abstract Task<OctoObjectId> ReplaceLargeBinaryAsync(IOctoSession session,
        string filename, string contentType, BinaryType binaryType, OctoObjectId? binaryId,
        Stream stream, CancellationToken cancellationToken = default);
}