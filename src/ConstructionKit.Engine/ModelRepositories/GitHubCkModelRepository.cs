using System.Net.Http;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Configuration;
using Microsoft.Extensions.Options;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;

/// <summary>
/// Construction kit model repository for GitHub
/// </summary>
public class GitHubCkModelRepository : ICkModelRepository
{
    private readonly ICkJsonSerializer _ckJsonSerializer;
    private readonly IOptions<GitHubOptions> _gitHubOptions;

    /// <summary>
    /// Creates a new instance of the <see cref="GitHubCkModelRepository"/> class.
    /// </summary>
    public GitHubCkModelRepository(ICkJsonSerializer ckJsonSerializer, IOptions<GitHubOptions> gitHubOptions)
    {
        _ckJsonSerializer = ckJsonSerializer;
        _gitHubOptions = gitHubOptions;
    }

    /// <inheritdoc />
    public int Order => 20;

    /// <inheritdoc />
    public string RepositoryName => "PublicGitHubRepository";

    /// <inheritdoc />
    public string Description => "Public github repository";

    /// <inheritdoc />
    public bool CanWrite => true;

    /// <inheritdoc />
    public bool IsSupportingSourceIdentifier(object? sourceIdentifier = null)
    {
        return sourceIdentifier == null;
    }

    /// <inheritdoc />
    public async Task<ModelExistingResult> IsModelIdExistingAsync(CkModelIdVersionRange modelIdVersionRange, object? sourceIdentifier = null)
    {
        var availableVersions = new List<CkModelId>();

        // GitHub repository structure: ck-models/{first_letter}/{ModelId}/{MajorVersion}/
        var gitHubClient = CreateGitHubClient(false);
        var basePath = $"ck-models/{modelIdVersionRange.ModelId[0].ToString().ToLower()}/{modelIdVersionRange.ModelId}";

        try
        {
            // Get all major version directories
            var directories = await gitHubClient.Repository.Content.GetAllContentsByRef(
                _gitHubOptions.Value.GitHubRepositoryOwner,
                _gitHubOptions.Value.GitHubRepositoryName,
                basePath,
                _gitHubOptions.Value.GitHubRepositoryBranch).ConfigureAwait(false);

            foreach (var directory in directories.Where(d => d.Type == ContentType.Dir))
            {
                // Check if directory name is a valid major version number
                if (int.TryParse(directory.Name, out _))
                {
                    // Get files in this major version directory
                    var files = await gitHubClient.Repository.Content.GetAllContentsByRef(
                        _gitHubOptions.Value.GitHubRepositoryOwner,
                        _gitHubOptions.Value.GitHubRepositoryName,
                        directory.Path,
                        _gitHubOptions.Value.GitHubRepositoryBranch).ConfigureAwait(false);

                    foreach (var file in files.Where(f => f.Type == ContentType.File && f.Name.EndsWith(".json")))
                    {
                        // Extract version from filename: ck-{modelid}-{version}.json
                        var prefix = $"ck-{modelIdVersionRange.ModelId.ToLower()}-";
                        var fileName = Path.GetFileNameWithoutExtension(file.Name);
                        if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            var versionPart = fileName.Substring(prefix.Length);
                            try
                            {
                                // Validate version format
                                _ = new CkVersion(versionPart);
                                var modelId = new CkModelId(modelIdVersionRange.ModelId, versionPart);
                                availableVersions.Add(modelId);
                            }
                            catch (ArgumentOutOfRangeException)
                            {
                                // Skip invalid version strings
                            }
                        }
                    }
                }
            }
        }
        catch (NotFoundException)
        {
            // Model directory doesn't exist
            return new ModelExistingResult { Exists = false };
        }

        if (!availableVersions.Any())
        {
            return new ModelExistingResult { Exists = false };
        }

        // Find the latest version that satisfies the version range
        var satisfiedVersions = availableVersions
            .Where(modelId => modelIdVersionRange.ModelVersionRange.IsSatisfiedBy(modelId.ModelVersion))
            .ToList();

        if (!satisfiedVersions.Any())
        {
            return new ModelExistingResult { Exists = false };
        }

        // Return the latest satisfied version
        var latestSatisfiedVersion = satisfiedVersions
            .OrderByDescending(modelId => modelId.ModelVersion)
            .First();

