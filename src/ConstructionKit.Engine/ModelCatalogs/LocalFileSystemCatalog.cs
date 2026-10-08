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

    private const string CatalogFileName = "catalog.json";
    private const int MaxCacheFileAgeSeconds = 60;

    private readonly ICkJsonSerializer _ckJsonSerializer;
    private readonly IOptions<LocalFileSystemCatalogOptions> _options;

    /// <summary>
    ///     Creates a new instance of the <see cref="LocalFileSystemCatalog" /> class.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="ckJsonSerializer"></param>
    public LocalFileSystemCatalog(IOptions<LocalFileSystemCatalogOptions> options,
        ICkJsonSerializer ckJsonSerializer) : base(10, Name,
        $"Local file system catalog at '{options.Value.RootPath}'", options.Value.IsEnabled, options.Value.IsEnabled, options.Value)
    {
        _options = options;
        _ckJsonSerializer = ckJsonSerializer;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Evaluated live from <see cref="LocalFileSystemCatalogOptions.IsEnabled" /> rather than frozen at
    ///     construction, so a runtime toggle (the CLI <c>-lce</c> switch applied after the singleton catalog
    ///     was built) is honoured — otherwise a "disabled" local catalog would still be read.
    /// </remarks>
    public override bool CanRead => _options.Value.IsEnabled;

    /// <inheritdoc />
    /// <remarks>
    ///     Evaluated live from <see cref="LocalFileSystemCatalogOptions.IsEnabled" /> — see <see cref="CanRead" />.
    /// </remarks>
    public override bool CanWrite => _options.Value.IsEnabled;

    /// <inheritdoc />
    public override Task RefreshCatalogAsync(object? sourceIdentifier = null, bool forceRefresh = false)
    {
        return RefreshCatalogAsync(forceRefresh, sourceIdentifier);
    }

    private async Task RefreshCatalogAsync(bool forceRefresh, object? sourceIdentifier = null)
    {
        if (!_options.Value.IsEnabled)
        {
            throw ModelCatalogException.CatalogNotEnabledToRead(CatalogName);
        }

        var maxAge = TimeSpan.FromSeconds(MaxCacheFileAgeSeconds);
        if (!forceRefresh && IsCacheFileRecentlyUpdatedAsync(maxAge))
        {
            return;
        }

        var cache = await ReadCacheAsync(false).ConfigureAwait(false);
        // F1.1-S6: one index tree per catalog root (ck-models/v3 first, then ck-models/v2).
        var catalogs = new List<(string Root, SharedCatalogTypes.RootCatalog Catalog)>();
        foreach (var root in CkCatalogLayout.ReadRoots)
        {
            var rootCatalog = await GetRootCatalogAsync(root).ConfigureAwait(false);
            if (rootCatalog != null)
            {
                catalogs.Add((root, rootCatalog));
            }
        }

        if (catalogs.Count != 0 && cache.UpdatedAt != null &&
            cache.UpdatedAt.Value == catalogs.Max(c => c.Catalog.UpdatedAt))
        {
            // No changes in the catalog so we can skip the refresh
            return;
        }

        CacheTypes.CacheCatalog cacheCatalog = new()
        {
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var (root, catalog) in catalogs)
        {
            foreach (var rootCatalogEntry in catalog.Models)
            {
                var modelLibraryCatalog =
                    await GetModelLibraryCatalogAsync(rootCatalogEntry.CatalogPath).ConfigureAwait(false);

                if (modelLibraryCatalog == null)
                {
                    continue;
                }

                // A model may have versions under both roots (v1 versions in v2, v2 versions in v3).
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
                    var versionsCatalog = await GetModelLibraryVersionsCatalogAsync(root,
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
    /// <remarks>
    ///     AB#5661: answered from the compiled model files on disk, not from the cache file. The cache is
    ///     shared by every <c>octo-ckc</c> process of a parallel build; a concurrent publish of a sibling
    ///     model could overwrite it with a snapshot taken before another model was published, after which
    ///     a dependent compile reported "Dependencies 'System-[2.4,3.0)' are unknown" while the manifest
    ///     was already on disk (or resolved a range to an older version). Enumerating one model's directory
    ///     is cheap and cannot be stale.
    /// </remarks>
    public override Task<ModelExistingResult> IsExistingAsync(CkModelIdVersionRange modelIdVersionRange,
        object? sourceIdentifier = null)
    {
        if (!CanRead)
        {
            throw ModelCatalogException.CatalogNotEnabledToRead(CatalogName);
        }

        var candidate = EnumerateVersionsOnDisk(modelIdVersionRange.Name)
            .Where(v => modelIdVersionRange.ModelVersionRange.IsSatisfiedBy(v))
            .OrderBy(v => v)
            .Select(v => (CkVersion?)v)
            .LastOrDefault();

        return Task.FromResult(new ModelExistingResult
        {
            Exists = candidate != null,
            ModelId = candidate != null ? new CkModelId(modelIdVersionRange.Name, candidate.Value) : null,
            CatalogName = CatalogName,
            // Answered live from the files on disk, so the "cache" it was answered from is current.
            CacheUpdatedAt = DateTime.UtcNow
        });
    }

    /// <inheritdoc />
    /// <remarks>AB#5661: answered from the file system — see <see cref="IsExistingAsync(CkModelIdVersionRange, object?)" />.</remarks>
    public override Task<bool> IsExistingAsync(CkModelId modelId, object? sourceIdentifier = null)
    {
        if (!CanRead)
        {
            throw ModelCatalogException.CatalogNotEnabledToRead(CatalogName);
        }

        return Task.FromResult(TryGetExistingModelPath(modelId, out _));
    }

    /// <summary>
    ///     Enumerates the versions of a model whose compiled file exists on disk
    ///     (<c>ck-models/{v3,v2}/&lt;letter&gt;/&lt;Name&gt;/&lt;major&gt;/ck-&lt;name&gt;-&lt;version&gt;.json</c>,
    ///     the layout written by <see cref="PublishAsync" />, F1.1-S6).
    /// </summary>
    private IEnumerable<CkVersion> EnumerateVersionsOnDisk(string modelName)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            yield break;
        }

        var prefix = $"ck-{modelName.ToLower()}-";
        var seen = new HashSet<CkVersion>();
        foreach (var root in CkCatalogLayout.ReadRoots)
        {
            var modelPath = Path.Combine(_options.Value.RootPath, CkCatalogLayout.ModelDirectory(root, modelName));
            if (!Directory.Exists(modelPath))
            {
                continue;
            }

            foreach (var majorDirectory in Directory.EnumerateDirectories(modelPath))
            {
                if (!int.TryParse(Path.GetFileName(majorDirectory), out _))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(majorDirectory, "ck-*.json"))
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    CkVersion version;
                    try
                    {
                        version = new CkVersion(fileName.Substring(prefix.Length));
                    }
                    catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
                    {
                        continue;
                    }

                    if (seen.Add(version))
                    {
                        yield return version;
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public override async Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        if (!CanRead)
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
            .DeserializeCompiledModelRootAsync(streamReader, compiledModelFilePath, operationResult,
                tolerantToUnknownProperties: true)
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
        if (!CanWrite)
        {
            throw ModelCatalogException.CatalogNotEnabledToWrite(Name);
        }

        // F1.1-S6: ckLanguage 2 / range-retaining models go to ck-models/v3, classic ones to ck-models/v2. A model
        // version exists under one root only: a forced re-publish into the other root removes the old file.
        var root = CkCatalogLayout.GetPublishRoot(ckCompiledModel);
        var compiledModelFilePath = CreatePath(root, ckCompiledModel.ModelId);
        var existsElsewhere = TryGetExistingModelPath(ckCompiledModel.ModelId, out var existingPath) &&
                              existingPath != compiledModelFilePath;
        if ((File.Exists(compiledModelFilePath) || existsElsewhere) && !force)
        {
            throw ModelCatalogException.ModelAlreadyExists(ckCompiledModel.ModelId, CatalogName);
        }

        if (existsElsewhere && existingPath != null)
        {
            File.Delete(existingPath);
        }

        var path = Path.GetDirectoryName(compiledModelFilePath)!;
        Directory.CreateDirectory(path);

        // Review M10: lookups trust file existence (AB#5661), so the model file must never be visible half
        // written. Write next to the target (same file system) and rename it into place; a temp name that does
        // not match "ck-*.json" is never picked up by EnumerateVersionsOnDisk.
        var tempFilePath = Path.Combine(path, $".{Path.GetFileName(compiledModelFilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
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
                    CatalogFileIo.MoveIntoPlace(tempFilePath, compiledModelFilePath);

                    // Review N3: the three index files are read-modify-write; parallel octo-ckc processes publishing
                    // sibling models lost each other's entries. Serialize the updates with a cross-process lock.
                    using (await CatalogFileIo.AcquireLockAsync(IndexLockPath(root)).ConfigureAwait(false))
                    {
                        // Update the major version
                        await UpdateModelVersionsCatalogAsync(root, ckCompiledModel.ModelId,
                                ckCompiledModel.Description)
                            .ConfigureAwait(false);

                        // Update the overall model library catalog
                        await UpdateModelLibraryCatalogAsync(root, ckCompiledModel.ModelId)
                            .ConfigureAwait(false);

                        // Update the root catalog
                        await UpdateRootCatalogAsync(root, ckCompiledModel.ModelId).ConfigureAwait(false);
                    }

                    // Refresh the in-memory catalog
                    await RefreshCatalogAsync(true).ConfigureAwait(false);

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
        finally
        {
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch (IOException)
            {
                // best effort; the name never matches a model file
            }
        }
    }




    private static readonly JsonSerializerOptions IndexJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Lock file serializing index updates of this catalog root across processes (review N3).</summary>
    private string IndexLockPath(string root) => Path.Combine(_options.Value.RootPath, root, ".catalog-index.lock");

    private string CreatePath(string root, CkModelId ckModelId) =>
        Path.Combine(_options.Value.RootPath, CkCatalogLayout.ModelFilePath(root, ckModelId));

    /// <summary>F1.1-S6: the model file under the first read root that has it (v3, then v2).</summary>
    private bool TryGetExistingModelPath(CkModelId ckModelId, out string? compiledModelFilePath)
    {
        foreach (var root in CkCatalogLayout.ReadRoots)
        {
            compiledModelFilePath = CreatePath(root, ckModelId);
            if (File.Exists(compiledModelFilePath))
            {
                return true;
            }
        }

        compiledModelFilePath = null;
        return false;
    }

    private async Task<SharedCatalogTypes.RootCatalog?> GetRootCatalogAsync(string root)
    {
        var catalogPath = Path.Combine(_options.Value.RootPath, $"{root}{CatalogFileName}");

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
    /// <param name="root">The catalog root (F1.1-S6: <c>ck-models/v2/</c> or <c>ck-models/v3/</c>)</param>
    /// <param name="modelName">The model ID (without version)</param>
    /// <param name="majorVersion">The major version number</param>
    /// <returns>The major version catalog content or null if not found</returns>
    private async Task<SharedCatalogTypes.ModelLibraryVersionsCatalog?> GetModelLibraryVersionsCatalogAsync(
        string root, string modelName, int majorVersion)
    {
        var catalogPath = $"{CkCatalogLayout.ModelDirectory(root, modelName)}{majorVersion}/{CatalogFileName}";
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

    private async Task UpdateModelVersionsCatalogAsync(string root, CkModelId modelId, string? description)
    {
        // Create catalog file path for this major version
        var catalogPath =
            $"{CkCatalogLayout.ModelDirectory(root, modelId.Name)}{modelId.Version.Major}/{CatalogFileName}";
        catalogPath = Path.Combine(_options.Value.RootPath, catalogPath);

        // Try to load existing catalog first
        var catalogData = await GetModelLibraryVersionsCatalogAsync(root, modelId.Name, modelId.Version.Major)
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

            // Review N3: atomic write (rename into place) — a reader never sees a half-written index.
            await CatalogFileIo.WriteJsonAtomicallyAsync(catalogPath, catalogData, IndexJsonOptions)
                .ConfigureAwait(false);
        }
    }

    private async Task UpdateModelLibraryCatalogAsync(string root, CkModelId modelId)
    {
        // Create catalog file path for the model
        var catalogPath = $"{CkCatalogLayout.ModelDirectory(root, modelId.Name)}{CatalogFileName}";
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
                CatalogPath = $"{CkCatalogLayout.ModelDirectory(root, modelId.Name)}{currentMajor}/{CatalogFileName}"
            });

            catalogData.UpdatedAt = DateTime.UtcNow;

            var directoryPath = Path.GetDirectoryName(catalogPath);
            if (directoryPath != null)
            {
                Directory.CreateDirectory(directoryPath);
            }

            // Review N3: atomic write (rename into place) — a reader never sees a half-written index.
            await CatalogFileIo.WriteJsonAtomicallyAsync(catalogPath, catalogData, IndexJsonOptions)
                .ConfigureAwait(false);
        }
    }

    private async Task UpdateRootCatalogAsync(string root, CkModelId modelId)
    {
        var catalogPath = $"{root}{CatalogFileName}";
        catalogPath = Path.Combine(_options.Value.RootPath, catalogPath);

        // Get or create catalog
        var catalogData = await GetRootCatalogAsync(root).ConfigureAwait(false);

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
                CatalogPath = $"{CkCatalogLayout.ModelDirectory(root, modelId.Name)}{CatalogFileName}"
            };
            catalogData.Models.Add(newEntry);

            // Sort models alphabetically
            catalogData.Models = catalogData.Models.OrderBy(m => m.ModelName).ToList();
            catalogData.UpdatedAt = DateTime.UtcNow;

            // Review N3: atomic write (rename into place) — a reader never sees a half-written index.
            await CatalogFileIo.WriteJsonAtomicallyAsync(catalogPath, catalogData, IndexJsonOptions)
                .ConfigureAwait(false);
        }
    }
}