using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
/// Blueprint catalog that uses the local file system to store blueprints.
/// </summary>
public class LocalFileSystemBlueprintCatalog : CachedBlueprintCatalog
{
    /// <summary>
    /// Defines the name of the catalog for local blueprints.
    /// </summary>
    public const string Name = "LocalFileSystemBlueprintCatalog";

    private const string RootPath = "blueprints/v1/";
    private const string BlueprintMetaFileName = "blueprint.yaml";
    private const int MaxCacheFileAgeSeconds = 60;

    private readonly IBlueprintSerializer _blueprintSerializer;
    private readonly IOptions<LocalFileSystemBlueprintCatalogOptions> _options;

    /// <summary>
    /// Creates a new instance of the <see cref="LocalFileSystemBlueprintCatalog" /> class.
    /// </summary>
    /// <param name="options">Catalog options</param>
    /// <param name="blueprintSerializer">Blueprint serializer</param>
    public LocalFileSystemBlueprintCatalog(
        IOptions<LocalFileSystemBlueprintCatalogOptions> options,
        IBlueprintSerializer blueprintSerializer)
        : base(10, Name,
            $"Local file system blueprint catalog at '{options.Value.RootPath}'",
            options.Value.IsEnabled, options.Value.IsEnabled, options.Value)
    {
        _options = options;
        _blueprintSerializer = blueprintSerializer;
    }

    /// <inheritdoc />
    public override Task RefreshCatalogAsync(object? sourceIdentifier = null)
    {
        return RefreshCatalogAsync(false, sourceIdentifier);
    }

