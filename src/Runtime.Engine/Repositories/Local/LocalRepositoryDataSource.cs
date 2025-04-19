using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Local;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Local;

internal class LocalRepositoryDataSource : RepositoryDataSource, ILocalRepositoryDataSource
{
    private readonly ICkCacheService _ckCacheService;
    private readonly string _directoryPath;
    private readonly IRtRepositorySerializer _rtSerializer;
    private readonly string _largeBinaryDirectoryPath;
    private readonly IDataSourceCollection<OctoObjectId, BinaryInfo> _largeBinaries;

    public LocalRepositoryDataSource(string tenantId, string directoryPath, ICkCacheService ckCacheService,
        IRtRepositorySerializer rtSerializer)
        : base(tenantId)
    {
        _ckCacheService = ckCacheService;
        _rtSerializer = rtSerializer;
        _directoryPath = directoryPath;
        _largeBinaryDirectoryPath = Path.Combine(directoryPath, "largeBinaries");

        RtAssociations = new LocalDataSourceCollection<OctoObjectId, RtAssociation, RtAssociationDto>(TenantId,
            Path.Combine(_directoryPath, "associations.json"),
            new RtAssociationDataSourceMapper(TenantId, _ckCacheService, _rtSerializer));

        _largeBinaries = new LocalDataSourceCollection<OctoObjectId, BinaryInfo, BinaryInfoDto>(TenantId,
            Path.Combine(_directoryPath, "largeBinaries.json"),
            new BinaryInfoDataSourceMapper(TenantId, _rtSerializer));
    }

    public override IDataSourceCollection<OctoObjectId, TEntity> GetRtCollection<TEntity>(CkTypeGraph ckTypeGraph)
    {
        var suffix = ckTypeGraph.CkTypeId.SemanticVersionedFullName.Replace("/", "_");

        var filePath = Path.Combine(_directoryPath, suffix + ".json");

        var dataSourceMapper = new RtEntityDataSourceMapper<TEntity>(TenantId, _ckCacheService, _rtSerializer);

        return new LocalDataSourceCollection<OctoObjectId, TEntity, RtEntityDto>(TenantId, filePath, dataSourceMapper);
    }

    public override IDataSourceCollection<OctoObjectId, RtAssociation> RtAssociations { get; }

    public override async Task<CurrentMultiplicity> GetCurrentRtAssociationMultiplicityAsync(IOctoSession session,
        RtEntityId rtEntityId,
        CkId<CkAssociationRoleId> ckRoleId, GraphDirections direction)
    {
        long counter = 0;
        var queryable = await RtAssociations.AsQueryableAsync(session).ConfigureAwait(false);

        if (direction == GraphDirections.Inbound || direction == GraphDirections.Any)
        {
            var r = queryable.Count(a =>
                a.TargetRtId == rtEntityId.RtId && a.TargetCkTypeId == rtEntityId.CkTypeId &&
                a.AssociationRoleId == ckRoleId);
            counter = Math.Max(r, counter);
        }

        if (direction == GraphDirections.Outbound || direction == GraphDirections.Any)
        {
            var r = queryable.Count(a =>
                a.OriginRtId == rtEntityId.RtId && a.OriginCkTypeId == rtEntityId.CkTypeId &&
                a.AssociationRoleId == ckRoleId);
            counter = Math.Max(r, counter);
        }

        if (counter >= 2)
        {
            return CurrentMultiplicity.Many;
        }

        if (counter == 1)
        {
            return CurrentMultiplicity.One;
        }

        return CurrentMultiplicity.Zero;
    }

    public override async Task<OctoObjectId> UploadLargeBinaryAsync(IOctoSession session, string filename,
        string contentType, BinaryType binaryType, Stream stream,
        CancellationToken cancellationToken = default)
    {
        EnsureLargeBinaryDirectory();

        var largeBinaryId = OctoObjectId.GenerateNewId();
        var filePath = GetFilePath(largeBinaryId);
        var size = stream.Length;

        await SaveFileAsync(stream, cancellationToken, filePath).ConfigureAwait(false);

        var binaryInfo = new BinaryInfo
        {
            BinaryId = largeBinaryId,
            Filename = filename,
            ContentType = contentType,
            BinaryType = binaryType,
            Size = size
        };

        await _largeBinaries.InsertOneAsync(session, binaryInfo).ConfigureAwait(false);

        return largeBinaryId;
    }

    public override async Task ReplaceLargeBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
        string filename, string contentType, BinaryType binaryType,
        Stream stream, CancellationToken cancellationToken = default)
    {
        EnsureLargeBinaryDirectory();

        var filePath = GetFilePath(largeBinaryId);
        var size = stream.Length;
        await SaveFileAsync(stream, cancellationToken, filePath).ConfigureAwait(false);

        var binaryInfo = new BinaryInfo
        {
            BinaryId = largeBinaryId,
            Filename = filename,
            ContentType = contentType,
            BinaryType = binaryType,
            Size = size
        };

        await _largeBinaries.ReplaceByIdAsync(session, largeBinaryId, binaryInfo).ConfigureAwait(false);
    }

    public override async Task<OctoObjectId> ReplaceLargeBinaryAsync(IOctoSession session, string filename,
        string contentType, BinaryType binaryType, Stream stream,
        CancellationToken cancellationToken = default)
    {
        EnsureLargeBinaryDirectory();

        var binaryInfo = await _largeBinaries
            .FindSingleOrDefaultAsync(session, info => info.Filename == filename && info.BinaryType == binaryType)
            .ConfigureAwait(false);
        if (binaryInfo == null)
        {
            throw RuntimeRepositoryException.BinaryWithFilenameNotFound(filename, binaryType);
        }

        binaryInfo.ContentType = contentType;
        binaryInfo.Size = stream.Length;

        var filePath = GetFilePath(binaryInfo.BinaryId);
        await SaveFileAsync(stream, cancellationToken, filePath).ConfigureAwait(false);

        await _largeBinaries.ReplaceByIdAsync(session, binaryInfo.BinaryId, binaryInfo).ConfigureAwait(false);

        return binaryInfo.BinaryId;
    }

    public override async Task DeleteLargeBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
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

    public override async Task<IDownloadStreamHandler> DownloadLargeBinaryAsync(IOctoSession session,
        OctoObjectId largeBinaryId, CancellationToken cancellationToken = default)
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

    public override async Task<IBinaryInfo?> GetLargeBinaryAsync(IOctoSession session, OctoObjectId largeBinaryId,
        CancellationToken cancellationToken = default)
    {
        EnsureLargeBinaryDirectory();

        var binaryInfo = await _largeBinaries.DocumentAsync(session, largeBinaryId).ConfigureAwait(false);
        return binaryInfo;
    }

    public override async Task<IBinaryInfo?> GetLargeBinaryAsync(IOctoSession session, string fileName, BinaryType binaryType,
        CancellationToken cancellationToken = default)
    {
        EnsureLargeBinaryDirectory();

        var binaryInfo = await _largeBinaries
            .FindSingleOrDefaultAsync(session, info => info.Filename == fileName && info.BinaryType == binaryType)
            .ConfigureAwait(false);

        return binaryInfo;
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