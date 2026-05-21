using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
/// Read-only blueprint catalog backed by embedded resources discovered through DI-registered
/// <see cref="IBlueprintEmbeddedSource" /> instances. Mirrors the CK-model <c>EmbeddedResourceCatalog</c>
/// pattern: a service NuGet declares its bundled blueprints via a source-generated
/// <c>IBlueprintEmbeddedSource</c> implementation, and this catalog enumerates / opens them.
/// </summary>
/// <remarks>
/// "Service-managed vs user-installable" is decided by the blueprint *name*
/// (<see cref="BlueprintIdExtensions.IsServiceManaged" /> returns true for names starting with
/// <c>System.</c>), not by which catalog hosts the blueprint. Studio uses that flag to hide
/// install / uninstall actions for system blueprints regardless of where they were discovered.
/// </remarks>
public class EmbeddedResourceBlueprintCatalog : IBlueprintCatalog
{
    /// <summary>
    /// Stable name used for log lines and the Studio "managed by" hint.
    /// </summary>
    public const string Name = "EmbeddedResourceBlueprintCatalog";

    private const string BlueprintMetaFileName = "blueprint.yaml";

    private readonly IBlueprintSerializer _blueprintSerializer;
    private readonly IReadOnlyList<IBlueprintEmbeddedSource> _sources;

    /// <summary>
    /// Lazy cache of deserialised manifests. Populated on first access per blueprint id; kept for
    /// the lifetime of the catalog (the embedded resources are static at runtime so we can hold
    /// the parsed DTOs without invalidation).
    /// </summary>
    private readonly Dictionary<BlueprintId, BlueprintMetaRootDto> _manifestCache = new();
    private readonly object _manifestCacheLock = new();

    /// <summary>
    /// Creates a new <see cref="EmbeddedResourceBlueprintCatalog" />.
    /// </summary>
    public EmbeddedResourceBlueprintCatalog(
        IBlueprintSerializer blueprintSerializer,
        IEnumerable<IBlueprintEmbeddedSource> sources)
    {
        _blueprintSerializer = blueprintSerializer;
        _sources = sources.ToList();
    }

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public string CatalogName => Name;

    /// <inheritdoc />
    public string Description =>
        $"Embedded blueprint catalog ({_sources.Count} blueprint(s) bundled with the host services).";

    /// <inheritdoc />
    public bool CanWrite => false;

    /// <inheritdoc />
    public bool CanRead => true;

