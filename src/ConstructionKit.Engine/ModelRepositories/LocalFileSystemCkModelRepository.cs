using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;

/// <summary>
///     CkModel repository that uses the local file system to store the compiled models.
/// </summary>
public class LocalFileSystemCkModelRepository : ICkModelRepository
{
    private readonly ICkJsonSerializer _ckJsonSerializer;
    private readonly IOptions<LocalCkModelRepositoryOptions> _options;

    /// <summary>
    ///     Creates a new instance of the <see cref="LocalFileSystemCkModelRepository" /> class.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="ckJsonSerializer"></param>
    public LocalFileSystemCkModelRepository(IOptions<LocalCkModelRepositoryOptions> options,
        ICkJsonSerializer ckJsonSerializer)
    {
        _options = options;
        _ckJsonSerializer = ckJsonSerializer;
    }

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public string RepositoryName => "LocalRepository";

    /// <inheritdoc />
    public string Description => $"Local file system repository at '{_options.Value.RootPath}'";

    /// <inheritdoc />
    public bool CanWrite => true;

    /// <inheritdoc />
    public bool IsSupportingSourceIdentifier(object? sourceIdentifier = null)
    {
        return sourceIdentifier == null;
    }

    /// <inheritdoc />
    public Task<ModelExistingResult> IsModelIdExistingAsync(CkModelIdVersionRange modelIdVersionRange, object? sourceIdentifier = null)
    {
        var rootPath = _options.Value.RootPath;
        var modelPath = Path.Combine(rootPath, "ck-models", modelIdVersionRange.ModelId);

        if (!Directory.Exists(modelPath))
        {
            return Task.FromResult(new ModelExistingResult { Exists = false });
        }

        // Get all major version directories
        var majorVersionDirectories = Directory.GetDirectories(modelPath)
            .Where(dir => int.TryParse(Path.GetFileName(dir), out _))
            .ToList();

        if (!majorVersionDirectories.Any())
        {
            return Task.FromResult(new ModelExistingResult { Exists = false });
        }

        var availableVersions = new List<CkModelId>();

        // Scan each major version directory for versioned model files
        foreach (var majorVersionDir in majorVersionDirectories)
        {
            // Look for versioned model files: ck-{modelid}-{version}.json
            var modelFiles = Directory.GetFiles(majorVersionDir, $"ck-{modelIdVersionRange.ModelId.ToLower()}-*.json");

            foreach (var modelFile in modelFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(modelFile);
                // Extract version from filename: ck-{modelid}-{version}.json
                var prefix = $"ck-{modelIdVersionRange.ModelId.ToLower()}-";
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

        if (!availableVersions.Any())
        {
            return Task.FromResult(new ModelExistingResult { Exists = false });
        }

        // Find the latest version that satisfies the version range
        var satisfiedVersions = availableVersions
            .Where(modelId => modelIdVersionRange.ModelVersionRange.IsSatisfiedBy(modelId.Version))
            .ToList();

        if (!satisfiedVersions.Any())
        {
            return Task.FromResult(new ModelExistingResult { Exists = false });
        }

        // Return the latest satisfied version
        var latestSatisfiedVersion = satisfiedVersions
            .OrderByDescending(modelId => modelId.Version)
            .First();

        return Task.FromResult(new ModelExistingResult
        {
            Exists = true,
            ModelId = latestSatisfiedVersion
        });
    }

    /// <inheritdoc />
    public Task<bool> IsModelIdExistingAsync(CkModelId modelId, object? sourceIdentifier = null)
    {
        if (!TryGetExistingModelPath(modelId, out _))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<CkCompiledModelRoot> GetModelAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        if (!TryGetExistingModelPath(modelId, out var compiledModelFilePath) || compiledModelFilePath == null)
        {
            throw ModelRepositoryException.ModelNotFound(modelId, RepositoryName);
        }

#if NETSTANDARD2_0
        using var streamReader = File.OpenRead(compiledModelFilePath);
#else
        await using var streamReader = File.OpenRead(compiledModelFilePath);
#endif
        var compiledModelRoot = await _ckJsonSerializer
            .DeserializeCompiledModelRootAsync(streamReader, compiledModelFilePath, operationResult)
            .ConfigureAwait(false);
        if (operationResult.HasErrors)
        {
            throw ModelRepositoryException.ErrorDuringModelLoad(modelId, RepositoryName, operationResult);
        }

        return compiledModelRoot;
    }

    /// <inheritdoc />
    public async Task PublishModelAsync(CkCompiledModelRoot ckCompiledModel, bool force = false,
        bool publishExtensions = false, object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        var compiledModelFilePath = CreatePath(ckCompiledModel.ModelId);
        if (File.Exists(compiledModelFilePath) && !force)
        {
            throw ModelRepositoryException.ModelAlreadyExists(ckCompiledModel.ModelId, RepositoryName);
        }

        var path = Path.GetDirectoryName(compiledModelFilePath)!;
        Directory.CreateDirectory(path);

        var tempFilePath = Path.GetTempFileName();

        try
        {
#if NETSTANDARD2_0
            using var streamWriter = new StreamWriter(tempFilePath);
#else
            await using var streamWriter = new StreamWriter(tempFilePath);
#endif
            await _ckJsonSerializer.SerializeAsync(streamWriter, ckCompiledModel).ConfigureAwait(false);
            streamWriter.Close();
        }
        catch (Exception e)
        {
            throw ModelRepositoryException.PublishFailed(ckCompiledModel.ModelId, RepositoryName, e);
        }

        var i = 0;
        while (i++ < 20)
        {
            try
            {
                File.Copy(tempFilePath, compiledModelFilePath, true);
                return;
            }
            catch (Exception)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public Task UpdateModelAsync(CkCompiledModelRoot ckCompiledModel, bool publishExtensions = false,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        return PublishModelAsync(ckCompiledModel, true, publishExtensions, sourceIdentifier, cancellationToken);
    }

    /// <inheritdoc />
    public Task CustomizeCkEnumAsync(CkId<CkEnumId> ckEnumId, ICollection<CkEnumUpdate> ckEnumUpdates, object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        throw ModelRepositoryException.CustomizationNotSupported(RepositoryName);
    }

    private string CreatePath(CkModelId ckModelId)
    {
        var rootPath = _options.Value.RootPath;
        var modelPath = Path.Combine(rootPath, "ck-models", ckModelId.Name);
        var modelVersionPath = Path.Combine(modelPath, ckModelId.Version.Major.ToString());
        // Include full version in filename: ck-{modelid}-{version}.json
        var compiledModelFile = $"ck-{ckModelId.Name.ToLower()}-{ckModelId.Version}.json";
        var compiledModelFilePath = Path.Combine(modelVersionPath, compiledModelFile);
        return compiledModelFilePath;
    }

    private bool TryGetExistingModelPath(CkModelId ckModelId, out string? compiledModelFilePath)
    {
        compiledModelFilePath = CreatePath(ckModelId);
        if (!File.Exists(compiledModelFilePath))
        {
            compiledModelFilePath = null;
            return false;
        }

        return true;
    }
}