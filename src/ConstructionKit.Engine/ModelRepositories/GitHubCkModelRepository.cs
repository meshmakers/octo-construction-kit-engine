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
}