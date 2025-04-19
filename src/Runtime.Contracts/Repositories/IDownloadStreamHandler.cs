using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     Handles the download of a stream from a persistent storage
/// </summary>
public interface IDownloadStreamHandler : IBinaryInfo, IDisposable
{
    /// <summary>
    ///     Returns the stream
    /// </summary>
    Stream Stream { get; }

    /// <summary>
    ///     Closes the GridFS stream.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    void Close(CancellationToken cancellationToken);

    /// <summary>
    ///     Closes the GridFS stream.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A Task.</returns>
    Task CloseAsync(CancellationToken cancellationToken = default);
}