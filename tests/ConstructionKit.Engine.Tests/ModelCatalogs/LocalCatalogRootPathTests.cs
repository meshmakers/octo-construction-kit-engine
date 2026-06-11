using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

/// <summary>
/// Tests for relocating the local catalog via <see cref="LocalFileSystemCatalogOptions.ApplyRootPath" />
/// (OctoLocalCatalogRootPath plumbing). The invariant under test: every component that applies a root
/// path gets catalog content AND catalog cache at that root — a writer and a reader configured with
/// the same root must see each other's models, and two different roots must be fully isolated.
/// </summary>
public class LocalCatalogRootPathTests : IDisposable
{
    private readonly string _tempRootA;
    private readonly string _tempRootB;

    public LocalCatalogRootPathTests()
    {
        _tempRootA = Path.Combine(Path.GetTempPath(), "LocalCatalogRootPathTests", Guid.NewGuid().ToString(), "a");
        _tempRootB = Path.Combine(Path.GetTempPath(), "LocalCatalogRootPathTests", Guid.NewGuid().ToString(), "b");
    }

    public void Dispose()
    {
        foreach (var root in new[] { _tempRootA, _tempRootB })
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void ApplyRootPath_SetsRootPathAndCacheDirectoryTogether()
    {
        var options = new LocalFileSystemCatalogOptions();

        options.ApplyRootPath(_tempRootA);

        Assert.Equal(_tempRootA, options.RootPath);
        Assert.Equal(Path.Combine(_tempRootA, "cache"), options.CacheDirectory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyRootPath_WithNullOrWhitespace_KeepsDefaultsUntouched(string? rootPath)
    {
        var options = new LocalFileSystemCatalogOptions();
        var defaults = new LocalFileSystemCatalogOptions();

        options.ApplyRootPath(rootPath);

        // No half-applied state: both values must remain at the defaults.
        Assert.Equal(defaults.RootPath, options.RootPath);
        Assert.Equal(defaults.CacheDirectory, options.CacheDirectory);
    }

    [Fact]
    public void ApplyRootPath_PreservesCase()
    {
        // Guards against the ConfigCommand-style .ToLower() bug — on case-sensitive file
        // systems a lower-cased path is a different directory (instant split-brain).
        var options = new LocalFileSystemCatalogOptions();
        var mixedCasePath = Path.Combine(_tempRootA, "MixedCase", "SubDir");

        options.ApplyRootPath(mixedCasePath);

        Assert.Equal(mixedCasePath, options.RootPath);
    }

    [Fact]
    public void DefaultRootPath_StaysInUserProfile()
    {
        // Back-compat contract for consumers without OctoRepoRootPath: no root path applied
        // means the catalog stays at ~/.octo/local-catalog.
        var options = new LocalFileSystemCatalogOptions();

        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".octo/local-catalog");
        Assert.Equal(expected, options.RootPath);
    }

    [Fact]
    public async Task PublishedModel_IsResolvableFromSameRoot_ButNotFromDifferentRoot()
    {
        var modelId = new CkModelId("TestModel", "1.0.0");
        var compiledModel = new CkCompiledModelRoot { ModelId = modelId, Description = "isolation test" };

        var serializer = A.Fake<ICkJsonSerializer>();
        A.CallTo(() => serializer.SerializeAsync(A<StreamWriter>._, compiledModel))
            .Returns(Task.CompletedTask);

        // Writer publishes into root A.
        var writer = CreateCatalog(_tempRootA, serializer);
        await writer.PublishAsync(compiledModel);

        // A separate reader configured with the SAME root must resolve the model …
        var readerSameRoot = CreateCatalog(_tempRootA, serializer);
        Assert.True(await readerSameRoot.IsExistingAsync(modelId));

        // … while a reader configured with a DIFFERENT root must not (this is the
        // split-brain failure mode this feature must surface instead of masking).
        var readerOtherRoot = CreateCatalog(_tempRootB, serializer);
        Assert.False(await readerOtherRoot.IsExistingAsync(modelId));
    }

    private static LocalFileSystemCatalog CreateCatalog(string rootPath, ICkJsonSerializer serializer)
    {
        var options = new LocalFileSystemCatalogOptions();
        options.ApplyRootPath(rootPath);
        return new LocalFileSystemCatalog(Options.Create(options), serializer);
    }
}