    private async Task RefreshCatalogAsync(bool forceRefresh, object? sourceIdentifier = null)
    {
        if (!_options.Value.IsEnabled)
        {
            throw BlueprintCatalogException.CatalogCannotRead(CatalogName);
        }

        var maxAge = TimeSpan.FromSeconds(MaxCacheFileAgeSeconds);
        if (!forceRefresh && IsCacheFileRecentlyUpdated(maxAge))
        {
            return;
        }

        var cacheCatalog = new BlueprintCacheTypes.BlueprintCacheCatalog
        {
            UpdatedAt = DateTime.UtcNow
        };

        var blueprintsRootPath = Path.Combine(_options.Value.RootPath, RootPath);
        if (Directory.Exists(blueprintsRootPath))
        {
            foreach (var blueprintDir in Directory.GetDirectories(blueprintsRootPath))
            {
                var blueprintName = Path.GetFileName(blueprintDir);

                // Look for version directories
                foreach (var versionDir in Directory.GetDirectories(blueprintDir))
                {
                    var versionName = Path.GetFileName(versionDir);
                    var blueprintMetaPath = Path.Combine(versionDir, BlueprintMetaFileName);

                    if (!File.Exists(blueprintMetaPath))
                    {
                        continue;
                    }

                    try
                    {
                        var operationResult = new OperationResult();
#if NETSTANDARD2_0
                        using var stream = File.OpenRead(blueprintMetaPath);
#else
                        await using var stream = File.OpenRead(blueprintMetaPath);
#endif
                        var blueprintMeta = await _blueprintSerializer
                            .DeserializeBlueprintMetaAsync(stream, blueprintMetaPath, operationResult)
                            .ConfigureAwait(false);

                        if (operationResult.HasErrors)
                        {
                            continue;
                        }

                        if (!cacheCatalog.Blueprints.TryGetValue(blueprintName, out var blueprintEntry))
                        {
                            blueprintEntry = new BlueprintCacheTypes.BlueprintCacheEntry
                            {
                                BlueprintName = blueprintName,
                                Versions = new Dictionary<string, BlueprintCacheTypes.BlueprintCacheVersionEntry>()
                            };
                            cacheCatalog.Blueprints[blueprintName] = blueprintEntry;
                        }

                        var version = blueprintMeta.BlueprintId.Version;
                        if (!blueprintEntry.Versions.ContainsKey(version.ToString()))
                        {
                            blueprintEntry.Versions[version.ToString()] = new BlueprintCacheTypes.BlueprintCacheVersionEntry
                            {
                                Version = version,
                                Description = blueprintMeta.Description,
                                DirectoryPath = versionDir
                            };
                        }
                    }
                    catch
                    {
                        // Skip blueprints that fail to parse
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
    public override async Task<BlueprintMetaRootDto> GetAsync(BlueprintId blueprintId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        if (!CanRead)
        {
            throw BlueprintCatalogException.CatalogCannotRead(CatalogName);
        }

        var blueprintPath = GetBlueprintPathInternal(blueprintId);
        var blueprintMetaPath = Path.Combine(blueprintPath, BlueprintMetaFileName);

        if (!File.Exists(blueprintMetaPath))
        {
            throw BlueprintCatalogException.BlueprintNotFoundInCatalog(CatalogName, blueprintId);
        }

#if NETSTANDARD2_0
        using var stream = File.OpenRead(blueprintMetaPath);
#else
        await using var stream = File.OpenRead(blueprintMetaPath);
#endif
        var blueprintMeta = await _blueprintSerializer
            .DeserializeBlueprintMetaAsync(stream, blueprintMetaPath, operationResult)
            .ConfigureAwait(false);

        if (operationResult.HasErrors)
        {
            throw new BlueprintCatalogException($"Error loading blueprint '{blueprintId}' from '{blueprintMetaPath}'");
        }

        return blueprintMeta;
    }

    /// <inheritdoc />
    public override Task<Stream> OpenBlueprintFileAsync(BlueprintId blueprintId, string relativePath,
        object? sourceIdentifier = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanRead)
        {
            throw BlueprintCatalogException.CatalogCannotRead(CatalogName);
        }

        var normalised = ValidateBlueprintRelativePath(relativePath);
        var blueprintPath = GetBlueprintPathInternal(blueprintId);
        var filePath = Path.Combine(blueprintPath, normalised.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(filePath))
        {
            throw BlueprintCatalogException.BlueprintFileNotFound(blueprintId, CatalogName, normalised);
        }

        Stream stream = File.OpenRead(filePath);
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    [Obsolete("Use OpenBlueprintFileAsync.")]
    public override string GetBlueprintPath(BlueprintId blueprintId, object? sourceIdentifier = null)
        => GetBlueprintPathInternal(blueprintId);

    private string GetBlueprintPathInternal(BlueprintId blueprintId)
    {
        return Path.Combine(
            _options.Value.RootPath,
            RootPath,
            blueprintId.Name,
            blueprintId.Version.ToString());
    }

    /// <inheritdoc />
    public override async Task PublishAsync(BlueprintMetaRootDto blueprintMetaRoot, string blueprintDirectory,
        bool force = false, object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        if (!CanWrite)
        {
            throw BlueprintCatalogException.CatalogCannotWrite(Name);
        }

        var targetPath = GetBlueprintPathInternal(blueprintMetaRoot.BlueprintId);

        if (Directory.Exists(targetPath) && !force)
        {
            throw BlueprintCatalogException.BlueprintAlreadyExists(blueprintMetaRoot.BlueprintId);
        }

        // Create directory structure
        Directory.CreateDirectory(targetPath);

        // Copy all files from source directory
        foreach (var sourceFile in Directory.GetFiles(blueprintDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = GetRelativePath(blueprintDirectory, sourceFile);
            var targetFile = Path.Combine(targetPath, relativePath);

            var targetDir = Path.GetDirectoryName(targetFile);
            if (targetDir != null && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            File.Copy(sourceFile, targetFile, force);
        }

        // Refresh cache to include new blueprint
        await RefreshCatalogAsync(true).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the relative path from one path to another.
    /// </summary>
    private static string GetRelativePath(string relativeTo, string path)
    {
#if NETSTANDARD2_0
        // Simple implementation for netstandard2.0
        var relativeToUri = new Uri(relativeTo.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        var pathUri = new Uri(path);
        var relativeUri = relativeToUri.MakeRelativeUri(pathUri);
        return Uri.UnescapeDataString(relativeUri.ToString().Replace('/', Path.DirectorySeparatorChar));
#else
        return Path.GetRelativePath(relativeTo, path);
#endif
    }
}
