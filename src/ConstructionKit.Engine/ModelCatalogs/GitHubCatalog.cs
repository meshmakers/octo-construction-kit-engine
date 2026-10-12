using System.Collections.Concurrent;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Microsoft.Extensions.Options;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
/// Public catalog on GitHub for construction kit models
/// </summary>
public class PublicGitHubCatalog(
    ICkJsonSerializer ckJsonSerializer,
    IHttpClientFactory httpClientFactory,
    IGitHubClientFactory gitHubClientFactory,
    IOptions<PublicGitHubCatalogOptions> gitHubOptions) : GitHubCatalog(ckJsonSerializer, httpClientFactory,
    gitHubClientFactory, gitHubOptions.Value, 20, PublicGitHubCatalog.Name,
    GitHubCatalogDescription.ForRepository("Public GitHub catalog", gitHubOptions.Value))
{
    /// <summary>The name of the public catalog; unpublishing from it needs a reason (AB#5433).</summary>
    public const string Name = "PublicGitHubCatalog";
}

/// <summary>
/// Private catalog on GitHub for construction kit models.
/// Checked before PublicGitHubCatalog so dev models take precedence.
/// </summary>
public class PrivateGitHubCatalog(
    ICkJsonSerializer ckJsonSerializer,
    IHttpClientFactory httpClientFactory,
    IGitHubClientFactory gitHubClientFactory,
    IOptions<PrivateGitHubCatalogOptions> gitHubOptions) : GitHubCatalog(ckJsonSerializer, httpClientFactory,
    gitHubClientFactory, gitHubOptions.Value, 15, "PrivateGitHubCatalog",
    GitHubCatalogDescription.ForRepository("Private GitHub catalog for development and testing",
        gitHubOptions.Value));

/// <summary>
/// Appends the repository coordinates to a catalog's human-readable description.
/// </summary>
/// <remarks>
/// AB#5139 makes the private catalog slots lane-scoped: the same catalog name
/// (<c>PrivateGitHubCatalog</c> / <c>PrivateGitHubBlueprintCatalog</c>) is bound to a different
/// GitHub repository per installation — main test-2 keeps <c>construction-kit-libraries-build</c> /
/// <c>blueprint-libraries-build</c>, the 0.2-dev instance points at <c>meshmakers/octo-catalog-dev</c>.
/// The catalog name is the identity everything else keys on and must not change, so without the
/// coordinates in the description two installations are indistinguishable in <c>octo-cli
/// ListCatalogs</c>, in the GraphQL <c>catalogs</c> field and in Refinery Studio — and "why is my
/// model not in the private catalog?" cannot be answered from the UI at all. The prose prefix is
/// kept verbatim so anything grepping logs or docs for it still matches.
/// </remarks>
internal static class GitHubCatalogDescription
{
    /// <summary>
    /// Renders <c>"{description} (owner/repo@branch)"</c>, degrading gracefully when the options
    /// carry no repository: an unset owner or branch is simply left out rather than producing a
    /// dangling separator, and without a repository name the plain description is returned so no
    /// empty parentheses ever reach the UI.
    /// </summary>
    public static string ForRepository(string description, IGitHubOptions gitHubOptions)
    {
        var repositoryName = gitHubOptions.GitHubRepositoryName;
        if (string.IsNullOrWhiteSpace(repositoryName))
        {
            return description;
        }

        var owner = gitHubOptions.GitHubRepositoryOwner;
        var branch = gitHubOptions.GitHubRepositoryBranch;

        var repository = string.IsNullOrWhiteSpace(owner) ? repositoryName : owner + "/" + repositoryName;
        if (!string.IsNullOrWhiteSpace(branch))
        {
            repository += "@" + branch;
        }

        return description + " (" + repository + ")";
    }
}

