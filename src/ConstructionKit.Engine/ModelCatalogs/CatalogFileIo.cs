using System.Text.Json;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
///     File primitives for catalogs shared by parallel octo-ckc processes (AB#5661, review M10 / N3):
///     atomic replace by rename, atomic JSON writes, and a cross-process lock for read-modify-write of index files.
/// </summary>
internal static class CatalogFileIo
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    ///     Atomically replaces (or creates) <paramref name="target" /> with <paramref name="source" /> by a rename on
    ///     the same file system — a reader sees the old or the new content, never a partial one.
    /// </summary>
    public static void MoveIntoPlace(string source, string target)
    {
        if (!File.Exists(source))
        {
            // Already moved by a previous attempt of a retry loop.
            return;
        }

#if NETSTANDARD2_0
        if (File.Exists(target))
        {
            File.Replace(source, target, null);
        }
        else
        {
            File.Move(source, target);
        }
#else
        File.Move(source, target, overwrite: true);
#endif
    }

    /// <summary>
    ///     Serializes <paramref name="value" /> to a temp file next to <paramref name="path" /> (never matched by
    ///     the <c>ck-*.json</c> / <c>catalog.json</c> names) and renames it into place; the temp file is removed in
    ///     all cases.
    /// </summary>
    public static async Task WriteJsonAtomicallyAsync<T>(string path, T value, JsonSerializerOptions options)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
#if NETSTANDARD2_0
            using (var stream = File.Create(tempPath))
#else
            await using (var stream = File.Create(tempPath))
#endif
            {
                await JsonSerializer.SerializeAsync(stream, value, options).ConfigureAwait(false);
            }

            MoveIntoPlace(tempPath, path);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>
    ///     Acquires an exclusive cross-process lock (an OS-level exclusive open of <paramref name="lockFilePath" />;
    ///     on Unix .NET maps <see cref="FileShare.None" /> to an advisory <c>flock</c>). Dispose to release.
    /// </summary>
    public static async Task<IDisposable> AcquireLockAsync(string lockFilePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(lockFilePath))!);
        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            try
            {
                return new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(25).ConfigureAwait(false);
            }
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best effort; temp names never match a catalog file
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }
}
