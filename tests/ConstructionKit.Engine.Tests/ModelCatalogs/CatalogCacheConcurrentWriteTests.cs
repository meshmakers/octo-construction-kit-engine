using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

/// <summary>
///     AB#9305: several CK model builds of one solution (parallel MSBuild nodes, i.e. processes) write the same catalog
///     cache at the same time. Each catalog instance stands for one process: it shares nothing with the others but the
///     cache file, exactly like two CK compile tasks in two MSBuild nodes. On Windows a rename over a file that another
///     writer replaces, or that a reader holds open, fails with <see cref="UnauthorizedAccessException" /> or a
///     sharing-violation <see cref="IOException" />; the write must outlast that instead of failing the build.
/// </summary>
public sealed class CatalogCacheConcurrentWriteTests : IDisposable
{
    private const string CacheFileName = "test-catalog-cache.json";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _cacheDirectory;

    public CatalogCacheConcurrentWriteTests()
    {
        _cacheDirectory = Path.Combine(Path.GetTempPath(), "CatalogCacheConcurrentWriteTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_cacheDirectory);
    }

    private string CachePath => Path.Combine(_cacheDirectory, CacheFileName);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                Directory.Delete(_cacheDirectory, true);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }

    private TestCachedCatalog NewProcess() =>
        new(new CatalogOptions(CacheFileName) { CacheDirectory = _cacheDirectory });

    private static CacheTypes.CacheCatalog Catalog(string writer, int models)
    {
        var catalog = new CacheTypes.CacheCatalog { UpdatedAt = DateTime.UtcNow };
        for (var i = 0; i < models; i++)
        {
            var id = $"{writer}-model-{i}";
            catalog.Models[id] = new CacheTypes.CacheModelEntry { ModelId = id };
        }

        return catalog;
    }

