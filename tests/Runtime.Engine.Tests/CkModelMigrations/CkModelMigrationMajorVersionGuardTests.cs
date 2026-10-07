using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;
using Meshmakers.Octo.Runtime.Engine.CkModelMigrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.CkModelMigrations;

/// <summary>
///     AB#4924 (G3): a schema-only bridge must never carry a tenant across a major version.
/// </summary>
/// <remarks>
///     The fixture mirrors the real incident: System.Communication 4.x renames Pool to DeploymentSite
///     with one script, and its migration-meta listed entry points only up to 3.36.0. Tenants on 3.40.0
///     fell through to the post-chain schema-only bridge and were lifted to 4.x without the rename.
///     Every case that stays inside one major line keeps its previous behaviour — those are pinned
///     here next to the refused ones so a future relaxation has to break a named test.
/// </remarks>
public class CkModelMigrationMajorVersionGuardTests
{
    private const string Model = "System.Communication";
    private const string Script = "3.35.0-to-4.0.0.yaml";

    private readonly ICkModelMigrationService _sut;
    private readonly ICkMigrationContentProvider _contentProvider;

    public CkModelMigrationMajorVersionGuardTests()
    {
        _contentProvider = A.Fake<ICkMigrationContentProvider>();

        _sut = new CkModelMigrationService(
            A.Fake<ICkMigrationParser>(),
            _contentProvider,
            A.Fake<IRuntimeRepositoryProvider>(),
            A.Fake<ICatalogService>(),
            A.Fake<ICkModelImportAuditTrail>(),
            NullLogger<CkModelMigrationService>.Instance);
    }

