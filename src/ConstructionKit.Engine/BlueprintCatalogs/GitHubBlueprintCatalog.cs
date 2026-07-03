using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.Options;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
/// Public catalog on GitHub for blueprints
/// </summary>
public class PublicGitHubBlueprintCatalog(
    IBlueprintSerializer blueprintSerializer,
    IHttpClientFactory httpClientFactory,
    IGitHubClientFactory gitHubClientFactory,
    IOptions<PublicGitHubBlueprintCatalogOptions> gitHubOptions) : GitHubBlueprintCatalog(blueprintSerializer,
    httpClientFactory,
    gitHubClientFactory,
    gitHubOptions.Value, 20, "PublicGitHubBlueprintCatalog", "Public GitHub blueprint catalog");

/// <summary>
/// Private catalog on GitHub for blueprints
/// </summary>
public class PrivateGitHubBlueprintCatalog(
    IBlueprintSerializer blueprintSerializer,
    IHttpClientFactory httpClientFactory,
    IGitHubClientFactory gitHubClientFactory,
    IOptions<PrivateGitHubBlueprintCatalogOptions> gitHubOptions) : GitHubBlueprintCatalog(blueprintSerializer,
    httpClientFactory,
    gitHubClientFactory,
    gitHubOptions.Value, 21, "PrivateGitHubBlueprintCatalog",
    "Private GitHub blueprint catalog for development and testing");

/// <summary>
/// Blueprint catalog for GitHub base class.
/// Provides access to blueprints hosted on GitHub.
/// </summary>
public abstract class GitHubBlueprintCatalog : CachedBlueprintCatalog
{
    private const string RootPath = "blueprints/v1/";
    private const string CatalogFileName = "catalog.json";
    private const string BlueprintMetaFileName = "blueprint.yaml";
    private const int MaxCacheFileAgeSeconds = 60;

    private readonly IBlueprintSerializer _blueprintSerializer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGitHubClientFactory _gitHubClientFactory;
    private readonly GitHubBlueprintCatalogOptions _gitHubOptions;
    private readonly bool _isGitHubClientAvailable;

    /// <summary>
    /// Creates a new instance of the <see cref="GitHubBlueprintCatalog"/> class.
    /// </summary>
    protected GitHubBlueprintCatalog(
        IBlueprintSerializer blueprintSerializer,
        IHttpClientFactory httpClientFactory,
        IGitHubClientFactory gitHubClientFactory,
        GitHubBlueprintCatalogOptions gitHubOptions,
        int order,
        string catalogName,
        string description) : base(order, catalogName, description, true, true, gitHubOptions)
    {
        _blueprintSerializer = blueprintSerializer;
        _httpClientFactory = httpClientFactory;
        _gitHubClientFactory = gitHubClientFactory;
        _gitHubOptions = gitHubOptions;

        _isGitHubClientAvailable = !string.IsNullOrWhiteSpace(_gitHubOptions.GitHubApiToken);
    }

    /// <inheritdoc />
    public override bool IsSupportingSourceIdentifier(object? sourceIdentifier = null)
    {
        return sourceIdentifier == null;
    }