    /// <summary>
    ///     Reads the cache file the way a reader of another process does and parses it completely. An open that meets
    ///     a rename on Windows (sharing violation, access denied) is retried - that is no partial read; a parse error
    ///     is never retried, it would be one.
    /// </summary>
    private static T ReadRaw<T>(string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (true)
        {
            byte[] bytes;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                bytes = copy.ToArray();
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
                continue;
            }

            return JsonSerializer.Deserialize<T>(bytes, ReadOptions)
                   ?? throw new InvalidOperationException("cache file deserialized to null");
        }
    }

    private string[] LeftoverTempFiles() => Directory.GetFiles(_cacheDirectory, "*.tmp");

    [Fact]
    public async Task Concurrent_writers_and_readers_of_one_cache_never_fail_and_leave_one_complete_cache()
    {
        var ct = TestContext.Current.CancellationToken;
        var processes = Enumerable.Range(0, 6).Select(_ => NewProcess()).ToArray();
        const int iterations = 20;
        const int modelsPerCatalog = 50;

        var writers = processes.Select((process, p) => Task.Run(async () =>
        {
            for (var i = 0; i < iterations; i++)
            {
                await process.WriteAsync(Catalog($"w{p}", modelsPerCatalog));
                // Every CK compile task reads the cache it (or a sibling) just wrote.
                await process.ReadAsync();
            }
        }, ct)).ToArray();

        await Task.WhenAll(writers);

        var final = ReadRaw<CacheTypes.CacheCatalog>(CachePath);
        Assert.Equal(modelsPerCatalog, final.Models.Count);
        // Last writer wins: the models all come from one writer, never a mix.
        Assert.Single(final.Models.Keys.Select(k => k.Split('-')[0]).Distinct());
        Assert.Empty(LeftoverTempFiles());
    }

    [Fact]
    public async Task A_write_outlasts_a_reader_that_holds_the_cache_open_without_delete_sharing()
    {
        var ct = TestContext.Current.CancellationToken;
        var process = NewProcess();
        await process.WriteAsync(Catalog("old", 3));

        // A reader of an older engine version (or a virus scanner) holds the cache open without FileShare.Delete; on
        // Windows a rename over the file fails while that handle is open.
        var reader = new FileStream(CachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var release = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500), ct);
            await reader.DisposeAsync();
        }, ct);

        await process.WriteAsync(Catalog("new", 4));
        await release;

        var final = ReadRaw<CacheTypes.CacheCatalog>(CachePath);
        Assert.Equal(4, final.Models.Count);
        Assert.All(final.Models.Keys, k => Assert.StartsWith("new-", k));
        Assert.Empty(LeftoverTempFiles());
    }

    [Fact]
    public async Task A_parallel_reader_sees_the_old_or_the_new_cache_never_a_partial_one()
    {
        var ct = TestContext.Current.CancellationToken;
        const int modelsPerCatalog = 400;
        var seed = NewProcess();
        await seed.WriteAsync(Catalog("seed", modelsPerCatalog));

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reads = 0;
        var readerFailures = new List<string>();
        var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
        {
            var process = NewProcess();
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    // The raw parse proves the file is complete JSON; the catalog's own read must not fall back to
                    // an empty catalog either (it does after repeated partial or unreadable reads).
                    var raw = ReadRaw<CacheTypes.CacheCatalog>(CachePath);
                    var viaCatalog = await process.ReadAsync();
                    if (raw.Models.Count != modelsPerCatalog || viaCatalog.Models.Count != modelsPerCatalog)
                    {
                        lock (readerFailures)
                        {
                            readerFailures.Add($"raw {raw.Models.Count}, catalog {viaCatalog.Models.Count}");
                        }
                    }

                    Interlocked.Increment(ref reads);
                    // A CK compile reads the cache a few times per build, not in a tight loop.
                    await Task.Delay(10, CancellationToken.None);
                }
                catch (JsonException e)
                {
                    lock (readerFailures)
                    {
                        readerFailures.Add($"{e.GetType().Name}: {e.Message}");
                    }
                }
            }
        }, ct)).ToArray();

        var writers = Enumerable.Range(0, 3).Select(p => Task.Run(async () =>
        {
            var process = NewProcess();
            for (var i = 0; i < 15; i++)
            {
                await process.WriteAsync(Catalog($"w{p}", modelsPerCatalog));
            }
        }, ct)).ToArray();

        try
        {
            await Task.WhenAll(writers);
        }
        finally
        {
            await stop.CancelAsync();
            await Task.WhenAll(readers);
        }

        Assert.True(reads > 0, "the readers never read the cache");
        Assert.Empty(readerFailures);
        Assert.Empty(LeftoverTempFiles());
    }

    [Fact]
    public async Task Concurrent_writers_of_one_blueprint_cache_never_fail_and_leave_one_complete_cache()
    {
        var ct = TestContext.Current.CancellationToken;
        var processes = Enumerable.Range(0, 6).Select(_ =>
            new TestCachedBlueprintCatalog(new BlueprintCatalogOptions(CacheFileName)
            {
                CacheDirectory = _cacheDirectory
            })).ToArray();
        const int blueprintsPerCatalog = 30;

        var writers = processes.Select((process, p) => Task.Run(async () =>
        {
            for (var i = 0; i < 20; i++)
            {
                var catalog = new BlueprintCacheTypes.BlueprintCacheCatalog { UpdatedAt = DateTime.UtcNow };
                for (var b = 0; b < blueprintsPerCatalog; b++)
                {
                    var name = $"w{p}-blueprint-{b}";
                    catalog.Blueprints[name] = new BlueprintCacheTypes.BlueprintCacheEntry { BlueprintName = name };
                }

                await process.WriteAsync(catalog);
                // Readers of another process see a complete file at any time.
                Assert.Equal(blueprintsPerCatalog,
                    ReadRaw<BlueprintCacheTypes.BlueprintCacheCatalog>(CachePath).Blueprints.Count);
            }
        }, ct)).ToArray();

        await Task.WhenAll(writers);

        var final = ReadRaw<BlueprintCacheTypes.BlueprintCacheCatalog>(CachePath);
        Assert.Equal(blueprintsPerCatalog, final.Blueprints.Count);
        Assert.Single(final.Blueprints.Keys.Select(k => k.Split('-')[0]).Distinct());
        Assert.Empty(LeftoverTempFiles());
    }

    /// <summary>
    ///     Both rename strategies run here on net10.0: <c>move</c> is the one the net10.0 build uses
    ///     (<c>File.Move(overwrite)</c>), <c>replace</c> the one of the netstandard2.0 build that MSBuild on .NET
    ///     Framework loads (<c>File.Replace</c>, or a move for the first writer). With <c>withUnlockedWriter</c> a writer
    ///     of an older engine, which takes no writer lock, writes the same cache meanwhile; the locked writers must
    ///     outlast it by their retries.
    /// </summary>
    [Theory]
    [InlineData("move", false)]
    [InlineData("replace", false)]
    [InlineData("move", true)]
    [InlineData("replace", true)]
    public async Task Concurrent_locked_writers_never_fail_with_either_rename_strategy(string strategy,
        bool withUnlockedWriter)
    {
        var ct = TestContext.Current.CancellationToken;
        Action<string, string> move = strategy == "move"
            ? CatalogFileIo.MoveIntoPlaceByMove
            : CatalogFileIo.MoveIntoPlaceByReplace;
        const int modelsPerCatalog = 40;

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var unlockedWriter = Task.Run(async () =>
        {
            while (withUnlockedWriter && !stop.IsCancellationRequested)
            {
                try
                {
                    await CatalogFileIo.WriteJsonAtomicallyAsync(CachePath, Catalog("old", modelsPerCatalog),
                        ReadOptions, move);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // The older engine fails its build here; this test is about the locked writers.
                }
            }
        }, ct);

        var writers = Enumerable.Range(0, 6).Select(p => Task.Run(async () =>
        {
            for (var i = 0; i < 20; i++)
            {
                await CatalogFileIo.WriteJsonExclusiveAsync(CachePath, Catalog($"w{p}", modelsPerCatalog),
                    ReadOptions, moveIntoPlace: move);
                Assert.Equal(modelsPerCatalog, ReadRaw<CacheTypes.CacheCatalog>(CachePath).Models.Count);
            }
        }, ct)).ToArray();

        try
        {
            await Task.WhenAll(writers);
        }
        finally
        {
            await stop.CancelAsync();
            await unlockedWriter;
        }

        Assert.Equal(modelsPerCatalog, ReadRaw<CacheTypes.CacheCatalog>(CachePath).Models.Count);
        Assert.Empty(LeftoverTempFiles());
    }

    [Theory]
    [InlineData("move")]
    [InlineData("replace")]
    public void Both_rename_strategies_create_a_missing_target_and_replace_an_existing_one(string strategy)
    {
        Action<string, string> move = strategy == "move"
            ? CatalogFileIo.MoveIntoPlaceByMove
            : CatalogFileIo.MoveIntoPlaceByReplace;
        var first = Path.Combine(_cacheDirectory, "first.tmp");
        var second = Path.Combine(_cacheDirectory, "second.tmp");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");

        move(first, CachePath);
        Assert.Equal("first", File.ReadAllText(CachePath));

        move(second, CachePath);
        Assert.Equal("second", File.ReadAllText(CachePath));
        Assert.Empty(LeftoverTempFiles());
    }

    [Fact]
    public async Task A_write_that_still_fails_after_its_retries_names_the_cache_path_and_the_error_type()
    {
        var process = NewProcess();
        process.CacheWriteRetryBudget = TimeSpan.FromMilliseconds(300);
        // A directory where the cache file belongs makes every rename fail, on every platform.
        Directory.CreateDirectory(CachePath);

        var e = await Assert.ThrowsAsync<IOException>(() => process.WriteAsync(Catalog("w", 1)));

        Assert.Contains($"'{CachePath}'", e.Message);
        Assert.NotNull(e.InnerException);
        Assert.Contains($"last error {e.InnerException!.GetType().FullName}", e.Message);
        Assert.Empty(LeftoverTempFiles());
    }

    /// <summary>A cached catalog that exposes the cache read and write of <see cref="CachedCatalog" />.</summary>
    private sealed class TestCachedCatalog(CatalogOptions options)
        : CachedCatalog(0, "test", "test", false, true, options)
    {
        public Task WriteAsync(CacheTypes.CacheCatalog catalog) => WriteCacheAsync(catalog);

        public Task<CacheTypes.CacheCatalog> ReadAsync() => ReadCacheAsync(false);

        public override Task RefreshCatalogAsync(object? sourceIdentifier = null, bool forceRefresh = false) =>
            Task.CompletedTask;

        public override bool IsSupportingSourceIdentifier(object? sourceIdentifier = null) => true;

        public override Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, OperationResult operationResult,
            object? sourceIdentifier = null, CancellationToken? cancellationToken = null) =>
            throw new NotSupportedException();

        public override Task PublishAsync(CkCompiledModelRoot ckCompiledModel, bool force = false,
            object? sourceIdentifier = null, CancellationToken? cancellationToken = null) =>
            throw new NotSupportedException();

        public override Task<CatalogUnpublishResult> UnpublishAsync(CkModelId modelId, string? reason = null,
            object? sourceIdentifier = null, CancellationToken? cancellationToken = null) =>
            throw new NotSupportedException();
    }

    /// <summary>A cached blueprint catalog that exposes the cache write of <see cref="CachedBlueprintCatalog" />.</summary>
    private sealed class TestCachedBlueprintCatalog(BlueprintCatalogOptions options)
        : CachedBlueprintCatalog(0, "test", "test", false, true, options)
    {
        public Task WriteAsync(BlueprintCacheTypes.BlueprintCacheCatalog catalog) => WriteCacheAsync(catalog);

        public override Task RefreshCatalogAsync(object? sourceIdentifier = null, bool forceRefresh = false) =>
            Task.CompletedTask;

        public override bool IsSupportingSourceIdentifier(object? sourceIdentifier = null) => true;

        public override Task<BlueprintMetaRootDto> GetAsync(BlueprintId blueprintId, OperationResult operationResult,
            object? sourceIdentifier = null, CancellationToken? cancellationToken = null) =>
            throw new NotSupportedException();

        public override Task<Stream> OpenBlueprintFileAsync(BlueprintId blueprintId, string relativePath,
            object? sourceIdentifier = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

#pragma warning disable CS0809, CS0672
        [Obsolete("Use OpenBlueprintFileAsync.")]
        public override string GetBlueprintPath(BlueprintId blueprintId, object? sourceIdentifier = null) =>
            throw new NotSupportedException();
#pragma warning restore CS0809, CS0672

        public override Task PublishAsync(BlueprintMetaRootDto blueprintMetaRoot, string blueprintDirectory,
            bool force = false, object? sourceIdentifier = null, CancellationToken? cancellationToken = null) =>
            throw new NotSupportedException();

        public override Task UnpublishAsync(BlueprintId blueprintId, object? sourceIdentifier = null,
            CancellationToken? cancellationToken = null) =>
            throw new NotSupportedException();

        public override Task UnpublishAllVersionsAsync(string blueprintName, object? sourceIdentifier = null,
            CancellationToken? cancellationToken = null) =>
            throw new NotSupportedException();
    }
}