/// <summary>
/// Construction kit model catalog for GitHub base class
/// </summary>
public abstract class GitHubCatalog : CachedCatalog, IReplaceablePublishCatalog, IWithdrawnVersionCatalog,
    IAuthoritativeVersionCatalog
{
    /// <summary>
    ///     Name of the GitHub repository of the development lane catalog (<c>meshmakers/octo-catalog-dev</c>, the private
    ///     catalog slot of the 0.2-dev instance, AB#5139). Published versions in this catalog may be replaced; in every
    ///     other GitHub catalog they are immutable (AB#6386).
    /// </summary>
    public const string LaneCatalogRepositoryName = "octo-catalog-dev";

    private const string CatalogFileName = "catalog.json";
    private const int MaxCacheFileAgeSeconds = 60;

    private static readonly System.Text.Json.JsonSerializerOptions CatalogJsonReadOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    private static readonly System.Text.Json.JsonSerializerOptions CatalogJsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    private readonly ICkJsonSerializer _ckJsonSerializer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGitHubClientFactory _gitHubClientFactory;
    private readonly GitHubCatalogOptions _gitHubOptions;

    // Raw JSON payload of compiled models per model id (AB#6330). A published model version is immutable, so the
    // downloaded file never goes stale except when this catalog republishes it (entry invalidated in PublishAsync).
    // The bytes are cached, not the deserialized object: every caller deserializes its own instance, so a caller
    // mutating the returned CkCompiledModelRoot can never corrupt the cache. Failures / 404s are never cached.
    private const int MaxCachedModelPayloads = 512;
    private readonly ConcurrentDictionary<CkModelId, byte[]> _modelPayloadCache = new();

    // Static gh-pages reads cost nothing against the per-user 5000/h GitHub REST quota
    // that Octokit pulls from. Prefer the pages URL for every read; the Octokit client is
    // only fallback for setups that explicitly disabled gh-pages, plus the publish path.
    private bool IsPagesUriConfigured => !string.IsNullOrWhiteSpace(_gitHubOptions.GitHubPagesUri);

    // A publisher (token present) can ask the repository itself whenever the Pages mirror lags or fails (AB#6580,
    // AB#6581); an anonymous reader only has the mirror.
    private bool HasApiToken => !string.IsNullOrWhiteSpace(_gitHubOptions.GitHubApiToken);

    /// <summary>
    /// Creates a new instance of the <see cref="Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs.GitHubCatalog"/> class.
    /// </summary>
    protected GitHubCatalog(ICkJsonSerializer ckJsonSerializer, IHttpClientFactory httpClientFactory,
        IGitHubClientFactory gitHubClientFactory,
        GitHubCatalogOptions gitHubOptions, int order, string catalogName, string description) : base(order,
        catalogName, description, gitHubOptions.IsEnabled, gitHubOptions.IsEnabled, gitHubOptions)
    {
        _ckJsonSerializer = ckJsonSerializer;
        _httpClientFactory = httpClientFactory;
        _gitHubClientFactory = gitHubClientFactory;
        _gitHubOptions = gitHubOptions;
    }

    /// <inheritdoc />
    public override bool IsSupportingSourceIdentifier(object? sourceIdentifier = null)
    {
        return sourceIdentifier == null;
    }

    /// <inheritdoc />
    public bool AllowsReplacingPublishedVersions =>
        string.Equals(_gitHubOptions.GitHubRepositoryName, LaneCatalogRepositoryName,
            StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override async Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        // F1.1-S6: the model lives under ck-models/v3 or ck-models/v2. The refreshed cache knows which; without a
        // cache entry the roots are probed (v3 first only when the catalog has v3 models, review G3 E-L4).
        foreach (var candidate in await GetCandidatePathsAsync(modelId).ConfigureAwait(false))
        {
            var model = await GetFromPathAsync(modelId, candidate, operationResult, cancellationToken)
                .ConfigureAwait(false);
            if (model != null)
            {
                return model;
            }
        }

        throw ModelCatalogException.ModelNotFound(modelId, CatalogName);
    }

    private async Task<IReadOnlyList<string>> GetCandidatePathsAsync(CkModelId modelId)
    {
        var candidates = new List<string>();
        // Review G3 E-L4: probe v3 first only when the catalog has v3 models at all; otherwise every v1 model
        // lookup without a cache entry would cost an extra 404 (and REST call on the Octokit path).
        IReadOnlyList<string> probeOrder = [CkCatalogLayout.V2Root, CkCatalogLayout.V3Root];
        try
        {
            var cache = await ReadCacheAsync(false).ConfigureAwait(false);
            if (cache.Models.TryGetValue(modelId.Name, out var entry) &&
                entry.Versions.TryGetValue(modelId.Version.ToString(), out var version) &&
                CkCatalogLayout.ReadRoots.Any(r => version.FilePath.StartsWith(r, StringComparison.Ordinal)))
            {
                candidates.Add(version.FilePath);
            }

            if (cache.Models.Values.Any(m => m.Versions.Values.Any(v =>
                    v.FilePath.StartsWith(CkCatalogLayout.V3Root, StringComparison.Ordinal))))
            {
                probeOrder = CkCatalogLayout.ReadRoots;
            }
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            // no usable cache — fall back to probing the roots
        }

        foreach (var root in probeOrder)
        {
            var path = CreatePath(root, modelId);
            if (!candidates.Contains(path))
            {
                candidates.Add(path);
            }
        }

        return candidates;
    }

    /// <summary>Loads the model from one path; <c>null</c> when the file does not exist there.</summary>
    private async Task<CkCompiledModelRoot?> GetFromPathAsync(CkModelId modelId, string pagesUrl,
        OperationResult operationResult, CancellationToken? cancellationToken)
    {
        if (_modelPayloadCache.TryGetValue(modelId, out var cachedPayload))
        {
            return await DeserializeAndCacheAsync(modelId, cachedPayload, operationResult).ConfigureAwait(false);
        }

        // The Pages mirror lags the repository by up to a minute after a publish and briefly answers 404 while a
        // deployment is replaced (AB#4872, AB#6581). A reader that holds a token therefore asks the Contents API
        // whenever the mirror does not deliver the file; only an anonymous reader takes the mirror's 404 at face value.
        // The lane catalog (AllowsReplacingPublishedVersions) replaces published versions, so there the mirror may
        // serve an outdated file with a 200: its reads go to the repository first.
        var canAskRepository = HasApiToken;
        if (IsPagesUriConfigured && !(canAskRepository && AllowsReplacingPublishedVersions))
        {
            var httpClient = CreateHttpClient();
            Exception? mirrorFailure = null;
            try
            {
                var response = await httpClient.GetAsync(pagesUrl, cancellationToken ?? CancellationToken.None).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var payload = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    return await DeserializeAndCacheAsync(modelId, payload, operationResult).ConfigureAwait(false);
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    if (!canAskRepository)
                    {
                        return null;
                    }
                }
                else
                {
                    mirrorFailure = ModelCatalogException.InvalidGitHubRepository(CatalogName,
                        _gitHubOptions.GitHubPagesUri);
                }
            }
            catch (HttpRequestException)
            {
                mirrorFailure = ModelCatalogException.InvalidGitHubRepository(CatalogName, _gitHubOptions.GitHubPagesUri);
            }
            catch (TaskCanceledException)
            {
                mirrorFailure = ModelCatalogException.RequestTimeoutGitHubRepository(CatalogName,
                    _gitHubOptions.GitHubPagesUri);
            }

            if (mirrorFailure != null && !canAskRepository)
            {
                throw mirrorFailure;
            }
        }

        var gitHubClient = CreateGitHubClient();

        var r = await gitHubClient.GetFileAsync(pagesUrl).ConfigureAwait(false);
        if (r == null)
        {
            return null;
        }

        return await DeserializeAndCacheAsync(modelId, System.Text.Encoding.UTF8.GetBytes(r.Value.Item1),
            operationResult).ConfigureAwait(false);
    }

    private async Task<CkCompiledModelRoot> DeserializeAndCacheAsync(CkModelId modelId, byte[] payload,
        OperationResult operationResult)
    {
        using var stream = new MemoryStream(payload, false);
        var ckCompiledModelRoot = await _ckJsonSerializer
            .DeserializeCompiledModelRootAsync(stream, "", operationResult,
                tolerantToUnknownProperties: true).ConfigureAwait(false);
        if (operationResult.HasErrors)
        {
            throw ModelCatalogException.ErrorDuringModelLoad(modelId, CatalogName, operationResult);
        }

        if (_modelPayloadCache.Count >= MaxCachedModelPayloads)
        {
            _modelPayloadCache.Clear();
        }

        _modelPayloadCache[modelId] = payload;
        return ckCompiledModelRoot;
    }

    /// <inheritdoc />
    public override async Task PublishAsync(CkCompiledModelRoot ckCompiledModel, bool force = false,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        var gitHubClient = CreateGitHubClient();

        // AB#5433: an unpublished version number is burned, also for a forced publish.
        try
        {
            await ThrowIfWithdrawnAsync(ckCompiledModel.ModelId, gitHubClient).ConfigureAwait(false);
        }
        catch (ApiException e)
        {
            throw ModelCatalogException.PublishFailed(ckCompiledModel.ModelId, CatalogName, e);
        }

        // F1.1-S6: ckLanguage 2 / range-retaining models are published under ck-models/v3.
        var root = CkCatalogLayout.GetPublishRoot(ckCompiledModel);
        string filePath = CreatePath(root, ckCompiledModel.ModelId);

        cancellationToken?.ThrowIfCancellationRequested();

        try
        {
            _modelPayloadCache.TryRemove(ckCompiledModel.ModelId, out _);
            var content = await ReadContentAsync(ckCompiledModel).ConfigureAwait(false);

            // Review G3 E-M5: a version lives under one root only, as in the local catalog — the same version in v2
            // and v3 with different content would let old engines (v2) and new engines (v3 first) read different
            // models. Without force the other root's file is a conflict; with force it is removed.
            var otherRoot = root == CkCatalogLayout.V3Root ? CkCatalogLayout.V2Root : CkCatalogLayout.V3Root;
            var otherPath = CreatePath(otherRoot, ckCompiledModel.ModelId);
            var existingElsewhere = await gitHubClient.GetFileAsync(otherPath).ConfigureAwait(false);
            if (existingElsewhere.HasValue)
            {
                if (!force)
                {
                    throw ModelCatalogException.ModelAlreadyExists(ckCompiledModel.ModelId, CatalogName);
                }

                await gitHubClient.DeleteFileAsync(otherPath,
                        $"Move {ckCompiledModel.ModelId.FullName} to {root}", existingElsewhere.Value.Item2)
                    .ConfigureAwait(false);
            }

            var existing = await gitHubClient.GetFileAsync(filePath).ConfigureAwait(false);
            if (existing.HasValue)
            {
                if (!force)
                {
                    throw ModelCatalogException.ModelAlreadyExists(ckCompiledModel.ModelId, CatalogName);
                }

                await gitHubClient.UpdateFileAsync(filePath, $"Update to {ckCompiledModel.ModelId.FullName}", content,
                    existing.Value.Item2).ConfigureAwait(false);
            }
            else
            {
                await gitHubClient.CreateFileAsync(filePath, $"First commit for {ckCompiledModel.ModelId.FullName}",
                        content)
                    .ConfigureAwait(false);
            }

            cancellationToken?.ThrowIfCancellationRequested();

            // Update the major version
            await UpdateModelVersionsCatalogAsync(root, ckCompiledModel.ModelId, ckCompiledModel.Description,
                    gitHubClient)
                .ConfigureAwait(false);

            cancellationToken?.ThrowIfCancellationRequested();

            // Update the overall model library catalog
            await UpdateModelLibraryCatalogAsync(root, ckCompiledModel.ModelId, gitHubClient)
                .ConfigureAwait(false);

            // Update the root catalog
            await UpdateRootCatalogAsync(root, ckCompiledModel.ModelId, gitHubClient).ConfigureAwait(false);

            cancellationToken?.ThrowIfCancellationRequested();
        }
        catch (ApiException e)
        {
            throw ModelCatalogException.PublishFailed(ckCompiledModel.ModelId, CatalogName, e);
        }

        // Drop again: a read racing the upload may have cached the pre-publish file.
        _modelPayloadCache.TryRemove(ckCompiledModel.ModelId, out _);

        try
        {
            // Refresh the local read cache. Best effort only: the refresh reads the gh-pages
            // mirror, which lags the commits just pushed and briefly serves 404 while a Pages
            // deployment is being replaced. A transient failure here must not fail a publish
            // that already succeeded (AB#4872: overlapping Pages deploys of two CI builds made
            // exactly this refresh kill both builds minutes after the models were safely
            // published). The cache has a 60 s max age, so the next read refreshes again.
            await RefreshCatalogAsync(true).ConfigureAwait(false);
        }
        catch (Exception e) when (e is ModelCatalogException or HttpRequestException or TaskCanceledException)
        {
            // Swallow — the publish itself is complete.
        }
    }

    /// <inheritdoc />
    public override async Task<CatalogUnpublishResult> UnpublishAsync(CkModelId modelId, string? reason = null,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        var gitHubClient = CreateGitHubClient();
        cancellationToken?.ThrowIfCancellationRequested();

        var suffix = string.IsNullOrWhiteSpace(reason) ? "" : $": {reason!.Trim()}";
        try
        {
            // The roots that hold the version: the model file exists or the per-major index lists it.
            var holdingRoots = new List<string>();
            foreach (var root in CkCatalogLayout.ReadRoots)
            {
                var file = await gitHubClient.GetFileAsync(CreatePath(root, modelId)).ConfigureAwait(false);
                var versions = await gitHubClient
                    .GetFileAsync(CatalogUnpublishIndex.VersionsCatalogPath(root, modelId)).ConfigureAwait(false);
                if (file.HasValue || CatalogUnpublishIndex.ListsVersion(versions?.Item1, modelId))
                {
                    holdingRoots.Add(root);
                }
            }

            if (holdingRoots.Count == 0)
            {
                if (await FindWithdrawnAsync(modelId, gitHubClient).ConfigureAwait(false) != null)
                {
                    return CatalogUnpublishResult.AlreadyWithdrawn;
                }

                throw ModelCatalogException.ModelNotFound(modelId, CatalogName);
            }

            _modelPayloadCache.TryRemove(modelId, out _);
            foreach (var root in holdingRoots)
            {
                cancellationToken?.ThrowIfCancellationRequested();

                // Safe order, like the blueprint unpublish: burn the number, prune the index tree (version, major,
                // model), THEN delete the model file. A failure in between leaves a burned number and an orphan file
                // that running unpublish again removes - never an index entry pointing at a deleted file.
                await gitHubClient.UpsertFileWithMergeAsync(CatalogUnpublishIndex.WithdrawnFilePath(root, modelId.Name),
                        $"Withdraw {modelId.FullName}{suffix}",
                        existing => CatalogUnpublishIndex.AddWithdrawn(existing, modelId, reason))
                    .ConfigureAwait(false);

                var majorEmpty = false;
                var versionsPath = CatalogUnpublishIndex.VersionsCatalogPath(root, modelId);
                await gitHubClient.UpsertFileWithMergeAsync(versionsPath,
                        $"Unpublish {modelId.FullName}{suffix}",
                        existing =>
                        {
                            var merged = CatalogUnpublishIndex.RemoveVersion(existing, modelId, out majorEmpty);
                            // The last version of the major: the file itself goes away below, not an empty index.
                            return majorEmpty ? null : merged;
                        })
                    .ConfigureAwait(false);

                if (majorEmpty)
                {
                    var modelEmpty = false;
                    var libraryPath = CatalogUnpublishIndex.LibraryCatalogPath(root, modelId.Name);
                    await gitHubClient.UpsertFileWithMergeAsync(libraryPath,
                            $"Unpublish {modelId.FullName}{suffix}",
                            existing =>
                            {
                                var merged = CatalogUnpublishIndex.RemoveMajor(existing, modelId.Version.Major,
                                    out modelEmpty);
                                return modelEmpty ? null : merged;
                            })
                        .ConfigureAwait(false);

                    if (modelEmpty)
                    {
                        await gitHubClient.UpsertFileWithMergeAsync(CatalogUnpublishIndex.RootCatalogPath(root),
                                $"Unpublish {modelId.FullName}{suffix}",
                                existing => CatalogUnpublishIndex.RemoveModel(existing, modelId.Name))
                            .ConfigureAwait(false);
                        await DeleteIfExistsAsync(gitHubClient, libraryPath, $"Unpublish {modelId.FullName}{suffix}")
                            .ConfigureAwait(false);
                    }

                    await DeleteIfExistsAsync(gitHubClient, versionsPath, $"Unpublish {modelId.FullName}{suffix}")
                        .ConfigureAwait(false);
                }

                await DeleteIfExistsAsync(gitHubClient, CreatePath(root, modelId),
                    $"Unpublish {modelId.FullName}{suffix}").ConfigureAwait(false);
            }
        }
        catch (ApiException e)
        {
            throw ModelCatalogException.UnpublishFailed(modelId, CatalogName, e);
        }

        _modelPayloadCache.TryRemove(modelId, out _);

        try
        {
            // Best effort, as after a publish: the refresh reads the gh-pages mirror, which lags the commits.
            await RefreshCatalogAsync(true).ConfigureAwait(false);
        }
        catch (Exception e) when (e is ModelCatalogException or HttpRequestException or TaskCanceledException)
        {
            // Swallow - the unpublish itself is complete.
        }

        return CatalogUnpublishResult.Removed;
    }

    private static async Task DeleteIfExistsAsync(IGitHubClientWrapper gitHubClient, string path, string commitMessage)
    {
        var file = await gitHubClient.GetFileAsync(path).ConfigureAwait(false);
        if (file.HasValue)
        {
            await gitHubClient.DeleteFileAsync(path, commitMessage, file.Value.Item2).ConfigureAwait(false);
        }
    }

    /// <summary>AB#5433: the withdrawn record of the version in either root, or null. Read through the API (consistent).</summary>
    private static async Task<SharedCatalogTypes.WithdrawnVersionEntry?> FindWithdrawnAsync(CkModelId modelId,
        IGitHubClientWrapper gitHubClient)
    {
        foreach (var root in CkCatalogLayout.ReadRoots)
        {
            var file = await gitHubClient.GetFileAsync(CatalogUnpublishIndex.WithdrawnFilePath(root, modelId.Name))
                .ConfigureAwait(false);
            var entry = CatalogUnpublishIndex.FindWithdrawn(file?.Item1, modelId.Version);
            if (entry != null)
            {
                return entry;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<WithdrawnVersionInfo?> FindWithdrawnVersionAsync(CkModelId modelId)
    {
        try
        {
            var entry = await FindWithdrawnAsync(modelId, CreateGitHubClient()).ConfigureAwait(false);
            return entry == null ? null : new WithdrawnVersionInfo(entry.WithdrawnAt, entry.Reason);
        }
        catch (ApiException)
        {
            // The gate must not fail on an unreadable record; the write checks the record again and refuses there.
            return null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     AB#6580: reads the index files through the authenticated Contents API (as the publish does when it merges them),
    ///     never through the Pages mirror. A <c>404</c> only means "the catalog does not hold this" when the repository and
    ///     its branch exist (<see cref="IGitHubClientWrapper.GetRepositoryStateAsync" />) and no index that lists the model
    ///     is missing; everything else - a missing repository or branch, a transport error, a rate limit that outlasted the
    ///     retries, a timeout, an unreadable or inconsistent index - is reported as unreachable. Cost: two root indexes
    ///     plus, per matching model, one library index and one index per major.
    /// </remarks>
    public async Task<CatalogVersionsResult> ReadVersionsAsync(string? modelName,
        CancellationToken? cancellationToken = null)
    {
        try
        {
            var gitHubClient = CreateGitHubClient();
            var repository = $"{_gitHubOptions.GitHubRepositoryOwner}/{_gitHubOptions.GitHubRepositoryName}";
            switch (await gitHubClient.GetRepositoryStateAsync().ConfigureAwait(false))
            {
                case GitHubRepositoryState.RepositoryNotFound:
                    return CatalogVersionsResult.Unreachable(
                        $"repository '{repository}' of catalog '{CatalogName}' does not exist or the token cannot see it");
                case GitHubRepositoryState.BranchNotFound:
                    return CatalogVersionsResult.Unreachable(
                        $"branch '{_gitHubOptions.GitHubRepositoryBranch}' of repository '{repository}' (catalog " +
                        $"'{CatalogName}') does not exist");
                case GitHubRepositoryState.Empty:
                    return CatalogVersionsResult.Found([]);
            }

            var versions = new List<CkModelId>();
            foreach (var root in CkCatalogLayout.ReadRoots)
            {
                cancellationToken?.ThrowIfCancellationRequested();
                var rootPath = $"{root}{CatalogFileName}";
                var rootCatalog = await ReadCatalogIndexAsync<SharedCatalogTypes.RootCatalog>(gitHubClient, rootPath)
                    .ConfigureAwait(false);
                if (rootCatalog == null)
                {
                    // The repository exists and has no index under this root: it holds no model of this layout.
                    continue;
                }

                foreach (var entry in rootCatalog.Models.Where(m =>
                             modelName == null || string.Equals(m.ModelName, modelName, StringComparison.OrdinalIgnoreCase)))
                {
                    cancellationToken?.ThrowIfCancellationRequested();
                    // An index that lists a model must be followed down to the versions: a listed sub-index that is
                    // missing is a damaged or half-deployed catalog, not an empty one.
                    var library = await ReadCatalogIndexAsync<SharedCatalogTypes.ModelLibraryCatalog>(gitHubClient,
                        entry.CatalogPath).ConfigureAwait(false);
                    if (library == null)
                    {
                        return CatalogVersionsResult.Unreachable(
                            $"index '{rootPath}' lists model '{entry.ModelName}', but '{entry.CatalogPath}' does not exist");
                    }

                    var name = string.IsNullOrWhiteSpace(library.ModelId) ? entry.ModelName : library.ModelId;
                    foreach (var major in library.MajorVersions)
                    {
                        var majorCatalog = await ReadCatalogIndexAsync<SharedCatalogTypes.ModelLibraryVersionsCatalog>(
                            gitHubClient, major.CatalogPath).ConfigureAwait(false);
                        if (majorCatalog == null)
                        {
                            return CatalogVersionsResult.Unreachable(
                                $"index '{entry.CatalogPath}' lists major {major.MajorVersion} of model '{name}', but " +
                                $"'{major.CatalogPath}' does not exist");
                        }

                        versions.AddRange(majorCatalog.Versions.Select(v => new CkModelId(name, new CkVersion(v.Version))));
                    }
                }
            }

            return CatalogVersionsResult.Found(versions);
        }
        catch (OperationCanceledException) when (cancellationToken?.IsCancellationRequested == true)
        {
            throw;
        }
        catch (Exception e) when (e is ApiException or HttpRequestException or OperationCanceledException
                                      or ModelCatalogException or System.Text.Json.JsonException or InvalidDataException
                                      or IOException)
        {
            return CatalogVersionsResult.Unreachable(DescribeSourceFailure(e));
        }
    }

    private static string DescribeSourceFailure(Exception e) => e switch
    {
        OperationCanceledException => "the request timed out",
        ApiException api => $"GitHub answered HTTP {(int)api.StatusCode}: {api.ApiError?.Message ?? api.Message}",
        _ => e.Message
    };

    /// <summary>Reads and parses an index file; null when the file does not exist, an exception when it is unreadable.</summary>
    private static async Task<T?> ReadCatalogIndexAsync<T>(IGitHubClientWrapper gitHubClient, string path)
        where T : class
    {
        var file = await gitHubClient.GetFileAsync(path).ConfigureAwait(false);
        if (file == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(file.Value.Item1))
        {
            throw new InvalidDataException($"index '{path}' is empty");
        }

        return DeserializeCatalog<T>(file.Value.Item1)
               ?? throw new InvalidDataException($"index '{path}' could not be parsed");
    }

    private async Task ThrowIfWithdrawnAsync(CkModelId modelId, IGitHubClientWrapper gitHubClient)
    {
        var entry = await FindWithdrawnAsync(modelId, gitHubClient).ConfigureAwait(false);
        if (entry != null)
        {
            throw ModelCatalogException.VersionWithdrawn(modelId, CatalogName, entry.Reason, entry.WithdrawnAt);
        }
    }

    private async Task<string> ReadContentAsync(CkCompiledModelRoot ckCompiledModel)
    {
        using var memoryStream = new MemoryStream();
        var streamWriter = new StreamWriter(memoryStream);
        await _ckJsonSerializer.SerializeAsync(streamWriter, ckCompiledModel).ConfigureAwait(false);

        // Ensure all data is written to the MemoryStream
        await streamWriter.FlushAsync().ConfigureAwait(false);

        // Convert the MemoryStream to a string
        memoryStream.Position = 0;

        using var streamReader = new StreamReader(memoryStream);
        return await streamReader.ReadToEndAsync().ConfigureAwait(false);
    }

    private IGitHubClientWrapper CreateGitHubClient()
    {
        if (string.IsNullOrWhiteSpace(_gitHubOptions.GitHubApiToken) ||
            _gitHubOptions.GitHubApiToken == null)
        {
            throw ModelCatalogException.GitHubTokenMissing();
        }

        return _gitHubClientFactory.CreateClient(_gitHubOptions);
    }

    private static string CreatePath(string root, CkModelId ckModelId) => CkCatalogLayout.ModelFilePath(root, ckModelId);

    private IHttpClientWrapper CreateHttpClient()
    {
        if (string.IsNullOrWhiteSpace(_gitHubOptions.GitHubPagesUri) ||
            _gitHubOptions.GitHubPagesUri == null)
        {
            throw ModelCatalogException.GitHubPagesUriMissing();
        }

        var baseUri = _gitHubOptions.GitHubPagesUri.TrimEnd('/');

        return _httpClientFactory.CreateClient(new Uri($"{baseUri}"));
    }

    /// <summary>
    /// Gets the catalog for a specific major version of a model
    /// </summary>
    /// <param name="root">The catalog root (F1.1-S6: <c>ck-models/v2/</c> or <c>ck-models/v3/</c>)</param>
    /// <param name="modelName">The model ID (without version)</param>
    /// <param name="majorVersion">The major version number</param>
    /// <returns>The major version catalog content or null if not found</returns>
    private async Task<SharedCatalogTypes.ModelLibraryVersionsCatalog?> GetModelLibraryVersionsCatalogAsync(
        string root, string modelName, int majorVersion)
    {
        var catalogPath = $"{CkCatalogLayout.ModelDirectory(root, modelName)}{majorVersion}/{CatalogFileName}";

        string? response;
        if (IsPagesUriConfigured)
        {
            var httpClient = CreateHttpClient();
            try
            {
                response = await httpClient.GetStringAsync(catalogPath).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                throw ModelCatalogException.InvalidGitHubRepository(CatalogName, _gitHubOptions.GitHubPagesUri);
            }
        }
        else
        {
            var gitHubClient = CreateGitHubClient();

            var r = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
            if (r == null)
            {
                return null;
            }

            response = r.Value.Item1;
        }

        if (!string.IsNullOrWhiteSpace(response) && response != null)
        {
            var versionsCatalog = System.Text.Json.JsonSerializer
                .Deserialize<SharedCatalogTypes.ModelLibraryVersionsCatalog>(
                    response,
                    new System.Text.Json.JsonSerializerOptions
                        { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

            return versionsCatalog;
        }

        return null;
    }

    // The publish path reads AND writes the catalog index files through the authenticated GitHub
    // Contents API (inside UpsertFileWithMergeAsync) and never through the gh-pages URL: Pages
    // deploys lag behind the repository and briefly serve 404 while a deployment is replaced,
    // which both failed builds ("index files are missing", AB#4872 — originally fixed via
    // AB#2826/AB#2831) and corrupted merges (a stale Pages read dropping a concurrent build's
    // freshly published version from the catalog). Quota note before swinging this pendulum
    // again: these are ~3 extra API reads per published model on top of the ~8 mutation calls a
    // publish already makes. The 5000/h-per-user REST-quota incident that moved reads to
    // gh-pages (commit b364570, octo-construction-kit-CI 33317) was caused by the
    // O(models × majors) refresh walk, which deliberately stays on gh-pages.
    private static async Task UpdateModelVersionsCatalogAsync(string root, CkModelId modelId, string? description,
        IGitHubClientWrapper gitHubClient)
    {
        // Catalog file path for this major version
        var catalogPath =
            $"{CkCatalogLayout.ModelDirectory(root, modelId.Name)}{modelId.Version.Major}/{CatalogFileName}";

        await gitHubClient.UpsertFileWithMergeAsync(catalogPath,
                $"Update catalog for {modelId.Name} v{modelId.Version.Major}",
                existingJson => MergeModelVersionsCatalog(existingJson, modelId, description, root))
            .ConfigureAwait(false);
    }

    internal static string? MergeModelVersionsCatalog(string? existingJson, CkModelId modelId, string? description,
        string root = CkCatalogLayout.V2Root)
    {
        var catalogData = DeserializeCatalog<SharedCatalogTypes.ModelLibraryVersionsCatalog>(existingJson);
        var isModified = false;

        catalogData ??= new SharedCatalogTypes.ModelLibraryVersionsCatalog
        {
            ModelId = modelId.Name,
            MajorVersion = modelId.Version.Major,
            Versions = new List<SharedCatalogTypes.ModelLibraryVersionsCatalogEntry>()
        };
        if (catalogData.Description != description)
        {
            isModified = true;
            catalogData.Description = description;
        }

        // Create a dictionary to merge versions (preserving timestamps)
        var versionDict = catalogData.Versions.ToDictionary(k => k.Version, v => v);

        // Check if the current version already exists in the catalog
        var currentVersionString = modelId.Version.ToString();
        if (!versionDict.ContainsKey(currentVersionString))
        {
            // Add the new version only if it doesn't exist
            var fileName = $"ck-{modelId.Name.ToLower()}-{currentVersionString}.json";
            var filePath = CkCatalogLayout.ModelFilePath(root, modelId);

            versionDict[currentVersionString] = new SharedCatalogTypes.ModelLibraryVersionsCatalogEntry
            {
                Version = currentVersionString,
                FileName = fileName,
                PublishedAt = DateTime.UtcNow,
                FilePath = filePath
            };

            catalogData.Versions.Clear();
            catalogData.Versions.AddRange(versionDict.Values.OrderBy(v => v.Version));
            isModified = true;
        }

        if (!isModified)
        {
            return null;
        }

        // Sort versions in descending order (latest first)
        var sortedVersions = versionDict.Values
            .OrderByDescending(v => new CkVersion(v.Version))
            .ToList();

        catalogData.UpdatedAt = DateTime.UtcNow;
        catalogData.LatestVersion = sortedVersions.FirstOrDefault()?.Version;

        return SerializeCatalog(catalogData);
    }

    private static T? DeserializeCatalog<T>(string? json) where T : class
    {
        return string.IsNullOrWhiteSpace(json)
            ? null
            : System.Text.Json.JsonSerializer.Deserialize<T>(json!, CatalogJsonReadOptions);
    }

    private static string SerializeCatalog<T>(T catalog)
    {
        return System.Text.Json.JsonSerializer.Serialize(catalog, CatalogJsonWriteOptions);
    }


    /// <summary>
    /// Refreshes the catalog file by scanning all model directories
    /// </summary>
    public override Task RefreshCatalogAsync(object? sourceIdentifier = null, bool forceRefresh = false)
    {
        return RefreshCatalogAsync(forceRefresh);
    }

    private async Task RefreshCatalogAsync(bool forceRefresh)
    {
        var maxAge = TimeSpan.FromSeconds(MaxCacheFileAgeSeconds);
        if (!forceRefresh && IsCacheFileRecentlyUpdatedAsync(maxAge))
        {
            return;
        }

        var cache = await ReadCacheAsync(false).ConfigureAwait(false);

        // gh-pages briefly serves 404 for the whole site while a Pages deployment is being
        // replaced (AB#4872). An index file that should exist — the root index, or a sub-catalog
        // the root index lists — answering 404 is therefore usually transient: retry a few times
        // before treating it as missing. The budget is shared across the whole refresh so a full
        // site swap cannot stall a refresh for minutes.
        var notFoundRetryBudget = _gitHubOptions.RefreshNotFoundRetryCount;
        var notFoundRetryDelay = TimeSpan.FromSeconds(_gitHubOptions.RefreshNotFoundRetryDelaySeconds);

        async Task<T?> ReadWithNotFoundRetryAsync<T>(Func<Task<T?>> read) where T : class
        {
            var result = await read().ConfigureAwait(false);
            while (result == null && notFoundRetryBudget > 0)
            {
                notFoundRetryBudget--;
                if (notFoundRetryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(notFoundRetryDelay).ConfigureAwait(false);
                }

                result = await read().ConfigureAwait(false);
            }

            return result;
        }

        var (catalog, sourceUnreachable) =
            await GetRootCatalogWithReachabilityAsync(CkCatalogLayout.V2Root).ConfigureAwait(false);
        while (catalog == null && !sourceUnreachable && notFoundRetryBudget > 0)
        {
            // A missing root index is either a genuinely empty catalog or the deploy swap —
            // retry before caching "empty", so models don't transiently vanish mid-deploy.
            notFoundRetryBudget--;
            if (notFoundRetryDelay > TimeSpan.Zero)
            {
                await Task.Delay(notFoundRetryDelay).ConfigureAwait(false);
            }

            (catalog, sourceUnreachable) =
                await GetRootCatalogWithReachabilityAsync(CkCatalogLayout.V2Root).ConfigureAwait(false);
        }

        // F1.1-S6: the ck-models/v3 root (ckLanguage 2 / range-retaining models) is optional — a catalog without
        // such models has no index there, so a 404 is not retried (it would spend the retry budget on every refresh).
        // AB#6580: the mirror answering 404 for the root index is "the catalog is empty" only when the repository agrees.
        // A publisher asks it; a mirror that disagrees (lagging or being replaced) is an unreachable source, never an
        // empty catalog. Anonymous readers have only the mirror.
        if (catalog == null && !sourceUnreachable &&
            !await RootIndexAbsentConfirmedAsync(CkCatalogLayout.V2Root).ConfigureAwait(false))
        {
            sourceUnreachable = true;
        }

        var (v3Catalog, v3Unreachable) =
            await GetRootCatalogWithReachabilityAsync(CkCatalogLayout.V3Root).ConfigureAwait(false);
        sourceUnreachable |= v3Unreachable;
        var catalogs = new List<(string Root, SharedCatalogTypes.RootCatalog Catalog)>();
        if (v3Catalog != null)
        {
            catalogs.Add((CkCatalogLayout.V3Root, v3Catalog));
        }

        if (catalog != null)
        {
            catalogs.Add((CkCatalogLayout.V2Root, catalog));
        }

        var cacheCatalog = new CacheTypes.CacheCatalog
        {
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var (root, rootCatalog) in catalogs)
        {
            foreach (var rootCatalogEntry in rootCatalog.Models)
            {
                var modelLibraryCatalog = await ReadWithNotFoundRetryAsync(() =>
                    GetModelLibraryCatalogAsync(rootCatalogEntry.CatalogPath)).ConfigureAwait(false);

                if (modelLibraryCatalog == null)
                {
                    // The root index lists the model, so its index should exist: the mirror failed to deliver it
                    // (AB#6580). The cache is written anyway, flagged, and never reads as "the model does not exist".
                    sourceUnreachable = true;
                    continue;
                }

                // A model may have versions under both roots.
                if (!cacheCatalog.Models.TryGetValue(rootCatalogEntry.ModelName, out var modelEntry))
                {
                    modelEntry = new CacheTypes.CacheModelEntry
                    {
                        ModelId = modelLibraryCatalog.ModelId,
                        Versions = new Dictionary<string, CacheTypes.CacheModelVersionEntry>()
                    };
                    cacheCatalog.Models.Add(rootCatalogEntry.ModelName, modelEntry);
                }

                foreach (var modelLibraryCatalogEntry in modelLibraryCatalog.MajorVersions)
                {
                    var versionsCatalog = await ReadWithNotFoundRetryAsync(() =>
                        GetModelLibraryVersionsCatalogAsync(root,
                            rootCatalogEntry.ModelName,
                            modelLibraryCatalogEntry.MajorVersion)).ConfigureAwait(false);

                    if (versionsCatalog == null)
                    {
                        sourceUnreachable = true;
                        continue;
                    }

                    foreach (var modelLibraryVersionsCatalogEntry in versionsCatalog.Versions)
                    {
                        var ckVersion = new CkVersion(modelLibraryVersionsCatalogEntry.Version);
                        if (!modelEntry.Versions.ContainsKey(ckVersion.ToString()))
                        {
                            modelEntry.Versions.Add(ckVersion.ToString(), new CacheTypes.CacheModelVersionEntry
                            {
                                Version = ckVersion,
                                Description = versionsCatalog.Description,
                                FilePath = modelLibraryVersionsCatalogEntry.FilePath
                            });
                        }
                    }
                }
            }
        }

        cacheCatalog.SourceUnreachable = sourceUnreachable;
        await WriteCacheAsync(cacheCatalog).ConfigureAwait(false);
    }

    /// <summary>
    ///     True when the mirror's 404 for a root index is confirmed by the repository (it exists and holds no such index),
    ///     or when the reader has no token to ask with. False when the repository holds the index, does not exist, or
    ///     could not be asked.
    /// </summary>
    private async Task<bool> RootIndexAbsentConfirmedAsync(string root)
    {
        if (!HasApiToken)
        {
            return true;
        }

        try
        {
            var gitHubClient = CreateGitHubClient();
            return await gitHubClient.GetRepositoryStateAsync().ConfigureAwait(false) switch
            {
                GitHubRepositoryState.Empty => true,
                GitHubRepositoryState.Ready => await gitHubClient.GetFileAsync($"{root}{CatalogFileName}")
                    .ConfigureAwait(false) == null,
                _ => false
            };
        }
        catch (Exception e) when (e is ApiException or HttpRequestException or OperationCanceledException
                                      or ModelCatalogException)
        {
            return false;
        }
    }


    private async Task<(SharedCatalogTypes.RootCatalog? Catalog, bool SourceUnreachable)>
        GetRootCatalogWithReachabilityAsync(string root)
    {
        var catalogPath = $"{root}{CatalogFileName}";

        string? response;
        if (IsPagesUriConfigured)
        {
            var httpClient = CreateHttpClient();
            try
            {
                var httpResponse = await httpClient.GetAsync(catalogPath, CancellationToken.None)
                    .ConfigureAwait(false);
                if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // The source responded: the catalog simply does not exist (empty catalog)
                    return (null, false);
                }

                if (!httpResponse.IsSuccessStatusCode)
                {
                    // Server error — the source of truth is unavailable.
                    return (null, true);
                }

                response = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Network-level failure — the source of truth is unreachable.
                // The refresh still writes a cache, but flags it so that a "model not found"
                // answer is not mistaken for "model does not exist" (silent-failure guard).
                return (null, true);
            }
            catch (TaskCanceledException)
            {
                // Request timeout — treat like an unreachable source
                return (null, true);
            }
        }
        else
        {
            // The Octokit path lets network failures propagate loudly; GetFileAsync only
            // returns null for a missing file (404), which means "empty catalog".
            var gitHubClient = CreateGitHubClient();

            var r = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
            if (r == null)
            {
                return (null, false);
            }

            response = r.Value.Item1;
        }

        if (string.IsNullOrEmpty(response) || response == null)
        {
            return (null, false);
        }

        return (System.Text.Json.JsonSerializer.Deserialize<SharedCatalogTypes.RootCatalog>(
            response,
            new System.Text.Json.JsonSerializerOptions
                { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }), false);
    }

    private async Task<SharedCatalogTypes.ModelLibraryCatalog?> GetModelLibraryCatalogAsync(string catalogPath)
    {
        string? response;
        if (IsPagesUriConfigured)
        {
            var httpClient = CreateHttpClient();
            try
            {
                response = await httpClient.GetStringAsync(catalogPath).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Catalog doesn't exist or couldn't be fetched
                return null;
            }
        }
        else
        {
            var gitHubClient = CreateGitHubClient();

            var r = await gitHubClient.GetFileAsync(catalogPath).ConfigureAwait(false);
            if (r == null)
            {
                return null;
            }

            response = r.Value.Item1;
        }

        if (!string.IsNullOrWhiteSpace(response) && response != null)
        {
            var modelLibraryCatalog = System.Text.Json.JsonSerializer
                .Deserialize<SharedCatalogTypes.ModelLibraryCatalog>(
                    response,
                    new System.Text.Json.JsonSerializerOptions
                        { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

            return modelLibraryCatalog;
        }

        return null;
    }

    // See the API-vs-gh-pages note on UpdateModelVersionsCatalogAsync.
    private static async Task UpdateRootCatalogAsync(string root, CkModelId modelId,
        IGitHubClientWrapper gitHubClient)
    {
        var catalogPath = $"{root}{CatalogFileName}";

        await gitHubClient.UpsertFileWithMergeAsync(catalogPath,
                $"Update model catalog for {modelId.Name}",
                existingJson => MergeRootCatalog(existingJson, modelId, root))
            .ConfigureAwait(false);
    }

    internal static string? MergeRootCatalog(string? existingJson, CkModelId modelId,
        string root = CkCatalogLayout.V2Root)
    {
        var catalogData = DeserializeCatalog<SharedCatalogTypes.RootCatalog>(existingJson);

        catalogData ??= new SharedCatalogTypes.RootCatalog
        {
            Version = "1.0",
            UpdatedAt = DateTime.UtcNow,
            Models = []
        };

        var existingEntry = catalogData.Models.FirstOrDefault(m => m.ModelName == modelId.Name);
        if (existingEntry != null)
        {
            return null;
        }

        catalogData.Models.Add(new SharedCatalogTypes.RootCatalogEntry
        {
            ModelName = modelId.Name,
            CatalogPath = $"{CkCatalogLayout.ModelDirectory(root, modelId.Name)}{CatalogFileName}"
        });

        // Sort models alphabetically
        catalogData.Models = catalogData.Models.OrderBy(m => m.ModelName).ToList();
        catalogData.UpdatedAt = DateTime.UtcNow;

        return SerializeCatalog(catalogData);
    }

    // See the API-vs-gh-pages note on UpdateModelVersionsCatalogAsync.
    private static async Task UpdateModelLibraryCatalogAsync(string root, CkModelId modelId,
        IGitHubClientWrapper gitHubClient)
    {
        // Catalog file path for the model
        var catalogPath = $"{CkCatalogLayout.ModelDirectory(root, modelId.Name)}{CatalogFileName}";

        await gitHubClient.UpsertFileWithMergeAsync(catalogPath,
                $"Update model catalog for {modelId.Name}",
                existingJson => MergeModelLibraryCatalog(existingJson, modelId, root))
            .ConfigureAwait(false);
    }

    internal static string? MergeModelLibraryCatalog(string? existingJson, CkModelId modelId,
        string root = CkCatalogLayout.V2Root)
    {
        var catalogData = DeserializeCatalog<SharedCatalogTypes.ModelLibraryCatalog>(existingJson);

        catalogData ??= new SharedCatalogTypes.ModelLibraryCatalog
        {
            ModelId = modelId.Name,
            MajorVersions = new List<SharedCatalogTypes.ModelLibraryCatalogEntry>()
        };

        var currentMajor = modelId.Version.Major;
        var majorVersionEntry = catalogData.MajorVersions.FirstOrDefault(m => m.MajorVersion == currentMajor);
        if (majorVersionEntry != null)
        {
            return null;
        }

        catalogData.MajorVersions.Add(new SharedCatalogTypes.ModelLibraryCatalogEntry
        {
            MajorVersion = currentMajor,
            CatalogPath = $"{CkCatalogLayout.ModelDirectory(root, modelId.Name)}{currentMajor}/{CatalogFileName}"
        });

        catalogData.UpdatedAt = DateTime.UtcNow;

        return SerializeCatalog(catalogData);
    }
}