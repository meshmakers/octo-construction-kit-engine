using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Octokit;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;

/// <summary>
/// Construction kit model repository for GitHub
/// </summary>
public class GitHubCkModelRepository : ICkModelRepository
{
    private const string GitRepositoryOwner = "meshmakers";
    private const string GitRepositoryName = "construction-kit-libraries";
    private const string GitRepositoryBranch = "main";

    private readonly ICkJsonSerializer _ckJsonSerializer;

    /// <summary>
    /// Creates a new instance of the <see cref="GitHubCkModelRepository"/> class.
    /// </summary>
    public GitHubCkModelRepository(ICkJsonSerializer ckJsonSerializer)
    {
        _ckJsonSerializer = ckJsonSerializer;
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
    public async Task<bool> IsModelIdExistingAsync(CkModelId modelId, object? sourceIdentifier = null)
    {
        var gitHubClient = CreateClient();

        var filePath = CreatePath(modelId);

        var contents =
            await gitHubClient.Repository.Content.GetAllContentsByRef(GitRepositoryOwner, GitRepositoryName, filePath, GitRepositoryBranch)
                .ConfigureAwait(false);

        return contents.Count > 0;
    }

    /// <inheritdoc />
    public async Task<CkCompiledModelRoot> GetModelAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        if (!await IsModelIdExistingAsync(modelId, sourceIdentifier).ConfigureAwait(false))
        {
            throw ModelRepositoryException.ModelNotFound(modelId, RepositoryName);
        }

        var gitHubClient = CreateClient();

        var filePath = CreatePath(modelId);

        var contents =
            await gitHubClient.Repository.Content.GetAllContentsByRef(GitRepositoryOwner, GitRepositoryName, filePath, GitRepositoryBranch)
                .ConfigureAwait(false);
        if (contents.Count == 0)
        {
            throw ModelRepositoryException.DownloadError(modelId, RepositoryName);
        }

        // Convert the Base64 content to byte array
        byte[] data = Convert.FromBase64String(contents[0].EncodedContent);

        // Create a MemoryStream from the byte array
        using (MemoryStream memoryStream = new MemoryStream(data))
        {
            var compiledModelRoot = await _ckJsonSerializer
                .DeserializeCompiledModelRootAsync(memoryStream, "", operationResult).ConfigureAwait(false);
            if (operationResult.HasErrors)
            {
                throw ModelRepositoryException.ErrorDuringModelLoad(modelId, RepositoryName, operationResult);
            }

            return compiledModelRoot;
        }
    }

    /// <inheritdoc />
    public async Task PublishModelAsync(CkCompiledModelRoot ckCompiledModel, bool force = false, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        try
        {
            var gitHubClient = CreateClient();

            var content = await ReadContentAsync(ckCompiledModel).ConfigureAwait(false);
            string filePath = CreatePath(ckCompiledModel.ModelId);

            await gitHubClient.Repository.Content.CreateFile(
                GitRepositoryOwner, GitRepositoryName, filePath,
                new CreateFileRequest($"First commit for {filePath}", content, GitRepositoryBranch)).ConfigureAwait(false);
        }
        catch (ApiValidationException)
        {
            throw ModelRepositoryException.ModelAlreadyExists(ckCompiledModel.ModelId, RepositoryName);
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

        string content = await new StreamReader(memoryStream).ReadToEndAsync().ConfigureAwait(false);
        return content;
    }

    /// <inheritdoc />
    public Task UpdateModelAsync(CkCompiledModelRoot ckCompiledModel, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        throw new NotImplementedException();
    }

    private IGitHubClient CreateClient()
    {
        var gitHubClient = new GitHubClient(new ProductHeaderValue("OctoMeshCompiler"))
        {
            Credentials = new Credentials("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")
        };

        return gitHubClient;
    }

    private string CreatePath(CkModelId ckModelId)
    {
        return "ck-models/"
               + ckModelId.ModelId[0].ToString().ToLower() + "/"
               + ckModelId.ModelId + "/"
               + ckModelId.ModelVersion.Major
               + "/ck-" + ckModelId.SemanticVersionedFullName.ToLower() + ".json";
    }
}