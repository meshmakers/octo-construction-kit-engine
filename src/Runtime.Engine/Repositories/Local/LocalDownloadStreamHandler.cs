using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

/// <summary>
///     Handles the download of a stream from a persistent storage
/// </summary>
internal class LocalDownloadStreamHandler(Stream stream, BinaryInfo binaryInfo) : IDownloadStreamHandler
{
    public string ContentType  => binaryInfo.ContentType;
    public OctoObjectId BinaryId => binaryInfo.BinaryId;
    public string Filename => binaryInfo.Filename;
    public DateTime UploadDateTime => binaryInfo.UploadDateTime;
    public BinaryType BinaryType => binaryInfo.BinaryType;
    public long Size => binaryInfo.Size;
    public Stream Stream => stream;

    public void Dispose()
    {
        // Dispose of the stream
        stream.Dispose();
    }

    public void Close(CancellationToken cancellationToken)
    {
        stream.Close();
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        stream.Close();

        return Task.CompletedTask;
    }
}