    /// <inheritdoc />
    public Task RefreshCatalogAsync(object? sourceIdentifier = null)
    {
        // Embedded resources are static at runtime, nothing to refresh.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool IsSupportingSourceIdentifier(object? sourceIdentifier = null)
    {
        return sourceIdentifier == null;
    }

    /// <inheritdoc />
    public Task<BlueprintExistingResult> IsExistingAsync(BlueprintIdVersionRange blueprintIdVersionRange,
        object? sourceIdentifier = null)
    {
        var candidates = _sources
            .Where(s => s.BlueprintId.Name == blueprintIdVersionRange.Name &&
                        blueprintIdVersionRange.BlueprintVersionRange.IsSatisfiedBy(s.BlueprintId.Version))
            .ToList();

        if (candidates.Count == 0)
        {
            return Task.FromResult(new BlueprintExistingResult
            {
                Exists = false,
                BlueprintId = null
            });
        }

        var newest = candidates.OrderByDescending(s => s.BlueprintId.Version).First();
        return Task.FromResult(new BlueprintExistingResult
        {
            Exists = true,
            BlueprintId = newest.BlueprintId
        });
    }

    /// <inheritdoc />
    public Task<bool> IsExistingAsync(BlueprintId blueprintId, object? sourceIdentifier = null)
    {
        return Task.FromResult(_sources.Any(s => s.BlueprintId == blueprintId));
    }

    /// <inheritdoc />
    public async Task<BlueprintMetaRootDto> GetAsync(BlueprintId blueprintId, OperationResult operationResult,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        var source = _sources.FirstOrDefault(s => s.BlueprintId == blueprintId)
                     ?? throw BlueprintCatalogException.BlueprintNotFoundInCatalog(CatalogName, blueprintId);

        lock (_manifestCacheLock)
        {
            if (_manifestCache.TryGetValue(blueprintId, out var cached))
            {
                return cached;
            }
        }

        var manifestResourceName = BuildResourceName(source, BlueprintMetaFileName);
        var stream = OpenResource(source, manifestResourceName, blueprintId);

#if NETSTANDARD2_0
        using (stream)
#else
        await using (stream)
#endif
        {
            var manifest = await _blueprintSerializer
                .DeserializeBlueprintMetaAsync(stream, manifestResourceName, operationResult)
                .ConfigureAwait(false);

            if (operationResult.HasErrors)
            {
                throw BlueprintCatalogException.ErrorDuringBlueprintLoad(blueprintId, CatalogName, operationResult);
            }

            lock (_manifestCacheLock)
            {
                _manifestCache[blueprintId] = manifest;
            }

            return manifest;
        }
    }

    /// <inheritdoc />
    public Task<Stream> OpenBlueprintFileAsync(BlueprintId blueprintId, string relativePath,
        object? sourceIdentifier = null,
        CancellationToken cancellationToken = default)
    {
        var source = _sources.FirstOrDefault(s => s.BlueprintId == blueprintId)
                     ?? throw BlueprintCatalogException.BlueprintNotFoundInCatalog(CatalogName, blueprintId);

        var normalised = BlueprintRelativePath.Validate(relativePath);
        var resourceName = BuildResourceName(source, normalised);
        Stream stream = OpenResource(source, resourceName, blueprintId, normalised);
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    [Obsolete("Use OpenBlueprintFileAsync.")]
    public string GetBlueprintPath(BlueprintId blueprintId, object? sourceIdentifier = null)
    {
        // Embedded blueprints have no on-disk root. The publish path uses GetBlueprintPath, but
        // embedded catalogs are read-only, so this should never be called in practice.
        throw BlueprintCatalogException.CatalogCannotWrite(CatalogName);
    }

    /// <inheritdoc />
    public Task PublishAsync(BlueprintMetaRootDto blueprintMetaRoot, string blueprintDirectory, bool force = false,
        object? sourceIdentifier = null, CancellationToken? cancellationToken = null)
    {
        throw BlueprintCatalogException.CatalogCannotWrite(CatalogName);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<BlueprintCatalogResultItem> ListAsync(object? sourceIdentifier)
    {
        await Task.Yield();

        foreach (var source in _sources.OrderBy(s => s.BlueprintId.Name).ThenByDescending(s => s.BlueprintId.Version))
        {
            yield return new BlueprintCatalogResultItem
            {
                CatalogName = CatalogName,
                BlueprintId = source.BlueprintId,
                Description = source.Description
            };
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<BlueprintCatalogResultItem> SearchAsync(string searchTerm, object? sourceIdentifier)
    {
        await Task.Yield();

        var trimmed = (searchTerm ?? string.Empty).Trim();

        foreach (var source in _sources)
        {
            if (string.IsNullOrEmpty(trimmed) ||
                source.BlueprintId.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                source.Description.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                yield return new BlueprintCatalogResultItem
                {
                    CatalogName = CatalogName,
                    BlueprintId = source.BlueprintId,
                    Description = source.Description
                };
            }
        }
    }

    /// <summary>
    /// Builds the manifest-resource name for <paramref name="relativePath" /> inside the blueprint's
    /// folder. Forward slashes are converted to dots, matching the default behaviour of MSBuild's
    /// <c>&lt;EmbeddedResource&gt;</c> name mangling.
    /// </summary>
    private static string BuildResourceName(IBlueprintEmbeddedSource source, string relativePath)
    {
        var normalised = relativePath.Replace('/', '.').Replace('\\', '.');
        return $"{source.ResourceNamespace}.{normalised}";
    }

    private Stream OpenResource(IBlueprintEmbeddedSource source, string resourceName, BlueprintId blueprintId,
        string? relativePathForErrors = null)
    {
        var stream = source.Assembly.GetManifestResourceStream(resourceName);
        if (stream != null)
        {
            return stream;
        }

        throw BlueprintCatalogException.BlueprintFileNotFound(
            blueprintId,
            CatalogName,
            relativePathForErrors ?? resourceName);
    }
}
