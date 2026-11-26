using System.IO.Compression;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
///     Blueprint files in a local directory (octo-bpm validate, pack and publish).
/// </summary>
public sealed class DirectoryBlueprintFileSource : IBlueprintFileSource
{
    private readonly string _rootPath;

    /// <summary>
    ///     Creates a source over the blueprint directory <paramref name="rootPath" />.
    /// </summary>
    /// <param name="rootPath">The folder that holds <c>blueprint.yaml</c></param>
    public DirectoryBlueprintFileSource(string rootPath)
    {
        _rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
    }

    /// <inheritdoc />
    public async Task<string?> ReadTextAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return null;
        }

#if NETSTANDARD2_0
        using var reader = new StreamReader(fullPath);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
#else
        return await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
#endif
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListFiles(string relativeDirectory)
    {
        var directory = Path.Combine(_rootPath, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var root = Path.GetFullPath(_rootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
            .Select(f => BlueprintSeedData.Normalise(Path.GetFullPath(f).Substring(root.Length)))
            .ToList();
    }
}

/// <summary>
///     Blueprint files held in memory: inline files or the entries of an uploaded zip (asset repository, MCP).
/// </summary>
public sealed class InMemoryBlueprintFileSource : IBlueprintFileSource
{
    /// <summary>Default limit of the unpacked size of a zip draft (bytes).</summary>
    public const long DefaultMaxUnpackedBytes = 20L * 1024 * 1024;

    /// <summary>Default limit of the number of files of a zip draft.</summary>
    public const int DefaultMaxEntries = 1000;

    private readonly Dictionary<string, string> _files;

    /// <summary>
    ///     Creates a source over files by relative path.
    /// </summary>
    /// <param name="files">Content by path relative to the blueprint root (<c>/</c> or <c>\</c> as separator)</param>
    public InMemoryBlueprintFileSource(IEnumerable<KeyValuePair<string, string>> files)
    {
        _files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            _files[BlueprintSeedData.Normalise(file.Key)] = file.Value;
        }
    }

    /// <summary>The relative paths of the files.</summary>
    public IReadOnlyCollection<string> Paths => _files.Keys;

    /// <summary>
    ///     Reads a zip of a blueprint draft. The blueprint root is the folder of the shallowest <c>blueprint.yaml</c>
    ///     (a zip of octo-bpm pack carries the blueprint folder itself); files outside the root are ignored.
    /// </summary>
    /// <param name="zipStream">The zip</param>
    /// <param name="maxUnpackedBytes">Limit of the unpacked size</param>
    /// <param name="maxEntries">Limit of the number of entries</param>
    /// <returns>The source</returns>
    /// <exception cref="InvalidDataException">The zip is invalid, too large or holds an unsafe path</exception>
    public static InMemoryBlueprintFileSource FromZip(Stream zipStream, long maxUnpackedBytes = DefaultMaxUnpackedBytes,
        int maxEntries = DefaultMaxEntries)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        var fileEntries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        if (fileEntries.Count > maxEntries)
        {
            throw new InvalidDataException($"The zip holds {fileEntries.Count} files; at most {maxEntries} are allowed.");
        }

        var manifest = fileEntries
            .Select(e => BlueprintSeedData.Normalise(e.FullName))
            .Where(p => p == "blueprint.yaml" || p.EndsWith("/blueprint.yaml", StringComparison.Ordinal))
            .OrderBy(p => p.Count(c => c == '/'))
            .FirstOrDefault();
        var prefix = manifest == null ? string.Empty : manifest.Substring(0, manifest.Length - "blueprint.yaml".Length);

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in fileEntries)
        {
            var path = BlueprintSeedData.Normalise(entry.FullName);
            if (path.StartsWith("/", StringComparison.Ordinal) || path.Split('/').Any(s => s == ".."))
            {
                throw new InvalidDataException($"The zip entry '{entry.FullName}' has an unsafe path.");
            }

            if (!path.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            total += entry.Length;
            if (total > maxUnpackedBytes)
            {
                throw new InvalidDataException($"The unpacked zip is larger than {maxUnpackedBytes} bytes.");
            }

            using var reader = new StreamReader(entry.Open());
            var content = reader.ReadToEnd();
            files[path.Substring(prefix.Length)] = content;
        }

        return new InMemoryBlueprintFileSource(files);
    }

    /// <inheritdoc />
    public Task<string?> ReadTextAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_files.TryGetValue(BlueprintSeedData.Normalise(relativePath), out var content)
            ? content
            : null);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListFiles(string relativeDirectory)
    {
        var prefix = BlueprintSeedData.Normalise(relativeDirectory).TrimEnd('/') + "/";
        return _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }
}

/// <summary>
///     The files of a blueprint of the blueprint catalogs (install path). Cannot list files.
/// </summary>
public sealed class CatalogBlueprintFileSource : IBlueprintFileSource
{
    private readonly IBlueprintCatalogManager _catalogManager;
    private readonly BlueprintId _blueprintId;

    /// <summary>
    ///     Creates a source over the catalog blueprint <paramref name="blueprintId" />.
    /// </summary>
    public CatalogBlueprintFileSource(IBlueprintCatalogManager catalogManager, BlueprintId blueprintId)
    {
        _catalogManager = catalogManager ?? throw new ArgumentNullException(nameof(catalogManager));
        _blueprintId = blueprintId;
    }

    /// <inheritdoc />
    public async Task<string?> ReadTextAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var stream = await _catalogManager
            .TryOpenBlueprintFileAsync(_blueprintId, relativePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (stream == null)
        {
            return null;
        }

        using (stream)
        using (var reader = new StreamReader(stream))
        {
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string>? ListFiles(string relativeDirectory) => null;
}
