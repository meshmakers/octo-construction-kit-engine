using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

/// <summary>
///     F1.1-S6 (AB#5909): <c>ckLanguage: 2</c> and range-retaining models are published under <c>ck-models/v3/</c>,
///     classic ones under <c>ck-models/v2/</c>; the catalog reads both.
/// </summary>
public sealed class LocalFileSystemCatalogV3RootTests : IDisposable
{
    private readonly string _root;
    private readonly LocalFileSystemCatalog _catalog;

    public LocalFileSystemCatalogV3RootTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "LocalFileSystemCatalogV3RootTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _catalog = new LocalFileSystemCatalog(Options.Create(new LocalFileSystemCatalogOptions
        {
            CacheDirectory = _root, RootPath = _root
        }), new CkJsonSerializer());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static CkCompiledModelRoot Model(string version, int? ckLanguage = null, bool rangeRetaining = false) => new()
    {
        ModelId = new CkModelId("Demo", version),
        CkLanguage = ckLanguage,
        DependencyRanges = rangeRetaining ? [] : null,
        MinEngineVersion = ckLanguage == 2 || rangeRetaining ? "3.4.0" : null
    };

    private string File(string root, string version) =>
        Path.Combine(_root, root, "d", "Demo", "1", $"ck-demo-{version}.json");

    [Fact]
    public async Task Publish_ChoosesTheRootByLanguageAndRangeRetention()
    {
        await _catalog.PublishAsync(Model("1.0.0"));
        await _catalog.PublishAsync(Model("1.1.0", ckLanguage: 2));
        await _catalog.PublishAsync(Model("1.2.0", rangeRetaining: true));

        Assert.True(System.IO.File.Exists(File("ck-models/v2", "1.0.0")));
        Assert.True(System.IO.File.Exists(File("ck-models/v3", "1.1.0")));
        Assert.True(System.IO.File.Exists(File("ck-models/v3", "1.2.0")));
        Assert.False(System.IO.File.Exists(File("ck-models/v2", "1.1.0")));
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "ck-models/v3/catalog.json")));
        Assert.True(System.IO.File.Exists(Path.Combine(_root, "ck-models/v2/catalog.json")));
    }

    [Fact]
    public async Task Lookups_ReadBothRoots()
    {
        await _catalog.PublishAsync(Model("1.0.0"));
        await _catalog.PublishAsync(Model("1.1.0", ckLanguage: 2));

        Assert.True(await _catalog.IsExistingAsync(new CkModelId("Demo", "1.0.0")));
        Assert.True(await _catalog.IsExistingAsync(new CkModelId("Demo", "1.1.0")));
        var highest = await _catalog.IsExistingAsync(new CkModelIdVersionRange("Demo", "[1.0,2.0)"));
        Assert.Equal("Demo-1.1.0", highest.ModelId!.FullName);

        var operationResult = new OperationResult();
        var v2 = await _catalog.GetAsync(new CkModelId("Demo", "1.1.0"), operationResult);
        Assert.Equal(2, v2.CkLanguage);
        Assert.Equal("3.4.0", v2.MinEngineVersion);
        Assert.Null((await _catalog.GetAsync(new CkModelId("Demo", "1.0.0"), operationResult)).MinEngineVersion);

        await _catalog.RefreshCatalogAsync(forceRefresh: true);
        var listed = _catalog.ListAsync(null).ToBlockingEnumerable(TestContext.Current.CancellationToken)
            .Select(i => i.ModelId.FullName).OrderBy(m => m).ToList();
        Assert.Equal(["Demo-1.0.0", "Demo-1.1.0"], listed);
    }

    [Fact]
    public async Task SameVersionInTheOtherRoot_RequiresForce_AndMovesTheFile()
    {
        await _catalog.PublishAsync(Model("1.0.0", ckLanguage: 2));

        await Assert.ThrowsAsync<ModelCatalogException>(() => _catalog.PublishAsync(Model("1.0.0")));

        await _catalog.PublishAsync(Model("1.0.0"), force: true);

        Assert.False(System.IO.File.Exists(File("ck-models/v3", "1.0.0")));
        Assert.True(System.IO.File.Exists(File("ck-models/v2", "1.0.0")));
        Assert.Null((await _catalog.GetAsync(new CkModelId("Demo", "1.0.0"), new OperationResult())).CkLanguage);
    }

    [Fact]
    public void Layout_Paths()
    {
        Assert.Equal("ck-models/v3/s/System/2/ck-system-2.5.0.json",
            CkCatalogLayout.ModelFilePath(CkCatalogLayout.V3Root, new CkModelId("System", "2.5.0")));
        Assert.Equal([CkCatalogLayout.V3Root, CkCatalogLayout.V2Root], CkCatalogLayout.ReadRoots);
        Assert.False(CkCatalogLayout.RequiresV3(Model("1.0.0")));
        Assert.True(CkCatalogLayout.RequiresV3(Model("1.0.0", ckLanguage: 2)));
    }
}
