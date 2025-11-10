using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
///     CkModel catalog that uses the local file system to store the compiled models.
/// </summary>
public class LocalFileSystemCatalog : CachedCatalog
{
    /// <summary>
    /// Defines the name of the catalog for local construction kit models.
    /// </summary>
    public const string Name = "LocalFileSystemCatalog";

    private const string RootPath = "ck-models/v2/";
    private const string CatalogFileName = "catalog.json";

    private readonly ICkJsonSerializer _ckJsonSerializer;
    private readonly IOptions<LocalFileSystemCatalogOptions> _options;

    /// <summary>
    ///     Creates a new instance of the <see cref="LocalFileSystemCatalog" /> class.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="ckJsonSerializer"></param>
    public LocalFileSystemCatalog(IOptions<LocalFileSystemCatalogOptions> options,
        ICkJsonSerializer ckJsonSerializer) : base(10, Name,
        $"Local file system catalog at '{options.Value.RootPath}'", true, options.Value.IsEnabled, options.Value)
    {
        _options = options;
        _ckJsonSerializer = ckJsonSerializer;
    }

    /// <inheritdoc />
    public override async Task RefreshCatalogAsync()
    {
        if (!_options.Value.IsEnabled)
        {
            throw ModelCatalogException.CatalogNotEnabledToRead(CatalogName);
        }

        var catalog = await GetRootCatalogAsync().ConfigureAwait(false);

        CacheTypes.CacheCatalog cacheCatalog = new()
        {
            UpdatedAt = DateTime.UtcNow
        };

        if (catalog != null)
        {
            foreach (var rootCatalogEntry in catalog.Models)
            {
                var modelLibraryCatalog =
                    await GetModelLibraryCatalogAsync(rootCatalogEntry.CatalogPath).ConfigureAwait(false);

                if (modelLibraryCatalog == null)
                {
                    continue;
                }

                var modelEntry = new CacheTypes.CacheModelEntry
                {
                    ModelId = modelLibraryCatalog.ModelId,
                    Versions = new Dictionary<string, CacheTypes.CacheModelVersionEntry>()
                };
                cacheCatalog.Models.Add(rootCatalogEntry.ModelName, modelEntry);

                foreach (var modelLibraryCatalogEntry in modelLibraryCatalog.MajorVersions)
                {
                    var versionsCatalog = await GetModelLibraryVersionsCatalogAsync(
                        rootCatalogEntry.ModelName,
                        modelLibraryCatalogEntry.MajorVersion).ConfigureAwait(false);

                    if (versionsCatalog == null)
                    {
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

        await WriteCacheAsync(cacheCatalog).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override bool IsSupportingSourceIdentifier(object? sourceIdentifier = null)
    {
        return sourceIdentifier == null;
    }

    /// <inheritdoc />
    public override async Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        if (!_options.Value.IsEnabled)
        {
            throw ModelCatalogException.CatalogNotEnabledToRead(CatalogName);
        }

        if (!TryGetExistingModelPath(modelId, out var compiledModelFilePath) || compiledModelFilePath == null)
        {
            throw ModelCatalogException.ModelNotFound(modelId, CatalogName);
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
            throw ModelCatalogException.ErrorDuringModelLoad(modelId, CatalogName, operationResult);
        }

        return compiledModelRoot;
    }

    /// <inheritdoc />
    public override async Task PublishAsync(CkCompiledModelRoot ckCompiledModel, bool force = false,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        if (!_options.Value.IsEnabled)
        {
            throw ModelCatalogException.CatalogNotEnabledToRead(Name);
        }

        var compiledModelFilePath = CreatePath(ckCompiledModel.ModelId);
        if (File.Exists(compiledModelFilePath) && !force)
        {
            throw ModelCatalogException.ModelAlreadyExists(ckCompiledModel.ModelId, CatalogName);
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
            throw ModelCatalogException.PublishFailed(ckCompiledModel.ModelId, CatalogName, e);
        }

        Exception? lastException = null;
        var i = 0;
        while (i++ < 20)
        {
            try
            {
                File.Copy(tempFilePath, compiledModelFilePath, true);

                // Update the major version
                await UpdateModelVersionsCatalogAsync(ckCompiledModel.ModelId, ckCompiledModel.Description)
                    .ConfigureAwait(false);

                // Update the overall model library catalog
                await UpdateModelLibraryCatalogAsync(ckCompiledModel.ModelId)
                    .ConfigureAwait(false);

                // Update the root catalog
                await UpdateRootCatalogAsync(ckCompiledModel.ModelId).ConfigureAwait(false);

                // Refresh the in-memory catalog
                await RefreshCatalogAsync().ConfigureAwait(false);

                return;
            }
            catch (Exception ex)
            {
                await Task.Delay(100).ConfigureAwait(false);
                lastException = ex;
            }
        }

        throw ModelCatalogException.PublishFailed(ckCompiledModel.ModelId, CatalogName, lastException!);
    }

    private string CreatePath(CkModelId ckModelId)
    {
        var modelPath = Path.Combine(_options.Value.RootPath, RootPath, ckModelId.Name[0].ToString().ToLower(),
            ckModelId.Name);
        var modelVersionPath = Path.Combine(modelPath, ckModelId.Version.Major.ToString());
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

    private async Task<SharedCatalogTypes.RootCatalog?> GetRootCatalogAsync()
    {
        var catalogPath = Path.Combine(_options.Value.RootPath, $"{RootPath}{CatalogFileName}");

        try
        {
            if (!File.Exists(catalogPath))
            {
                return null;
            }

            using var fileStream = File.OpenRead(catalogPath);

            return await JsonSerializer.DeserializeAsync<SharedCatalogTypes.RootCatalog>(
                fileStream,
                new JsonSerializerOptions
                    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // Catalog doesn't exist or couldn't be fetched
            return null;
        }
    }

    private async Task<SharedCatalogTypes.ModelLibraryCatalog?> GetModelLibraryCatalogAsync(string catalogPath)
    {
        try
        {
            var fullCatalogPath = Path.Combine(_options.Value.RootPath, catalogPath);
            if (!File.Exists(fullCatalogPath))
            {
                return null;
            }

            using var fileStream = File.OpenRead(fullCatalogPath);

            var modelLibraryCatalog = await JsonSerializer
                .DeserializeAsync<SharedCatalogTypes.ModelLibraryCatalog>(
                    fileStream,
                    new JsonSerializerOptions
                        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }).ConfigureAwait(false);

            return modelLibraryCatalog;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the catalog for a specific major version of a model
    /// </summary>
    /// <param name="modelName">The model ID (without version)</param>
    /// <param name="majorVersion">The major version number</param>
    /// <returns>The major version catalog content or null if not found</returns>
    private async Task<SharedCatalogTypes.ModelLibraryVersionsCatalog?> GetModelLibraryVersionsCatalogAsync(
        string modelName, int majorVersion)
    {
        var catalogPath = $"{RootPath}{modelName[0].ToString().ToLower()}/{modelName}/{majorVersion}/{CatalogFileName}";
        catalogPath = Path.Combine(_options.Value.RootPath, catalogPath);

        try
        {
            if (!File.Exists(catalogPath))
            {
                return null;
            }

            using var fileStream = File.OpenRead(catalogPath);

            var versionsCatalog = await JsonSerializer
                .DeserializeAsync<SharedCatalogTypes.ModelLibraryVersionsCatalog>(
                    fileStream,
                    new JsonSerializerOptions
                        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }).ConfigureAwait(false);

            return versionsCatalog;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task UpdateModelVersionsCatalogAsync(CkModelId modelId, string? description)
    {
        // Create catalog file path for this major version
        var catalogPath =
            $"{RootPath}{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{modelId.Version.Major}/{CatalogFileName}";
        catalogPath = Path.Combine(_options.Value.RootPath, catalogPath);

        // Try to load existing catalog first
        var catalogData = await GetModelLibraryVersionsCatalogAsync(modelId.Name, modelId.Version.Major)
            .ConfigureAwait(false);
        bool isModified = false;

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
            var filePath =
                $"{RootPath}{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{modelId.Version.Major}/{fileName}";

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

        if (isModified)
        {
            // Sort versions in descending order (latest first)
            var sortedVersions = versionDict.Values
                .OrderByDescending(v => new CkVersion(v.Version))
                .ToList();

            catalogData.UpdatedAt = DateTime.UtcNow;
            catalogData.LatestVersion = sortedVersions.FirstOrDefault()?.Version;

            var directoryPath = Path.GetDirectoryName(catalogPath);
            if (directoryPath != null)
            {
                Directory.CreateDirectory(directoryPath);
            }

            using var fileStream = File.OpenWrite(catalogPath);

            // Serialize catalog to JSON
            await JsonSerializer.SerializeAsync(fileStream, catalogData, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }).ConfigureAwait(false);
        }
    }

    private async Task UpdateModelLibraryCatalogAsync(CkModelId modelId)
    {
        // Create catalog file path for the model
        var catalogPath = $"{RootPath}{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{CatalogFileName}";
        catalogPath = Path.Combine(_options.Value.RootPath, catalogPath);

        // Try to load existing catalog first
        var catalogData = await GetModelLibraryCatalogAsync(catalogPath).ConfigureAwait(false);

        catalogData ??= new SharedCatalogTypes.ModelLibraryCatalog
        {
            ModelId = modelId.Name,
            MajorVersions = new List<SharedCatalogTypes.ModelLibraryCatalogEntry>()
        };

        // Check or update the entry for the current major version
        var currentMajor = modelId.Version.Major;
        var majorVersionEntry = catalogData.MajorVersions.FirstOrDefault(m => m.MajorVersion == currentMajor);

        if (majorVersionEntry == null)
        {
            catalogData.MajorVersions.Add(new SharedCatalogTypes.ModelLibraryCatalogEntry
            {
                MajorVersion = currentMajor,
                CatalogPath =
                    $"{RootPath}{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{currentMajor}/{CatalogFileName}"
            });

            catalogData.UpdatedAt = DateTime.UtcNow;

            var directoryPath = Path.GetDirectoryName(catalogPath);
            if (directoryPath != null)
            {
                Directory.CreateDirectory(directoryPath);
            }

            using var fileStream = File.OpenWrite(catalogPath);
            await JsonSerializer.SerializeAsync(fileStream, catalogData, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }).ConfigureAwait(false);
        }
    }

    private async Task UpdateRootCatalogAsync(CkModelId modelId)
    {
        var catalogPath = $"{RootPath}{CatalogFileName}";
        catalogPath = Path.Combine(_options.Value.RootPath, catalogPath);

        // Get or create catalog
        var catalogData = await GetRootCatalogAsync().ConfigureAwait(false);

        catalogData ??= new SharedCatalogTypes.RootCatalog
        {
            Version = "1.0",
            UpdatedAt = DateTime.UtcNow,
            Models = []
        };

        // Find or create entry for this model
        var existingEntry = catalogData.Models.FirstOrDefault(m => m.ModelName == modelId.Name);

        // Get the model catalog to verify the latest version
        if (existingEntry == null)
        {
            // Add new entry
            var newEntry = new SharedCatalogTypes.RootCatalogEntry
            {
                ModelName = modelId.Name,
                CatalogPath = $"{RootPath}{modelId.Name[0].ToString().ToLower()}/{modelId.Name}/{CatalogFileName}"
            };
            catalogData.Models.Add(newEntry);

            // Sort models alphabetically
            catalogData.Models = catalogData.Models.OrderBy(m => m.ModelName).ToList();
            catalogData.UpdatedAt = DateTime.UtcNow;

            using var fileStream = File.OpenWrite(catalogPath);
            await JsonSerializer.SerializeAsync(fileStream, catalogData, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }).ConfigureAwait(false);
        }
    }
}