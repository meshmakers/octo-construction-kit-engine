using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

/// <summary>
///     AB#5434: gives one command run a private working copy of the local file system catalog. The command
///     registers the models it validated (so dependent packages of the same run can compile against them) in the
///     copy instead of in the catalog every other build on the machine reads; the copy is removed and the original
///     options are restored when the scope is disposed. The original catalog is only read (copied), never written.
/// </summary>
internal sealed class PerRunLocalCatalogScope : IDisposable
{
    private readonly LocalFileSystemCatalogOptions _options;
    private readonly string _originalRootPath;
    private readonly string _originalCacheDirectory;

    private PerRunLocalCatalogScope(LocalFileSystemCatalogOptions options, string runRootPath)
    {
        _options = options;
        _originalRootPath = options.RootPath;
        _originalCacheDirectory = options.CacheDirectory;
        RunRootPath = runRootPath;
        OriginalRootPath = _originalRootPath;
    }

    /// <summary>The per-run catalog root (a copy of the original, removed on dispose).</summary>
    public string RunRootPath { get; }

    /// <summary>The root of the catalog the copy was taken from; it is never written.</summary>
    public string OriginalRootPath { get; }

    /// <summary>
    ///     Copies the current local catalog root (index and model files; not the cache and lock files) into a new
    ///     temporary directory and points <paramref name="options" /> at the copy.
    /// </summary>
    public static PerRunLocalCatalogScope Create(LocalFileSystemCatalogOptions options)
    {
        var runRoot = Path.Combine(Path.GetTempPath(), $"octo-ckc-run-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runRoot);
        if (Directory.Exists(options.RootPath))
        {
            CopyCatalog(options.RootPath, runRoot);
        }

        var scope = new PerRunLocalCatalogScope(options, runRoot);
        options.ApplyRootPath(runRoot);
        return scope;
    }

    public void Dispose()
    {
        _options.RootPath = _originalRootPath;
        _options.CacheDirectory = _originalCacheDirectory;
        try
        {
            if (Directory.Exists(RunRootPath))
            {
                Directory.Delete(RunRootPath, true);
            }
        }
        catch (IOException)
        {
            // best effort: the directory lives in the temp folder
        }
        catch (UnauthorizedAccessException)
        {
            // best effort: the directory lives in the temp folder
        }
    }

    private static void CopyCatalog(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name == "cache")
            {
                continue;
            }

            var targetDirectory = Path.Combine(target, name);
            Directory.CreateDirectory(targetDirectory);
            CopyCatalog(directory, targetDirectory);
        }

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".lock", StringComparison.Ordinal) || name.EndsWith(".tmp", StringComparison.Ordinal))
            {
                continue;
            }

            File.Copy(file, Path.Combine(target, name), true);
        }
    }
}
