using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

internal class LocalLinkedBinaryDataSource(string tenantId, string directoryPath, IRtRepositorySerializer rtSerializer)
    : LinkedBinaryDataSource
{
    private readonly string _largeBinaryDirectoryPath = Path.Combine(directoryPath, "largeBinaries");

    private readonly IDataSourceCollection<OctoObjectId, BinaryInfo> _largeBinaries =
        new LocalDataSourceCollection<OctoObjectId, BinaryInfo, BinaryInfoTcDto>(tenantId,
            Path.Combine(directoryPath, "largeBinaries.json"),
            new BinaryInfoDataSourceMapper(tenantId, rtSerializer));

    public override async Task DeleteAllFileSystemBinariesAsync(IOctoSession session, RtEntityId rtEntityId,
        CancellationToken cancellationToken = default)
    {
        EnsureLargeBinaryDirectory();

        var binaryInfos = await _largeBinaries
            .FindManyAsync(session,
                info => info.RtEntityId == rtEntityId &&
                        info.BinaryType == BinaryType.FileSystem)
            .ConfigureAwait(false);


        foreach (var binaryInfo in binaryInfos)
        {
            var filePath = GetFilePath(binaryInfo.BinaryId);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            await _largeBinaries.DeleteOneAsync(session, binaryInfo.BinaryId).ConfigureAwait(false);
        }
    }

    public override async Task DeleteTemporaryLargeBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default)
    {
        EnsureLargeBinaryDirectory();

        var binaryInfo = await _largeBinaries.DocumentAsync(session, largeBinaryId).ConfigureAwait(false);
        if (binaryInfo == null)
        {
            throw RuntimeRepositoryException.BinaryWithIdNotFound(largeBinaryId);
        }

        var filePath = GetFilePath(largeBinaryId);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        await _largeBinaries.DeleteOneAsync(session, largeBinaryId).ConfigureAwait(false);
    }

    public override async Task<IDownloadStreamHandler> DownloadBinaryAsync(IOctoSession session,
        OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default)
    {
        EnsureLargeBinaryDirectory();

        var binaryInfo = await _largeBinaries.DocumentAsync(session, largeBinaryId).ConfigureAwait(false);

        if (binaryInfo == null)
        {
            throw RuntimeRepositoryException.BinaryWithIdNotFound(largeBinaryId);
        }

        var filePath = GetFilePath(largeBinaryId);

        if (File.Exists(filePath))
        {
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var downloadStreamHandler = new LocalDownloadStreamHandler(stream, binaryInfo);
            return downloadStreamHandler;
        }

        throw RuntimeRepositoryException.BinaryContentWithIdNotFound(largeBinaryId);
    }

    public override async Task<IBinaryInfo?> GetFileSystemBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default)
    {
        EnsureLargeBinaryDirectory();

        var binaryInfo = await _largeBinaries.DocumentAsync(session, largeBinaryId).ConfigureAwait(false);
        return binaryInfo;
    }

    public override async Task<IBinaryInfo?> GetTemporaryBinaryAsync(IOctoSession session, string fileName,
        CancellationToken cancellationToken = default)
    {
        EnsureLargeBinaryDirectory();

        var binaryInfo = await _largeBinaries
            .FindSingleOrDefaultAsync(session,
                info => info.Filename == fileName && info.BinaryType == BinaryType.Temporary)
            .ConfigureAwait(false);

        return binaryInfo;
    }

    public override async Task DeleteExpiredTemporaryLargeBinariesAsync(IOctoSession session, DateTime expiryDateTime,
        CancellationToken cancellationToken)
    {
        EnsureLargeBinaryDirectory();

        var binaryInfos = await _largeBinaries
            .FindManyAsync(session,
                info => info.ExpiryDateTime < expiryDateTime && info.BinaryType == BinaryType.Temporary)
            .ConfigureAwait(false);

        foreach (var binaryInfo in binaryInfos)
        {
            var filePath = GetFilePath(binaryInfo.BinaryId);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            await _largeBinaries.DeleteOneAsync(session, binaryInfo.BinaryId).ConfigureAwait(false);
        }
    }

    public override async Task DeleteAllTemporaryLargeBinariesAsync(IOctoSession session,
        CancellationToken cancellationToken)
    {
        EnsureLargeBinaryDirectory();

        var binaryInfos = await _largeBinaries
            .FindManyAsync(session, info => info.BinaryType == BinaryType.Temporary)
            .ConfigureAwait(false);

        foreach (var binaryInfo in binaryInfos)
        {
            var filePath = GetFilePath(binaryInfo.BinaryId);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            await _largeBinaries.DeleteOneAsync(session, binaryInfo.BinaryId).ConfigureAwait(false);
        }
    }

    protected override async Task<OctoObjectId> UploadLargeBinaryAsync(IOctoSession session, string filename,
        string contentType, BinaryType binaryType,
        RtEntityId? rtEntityId, DateTime? expiryDateTime, Stream stream, CancellationToken cancellationToken = default)
    {
        EnsureLargeBinaryDirectory();

        var largeBinaryId = OctoObjectId.GenerateNewId();
        var filePath = GetFilePath(largeBinaryId);
        var size = stream.Length;

        await SaveFileAsync(stream, cancellationToken, filePath).ConfigureAwait(false);

        var binaryInfo = new BinaryInfo
        {
            BinaryId = largeBinaryId,
            RtEntityId = rtEntityId,
            Filename = filename,
            ContentType = contentType,
            BinaryType = BinaryType.FileSystem,
            UploadDateTime = DateTime.UtcNow,
            ExpiryDateTime = expiryDateTime,
            Size = size
        };

        await _largeBinaries.InsertOneAsync(session, binaryInfo).ConfigureAwait(false);

        return largeBinaryId;
    }

    protected override async Task<OctoObjectId> ReplaceLargeBinaryAsync(IOctoSession session, string filename,
        string contentType, BinaryType binaryType,
        OctoObjectId? binaryId, Stream stream, CancellationToken cancellationToken = default)
    {
        EnsureLargeBinaryDirectory();

        BinaryInfo? binaryInfo;
        if (binaryId == null)
        {
            binaryInfo = await _largeBinaries
                .FindSingleOrDefaultAsync(session,
                    info => info.Filename == filename &&
                            info.BinaryType == binaryType)
                .ConfigureAwait(false);
            if (binaryInfo == null)
            {
                throw RuntimeRepositoryException.BinaryWithFilenameNotFound(filename, binaryType);
            }

            binaryId = binaryInfo.BinaryId;
        }
        else
        {
            binaryInfo = await _largeBinaries
                .DocumentAsync(session, binaryId.Value)
                .ConfigureAwait(false);
            if (binaryInfo == null)
            {
                throw RuntimeRepositoryException.BinaryWithIdNotFound(binaryId.Value);
            }

            binaryId = binaryInfo.BinaryId;
        }

        var filePath = GetFilePath(binaryId.Value);
        var size = stream.Length;
        await SaveFileAsync(stream, cancellationToken, filePath).ConfigureAwait(false);

        binaryInfo.Filename = filename;
        binaryInfo.ContentType = contentType;
        binaryInfo.Size = size;
        binaryInfo.UploadDateTime = DateTime.UtcNow;
        binaryInfo.BinaryType = binaryType;

        await _largeBinaries.ReplaceByIdAsync(session, binaryId.Value, binaryInfo).ConfigureAwait(false);

        return binaryId.Value;
    }

    private void EnsureLargeBinaryDirectory()
    {
        if (!Directory.Exists(_largeBinaryDirectoryPath))
        {
            Directory.CreateDirectory(_largeBinaryDirectoryPath);
        }
    }

    private string GetFilePath(OctoObjectId largeBinaryId)
    {
        var filePath = Path.Combine(_largeBinaryDirectoryPath, largeBinaryId.ToString());

        return filePath;
    }

    private static async Task SaveFileAsync(Stream stream, CancellationToken cancellationToken, string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Exists)
        {
            fileInfo.Delete();
        }

        using var fileStream = fileInfo.Create();
        await stream.CopyToAsync(fileStream).ConfigureAwait(false);
        await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        fileStream.Close();
        stream.Close();
        stream.Dispose();
    }
}