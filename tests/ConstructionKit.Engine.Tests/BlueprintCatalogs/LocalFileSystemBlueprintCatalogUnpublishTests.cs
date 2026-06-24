using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.BlueprintCatalogs;

/// <summary>
/// Round-trip tests for <see cref="LocalFileSystemBlueprintCatalog" /> unpublish against a temp directory.
/// File-system state is asserted directly; the single-version test additionally verifies the cache-backed
/// <see cref="LocalFileSystemBlueprintCatalog.IsExistingAsync(BlueprintId, object?)" /> reflects the removal.
/// </summary>
public sealed class LocalFileSystemBlueprintCatalogUnpublishTests : IDisposable
{
    private const string BlueprintName = "MyBlueprint";

    private readonly string _root;
    private readonly IBlueprintSerializer _serializer;

    public LocalFileSystemBlueprintCatalogUnpublishTests()
    {
        _root = Path.Combine(Path.GetTempPath(), nameof(LocalFileSystemBlueprintCatalogUnpublishTests), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_root);
        _serializer = A.Fake<IBlueprintSerializer>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public async Task UnpublishAsync_SingleVersion_RemovesDirectory_AndIsNoLongerResolvable()
    {
        var blueprintId = new BlueprintId(BlueprintName, "1.0.0");
        // Make the cache scan resolve this version so IsExistingAsync goes through the real cache path.
        A.CallTo(() => _serializer.DeserializeBlueprintMetaAsync(A<Stream>._, A<string>._, A<OperationResult>._))
            .Returns(new BlueprintMetaRootDto { BlueprintId = blueprintId, Description = "d" });

        var catalog = CreateCatalog();
        var versionPath = await PublishAsync(catalog, "1.0.0");

        Assert.True(Directory.Exists(versionPath));
        Assert.True(await catalog.IsExistingAsync(blueprintId));

        await catalog.UnpublishAsync(blueprintId);

        Assert.False(Directory.Exists(versionPath));
        Assert.False(await catalog.IsExistingAsync(blueprintId));
    }

    [Fact]
    public async Task UnpublishAsync_RemovesOnlyTheTargetVersion_KeepsSibling()
    {
        var catalog = CreateCatalog();
        var v1 = await PublishAsync(catalog, "1.0.0");
        var v2 = await PublishAsync(catalog, "1.1.0");

        await catalog.UnpublishAsync(new BlueprintId(BlueprintName, "1.0.0"));

        Assert.False(Directory.Exists(v1));
        Assert.True(Directory.Exists(v2));
        // Blueprint folder survives because a version remains.
        Assert.True(Directory.Exists(BlueprintFolder()));
    }

    [Fact]
    public async Task UnpublishAsync_LastVersion_RemovesEmptyBlueprintFolder()
    {
        var catalog = CreateCatalog();
        await PublishAsync(catalog, "1.0.0");

        await catalog.UnpublishAsync(new BlueprintId(BlueprintName, "1.0.0"));

        Assert.False(Directory.Exists(BlueprintFolder()));
    }

    [Fact]
    public async Task UnpublishAllVersionsAsync_RemovesTheWholeBlueprintFolder()
    {
        var catalog = CreateCatalog();
        await PublishAsync(catalog, "1.0.0");
        await PublishAsync(catalog, "1.1.0");
        await PublishAsync(catalog, "2.0.0");

        await catalog.UnpublishAllVersionsAsync(BlueprintName);

        Assert.False(Directory.Exists(BlueprintFolder()));
    }

    [Fact]
    public async Task UnpublishAsync_MissingBlueprint_IsIdempotentNoOp()
    {
        var catalog = CreateCatalog();

        // Nothing published — must not throw.
        await catalog.UnpublishAsync(new BlueprintId(BlueprintName, "1.0.0"));
        await catalog.UnpublishAllVersionsAsync(BlueprintName);
    }

    [Fact]
    public async Task UnpublishAsync_DisabledCatalog_Throws()
    {
        var catalog = new LocalFileSystemBlueprintCatalog(
            Options.Create(new LocalFileSystemBlueprintCatalogOptions
            {
                RootPath = _root,
                CacheDirectory = _root,
                IsEnabled = false
            }),
            _serializer);

        await Assert.ThrowsAsync<BlueprintCatalogException>(
            () => catalog.UnpublishAsync(new BlueprintId(BlueprintName, "1.0.0")));
    }

    private LocalFileSystemBlueprintCatalog CreateCatalog()
        => new(
            Options.Create(new LocalFileSystemBlueprintCatalogOptions
            {
                RootPath = _root,
                CacheDirectory = _root,
                IsEnabled = true
            }),
            _serializer);

    private async Task<string> PublishAsync(LocalFileSystemBlueprintCatalog catalog, string version)
    {
        var dto = new BlueprintMetaRootDto
        {
            BlueprintId = new BlueprintId(BlueprintName, version),
            Description = $"Test blueprint {version}"
        };

        var sourceDir = Path.Combine(_root, $"src-{version}");
        Directory.CreateDirectory(Path.Combine(sourceDir, "seed-data"));
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "blueprint.yaml"), $"id: {BlueprintName}-{version}");
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "seed-data", "entities.yaml"), "entities: []");

        await catalog.PublishAsync(dto, sourceDir, force: true);

        return Path.Combine(_root, "blueprints", "v1", BlueprintName, version);
    }

    private string BlueprintFolder() => Path.Combine(_root, "blueprints", "v1", BlueprintName);
}
