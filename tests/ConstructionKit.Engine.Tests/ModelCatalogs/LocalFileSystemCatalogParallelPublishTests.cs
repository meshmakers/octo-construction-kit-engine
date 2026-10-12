using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

/// <summary>
///     AB#6584: parallel publishes to the local file-system catalog. Two catalog instances over one root stand for two
///     <c>octo-ckc</c> processes; the cross-process lock must make the existence check, the move into place and the
///     index updates one unit, so the second publish sees the first.
/// </summary>
public sealed class LocalFileSystemCatalogParallelPublishTests : IDisposable
{
    private readonly string _root;

    public LocalFileSystemCatalogParallelPublishTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "LocalFileSystemCatalogParallelPublishTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private LocalFileSystemCatalog NewProcess() => new(Options.Create(new LocalFileSystemCatalogOptions
    {
        CacheDirectory = Path.Combine(_root, ".cache-" + Guid.NewGuid().ToString("N")), RootPath = _root
    }), new CkJsonSerializer());

    private static CkCompiledModelRoot Model(string version, string description = "d") => new()
    {
        ModelId = new CkModelId("Demo", version), Description = description
    };

    [Fact]
    public async Task Parallel_publishes_of_different_versions_of_one_model_are_all_in_the_index()
    {
        var processes = new[] { NewProcess(), NewProcess() };
        var versions = Enumerable.Range(0, 24).Select(i => $"1.{i}.0").ToList();

        await Task.WhenAll(versions.Select((v, i) => Task.Run(() => processes[i % 2].PublishAsync(Model(v)))));

        var index = await File.ReadAllTextAsync(Path.Combine(_root, "ck-models/v2/d/Demo/1/catalog.json"),
            TestContext.Current.CancellationToken);
        foreach (var version in versions)
        {
            Assert.Contains($"\"{version}\"", index);
            Assert.True(File.Exists(Path.Combine(_root, $"ck-models/v2/d/Demo/1/ck-demo-{version}.json")));
        }

        var fresh = NewProcess();
        foreach (var version in versions)
        {
            Assert.True(await fresh.IsExistingAsync(new CkModelId("Demo", version)));
        }
    }

    [Fact]
    public async Task Parallel_publishes_of_one_version_let_exactly_one_win_and_the_other_sees_it()
    {
        var processes = new[] { NewProcess(), NewProcess() };

        var results = await Task.WhenAll(new[] { "first", "second" }.Select((description, i) => Task.Run(async () =>
        {
            try
            {
                await processes[i].PublishAsync(Model("1.0.0", description));
                return (Exception?)null;
            }
            catch (ModelCatalogException e)
            {
                return e;
            }
        })));

        Assert.Single(results, r => r == null);
        var loser = Assert.Single(results, r => r != null);
        Assert.Contains("already exists", loser!.Message, StringComparison.OrdinalIgnoreCase);
        var published = await File.ReadAllTextAsync(Path.Combine(_root, "ck-models/v2/d/Demo/1/ck-demo-1.0.0.json"),
            TestContext.Current.CancellationToken);
        Assert.True(published.Contains("first") ^ published.Contains("second"), "the file holds exactly one publish");
    }
}