        return new ModelExistingResult
        {
            Exists = true,
            ModelId = latestSatisfiedVersion
        };
    }

    /// <inheritdoc />
    public async Task<bool> IsModelIdExistingAsync(CkModelId modelId, object? sourceIdentifier = null)
    {
        // Try GitHub Pages first if enabled
        if (IsGitHubPagesEnabled())
        {
            var httpClient = CreateHttpClient();
            var pagesUrl = CreatePath(modelId);
            try
            {
                var response = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, pagesUrl))
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                // Fall back to GitHub API if Pages request fails
            }
        }

        // Fall back to GitHub API
        var contents = await GetContentAsync(modelId).ConfigureAwait(false);
        return contents.Count > 0;
    }

    private async Task<IReadOnlyList<RepositoryContent>> GetContentAsync(CkModelId modelId)
    {
        var gitHubClient = CreateGitHubClient(false);

        var filePath = CreatePath(modelId);

        try
        {
            var contents =
                await gitHubClient.Repository.Content.GetAllContentsByRef(_gitHubOptions.Value.GitHubRepositoryOwner,
                        _gitHubOptions.Value.GitHubRepositoryName, filePath,
                        _gitHubOptions.Value.GitHubRepositoryBranch)
                    .ConfigureAwait(false);
            return contents;
        }
        catch (NotFoundException)
        {
            return new List<RepositoryContent>();
        }
    }

    /// <inheritdoc />
    public async Task<CkCompiledModelRoot> GetModelAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        // Try GitHub Pages first if enabled
        if (IsGitHubPagesEnabled())
        {
            var httpClient = CreateHttpClient();
            var pagesUrl = CreatePath(modelId);
            try
            {
                var response = await httpClient.GetAsync(pagesUrl, cancellationToken ?? CancellationToken.None)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
#if NETSTANDARD2_0
                        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
                    await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
                    var pagesCompiledModelRoot = await _ckJsonSerializer
                        .DeserializeCompiledModelRootAsync(stream, "", operationResult).ConfigureAwait(false);
                    if (operationResult.HasErrors)
                    {
                        throw ModelRepositoryException.ErrorDuringModelLoad(modelId, RepositoryName,
                            operationResult);
                    }

                    return pagesCompiledModelRoot;
                }
            }
            catch (HttpRequestException)
            {
                // Fall back to GitHub API if Pages request fails
            }
            catch (TaskCanceledException)
            {
                // Fall back to GitHub API if request times out
            }
        }

        // Fall back to GitHub API
        if (!await IsModelIdExistingAsync(modelId, sourceIdentifier).ConfigureAwait(false))
        {
            throw ModelRepositoryException.ModelNotFound(modelId, RepositoryName);
        }

        var gitHubClient = CreateGitHubClient(false);

        var filePath = CreatePath(modelId);

        var contents =
            await gitHubClient.Repository.Content.GetAllContentsByRef(_gitHubOptions.Value.GitHubRepositoryOwner,
                    _gitHubOptions.Value.GitHubRepositoryName, filePath, _gitHubOptions.Value.GitHubRepositoryBranch)
                .ConfigureAwait(false);
        if (contents.Count == 0)
        {
            throw ModelRepositoryException.DownloadError(modelId, RepositoryName);
        }

        // Convert the Base64 content to a byte array
        byte[] data = Convert.FromBase64String(contents[0].EncodedContent);

        // Create a MemoryStream from the byte array
        using MemoryStream memoryStream = new MemoryStream(data);
        var apiCompiledModelRoot = await _ckJsonSerializer
            .DeserializeCompiledModelRootAsync(memoryStream, "", operationResult).ConfigureAwait(false);
        if (operationResult.HasErrors)
        {
            throw ModelRepositoryException.ErrorDuringModelLoad(modelId, RepositoryName, operationResult);
        }

        return apiCompiledModelRoot;
    }

    /// <inheritdoc />
    public async Task PublishModelAsync(CkCompiledModelRoot ckCompiledModel, bool force = false,
        bool publishExtensions = false,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        try
        {
            var gitHubClient = CreateGitHubClient(true);

            var content = await ReadContentAsync(ckCompiledModel).ConfigureAwait(false);
            string filePath = CreatePath(ckCompiledModel.ModelId);

            var contents = await GetContentAsync(ckCompiledModel.ModelId).ConfigureAwait(false);
            if (contents.Any())
            {
                if (force)
                {
                    await gitHubClient.Repository.Content.UpdateFile(
                        _gitHubOptions.Value.GitHubRepositoryOwner, _gitHubOptions.Value.GitHubRepositoryName, filePath,
                        new UpdateFileRequest($"Update to {ckCompiledModel.ModelId.FullName}", content,
                            contents.First().Sha)).ConfigureAwait(false);
                }
                else
                {
                    throw ModelRepositoryException.ModelAlreadyExists(ckCompiledModel.ModelId, RepositoryName);
                }
            }
            else
            {
                await gitHubClient.Repository.Content.CreateFile(
                    _gitHubOptions.Value.GitHubRepositoryOwner, _gitHubOptions.Value.GitHubRepositoryName, filePath,
                    new CreateFileRequest($"First commit for {ckCompiledModel.ModelId.FullName}", content,
                        _gitHubOptions.Value.GitHubRepositoryBranch)).ConfigureAwait(false);
            }

            // Update the index file for this major version
            await UpdateMajorVersionIndexAsync(ckCompiledModel.ModelId, gitHubClient).ConfigureAwait(false);

            // Update the overall model index
            await UpdateModelIndexAsync(ckCompiledModel.ModelId, gitHubClient).ConfigureAwait(false);
        }
        catch (ApiValidationException e)
        {
            throw ModelRepositoryException.PublishFailed(ckCompiledModel.ModelId, RepositoryName, e);
        }
    }

    /// <inheritdoc />
    public Task UpdateModelAsync(CkCompiledModelRoot ckCompiledModel, bool publishExtensions = false,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        return PublishModelAsync(ckCompiledModel, false, publishExtensions, sourceIdentifier, cancellationToken);
    }

    /// <inheritdoc />
    public Task CustomizeCkEnumAsync(CkId<CkEnumId> ckEnumId, ICollection<CkEnumUpdate> ckEnumUpdates,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        throw ModelRepositoryException.CustomizationNotSupported(RepositoryName);
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

        string content = await new StreamReader(memoryStream).ReadToEndAsync().ConfigureAwait(false);
        return content;
    }

    private IGitHubClient CreateGitHubClient(bool isWrite)
    {
        var gitHubClient = new GitHubClient(new ProductHeaderValue("OctoMeshCompiler"));
        if (!string.IsNullOrWhiteSpace(_gitHubOptions.Value.GitHubApiToken))
        {
            gitHubClient.Credentials = new Credentials(_gitHubOptions.Value.GitHubApiToken);
        }

        if (isWrite && string.IsNullOrWhiteSpace(_gitHubOptions.Value.GitHubApiToken))
        {
            throw ModelRepositoryException.GitHubTokenMissing();
        }

        return gitHubClient;
    }

    private string CreatePath(CkModelId ckModelId)
    {
        return "ck-models/"
               + ckModelId.ModelId[0].ToString().ToLower() + "/"
               + ckModelId.ModelId + "/"
               + ckModelId.ModelVersion.Major
               + "/ck-" + ckModelId.ModelId.ToLower() + "-" + ckModelId.ModelVersion + ".json";
    }

    private HttpClient CreateHttpClient()
    {
        if (string.IsNullOrWhiteSpace(_gitHubOptions.Value.GitHubPagesUri) ||
            _gitHubOptions.Value.GitHubPagesUri == null)
        {
            throw ModelRepositoryException.GitHubPagesUriMissing();
        }

        var baseUri = _gitHubOptions.Value.GitHubPagesUri.TrimEnd('/');

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri($"{baseUri}")
        };

        return httpClient;
    }

    private bool IsGitHubPagesEnabled()
    {
        return !string.IsNullOrWhiteSpace(_gitHubOptions.Value.GitHubPagesUri);
    }

    /// <summary>
    /// Gets the index for a specific major version of a model
    /// </summary>
    /// <param name="modelId">The model ID (without version)</param>
    /// <param name="majorVersion">The major version number</param>
    /// <param name="sourceIdentifier">Optional source identifier</param>
    /// <returns>The major version index content or null if not found</returns>
    public async Task<string?> GetMajorVersionIndexAsync(string modelId, int majorVersion, object? sourceIdentifier = null)
    {
        var indexPath = $"ck-models/{modelId[0].ToString().ToLower()}/{modelId}/{majorVersion}/index.json";

        // Try GitHub Pages first if enabled
        if (IsGitHubPagesEnabled())
        {
            var httpClient = CreateHttpClient();
            try
            {
                var response = await httpClient.GetStringAsync(indexPath).ConfigureAwait(false);
                return response;
            }
            catch (HttpRequestException)
            {
                // Fall back to GitHub API
            }
        }

        // Use GitHub API
        var gitHubClient = CreateGitHubClient(false);
        try
        {
            var contents = await gitHubClient.Repository.Content.GetAllContentsByRef(
                _gitHubOptions.Value.GitHubRepositoryOwner,
                _gitHubOptions.Value.GitHubRepositoryName,
                indexPath,
                _gitHubOptions.Value.GitHubRepositoryBranch).ConfigureAwait(false);

            if (contents.Any())
            {
                var data = Convert.FromBase64String(contents[0].EncodedContent);
                return System.Text.Encoding.UTF8.GetString(data);
            }
        }
        catch (NotFoundException)
        {
            // Index file doesn't exist
        }

        return null;
    }

    /// <summary>
    /// Gets the overall index for a model showing all major versions
    /// </summary>
    /// <param name="modelId">The model ID (without version)</param>
    /// <param name="sourceIdentifier">Optional source identifier</param>
    /// <returns>The model index content or null if not found</returns>
    public async Task<string?> GetModelIndexAsync(string modelId, object? sourceIdentifier = null)
    {
        var indexPath = $"ck-models/{modelId[0].ToString().ToLower()}/{modelId}/index.json";

        // Try GitHub Pages first if enabled
        if (IsGitHubPagesEnabled())
        {
            var httpClient = CreateHttpClient();
            try
            {
                var response = await httpClient.GetStringAsync(indexPath).ConfigureAwait(false);
                return response;
            }
            catch (HttpRequestException)
            {
                // Fall back to GitHub API
            }
        }

        // Use GitHub API
        var gitHubClient = CreateGitHubClient(false);
        try
        {
            var contents = await gitHubClient.Repository.Content.GetAllContentsByRef(
                _gitHubOptions.Value.GitHubRepositoryOwner,
                _gitHubOptions.Value.GitHubRepositoryName,
                indexPath,
                _gitHubOptions.Value.GitHubRepositoryBranch).ConfigureAwait(false);

            if (contents.Any())
            {
                var data = Convert.FromBase64String(contents[0].EncodedContent);
                return System.Text.Encoding.UTF8.GetString(data);
            }
        }
        catch (NotFoundException)
        {
            // Index file doesn't exist
        }

        return null;
    }

    private async Task UpdateMajorVersionIndexAsync(CkModelId modelId, IGitHubClient gitHubClient)
    {
        // Create index file path for this major version
        var indexPath = $"ck-models/{modelId.ModelId[0].ToString().ToLower()}/{modelId.ModelId}/{modelId.ModelVersion.Major}/index.json";

        // Get all files in this major version directory to build the index
        var majorVersionPath = $"ck-models/{modelId.ModelId[0].ToString().ToLower()}/{modelId.ModelId}/{modelId.ModelVersion.Major}";

        var versions = new List<VersionIndexEntry>();

        try
        {
            // Get all files in the major version directory
            var files = await gitHubClient.Repository.Content.GetAllContentsByRef(
                _gitHubOptions.Value.GitHubRepositoryOwner,
                _gitHubOptions.Value.GitHubRepositoryName,
                majorVersionPath,
                _gitHubOptions.Value.GitHubRepositoryBranch).ConfigureAwait(false);

            // Extract version information from each model file
            foreach (var file in files.Where(f => f.Type == ContentType.File && f.Name.EndsWith(".json") && f.Name != "index.json"))
            {
                var prefix = $"ck-{modelId.ModelId.ToLower()}-";
                var fileName = Path.GetFileNameWithoutExtension(file.Name);
                if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var versionPart = fileName.Substring(prefix.Length);
                    try
                    {
                        // Validate version format
                        _ = new CkVersion(versionPart);
                        versions.Add(new VersionIndexEntry
                        {
                            Version = versionPart,
                            FileName = file.Name,
                            PublishedAt = DateTime.UtcNow, // Use current time as GitHub API doesn't provide creation date easily
                            FilePath = file.Path
                        });
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Skip invalid version strings
                    }
                }
            }
        }
        catch (NotFoundException)
        {
            // Directory doesn't exist yet, will be created with the first file
        }

        // Sort versions in descending order (latest first)
        versions = versions.OrderByDescending(v => new CkVersion(v.Version)).ToList();

        // Create index content
        var index = new MajorVersionIndex
        {
            ModelId = modelId.ModelId,
            MajorVersion = modelId.ModelVersion.Major,
            LatestVersion = versions.FirstOrDefault()?.Version,
            Versions = versions,
            UpdatedAt = DateTime.UtcNow
        };

        // Serialize index to JSON
        var indexContent = System.Text.Json.JsonSerializer.Serialize(index, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        // Check if index file exists
        try
        {
            var existingIndex = await gitHubClient.Repository.Content.GetAllContentsByRef(
                _gitHubOptions.Value.GitHubRepositoryOwner,
                _gitHubOptions.Value.GitHubRepositoryName,
                indexPath,
                _gitHubOptions.Value.GitHubRepositoryBranch).ConfigureAwait(false);

            if (existingIndex.Any())
            {
                // Update existing index
                await gitHubClient.Repository.Content.UpdateFile(
                    _gitHubOptions.Value.GitHubRepositoryOwner,
                    _gitHubOptions.Value.GitHubRepositoryName,
                    indexPath,
                    new UpdateFileRequest($"Update index for {modelId.ModelId} v{modelId.ModelVersion.Major}", indexContent, existingIndex.First().Sha))
                    .ConfigureAwait(false);
            }
            else
            {
                // This shouldn't happen but handle it anyway
                await CreateIndexFile(gitHubClient, indexPath, indexContent, modelId).ConfigureAwait(false);
            }
        }
        catch (NotFoundException)
        {
            // Create new index file
            await CreateIndexFile(gitHubClient, indexPath, indexContent, modelId).ConfigureAwait(false);
        }
    }

    private async Task CreateIndexFile(IGitHubClient gitHubClient, string indexPath, string indexContent, CkModelId modelId)
    {
        await gitHubClient.Repository.Content.CreateFile(
            _gitHubOptions.Value.GitHubRepositoryOwner,
            _gitHubOptions.Value.GitHubRepositoryName,
            indexPath,
            new CreateFileRequest($"Create index for {modelId.ModelId} v{modelId.ModelVersion.Major}", indexContent, _gitHubOptions.Value.GitHubRepositoryBranch))
            .ConfigureAwait(false);
    }

    private class MajorVersionIndex
    {
        public string ModelId { get; set; } = string.Empty;
        public int MajorVersion { get; set; }
        public string? LatestVersion { get; set; }
        public List<VersionIndexEntry> Versions { get; set; } = new();
        public DateTime UpdatedAt { get; set; }
    }

    private class VersionIndexEntry
    {
        public string Version { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
    }

    private async Task UpdateModelIndexAsync(CkModelId modelId, IGitHubClient gitHubClient)
    {
        // Create index file path for the model
        var indexPath = $"ck-models/{modelId.ModelId[0].ToString().ToLower()}/{modelId.ModelId}/index.json";
        var modelBasePath = $"ck-models/{modelId.ModelId[0].ToString().ToLower()}/{modelId.ModelId}";

        var majorVersions = new List<MajorVersionEntry>();

        try
        {
            // Get all major version directories
            var directories = await gitHubClient.Repository.Content.GetAllContentsByRef(
                _gitHubOptions.Value.GitHubRepositoryOwner,
                _gitHubOptions.Value.GitHubRepositoryName,
                modelBasePath,
                _gitHubOptions.Value.GitHubRepositoryBranch).ConfigureAwait(false);

            foreach (var directory in directories.Where(d => d.Type == ContentType.Dir))
            {
                // Check if directory name is a valid major version number
                if (int.TryParse(directory.Name, out var majorVersionNumber))
                {
                    var versions = new List<string>();

                    // Get all files in this major version directory
                    var files = await gitHubClient.Repository.Content.GetAllContentsByRef(
                        _gitHubOptions.Value.GitHubRepositoryOwner,
                        _gitHubOptions.Value.GitHubRepositoryName,
                        directory.Path,
                        _gitHubOptions.Value.GitHubRepositoryBranch).ConfigureAwait(false);

                    foreach (var file in files.Where(f => f.Type == ContentType.File && f.Name.EndsWith(".json") && f.Name != "index.json"))
                    {
                        var prefix = $"ck-{modelId.ModelId.ToLower()}-";
                        var fileName = Path.GetFileNameWithoutExtension(file.Name);
                        if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            var versionPart = fileName.Substring(prefix.Length);
                            try
                            {
                                // Validate version format
                                _ = new CkVersion(versionPart);
                                versions.Add(versionPart);
                            }
                            catch (ArgumentOutOfRangeException)
                            {
                                // Skip invalid version strings
                            }
                        }
                    }

                    if (versions.Any())
                    {
                        // Sort versions to get the latest
                        var latestVersion = versions
                            .OrderByDescending(v => new CkVersion(v))
                            .FirstOrDefault();

                        majorVersions.Add(new MajorVersionEntry
                        {
                            MajorVersion = majorVersionNumber,
                            LatestVersion = latestVersion,
                            AvailableVersionsCount = versions.Count,
                            IndexPath = $"{directory.Path}/index.json"
                        });
                    }
                }
            }
        }
        catch (NotFoundException)
        {
            // Model directory doesn't exist yet
            return;
        }

        if (!majorVersions.Any())
        {
            // No versions found
            return;
        }

        // Sort major versions in descending order
        majorVersions = majorVersions.OrderByDescending(v => v.MajorVersion).ToList();

        // Create overall index content
        var modelIndex = new ModelIndex
        {
            ModelId = modelId.ModelId,
            LatestVersion = majorVersions.FirstOrDefault()?.LatestVersion,
            MajorVersions = majorVersions,
            UpdatedAt = DateTime.UtcNow
        };

        // Serialize index to JSON
        var indexContent = System.Text.Json.JsonSerializer.Serialize(modelIndex, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        // Check if index file exists
        try
        {
            var existingIndex = await gitHubClient.Repository.Content.GetAllContentsByRef(
                _gitHubOptions.Value.GitHubRepositoryOwner,
                _gitHubOptions.Value.GitHubRepositoryName,
                indexPath,
                _gitHubOptions.Value.GitHubRepositoryBranch).ConfigureAwait(false);

            if (existingIndex.Any())
            {
                // Update existing index
                await gitHubClient.Repository.Content.UpdateFile(
                    _gitHubOptions.Value.GitHubRepositoryOwner,
                    _gitHubOptions.Value.GitHubRepositoryName,
                    indexPath,
                    new UpdateFileRequest($"Update model index for {modelId.ModelId}", indexContent, existingIndex.First().Sha))
                    .ConfigureAwait(false);
            }
            else
            {
                // Create new index file
                await gitHubClient.Repository.Content.CreateFile(
                    _gitHubOptions.Value.GitHubRepositoryOwner,
                    _gitHubOptions.Value.GitHubRepositoryName,
                    indexPath,
                    new CreateFileRequest($"Create model index for {modelId.ModelId}", indexContent, _gitHubOptions.Value.GitHubRepositoryBranch))
                    .ConfigureAwait(false);
            }
        }
        catch (NotFoundException)
        {
            // Create new index file
            await gitHubClient.Repository.Content.CreateFile(
                _gitHubOptions.Value.GitHubRepositoryOwner,
                _gitHubOptions.Value.GitHubRepositoryName,
                indexPath,
                new CreateFileRequest($"Create model index for {modelId.ModelId}", indexContent, _gitHubOptions.Value.GitHubRepositoryBranch))
                .ConfigureAwait(false);
        }
    }

    private class ModelIndex
    {
        public string ModelId { get; set; } = string.Empty;
        public string? LatestVersion { get; set; }
        public List<MajorVersionEntry> MajorVersions { get; set; } = new();
        public DateTime UpdatedAt { get; set; }
    }

    private class MajorVersionEntry
    {
        public int MajorVersion { get; set; }
        public string? LatestVersion { get; set; }
        public int AvailableVersionsCount { get; set; }
        public string IndexPath { get; set; } = string.Empty;
    }
}