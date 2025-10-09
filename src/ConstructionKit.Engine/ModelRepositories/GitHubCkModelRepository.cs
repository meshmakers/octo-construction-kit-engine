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

        // First, try to use the model index file for efficient lookup
        var modelIndexJson = await GetModelIndexAsync(modelIdVersionRange.ModelId, sourceIdentifier).ConfigureAwait(false);

        if (modelIndexJson != null)
        {
            try
            {
                // Parse the model index
                var modelIndex = System.Text.Json.JsonSerializer.Deserialize<ModelIndex>(
                    modelIndexJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

                if (modelIndex?.MajorVersions != null && modelIndex.MajorVersions.Any())
                {
                    // Check each major version that could potentially satisfy the range
                    foreach (var majorVersion in modelIndex.MajorVersions)
                    {
                        // Get the major version index for detailed version information
                        var majorIndexJson = await GetMajorVersionIndexAsync(
                            modelIdVersionRange.ModelId,
                            majorVersion.MajorVersion,
                            sourceIdentifier).ConfigureAwait(false);

                        if (majorIndexJson != null)
                        {
                            var majorIndex = System.Text.Json.JsonSerializer.Deserialize<MajorVersionIndex>(
                                majorIndexJson,
                                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

                            if (majorIndex?.Versions != null)
                            {
                                // Add all versions from this major version
                                foreach (var version in majorIndex.Versions)
                                {
                                    try
                                    {
                                        var modelId = new CkModelId(modelIdVersionRange.ModelId, version.Version);
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

                    if (availableVersions.Any())
                    {
                        // Find the latest version that satisfies the version range
                        var satisfiedVersions = availableVersions
                            .Where(modelId => modelIdVersionRange.ModelVersionRange.IsSatisfiedBy(modelId.Version))
                            .ToList();

                        if (satisfiedVersions.Any())
                        {
                            // Return the latest satisfied version
                            var latestSatisfiedVersion = satisfiedVersions
                                .OrderByDescending(modelId => modelId.Version)
                                .First();

                            return new ModelExistingResult
                            {
                                Exists = true,
                                ModelId = latestSatisfiedVersion
                            };
                        }
                    }
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Fall back to directory scanning if index parsing fails
                availableVersions.Clear();
            }
        }

        if (!availableVersions.Any())
        {
            return new ModelExistingResult { Exists = false };
        }

        // Find the latest version that satisfies the version range
        var satisfiedVersionList = availableVersions
            .Where(modelId => modelIdVersionRange.ModelVersionRange.IsSatisfiedBy(modelId.Version))
            .ToList();

        if (!satisfiedVersionList.Any())
        {
            return new ModelExistingResult { Exists = false };
        }

        // Return the latest satisfied version
        var latestSatisfied = satisfiedVersionList
            .OrderByDescending(modelId => modelId.Version)
            .First();

        return new ModelExistingResult
        {
            Exists = true,
            ModelId = latestSatisfied
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
               + ckModelId.Name[0].ToString().ToLower() + "/"
               + ckModelId.Name + "/"
               + ckModelId.Version.Major
               + "/ck-" + ckModelId.Name.ToLower() + "-" + ckModelId.Version + ".json";
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
        var indexPath = $"ck-models/{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{modelId.Version.Major}/index.json";

        MajorVersionIndex? existingIndexData = null;
        string? existingIndexSha = null;

        // Try to load existing index first
        try
        {
            var existingIndexContent = await gitHubClient.Repository.Content.GetAllContentsByRef(
                _gitHubOptions.Value.GitHubRepositoryOwner,
                _gitHubOptions.Value.GitHubRepositoryName,
                indexPath,
                _gitHubOptions.Value.GitHubRepositoryBranch).ConfigureAwait(false);

            if (existingIndexContent.Any())
            {
                existingIndexSha = existingIndexContent.First().Sha;
                var data = Convert.FromBase64String(existingIndexContent.First().EncodedContent);
                var jsonString = System.Text.Encoding.UTF8.GetString(data);
                existingIndexData = System.Text.Json.JsonSerializer.Deserialize<MajorVersionIndex>(
                    jsonString,
                    new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            }
        }
        catch (NotFoundException)
        {
            // Index doesn't exist yet, will create new one
        }

        // Create a dictionary to merge versions (preserving timestamps)
        var versionDict = new Dictionary<string, VersionIndexEntry>();

        // Add existing versions first (preserving their timestamps)
        if (existingIndexData?.Versions != null)
        {
            foreach (var version in existingIndexData.Versions)
            {
                versionDict[version.Version] = version;
            }
        }

        // Check if the current version already exists in the index
        var currentVersionString = modelId.Version.ToString();
        if (!versionDict.ContainsKey(currentVersionString))
        {
            // Add the new version only if it doesn't exist
            var fileName = $"ck-{modelId.Name.ToLower()}-{currentVersionString}.json";
            var filePath = $"ck-models/{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{modelId.Version.Major}/{fileName}";

            versionDict[currentVersionString] = new VersionIndexEntry
            {
                Version = currentVersionString,
                FileName = fileName,
                PublishedAt = DateTime.UtcNow,
                FilePath = filePath
            };
        }

        // Sort versions in descending order (latest first)
        var sortedVersions = versionDict.Values
            .OrderByDescending(v => new CkVersion(v.Version))
            .ToList();

        // Create updated index
        var index = new MajorVersionIndex
        {
            ModelId = modelId.Name,
            MajorVersion = modelId.Version.Major,
            LatestVersion = sortedVersions.FirstOrDefault()?.Version,
            Versions = sortedVersions,
            UpdatedAt = existingIndexData?.UpdatedAt ?? DateTime.UtcNow // Preserve original creation time if exists
        };

        // Only update the UpdatedAt if we actually added a new version
        if (!versionDict.ContainsKey(currentVersionString) || existingIndexData == null)
        {
            index.UpdatedAt = DateTime.UtcNow;
        }

        // Serialize index to JSON
        var indexContent = System.Text.Json.JsonSerializer.Serialize(index, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        // Update or create the index file
        if (existingIndexSha != null)
        {
            // Update existing index
            await gitHubClient.Repository.Content.UpdateFile(
                _gitHubOptions.Value.GitHubRepositoryOwner,
                _gitHubOptions.Value.GitHubRepositoryName,
                indexPath,
                new UpdateFileRequest($"Update index for {modelId.Name} v{modelId.Version.Major}", indexContent, existingIndexSha))
                .ConfigureAwait(false);
        }
        else
        {
            // Create new index file
            await gitHubClient.Repository.Content.CreateFile(
                _gitHubOptions.Value.GitHubRepositoryOwner,
                _gitHubOptions.Value.GitHubRepositoryName,
                indexPath,
                new CreateFileRequest($"Create index for {modelId.Name} v{modelId.Version.Major}", indexContent, _gitHubOptions.Value.GitHubRepositoryBranch))
                .ConfigureAwait(false);
        }
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
        var indexPath = $"ck-models/{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/index.json";

        ModelIndex? existingIndexData = null;
        string? existingIndexSha = null;
        DateTime? originalCreatedAt = null;

        // Try to load existing index first
        try
        {
            var existingIndexContent = await gitHubClient.Repository.Content.GetAllContentsByRef(
                _gitHubOptions.Value.GitHubRepositoryOwner,
                _gitHubOptions.Value.GitHubRepositoryName,
                indexPath,
                _gitHubOptions.Value.GitHubRepositoryBranch).ConfigureAwait(false);

            if (existingIndexContent.Any())
            {
                existingIndexSha = existingIndexContent.First().Sha;
                var data = Convert.FromBase64String(existingIndexContent.First().EncodedContent);
                var jsonString = System.Text.Encoding.UTF8.GetString(data);
                existingIndexData = System.Text.Json.JsonSerializer.Deserialize<ModelIndex>(
                    jsonString,
                    new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                originalCreatedAt = existingIndexData?.UpdatedAt;
            }
        }
        catch (NotFoundException)
        {
            // Index doesn't exist yet, will create new one
        }

        // Check or update the entry for the current major version
        var currentMajor = modelId.Version.Major;
        var majorVersionEntry = existingIndexData?.MajorVersions?.FirstOrDefault(m => m.MajorVersion == currentMajor);

        if (majorVersionEntry == null)
        {
            // This is a new major version
            var majorVersions = new List<MajorVersionEntry>();

            // Preserve existing major versions from the index
            if (existingIndexData?.MajorVersions != null)
            {
                majorVersions.AddRange(existingIndexData.MajorVersions);
            }

            // Add or update the current major version
            var existingEntry = majorVersions.FirstOrDefault(m => m.MajorVersion == currentMajor);
            if (existingEntry != null)
            {
                // Update the existing entry
                existingEntry.LatestVersion = modelId.Version.ToString();
                existingEntry.AvailableVersionsCount++; // Increment count
            }
            else
            {
                // Add new major version entry
                majorVersions.Add(new MajorVersionEntry
                {
                    MajorVersion = currentMajor,
                    LatestVersion = modelId.Version.ToString(),
                    AvailableVersionsCount = 1,
                    IndexPath = $"ck-models/{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{currentMajor}/index.json"
                });
            }

            // Sort major versions in descending order
            majorVersions = majorVersions.OrderByDescending(v => v.MajorVersion).ToList();

            // Create updated index
            var modelIndex = new ModelIndex
            {
                ModelId = modelId.Name,
                LatestVersion = majorVersions.FirstOrDefault()?.LatestVersion,
                MajorVersions = majorVersions,
                UpdatedAt = originalCreatedAt ?? DateTime.UtcNow
            };

            // Update timestamp only if we actually modified something
            if (existingEntry == null || existingIndexData == null)
            {
                modelIndex.UpdatedAt = DateTime.UtcNow;
            }

            // Serialize index to JSON
            var indexContent = System.Text.Json.JsonSerializer.Serialize(modelIndex, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

            // Update or create the index file
            if (existingIndexSha != null)
            {
                // Update existing index
                await gitHubClient.Repository.Content.UpdateFile(
                    _gitHubOptions.Value.GitHubRepositoryOwner,
                    _gitHubOptions.Value.GitHubRepositoryName,
                    indexPath,
                    new UpdateFileRequest($"Update model index for {modelId.Name}", indexContent, existingIndexSha))
                    .ConfigureAwait(false);
            }
            else
            {
                // Create new index file
                await gitHubClient.Repository.Content.CreateFile(
                    _gitHubOptions.Value.GitHubRepositoryOwner,
                    _gitHubOptions.Value.GitHubRepositoryName,
                    indexPath,
                    new CreateFileRequest($"Create model index for {modelId.Name}", indexContent, _gitHubOptions.Value.GitHubRepositoryBranch))
                    .ConfigureAwait(false);
            }
        }
        else
        {
            // Major version already exists, just check if we need to update the latest version
            var currentVersionObj = new CkVersion(modelId.Version.ToString());
            var existingLatestObj = new CkVersion(majorVersionEntry.LatestVersion ?? "0.0.0");

            if (currentVersionObj.CompareTo(existingLatestObj) > 0)
            {
                // Current version is newer, update the index
                majorVersionEntry.LatestVersion = modelId.Version.ToString();

                // Check if we need to update the overall latest version
                var overallLatest = existingIndexData!.MajorVersions
                    .Where(m => m.LatestVersion != null)
                    .OrderByDescending(m => m.MajorVersion)
                    .FirstOrDefault()?.LatestVersion;

                var modelIndex = new ModelIndex
                {
                    ModelId = modelId.Name,
                    LatestVersion = overallLatest,
                    MajorVersions = existingIndexData.MajorVersions,
                    UpdatedAt = DateTime.UtcNow
                };

                // Serialize index to JSON
                var indexContent = System.Text.Json.JsonSerializer.Serialize(modelIndex, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

                // Update existing index
                await gitHubClient.Repository.Content.UpdateFile(
                    _gitHubOptions.Value.GitHubRepositoryOwner,
                    _gitHubOptions.Value.GitHubRepositoryName,
                    indexPath,
                    new UpdateFileRequest($"Update model index for {modelId.Name}", indexContent, existingIndexSha!))
                    .ConfigureAwait(false);
            }
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