    /// <inheritdoc />
    public override async Task<BlueprintMetaRootDto> GetAsync(
        BlueprintId blueprintId,
        OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var blueprintPath = CreateBlueprintMetaPath(blueprintId);
        var httpClient = CreateHttpClient();

        try
        {
            var response = await httpClient.GetAsync(blueprintPath, cancellationToken ?? CancellationToken.None)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
#if NETSTANDARD2_0
                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
                await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
                var blueprintMeta = await _blueprintSerializer
                    .DeserializeBlueprintMetaAsync(stream, blueprintPath, operationResult)
                    .ConfigureAwait(false);

                if (operationResult.HasErrors)
                {
                    throw BlueprintCatalogException.ErrorDuringBlueprintLoad(blueprintId, CatalogName, operationResult);
                }

                return blueprintMeta;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw BlueprintCatalogException.BlueprintNotFound(blueprintId);
            }

            throw BlueprintCatalogException.InvalidGitHubRepository(CatalogName, _gitHubOptions.GitHubPagesUri);
        }
        catch (HttpRequestException)
        {
            throw BlueprintCatalogException.InvalidGitHubRepository(CatalogName, _gitHubOptions.GitHubPagesUri);
        }
        catch (TaskCanceledException)
        {
            throw BlueprintCatalogException.RequestTimeout(CatalogName, _gitHubOptions.GitHubPagesUri);
        }
    }

    /// <inheritdoc />
    public override async Task<Stream> OpenBlueprintFileAsync(BlueprintId blueprintId, string relativePath,
        object? sourceIdentifier = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanRead)
        {
            throw BlueprintCatalogException.CatalogCannotRead(CatalogName);
        }

        var normalised = ValidateBlueprintRelativePath(relativePath);
        var blueprintDirectoryPath = CreateBlueprintDirectoryPath(blueprintId);
        var fileRequestPath = $"{blueprintDirectoryPath}/{normalised}";

        var httpClient = CreateHttpClient();

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(fileRequestPath, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw BlueprintCatalogException.InvalidGitHubRepository(CatalogName, _gitHubOptions.GitHubPagesUri);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw BlueprintCatalogException.RequestTimeout(CatalogName, _gitHubOptions.GitHubPagesUri);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            response.Dispose();
            throw BlueprintCatalogException.BlueprintFileNotFound(blueprintId, CatalogName, normalised);
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw BlueprintCatalogException.InvalidGitHubRepository(CatalogName, _gitHubOptions.GitHubPagesUri);
        }

        // The returned stream is owned by the caller; the HttpResponseMessage stays alive as long as
        // the stream is, so wrap them together in a small bridge that disposes the response when the
        // stream is disposed.
        var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return new HttpResponseOwnedStream(response, contentStream);
    }

    /// <inheritdoc />
    [Obsolete("Use OpenBlueprintFileAsync.")]
    public override string GetBlueprintPath(BlueprintId blueprintId, object? sourceIdentifier = null)
    {
        var baseUri = _gitHubOptions.GitHubPagesUri.TrimEnd('/');
        return $"{baseUri}/{CreateBlueprintDirectoryPath(blueprintId)}";
    }

    /// <summary>
    ///     Wraps the content stream of an <see cref="HttpResponseMessage" /> so the response is disposed
    ///     when the consumer disposes the stream — this avoids "response disposed before stream read"
    ///     bugs at call sites that only see the stream.
    /// </summary>
    private sealed class HttpResponseOwnedStream : Stream
    {
        private readonly HttpResponseMessage _response;
        private readonly Stream _inner;

        public HttpResponseOwnedStream(HttpResponseMessage response, Stream inner)
        {
            _response = response;
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => _inner.ReadAsync(buffer, offset, count, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <inheritdoc />
    public override async Task PublishAsync(
        BlueprintMetaRootDto blueprintMetaRoot,
        string blueprintDirectory,
        bool force = false,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var gitHubClient = CreateGitHubClient();
        var blueprintId = blueprintMetaRoot.BlueprintId;

        cancellationToken?.ThrowIfCancellationRequested();

        try
        {
            // Upload all blueprint files to GitHub
            await UploadBlueprintFilesAsync(blueprintId, blueprintDirectory, force, gitHubClient, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken?.ThrowIfCancellationRequested();

            // Update the major version catalog
            await UpdateBlueprintVersionsCatalogAsync(blueprintId, blueprintMetaRoot.Description, gitHubClient)
                .ConfigureAwait(false);

            cancellationToken?.ThrowIfCancellationRequested();

            // Update the overall blueprint library catalog
            await UpdateBlueprintLibraryCatalogAsync(blueprintId, gitHubClient)
                .ConfigureAwait(false);

            // Update the root catalog
            await UpdateRootCatalogAsync(blueprintId, gitHubClient).ConfigureAwait(false);

            cancellationToken?.ThrowIfCancellationRequested();

            // Refresh the in-memory catalog
            await RefreshCatalogAsync(true).ConfigureAwait(false);
        }
        catch (ApiValidationException e)
        {
            throw BlueprintCatalogException.PublishFailed(blueprintId, CatalogName, e);
        }
    }

    /// <inheritdoc />
    public override async Task UnpublishAsync(BlueprintId blueprintId, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var gitHubClient = CreateGitHubClient();

        cancellationToken?.ThrowIfCancellationRequested();

        try
        {
            // Inverse of PublishAsync, but in the safe order: prune the version out of the index FIRST
            // (cascading up to remove now-empty major-version / blueprint / root entries), THEN delete the
            // blueprint's files. A partial failure between the two therefore leaves harmless orphan blobs
            // that a re-run cleans up — never a live index entry pointing at files that are already gone
            // (which would 404 a concurrent Get / install).
            await PruneVersionsCatalogAsync(blueprintId, gitHubClient).ConfigureAwait(false);
            await DeleteBlueprintFilesAsync(blueprintId, gitHubClient, cancellationToken).ConfigureAwait(false);

            await RefreshCatalogAsync(true).ConfigureAwait(false);
        }
        catch (ApiException e)
        {
            throw BlueprintCatalogException.UnpublishFailed(blueprintId, CatalogName, e);
        }
    }

    /// <inheritdoc />
    public override async Task UnpublishAllVersionsAsync(string blueprintName, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var gitHubClient = CreateGitHubClient();

        cancellationToken?.ThrowIfCancellationRequested();

        try
        {
            // Enumerate every published version from the index (source of truth), then unpublish each.
            // Removing the last version cascades the major / blueprint / root entries away.
            var versions = await GetAllPublishedVersionsAsync(blueprintName, gitHubClient).ConfigureAwait(false);
            foreach (var blueprintId in versions)
            {
                cancellationToken?.ThrowIfCancellationRequested();
                // Prune the index entry before deleting files (see UnpublishAsync) so a partial failure
                // leaves orphan blobs rather than dangling index pointers.
                await PruneVersionsCatalogAsync(blueprintId, gitHubClient).ConfigureAwait(false);
                await DeleteBlueprintFilesAsync(blueprintId, gitHubClient, cancellationToken).ConfigureAwait(false);
            }

            await RefreshCatalogAsync(true).ConfigureAwait(false);
        }
        catch (ApiException e)
        {
            throw BlueprintCatalogException.UnpublishAllFailed(blueprintName, CatalogName, e);
        }
    }

    private async Task DeleteBlueprintFilesAsync(BlueprintId blueprintId, IGitHubClientWrapper gitHubClient,
        CancellationToken? cancellationToken)
    {
        var directoryPath = CreateBlueprintDirectoryPath(blueprintId);
        var files = await gitHubClient.ListFilesRecursiveAsync(directoryPath).ConfigureAwait(false);

        foreach (var (path, sha) in files)
        {
            cancellationToken?.ThrowIfCancellationRequested();
            await gitHubClient.DeleteFileAsync(path, $"Remove {path} for {blueprintId.FullName}", sha)
                .ConfigureAwait(false);
        }
    }

    private async Task<List<BlueprintId>> GetAllPublishedVersionsAsync(string blueprintName,
        IGitHubClientWrapper gitHubClient)
    {
        var result = new List<BlueprintId>();

        var libraryCatalogPath = $"{RootPath}{blueprintName[0].ToString().ToLower()}/{blueprintName}/{CatalogFileName}";
        var libraryCatalog = await GetBlueprintLibraryCatalogWithClientAsync(libraryCatalogPath, gitHubClient)
            .ConfigureAwait(false);
        if (libraryCatalog == null)
        {
            return result;
        }

        foreach (var majorEntry in libraryCatalog.MajorVersions)
        {
            var versionsCatalog = await GetBlueprintLibraryVersionsCatalogWithClientAsync(
                blueprintName, majorEntry.MajorVersion, gitHubClient).ConfigureAwait(false);
            if (versionsCatalog == null)
            {
                continue;
            }

            foreach (var versionEntry in versionsCatalog.Versions)
            {
                result.Add(new BlueprintId(blueprintName, versionEntry.Version));
            }
        }

        return result;
    }

    private async Task PruneVersionsCatalogAsync(BlueprintId blueprintId, IGitHubClientWrapper gitHubClient)
    {
        var catalogPath =
            $"{RootPath}{blueprintId.Name[0].ToString().ToLower()}/{blueprintId.Name}/{blueprintId.Version.Major}/{CatalogFileName}";

        var catalogData = await GetBlueprintLibraryVersionsCatalogWithClientAsync(
                blueprintId.Name, blueprintId.Version.Major, gitHubClient)
            .ConfigureAwait(false);
        if (catalogData == null)
        {
            return;
        }

        var currentVersionString = blueprintId.Version.ToString();
        if (catalogData.Versions.RemoveAll(v => v.Version == currentVersionString) == 0)
        {
            return;
        }

        var existing = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);

        if (catalogData.Versions.Count == 0)
        {
            // Last version of this major gone: delete the versions catalog and cascade up.
            if (existing.HasValue)
            {
                await gitHubClient.DeleteFileAsync(catalogPath,
                        $"Remove versions catalog for {blueprintId.Name} v{blueprintId.Version.Major}",
                        existing.Value.Item2)
                    .ConfigureAwait(false);
            }

            await PruneLibraryCatalogAsync(blueprintId, gitHubClient).ConfigureAwait(false);
            return;
        }

        var sortedVersions = catalogData.Versions
            .OrderByDescending(v => new CkVersion(v.Version))
            .ToList();
        catalogData.LatestVersion = sortedVersions.FirstOrDefault()?.Version;
        catalogData.UpdatedAt = DateTime.UtcNow;

        if (existing.HasValue)
        {
            await gitHubClient.UpdateFileAsync(catalogPath,
                    $"Update catalog for {blueprintId.Name} v{blueprintId.Version.Major}",
                    SerializeCatalog(catalogData), existing.Value.Item2)
                .ConfigureAwait(false);
        }
    }

    private async Task PruneLibraryCatalogAsync(BlueprintId blueprintId, IGitHubClientWrapper gitHubClient)
    {
        var catalogPath = $"{RootPath}{blueprintId.Name[0].ToString().ToLower()}/{blueprintId.Name}/{CatalogFileName}";

        var catalogData = await GetBlueprintLibraryCatalogWithClientAsync(catalogPath, gitHubClient)
            .ConfigureAwait(false);
        if (catalogData == null)
        {
            return;
        }

        if (catalogData.MajorVersions.RemoveAll(m => m.MajorVersion == blueprintId.Version.Major) == 0)
        {
            return;
        }

        var existing = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);

        if (catalogData.MajorVersions.Count == 0)
        {
            // Last major of this blueprint gone: delete the library catalog and cascade to the root.
            if (existing.HasValue)
            {
                await gitHubClient.DeleteFileAsync(catalogPath,
                        $"Remove blueprint catalog for {blueprintId.Name}", existing.Value.Item2)
                    .ConfigureAwait(false);
            }

            await PruneRootCatalogAsync(blueprintId.Name, gitHubClient).ConfigureAwait(false);
            return;
        }

        catalogData.UpdatedAt = DateTime.UtcNow;

        if (existing.HasValue)
        {
            await gitHubClient.UpdateFileAsync(catalogPath,
                    $"Update blueprint catalog for {blueprintId.Name}",
                    SerializeCatalog(catalogData), existing.Value.Item2)
                .ConfigureAwait(false);
        }
    }

    private async Task PruneRootCatalogAsync(string blueprintName, IGitHubClientWrapper gitHubClient)
    {
        var catalogPath = $"{RootPath}{CatalogFileName}";

        var catalogData = await GetRootCatalogWithClientAsync(gitHubClient).ConfigureAwait(false);
        if (catalogData == null)
        {
            return;
        }

        if (catalogData.Blueprints.RemoveAll(b => b.BlueprintName == blueprintName) == 0)
        {
            return;
        }

        catalogData.UpdatedAt = DateTime.UtcNow;

        var existing = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
        if (existing.HasValue)
        {
            await gitHubClient.UpdateFileAsync(catalogPath,
                    $"Update blueprint catalog after removing {blueprintName}",
                    SerializeCatalog(catalogData), existing.Value.Item2)
                .ConfigureAwait(false);
        }
    }

    private static string SerializeCatalog<T>(T catalogData)
    {
        return System.Text.Json.JsonSerializer.Serialize(catalogData,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
    }

    private async Task UploadBlueprintFilesAsync(
        BlueprintId blueprintId,
        string blueprintDirectory,
        bool force,
        IGitHubClientWrapper gitHubClient,
        CancellationToken? cancellationToken)
    {
        // Get all files in the blueprint directory
        var files = Directory.GetFiles(blueprintDirectory, "*", SearchOption.AllDirectories);

        foreach (var filePath in files)
        {
            cancellationToken?.ThrowIfCancellationRequested();

            var relativePath = GetRelativePath(blueprintDirectory, filePath);
            var gitHubPath = $"{CreateBlueprintDirectoryPath(blueprintId)}/{relativePath.Replace('\\', '/')}";

#if NETSTANDARD2_0
            var content = File.ReadAllText(filePath);
#else
            var content = await File.ReadAllTextAsync(filePath, cancellationToken ?? CancellationToken.None)
                .ConfigureAwait(false);
#endif

            var existing = await gitHubClient.GetFileAsync(gitHubPath).ConfigureAwait(false);
            if (existing.HasValue)
            {
                if (!force)
                {
                    throw BlueprintCatalogException.BlueprintAlreadyExistsInCatalog(blueprintId, CatalogName);
                }

                await gitHubClient.UpdateFileAsync(gitHubPath, $"Update {relativePath} for {blueprintId.FullName}",
                        content, existing.Value.Item2)
                    .ConfigureAwait(false);
            }
            else
            {
                await gitHubClient.CreateFileAsync(gitHubPath, $"Add {relativePath} for {blueprintId.FullName}", content)
                    .ConfigureAwait(false);
            }
        }
    }

    private static string GetRelativePath(string basePath, string fullPath)
    {
#if NETSTANDARD2_0
        // Manual implementation for netstandard2.0
        var baseUri = new Uri(basePath.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? basePath
            : basePath + Path.DirectorySeparatorChar);
        var fullUri = new Uri(fullPath);
        var relativeUri = baseUri.MakeRelativeUri(fullUri);
        return Uri.UnescapeDataString(relativeUri.ToString().Replace('/', Path.DirectorySeparatorChar));
#else
        return Path.GetRelativePath(basePath, fullPath);
#endif
    }

    private IGitHubClientWrapper CreateGitHubClient()
    {
        if (!_isGitHubClientAvailable)
        {
            throw BlueprintCatalogException.GitHubTokenMissing();
        }

        return _gitHubClientFactory.CreateClient(_gitHubOptions);
    }

    private async Task UpdateBlueprintVersionsCatalogAsync(
        BlueprintId blueprintId,
        string? description,
        IGitHubClientWrapper gitHubClient)
    {
        var catalogPath =
            $"{RootPath}{blueprintId.Name[0].ToString().ToLower()}/{blueprintId.Name}/{blueprintId.Version.Major}/{CatalogFileName}";

        var catalogData = await GetBlueprintLibraryVersionsCatalogWithClientAsync(
                blueprintId.Name, blueprintId.Version.Major, gitHubClient)
            .ConfigureAwait(false);

        var isNew = catalogData == null;
        var isModified = false;

        catalogData ??= new SharedBlueprintCatalogTypes.BlueprintLibraryVersionsCatalog
        {
            BlueprintId = blueprintId.Name,
            MajorVersion = blueprintId.Version.Major,
            Versions = []
        };

        if (catalogData.Description != description)
        {
            isModified = true;
            catalogData.Description = description;
        }

        var versionDict = catalogData.Versions.ToDictionary(k => k.Version, v => v);
        var currentVersionString = blueprintId.Version.ToString();

        if (!versionDict.ContainsKey(currentVersionString))
        {
            var directoryPath = CreateBlueprintDirectoryPath(blueprintId);

            versionDict[currentVersionString] = new SharedBlueprintCatalogTypes.BlueprintLibraryVersionsCatalogEntry
            {
                Version = currentVersionString,
                DirectoryPath = directoryPath,
                PublishedAt = DateTime.UtcNow
            };

            catalogData.Versions.Clear();
            catalogData.Versions.AddRange(versionDict.Values.OrderBy(v => v.Version));
            isModified = true;
        }

        if (isModified)
        {
            var sortedVersions = versionDict.Values
                .OrderByDescending(v => new CkVersion(v.Version))
                .ToList();

            catalogData.UpdatedAt = DateTime.UtcNow;
            catalogData.LatestVersion = sortedVersions.FirstOrDefault()?.Version;

            var catalogContent = System.Text.Json.JsonSerializer.Serialize(catalogData,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

            if (isNew)
            {
                await gitHubClient.CreateFileAsync(
                        catalogPath, $"Create catalog for {blueprintId.Name} v{blueprintId.Version.Major}",
                        catalogContent)
                    .ConfigureAwait(false);
            }
            else
            {
                var r = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
                if (!r.HasValue)
                {
                    throw BlueprintCatalogException.CannotReadExistingVersionsCatalog(blueprintId, CatalogName,
                        catalogPath);
                }

                await gitHubClient.UpdateFileAsync(
                    catalogPath, $"Update catalog for {blueprintId.Name} v{blueprintId.Version.Major}",
                    catalogContent, r.Value.Item2).ConfigureAwait(false);
            }
        }
    }

    private async Task<SharedBlueprintCatalogTypes.BlueprintLibraryVersionsCatalog?> GetBlueprintLibraryVersionsCatalogWithClientAsync(
        string blueprintName,
        int majorVersion,
        IGitHubClientWrapper gitHubClient)
    {
        var catalogPath =
            $"{RootPath}{blueprintName[0].ToString().ToLower()}/{blueprintName}/{majorVersion}/{CatalogFileName}";

        var r = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
        if (r == null)
        {
            return null;
        }

        return System.Text.Json.JsonSerializer.Deserialize<SharedBlueprintCatalogTypes.BlueprintLibraryVersionsCatalog>(
            r.Value.Item1,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
    }

    private async Task UpdateBlueprintLibraryCatalogAsync(BlueprintId blueprintId, IGitHubClientWrapper gitHubClient)
    {
        var catalogPath = $"{RootPath}{blueprintId.Name[0].ToString().ToLower()}/{blueprintId.Name}/{CatalogFileName}";

        var catalogData = await GetBlueprintLibraryCatalogWithClientAsync(catalogPath, gitHubClient)
            .ConfigureAwait(false);

        var isNew = catalogData == null;

        catalogData ??= new SharedBlueprintCatalogTypes.BlueprintLibraryCatalog
        {
            BlueprintId = blueprintId.Name,
            MajorVersions = []
        };

        var currentMajor = blueprintId.Version.Major;
        var majorVersionEntry = catalogData.MajorVersions.FirstOrDefault(m => m.MajorVersion == currentMajor);

        if (majorVersionEntry == null)
        {
            catalogData.MajorVersions.Add(new SharedBlueprintCatalogTypes.BlueprintLibraryCatalogEntry
            {
                MajorVersion = currentMajor,
                CatalogPath =
                    $"{RootPath}{blueprintId.Name[0].ToString().ToLower()}/{blueprintId.Name}/{currentMajor}/{CatalogFileName}"
            });

            catalogData.UpdatedAt = DateTime.UtcNow;

            var catalogContent = System.Text.Json.JsonSerializer.Serialize(catalogData,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

            if (isNew)
            {
                await gitHubClient.CreateFileAsync(
                        catalogPath, $"Create blueprint catalog for {blueprintId.Name}", catalogContent)
                    .ConfigureAwait(false);
            }
            else
            {
                var existingCatalog = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
                if (!existingCatalog.HasValue)
                {
                    throw BlueprintCatalogException.CannotReadExistingLibraryCatalog(blueprintId, CatalogName,
                        catalogPath);
                }

                await gitHubClient.UpdateFileAsync(
                        catalogPath, $"Update blueprint catalog for {blueprintId.Name}", catalogContent,
                        existingCatalog.Value.Item2)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<SharedBlueprintCatalogTypes.BlueprintLibraryCatalog?> GetBlueprintLibraryCatalogWithClientAsync(
        string catalogPath,
        IGitHubClientWrapper gitHubClient)
    {
        var r = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
        if (r == null)
        {
            return null;
        }

        return System.Text.Json.JsonSerializer.Deserialize<SharedBlueprintCatalogTypes.BlueprintLibraryCatalog>(
            r.Value.Item1,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
    }

    private async Task UpdateRootCatalogAsync(BlueprintId blueprintId, IGitHubClientWrapper gitHubClient)
    {
        var catalogPath = $"{RootPath}{CatalogFileName}";

        var catalogData = await GetRootCatalogWithClientAsync(gitHubClient).ConfigureAwait(false);
        var isNew = catalogData == null;

        catalogData ??= new SharedBlueprintCatalogTypes.RootCatalog
        {
            Version = "1.0",
            UpdatedAt = DateTime.UtcNow,
            Blueprints = []
        };

        var existingEntry = catalogData.Blueprints.FirstOrDefault(m => m.BlueprintName == blueprintId.Name);

        if (existingEntry == null)
        {
            var newEntry = new SharedBlueprintCatalogTypes.RootCatalogEntry
            {
                BlueprintName = blueprintId.Name,
                CatalogPath =
                    $"{RootPath}{blueprintId.Name[0].ToString().ToLower()}/{blueprintId.Name}/{CatalogFileName}"
            };
            catalogData.Blueprints.Add(newEntry);

            catalogData.Blueprints = catalogData.Blueprints.OrderBy(m => m.BlueprintName).ToList();
            catalogData.UpdatedAt = DateTime.UtcNow;

            var catalogContent = System.Text.Json.JsonSerializer.Serialize(catalogData,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

            if (isNew)
            {
                await gitHubClient.CreateFileAsync(
                        catalogPath, $"Create blueprint catalog for {blueprintId.Name}", catalogContent)
                    .ConfigureAwait(false);
            }
            else
            {
                var existingCatalog = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
                if (!existingCatalog.HasValue)
                {
                    throw BlueprintCatalogException.CannotReadExistingLibraryCatalog(blueprintId, CatalogName,
                        catalogPath);
                }

                await gitHubClient.UpdateFileAsync(
                        catalogPath, $"Update blueprint catalog for {blueprintId.Name}", catalogContent,
                        existingCatalog.Value.Item2)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<SharedBlueprintCatalogTypes.RootCatalog?> GetRootCatalogWithClientAsync(
        IGitHubClientWrapper gitHubClient)
    {
        var catalogPath = $"{RootPath}{CatalogFileName}";

        var r = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
        if (r == null)
        {
            return null;
        }

        return System.Text.Json.JsonSerializer.Deserialize<SharedBlueprintCatalogTypes.RootCatalog>(
            r.Value.Item1,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
    }

    /// <inheritdoc />
    public override Task RefreshCatalogAsync(object? sourceIdentifier = null, bool forceRefresh = false)
    {
        return RefreshCatalogAsync(forceRefresh);
    }

    private async Task RefreshCatalogAsync(bool forceRefresh)
    {
        var maxAge = TimeSpan.FromSeconds(MaxCacheFileAgeSeconds);
        if (!forceRefresh && IsCacheFileRecentlyUpdated(maxAge))
        {
            return;
        }

        var cache = await ReadCacheAsync(false).ConfigureAwait(false);
        var catalog = await GetRootCatalogAsync().ConfigureAwait(false);

        if (catalog != null && cache.UpdatedAt != null && cache.UpdatedAt.Value == catalog.UpdatedAt)
        {
            // No changes in the catalog so we can skip the refresh
            return;
        }

        BlueprintCacheTypes.BlueprintCacheCatalog cacheCatalog = new()
        {
            UpdatedAt = DateTime.UtcNow
        };

        if (catalog != null)
        {
            foreach (var rootCatalogEntry in catalog.Blueprints)
            {
                var blueprintLibraryCatalog = await GetBlueprintLibraryCatalogAsync(rootCatalogEntry.CatalogPath)
                    .ConfigureAwait(false);

                if (blueprintLibraryCatalog == null)
                {
                    continue;
                }

                var blueprintEntry = new BlueprintCacheTypes.BlueprintCacheEntry
                {
                    BlueprintName = blueprintLibraryCatalog.BlueprintId,
                    Versions = new Dictionary<string, BlueprintCacheTypes.BlueprintCacheVersionEntry>()
                };
                cacheCatalog.Blueprints.Add(rootCatalogEntry.BlueprintName, blueprintEntry);

                foreach (var majorVersionEntry in blueprintLibraryCatalog.MajorVersions)
                {
                    var versionsCatalog = await GetBlueprintLibraryVersionsCatalogAsync(
                        rootCatalogEntry.BlueprintName,
                        majorVersionEntry.MajorVersion).ConfigureAwait(false);

                    if (versionsCatalog == null)
                    {
                        continue;
                    }

                    foreach (var versionEntry in versionsCatalog.Versions)
                    {
                        var ckVersion = new CkVersion(versionEntry.Version);
                        if (!blueprintEntry.Versions.ContainsKey(ckVersion.ToString()))
                        {
                            blueprintEntry.Versions.Add(ckVersion.ToString(), new BlueprintCacheTypes.BlueprintCacheVersionEntry
                            {
                                Version = ckVersion,
                                Description = versionsCatalog.Description,
                                DirectoryPath = versionEntry.DirectoryPath
                            });
                        }
                    }
                }
            }
        }

        await WriteCacheAsync(cacheCatalog).ConfigureAwait(false);
    }

    private string CreateBlueprintMetaPath(BlueprintId blueprintId)
    {
        return $"{CreateBlueprintDirectoryPath(blueprintId)}/{BlueprintMetaFileName}";
    }

    private string CreateBlueprintDirectoryPath(BlueprintId blueprintId)
    {
        return RootPath
               + blueprintId.Name[0].ToString().ToLower() + "/"
               + blueprintId.Name + "/"
               + blueprintId.Version.Major + "/"
               + blueprintId.FullName;
    }

    private IHttpClientWrapper CreateHttpClient()
    {
        if (string.IsNullOrWhiteSpace(_gitHubOptions.GitHubPagesUri))
        {
            throw BlueprintCatalogException.GitHubPagesUriMissing();
        }

        var baseUri = _gitHubOptions.GitHubPagesUri.TrimEnd('/');
        return _httpClientFactory.CreateClient(new Uri($"{baseUri}"));
    }

    // The fetch helpers below intentionally let transport failures (HttpRequestException after
    // retries: DNS/TLS/5xx) propagate. A missing catalog file (404) is already mapped to an empty
    // response by the HTTP client wrapper and yields null. Swallowing transport failures here would
    // make a refresh silently replace a previously good cache with an empty/partial one and report
    // success (AB#4309).
    private async Task<SharedBlueprintCatalogTypes.RootCatalog?> GetRootCatalogAsync()
    {
        var catalogPath = $"{RootPath}{CatalogFileName}";
        var httpClient = CreateHttpClient();

        var response = await httpClient.GetStringAsync(catalogPath).ConfigureAwait(false);

        if (string.IsNullOrEmpty(response))
        {
            return null;
        }

        return System.Text.Json.JsonSerializer.Deserialize<SharedBlueprintCatalogTypes.RootCatalog>(
            response!,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
    }

    private async Task<SharedBlueprintCatalogTypes.BlueprintLibraryCatalog?> GetBlueprintLibraryCatalogAsync(
        string catalogPath)
    {
        var httpClient = CreateHttpClient();

        var response = await httpClient.GetStringAsync(catalogPath).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(response))
        {
            return System.Text.Json.JsonSerializer
                .Deserialize<SharedBlueprintCatalogTypes.BlueprintLibraryCatalog>(
                    response!,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    });
        }

        return null;
    }

    private async Task<SharedBlueprintCatalogTypes.BlueprintLibraryVersionsCatalog?> GetBlueprintLibraryVersionsCatalogAsync(
        string blueprintName,
        int majorVersion)
    {
        var catalogPath = $"{RootPath}{blueprintName[0].ToString().ToLower()}/{blueprintName}/{majorVersion}/{CatalogFileName}";
        var httpClient = CreateHttpClient();

        var response = await httpClient.GetStringAsync(catalogPath).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(response))
        {
            return System.Text.Json.JsonSerializer
                .Deserialize<SharedBlueprintCatalogTypes.BlueprintLibraryVersionsCatalog>(
                    response!,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    });
        }

        return null;
    }
}
