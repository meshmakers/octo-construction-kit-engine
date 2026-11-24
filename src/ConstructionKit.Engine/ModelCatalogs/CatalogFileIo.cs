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
    ///     AB#9305: how long <see cref="WriteJsonExclusiveAsync{T}" /> retries a write whose rename fails transiently
    ///     (a parallel writer of an older engine without the writer lock, a reader or a virus scanner holding the
    ///     target open). Long enough for any parallel writer or reader of a catalog cache; bounded so a real failure
    ///     still fails the build.
    /// </summary>
    internal static readonly TimeSpan DefaultWriteRetryBudget = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(1);

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
        MoveIntoPlaceByReplace(source, target);
#else
        MoveIntoPlaceByMove(source, target);
#endif
    }

    /// <summary>
    ///     The <c>net10.0</c> strategy of <see cref="MoveIntoPlace" />: one rename that replaces an existing target.
    ///     Internal (and compiled for both target frameworks) so the tests run it on every platform.
    /// </summary>
    internal static void MoveIntoPlaceByMove(string source, string target)
    {
#if NETSTANDARD2_0
        // netstandard2.0 has no File.Move(overwrite); only the tests call this method, and they run on net10.0.
        MoveIntoPlaceByReplace(source, target);
#else
        File.Move(source, target, overwrite: true);
#endif
    }

    /// <summary>
    ///     The <c>netstandard2.0</c> strategy of <see cref="MoveIntoPlace" /> (MSBuild on .NET Framework has no
    ///     <c>File.Move(overwrite)</c>): <see cref="File.Replace(string, string, string)" /> when the target exists,
    ///     a plain move for the first writer. AB#9305: the former exists-check followed by replace or move raced with a
    ///     parallel writer (the target appears or disappears in between); a lost race now throws an
    ///     <see cref="IOException" /> the caller retries instead of a wrong-branch failure. Internal (and compiled for
    ///     both target frameworks) so the tests run this strategy on net10.0, too.
    /// </summary>
    internal static void MoveIntoPlaceByReplace(string source, string target)
    {
        try
        {
            File.Replace(source, target, null);
        }
        catch (FileNotFoundException) when (File.Exists(source) && !File.Exists(target))
        {
            // No target yet: the first writer creates it. A writer that creates it in between makes this move throw
            // an IOException, which the retry of WriteJsonExclusiveAsync answers with a replace.
            File.Move(source, target);
        }
    }

    /// <summary>
    ///     AB#9305: writes <paramref name="value" /> as JSON to the cache file <paramref name="path" /> that several
    ///     processes (parallel MSBuild nodes of one solution, parallel <c>octo-ckc</c> runs) write at the same time.
    ///     Writers are serialized by a cross-process lock file next to the cache (<c>.{name}.lock</c>, kept, like the
    ///     index locks); the write itself stays atomic (temp file next to the target, then rename), so a parallel
    ///     reader sees the old or the new cache, never a partial one; last writer wins. A rename that still fails
    ///     transiently (<see cref="IOException" /> or <see cref="UnauthorizedAccessException" />: a writer of an
    ///     older engine without the lock, or a reader holding the target open without delete sharing) is retried with
    ///     a growing delay for <paramref name="retryBudget" /> (default <see cref="DefaultWriteRetryBudget" />). The
    ///     final failure is an <see cref="IOException" /> that names the cache path and the type of the last error.
    /// </summary>
    /// <param name="path">The cache file.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">JSON options.</param>
    /// <param name="retryBudget">How long transient failures are retried; tests pass a short one.</param>
    /// <param name="moveIntoPlace">The rename strategy; tests pass the one of the other target framework.</param>
    public static async Task WriteJsonExclusiveAsync<T>(string path, T value, JsonSerializerOptions options,
        TimeSpan? retryBudget = null, Action<string, string>? moveIntoPlace = null)
    {
        var fullPath = Path.GetFullPath(path);
        var budget = retryBudget ?? DefaultWriteRetryBudget;
        var lockPath = WriterLockPath(fullPath);

        IDisposable writerLock;
        var lockStarted = DateTime.UtcNow;
        try
        {
            writerLock = await AcquireLockAsync(lockPath).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw WriteFailed(fullPath, DateTime.UtcNow - lockStarted >= LockTimeout
                ? $"the writer lock '{lockPath}' was not free within {LockTimeout.TotalSeconds:0} s"
                : $"the writer lock '{lockPath}' could not be opened", e);
        }

        using (writerLock)
        {
            var deadline = DateTime.UtcNow + budget;
            var delay = FirstRetryDelay;
            var attempts = 0;
            while (true)
            {
                attempts++;
                try
                {
                    await WriteJsonAtomicallyAsync(fullPath, value, options, moveIntoPlace).ConfigureAwait(false);
                    return;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    if (DateTime.UtcNow + delay > deadline)
                    {
                        throw WriteFailed(fullPath,
                            $"{attempts} attempt(s) within {budget.TotalSeconds:0.#} s", e);
                    }

                    await Task.Delay(delay).ConfigureAwait(false);
                    delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
                }
            }
        }
    }

    /// <summary>
    ///     AB#9305: reads a cache file that parallel writers replace into memory and closes it at once. On Windows a
    ///     rename over a file fails while a handle on it is open (also one with <see cref="FileShare.Delete" />), so a
    ///     reader holds the handle only for the copy, not while it parses. <see cref="FileShare.Delete" /> is asked for
    ///     anyway, so that a file system with POSIX rename semantics can replace the file even during the copy.
    /// </summary>
    public static async Task<MemoryStream> ReadSharedAsync(string path)
    {
        var content = new MemoryStream();
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete, bufferSize: 81920, useAsync: true))
            {
                await stream.CopyToAsync(content).ConfigureAwait(false);
            }

            content.Position = 0;
            return content;
        }
        catch
        {
            content.Dispose();
            throw;
        }
    }

    /// <summary>The lock file that serializes the writers of <paramref name="cachePath" />.</summary>
    internal static string WriterLockPath(string cachePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cachePath))!, $".{Path.GetFileName(cachePath)}.lock");

    private static IOException WriteFailed(string path, string detail, Exception e) =>
        new($"Could not write the catalog cache '{path}' ({detail}); last error {e.GetType().FullName}: {e.Message}",
            e);

    /// <summary>
    ///     Serializes <paramref name="value" /> to a temp file next to <paramref name="path" /> (never matched by
    ///     the <c>ck-*.json</c> / <c>catalog.json</c> names) and renames it into place; the temp file is removed in
    ///     all cases.
    /// </summary>
    public static async Task WriteJsonAtomicallyAsync<T>(string path, T value, JsonSerializerOptions options,
        Action<string, string>? moveIntoPlace = null)
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

            (moveIntoPlace ?? MoveIntoPlace)(tempPath, path);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>
    ///     Atomically writes <paramref name="text" /> to <paramref name="path" /> (temp file next to it, then rename).
    /// </summary>
    public static void WriteTextAtomically(string path, string text)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, text);
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
