using System.Text;
using FakeItEasy;
using Meshmakers.Octo.BlueprintManager.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.BlueprintManager.Tests;

/// <summary>
/// Validates the version-honesty rules of <c>validateVersion</c>: a published version is immutable
/// (content drift without a bump is OCTO-BP100), downgrades are rejected (OCTO-BP101), a failed
/// catalog refresh never lets a published blueprint pass as a first publication (OCTO-BP102), and
/// blueprint dependencies must resolve against the catalogs or a sibling of the same invocation
/// (OCTO-BP103).
/// </summary>
public sealed class ValidateVersionCommandTests : IDisposable
{
    private readonly string _blueprintDir;

    public ValidateVersionCommandTests()
    {
        _blueprintDir = Path.Combine(Path.GetTempPath(), $"bpm-validate-version-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_blueprintDir, "seed-data"));
        File.WriteAllText(Path.Combine(_blueprintDir, "blueprint.yaml"), "blueprintId: MyBlueprint-1.1.0\n");
        File.WriteAllText(Path.Combine(_blueprintDir, "seed-data", "entities.yaml"), "entities: []\n");
    }

    public void Dispose()
    {
        Directory.Delete(_blueprintDir, true);
    }

    private ValidateVersionCommand CreateCommand(IBlueprintCompilerService compiler,
        params IBlueprintCatalog[] catalogs)
    {
        var manager = A.Fake<IBlueprintCatalogManager>();
        A.CallTo(() => manager.RefreshAllCatalogCachesAsync(A<object?>._, true))
            .Returns(catalogs
                .Select(c => new BlueprintCatalogRefreshResult
                {
                    CatalogName = c.CatalogName, Status = BlueprintCatalogRefreshStatus.Refreshed
                })
                .ToList());

        return new ValidateVersionCommand(NullLogger<ValidateVersionCommand>.Instance,
            Options.Create(new BpmToolOptions()), manager, compiler, catalogs);
    }

    private static IBlueprintCompilerService CreateCompiler(string blueprintId,
        List<BlueprintIdVersionRange>? blueprintDependencies = null)
    {
        var compiler = A.Fake<IBlueprintCompilerService>();
        var meta = new BlueprintMetaRootDto
        {
            BlueprintId = new BlueprintId(blueprintId), BlueprintDependencies = blueprintDependencies
        };
        A.CallTo(() => compiler.ValidateAsync(A<string>._, A<OperationResult>._, A<CancellationToken>._))
            .Returns(meta);
        return compiler;
    }

    private static IBlueprintCatalog CreateCatalog(string name, BlueprintId? highestPublished,
        params BlueprintId[] publishedIds)
    {
        var catalog = A.Fake<IBlueprintCatalog>();
        A.CallTo(() => catalog.CatalogName).Returns(name);
        A.CallTo(() => catalog.CanRead).Returns(true);
        A.CallTo(() => catalog.IsExistingAsync(A<BlueprintIdVersionRange>._, A<object?>._))
            .Returns(new BlueprintExistingResult { Exists = highestPublished != null, BlueprintId = highestPublished });
        A.CallTo(() => catalog.IsExistingAsync(A<BlueprintId>._, A<object?>._))
            .ReturnsLazily((BlueprintId id, object? _) => publishedIds.Contains(id));
        return catalog;
    }

    private static void SetPublishedContent(IBlueprintCatalog catalog, string relativePath, string? content)
    {
        var call = A.CallTo(() => catalog.OpenBlueprintFileAsync(A<BlueprintId>._, relativePath,
            A<object?>._, A<CancellationToken>._));
        if (content == null)
        {
            call.ThrowsAsync((BlueprintId id, string path, object? _, CancellationToken _) =>
                new BlueprintFileNotFoundException(id, "test", path));
        }
        else
        {
            // A fresh stream per call — the command disposes what it reads.
            call.ReturnsLazily(() => (Stream)new MemoryStream(Encoding.UTF8.GetBytes(content)));
        }
    }

    private void MirrorLocalContentAsPublished(IBlueprintCatalog catalog)
    {
        SetPublishedContent(catalog, "blueprint.yaml",
            File.ReadAllText(Path.Combine(_blueprintDir, "blueprint.yaml")));
        SetPublishedContent(catalog, "seed-data/entities.yaml",
            File.ReadAllText(Path.Combine(_blueprintDir, "seed-data", "entities.yaml")));
    }

    [Fact]
    public async Task Execute_FirstPublication_IsValid()
    {
        var catalog = CreateCatalog("TestCatalog", highestPublished: null);
        var cmd = CreateCommand(CreateCompiler("MyBlueprint-1.0.0"), catalog);
        cmd.CommandArgumentValue.ParseLayer(["-p", _blueprintDir]);

        await cmd.PreValidate();
        await cmd.Execute();
    }

    [Fact]
    public async Task Execute_PublishedVersionWithIdenticalContent_IsValid()
    {
        var published = new BlueprintId("MyBlueprint", "1.1.0");
        var catalog = CreateCatalog("TestCatalog", published, published);
        MirrorLocalContentAsPublished(catalog);
        var cmd = CreateCommand(CreateCompiler("MyBlueprint-1.1.0"), catalog);
        cmd.CommandArgumentValue.ParseLayer(["-p", _blueprintDir]);

        await cmd.PreValidate();
        await cmd.Execute();
    }

    [Fact]
    public async Task Execute_PublishedVersionWithDriftedContent_FailsWithBp100()
    {
        var published = new BlueprintId("MyBlueprint", "1.1.0");
        var catalog = CreateCatalog("TestCatalog", published, published);
        MirrorLocalContentAsPublished(catalog);
        SetPublishedContent(catalog, "seed-data/entities.yaml", "entities: [something-else]\n");
        var cmd = CreateCommand(CreateCompiler("MyBlueprint-1.1.0"), catalog);
        cmd.CommandArgumentValue.ParseLayer(["-p", _blueprintDir]);

        await cmd.PreValidate();
        var ex = await Assert.ThrowsAsync<ModelValidationException>(cmd.Execute);
        Assert.Contains("OCTO-BP100", ex.Message);
        Assert.Contains("seed-data/entities.yaml", ex.Message);
    }

    [Fact]
    public async Task Execute_LocalFileMissingFromPublishedVersion_FailsWithBp100()
    {
        var published = new BlueprintId("MyBlueprint", "1.1.0");
        var catalog = CreateCatalog("TestCatalog", published, published);
        MirrorLocalContentAsPublished(catalog);
        File.WriteAllText(Path.Combine(_blueprintDir, "README.md"), "new file\n");
        SetPublishedContent(catalog, "README.md", null);
        var cmd = CreateCommand(CreateCompiler("MyBlueprint-1.1.0"), catalog);
        cmd.CommandArgumentValue.ParseLayer(["-p", _blueprintDir]);

        await cmd.PreValidate();
        var ex = await Assert.ThrowsAsync<ModelValidationException>(cmd.Execute);
        Assert.Contains("OCTO-BP100", ex.Message);
        Assert.Contains("README.md (not in published version)", ex.Message);
    }

    [Fact]
    public async Task Execute_LineEndingDifferenceOnly_IsNotDrift()
    {
        var published = new BlueprintId("MyBlueprint", "1.1.0");
        var catalog = CreateCatalog("TestCatalog", published, published);
        SetPublishedContent(catalog, "blueprint.yaml",
            File.ReadAllText(Path.Combine(_blueprintDir, "blueprint.yaml")).Replace("\n", "\r\n"));
        SetPublishedContent(catalog, "seed-data/entities.yaml",
            File.ReadAllText(Path.Combine(_blueprintDir, "seed-data", "entities.yaml")).Replace("\n", "\r\n"));
        var cmd = CreateCommand(CreateCompiler("MyBlueprint-1.1.0"), catalog);
        cmd.CommandArgumentValue.ParseLayer(["-p", _blueprintDir]);

        await cmd.PreValidate();
        await cmd.Execute();
    }

    [Fact]
    public async Task Execute_DeclaredLowerThanHighestPublished_FailsWithBp101()
    {
        var catalog = CreateCatalog("TestCatalog", new BlueprintId("MyBlueprint", "2.0.0"));
        var cmd = CreateCommand(CreateCompiler("MyBlueprint-1.1.0"), catalog);
        cmd.CommandArgumentValue.ParseLayer(["-p", _blueprintDir]);

        await cmd.PreValidate();
        var ex = await Assert.ThrowsAsync<ModelValidationException>(cmd.Execute);
        Assert.Contains("OCTO-BP101", ex.Message);
    }

    [Fact]
    public async Task Execute_NewHigherVersionWithChangedContent_IsValid()
    {
        var published = new BlueprintId("MyBlueprint", "1.0.0");
        var catalog = CreateCatalog("TestCatalog", published, published);
        MirrorLocalContentAsPublished(catalog);
        SetPublishedContent(catalog, "seed-data/entities.yaml", "entities: [old-content]\n");
        var cmd = CreateCommand(CreateCompiler("MyBlueprint-1.1.0"), catalog);
        cmd.CommandArgumentValue.ParseLayer(["-p", _blueprintDir]);

        await cmd.PreValidate();
        await cmd.Execute();
    }

    [Fact]
    public async Task Execute_FailedCatalogRefresh_FailsWithBp102()
    {
        var catalog = CreateCatalog("TestCatalog", highestPublished: null);
        var manager = A.Fake<IBlueprintCatalogManager>();
        A.CallTo(() => manager.RefreshAllCatalogCachesAsync(A<object?>._, true))
            .Returns(new List<BlueprintCatalogRefreshResult>
            {
                new()
                {
                    CatalogName = "TestCatalog",
                    Status = BlueprintCatalogRefreshStatus.Failed,
                    Message = "host unreachable"
                }
            });
        var cmd = new ValidateVersionCommand(NullLogger<ValidateVersionCommand>.Instance,
            Options.Create(new BpmToolOptions()), manager, CreateCompiler("MyBlueprint-1.0.0"), [catalog]);
        cmd.CommandArgumentValue.ParseLayer(["-p", _blueprintDir]);

        await cmd.PreValidate();
        var ex = await Assert.ThrowsAsync<ModelValidationException>(cmd.Execute);
        Assert.Contains("OCTO-BP102", ex.Message);
    }

    [Fact]
    public async Task Execute_UnsatisfiedBlueprintDependency_FailsWithBp103()
    {
        var catalog = CreateCatalog("TestCatalog", highestPublished: null);
        var compiler = CreateCompiler("MyBlueprint-1.0.0", [new BlueprintIdVersionRange("Other", "[1.0,)")]);
        var cmd = CreateCommand(compiler, catalog);
        cmd.CommandArgumentValue.ParseLayer(["-p", _blueprintDir]);

        await cmd.PreValidate();
        var ex = await Assert.ThrowsAsync<ModelValidationException>(cmd.Execute);
        Assert.Contains("OCTO-BP103", ex.Message);
    }

    [Fact]
    public async Task Execute_DependencySatisfiedBySiblingOfSameRun_IsValid()
    {
        var catalog = CreateCatalog("TestCatalog", highestPublished: null);
        var siblingDir = Path.Combine(Path.GetTempPath(), $"bpm-validate-version-sibling-{Guid.NewGuid():N}");
        Directory.CreateDirectory(siblingDir);
        File.WriteAllText(Path.Combine(siblingDir, "blueprint.yaml"), "blueprintId: Base-1.0.0\n");
        try
        {
            var compiler = A.Fake<IBlueprintCompilerService>();
            A.CallTo(() => compiler.ValidateAsync(siblingDir, A<OperationResult>._, A<CancellationToken>._))
                .Returns(new BlueprintMetaRootDto { BlueprintId = new BlueprintId("Base", "1.0.0") });
            A.CallTo(() => compiler.ValidateAsync(_blueprintDir, A<OperationResult>._, A<CancellationToken>._))
                .Returns(new BlueprintMetaRootDto
                {
                    BlueprintId = new BlueprintId("MyBlueprint", "1.0.0"),
                    BlueprintDependencies = [new BlueprintIdVersionRange("Base", "[1.0,)")]
                });

            var cmd = CreateCommand(compiler, catalog);
            cmd.CommandArgumentValue.ParseLayer(["-p", siblingDir, "-p", _blueprintDir]);

            await cmd.PreValidate();
            await cmd.Execute();
        }
        finally
        {
            Directory.Delete(siblingDir, true);
        }
    }

    [Fact]
    public async Task Execute_UnknownCatalogName_FailsWithActionableMessage()
    {
        var catalog = CreateCatalog("TestCatalog", highestPublished: null);
        var cmd = CreateCommand(CreateCompiler("MyBlueprint-1.0.0"), catalog);
        cmd.CommandArgumentValue.ParseLayer(["-p", _blueprintDir, "-cn", "NoSuchCatalog"]);

        await cmd.PreValidate();
        var ex = await Assert.ThrowsAsync<ModelValidationException>(cmd.Execute);
        Assert.Contains("NoSuchCatalog", ex.Message);
        Assert.Contains("TestCatalog", ex.Message);
    }
}