    /// <summary>The 4.x meta as it stood before the G3 fix: entries only for 3.35.0 and 3.36.0.</summary>
    private void SetupMeta(CkModelId toModel, params string[] fromVersions)
    {
        var meta = new CkMigrationMetaDto
        {
            CkModelId = $"{Model}-4.0.0",
            Migrations = fromVersions.Select(v => new CkMigrationReferenceDto
            {
                FromVersion = v,
                ToVersion = "4.0.0",
                ScriptPath = Script,
                Description = "Rename CK type Pool to DeploymentSite",
                Breaking = true
            }).ToList()
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._)).Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, A<string>._, "4.0.0", A<CancellationToken>._))
            .ReturnsLazily((CkModelId _, string from, string _, CancellationToken _) =>
                new CkMigrationScriptDto { SourceVersion = "3.35.0", TargetVersion = "4.0.0", Description = from });
    }

    [Fact]
    public async Task PostChainBridge_AcrossMajor_IsRefused()
    {
        // The incident: 3.40.0 is above every entry point, target is a higher major.
        var from = new CkModelId(Model, "3.40.0");
        var to = new CkModelId(Model, "4.5.0");
        SetupMeta(to, "3.35.0", "3.36.0");

        var path = await _sut.FindMigrationPathAsync(from, to, TestContext.Current.CancellationToken);

        Assert.Null(path);
    }

    [Fact]
    public async Task PostChainBridge_WithinMajor_IsUnchanged()
    {
        // A tenant already on 4.0.0 moving to 4.5.0: additive minor bumps, no script needed.
        var from = new CkModelId(Model, "4.0.0");
        var to = new CkModelId(Model, "4.5.0");
        SetupMeta(to, "3.35.0", "3.36.0");

        var path = await _sut.FindMigrationPathAsync(from, to, TestContext.Current.CancellationToken);

        Assert.NotNull(path);
        var step = Assert.Single(path.Steps);
        Assert.Equal("4.0.0", step.FromVersion);
        Assert.Equal("4.5.0", step.ToVersion);
        Assert.Null(step.Script);
        Assert.False(path.HasBreakingChanges);
    }

    [Fact]
    public async Task PostChainBridge_PatchWithinMajor_IsUnchanged()
    {
        var from = new CkModelId(Model, "4.3.0");
        var to = new CkModelId(Model, "4.3.1");
        SetupMeta(to, "3.35.0", "3.36.0");

        var path = await _sut.FindMigrationPathAsync(from, to, TestContext.Current.CancellationToken);

        Assert.NotNull(path);
        Assert.Null(Assert.Single(path.Steps).Script);
    }

    [Theory]
    [InlineData("3.35.0")]
    [InlineData("3.36.0")]
    [InlineData("3.37.0")]
    [InlineData("3.37.1")]
    [InlineData("3.38.0")]
    [InlineData("3.39.0")]
    [InlineData("3.39.1")]
    [InlineData("3.40.0")]
    [InlineData("3.41.0")]
    [InlineData("3.42.0")]
    public async Task EveryListedEntryPoint_RunsTheScript(string installed)
    {
        // The fixed meta: one entry per published / announced 3.x >= 3.35.0.
        var from = new CkModelId(Model, installed);
        var to = new CkModelId(Model, "4.5.0");
        SetupMeta(to, "3.35.0", "3.36.0", "3.37.0", "3.37.1", "3.38.0", "3.39.0", "3.39.1", "3.40.0",
            "3.41.0", "3.42.0");

        var path = await _sut.FindMigrationPathAsync(from, to, TestContext.Current.CancellationToken);

        Assert.NotNull(path);
        Assert.Equal(installed, path.Steps[0].FromVersion);
        var scripted = Assert.Single(path.Steps, s => s.Script != null);
        Assert.Equal("4.0.0", scripted.ToVersion);
        Assert.True(path.HasBreakingChanges);
        // The end gap 4.0.0 -> 4.5.0 is schema-only and stays inside major 4.
        Assert.True(path.IsPartialPath);
    }

    [Fact]
    public async Task UnlistedVersionBelowTheLastEntry_BridgesUpToTheNextEntry()
    {
        // A version nobody listed (e.g. a local-only 3.38.5) still finds the script through the
        // start-gap bridge to the next entry above it.
        var from = new CkModelId(Model, "3.38.5");
        var to = new CkModelId(Model, "4.5.0");
        SetupMeta(to, "3.35.0", "3.36.0", "3.39.0", "3.40.0");

        var path = await _sut.FindMigrationPathAsync(from, to, TestContext.Current.CancellationToken);

        Assert.NotNull(path);
        Assert.Equal(2, path.Steps.Count);
        Assert.Null(path.Steps[0].Script);
        Assert.Equal("3.39.0", path.Steps[0].ToVersion);
        Assert.NotNull(path.Steps[1].Script);
    }

    [Fact]
    public async Task VersionAboveTheLastEntry_IsRefused()
    {
        // The next main bump nobody added to the 4.x meta (3.43.0) must fail loudly, not slip through.
        var from = new CkModelId(Model, "3.43.0");
        var to = new CkModelId(Model, "4.5.0");
        SetupMeta(to, "3.35.0", "3.36.0", "3.40.0", "3.42.0");

        var path = await _sut.FindMigrationPathAsync(from, to, TestContext.Current.CancellationToken);

        Assert.Null(path);
    }

    [Fact]
    public async Task BridgedPath_EndGapAcrossMajor_IsRefused()
    {
        // Chain 3.0.0 -> 3.1.0 is reachable through the start gap, but it ends in major 3 while the
        // target is 5.0.0: the remaining gap would be a schema-only bridge across two majors.
        var from = new CkModelId("TestModel", "1.0.0");
        var to = new CkModelId("TestModel", "5.0.0");
        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-5.0.0",
            Migrations =
            [
                new CkMigrationReferenceDto { FromVersion = "3.0.0", ToVersion = "3.1.0", ScriptPath = "a.yaml" }
            ]
        };
        A.CallTo(() => _contentProvider.HasMigrationsAsync(to, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(to, A<CancellationToken>._)).Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(to, "3.0.0", "3.1.0", A<CancellationToken>._))
            .Returns(new CkMigrationScriptDto { SourceVersion = "3.0.0", TargetVersion = "3.1.0" });

        var path = await _sut.FindMigrationPathAsync(from, to, TestContext.Current.CancellationToken);

        Assert.Null(path);
    }

    [Fact]
    public async Task PartialPath_EndGapAcrossMajor_IsRefused()
    {
        // An exact-from entry 1.0.0 -> 2.0.0, target 3.1.0: the 2.0.0 -> 3.1.0 tail crosses a major.
        var from = new CkModelId("TestModel", "1.0.0");
        var to = new CkModelId("TestModel", "3.1.0");
        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-3.1.0",
            Migrations =
            [
                new CkMigrationReferenceDto { FromVersion = "1.0.0", ToVersion = "2.0.0", ScriptPath = "a.yaml" }
            ]
        };
        A.CallTo(() => _contentProvider.HasMigrationsAsync(to, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(to, A<CancellationToken>._)).Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(to, "1.0.0", "2.0.0", A<CancellationToken>._))
            .Returns(new CkMigrationScriptDto { SourceVersion = "1.0.0", TargetVersion = "2.0.0" });

        var path = await _sut.FindMigrationPathAsync(from, to, TestContext.Current.CancellationToken);

        Assert.Null(path);
    }

    [Fact]
    public async Task StartGapBridgeAcrossMajor_IsUnchanged()
    {
        // The chain itself carries the data across the major (2.2.0 -> [3.0.1 -> 3.1.0]); only the
        // leading no-op crosses it. Unchanged by the guard.
        var from = new CkModelId("TestModel", "2.2.0");
        var to = new CkModelId("TestModel", "3.1.0");
        var meta = new CkMigrationMetaDto
        {
            CkModelId = "TestModel-3.1.0",
            Migrations =
            [
                new CkMigrationReferenceDto { FromVersion = "3.0.1", ToVersion = "3.1.0", ScriptPath = "a.yaml" }
            ]
        };
        A.CallTo(() => _contentProvider.HasMigrationsAsync(to, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(to, A<CancellationToken>._)).Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(to, "3.0.1", "3.1.0", A<CancellationToken>._))
            .Returns(new CkMigrationScriptDto { SourceVersion = "3.0.1", TargetVersion = "3.1.0" });

        var path = await _sut.FindMigrationPathAsync(from, to, TestContext.Current.CancellationToken);

        Assert.NotNull(path);
        Assert.False(path.IsPartialPath);
        Assert.Equal(2, path.Steps.Count);
    }

    [Fact]
    public async Task NoMigrationsAtAll_AcrossMajor_IsUnchanged()
    {
        // A model without any migration-meta declares that it has nothing to migrate; the guard is
        // about models that DO migrate data and simply do not list the installed version.
        var from = new CkModelId("Samples.Demo", "1.4.0");
        var to = new CkModelId("Samples.Demo", "2.0.0");
        A.CallTo(() => _contentProvider.HasMigrationsAsync(to, A<CancellationToken>._)).Returns(false);

        var path = await _sut.FindMigrationPathAsync(from, to, TestContext.Current.CancellationToken);

        Assert.NotNull(path);
        Assert.Null(Assert.Single(path.Steps).Script);
    }

    [Fact]
    public async Task MigrateAsync_AcrossMajorWithoutEntry_FailsWithTheRefusalText()
    {
        var from = new CkModelId(Model, "3.40.0");
        var to = new CkModelId(Model, "4.5.0");
        SetupMeta(to, "3.35.0", "3.36.0");

        var result = await _sut.MigrateAsync("tenant1", from, to,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("No migration path found from System.Communication-3.40.0 to System.Communication-4.5.0", error);
        Assert.Contains("crosses a major version (3.x -> 4.x)", error);
        Assert.Contains("schema-only bridge across a major version is refused", error);
    }

    [Fact]
    public async Task ValidateAsync_AcrossMajorWithoutEntry_IsInvalid()
    {
        var from = new CkModelId(Model, "3.40.0");
        var to = new CkModelId(Model, "4.5.0");
        SetupMeta(to, "3.35.0", "3.36.0");

        var result = await _sut.ValidateAsync("tenant1", from, to, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Message.Contains("crosses a major version"));
    }

    [Theory]
    [InlineData("3.40.0", "4.5.0", true)]
    [InlineData("3.99.9", "4.0.0", true)]
    [InlineData("1.0.0", "3.0.0", true)]
    [InlineData("4.0.0", "4.5.0", false)]
    [InlineData("4.5.0", "4.5.1", false)]
    [InlineData("4.5.0", "3.40.0", false)]
    public void CrossesMajor(string from, string to, bool expected)
    {
        Assert.Equal(expected, CkMigrationMajorVersionGuard.CrossesMajor(new CkVersion(from), new CkVersion(to)));
    }
}
