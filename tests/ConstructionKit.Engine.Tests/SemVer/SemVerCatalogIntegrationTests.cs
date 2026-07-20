using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

/// <summary>
///     Integration of the ValidateVersion building blocks against a real
///     <c>LocalFileSystemCatalog</c> in a temp directory: publish a baseline, then validate a
///     changed model with and without a version bump, and cover the first-publication case.
/// </summary>
public class SemVerCatalogIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;
    private readonly ICatalogService _catalogService;
    private readonly ICkModelDiffService _diffService;
    private readonly ICkSemVerClassifier _classifier;

    public SemVerCatalogIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SemVerCatalogTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConstructionKit();
        services.Configure<LocalFileSystemCatalogOptions>(options =>
        {
            options.ApplyRootPath(_tempDir);
            options.IsEnabled = true;
        });
        services.Configure<PublicGitHubCatalogOptions>(options => options.IsEnabled = false);
        services.Configure<PrivateGitHubCatalogOptions>(options => options.IsEnabled = false);
        _serviceProvider = services.BuildServiceProvider();

        _catalogService = _serviceProvider.GetRequiredService<ICatalogService>();
        _diffService = _serviceProvider.GetRequiredService<ICkModelDiffService>();
        _classifier = _serviceProvider.GetRequiredService<ICkSemVerClassifier>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    /// <summary>
    ///     Self-contained model (no dependencies, no types) so publishing needs no other
    ///     catalog content.
    /// </summary>
    private static CkCompiledModelRoot BuildFixtureModel(string version)
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("SemVerFixture", version),
            Description = "SemVer integration fixture",
            Attributes =
            [
                new CkAttributeDto { AttributeId = "Serial", ValueType = AttributeValueTypesDto.String }
            ],
            Enums =
            [
                new CkEnumDto
                {
                    EnumId = "State",
                    Values =
                    [
                        new CkEnumValueDto { Key = 0, Name = "Off" },
                        new CkEnumValueDto { Key = 1, Name = "On" }
                    ]
                }
            ]
        };
    }

    private async Task PublishAsync(CkCompiledModelRoot model)
    {
        await _catalogService.PublishAsync(LocalFileSystemCatalog.Name, model,
            new OriginFileResolver(_tempDir), isForced: true);
    }

    private Task<ModelExistingResult> GetBaselineAsync()
    {
        return _catalogService.IsExistingAsync(new CkModelIdVersionRange("SemVerFixture", "[0.0,)"));
    }

    [Fact]
    public async Task EmptyCatalog_ReportsFirstPublication_NotUnreachable()
    {
        var result = await GetBaselineAsync();

        Assert.False(result.Exists);
        Assert.False(result.SourceUnreachable);
    }

    [Fact]
    public async Task PublishedModel_IsFoundAsBaseline_WithCatalogNameAndCacheAge()
    {
        await PublishAsync(BuildFixtureModel("1.0.0"));

        var result = await GetBaselineAsync();

        Assert.True(result.Exists);
        Assert.Equal(new CkVersion("1.0.0"), result.ModelId!.Version);
        Assert.Equal(LocalFileSystemCatalog.Name, result.CatalogName);
        Assert.NotNull(result.CacheUpdatedAt);
        Assert.False(result.SourceUnreachable);
    }

    [Fact]
    public async Task MultiplePublishedVersions_HighestWinsAsBaseline()
    {
        await PublishAsync(BuildFixtureModel("1.0.0"));
        await PublishAsync(BuildFixtureModel("1.1.0"));

        var result = await GetBaselineAsync();

        Assert.Equal(new CkVersion("1.1.0"), result.ModelId!.Version);
    }

    [Fact]
    public async Task ChangeWithoutBump_FailsValidation()
    {
        await PublishAsync(BuildFixtureModel("1.0.0"));
        var baselineResult = await GetBaselineAsync();
        var operationResult = new OperationResult();
        var baseline = await _catalogService.GetAsync(baselineResult.CatalogName!, baselineResult.ModelId!,
            operationResult);
        Assert.False(operationResult.HasErrors);

        // The "changer does not touch the version" core case: additive change, version untouched
        var current = BuildFixtureModel("1.0.0");
        current.Enums!.Single().Values.Add(new CkEnumValueDto { Key = 2, Name = "Standby" });

        var changes = _diffService.Diff(baseline!, current);
        var classified = _classifier.Classify(changes, baseline!, current);
        var requiredLevel = _classifier.GetRequiredLevel(classified);
        var validation = _classifier.ValidateDeclaredVersion(baselineResult.ModelId!.Version,
            current.ModelId.Version, requiredLevel);

        Assert.Equal(CkSemVerLevel.Minor, requiredLevel);
        Assert.Equal(CkSemVerVerdict.VersionTooLow, validation.Verdict);
        Assert.Equal(new CkVersion("1.1.0"), validation.MinimumVersion);
    }

    [Fact]
    public async Task ChangeWithSufficientBump_PassesValidation()
    {
        await PublishAsync(BuildFixtureModel("1.0.0"));
        var baselineResult = await GetBaselineAsync();
        var operationResult = new OperationResult();
        var baseline = await _catalogService.GetAsync(baselineResult.CatalogName!, baselineResult.ModelId!,
            operationResult);

        var current = BuildFixtureModel("1.1.0");
        current.Enums!.Single().Values.Add(new CkEnumValueDto { Key = 2, Name = "Standby" });

        var changes = _diffService.Diff(baseline!, current);
        var classified = _classifier.Classify(changes, baseline!, current);
        var requiredLevel = _classifier.GetRequiredLevel(classified);
        var validation = _classifier.ValidateDeclaredVersion(baselineResult.ModelId!.Version,
            current.ModelId.Version, requiredLevel);

        Assert.Equal(CkSemVerVerdict.Valid, validation.Verdict);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public async Task LocalCatalogDisabledAfterConstruction_IsExcludedFromBaselineRetrieval()
    {
        // Regression for the frozen-ctor bug: LocalFileSystemCatalog.CanRead/CanWrite were captured at
        // construction time, so toggling IsEnabled at runtime (the CLI -lce switch, applied after the
        // singleton catalog had already been built) had no effect and the "disabled" local catalog was
        // still read. CanRead/CanWrite are now evaluated live from the options.
        await PublishAsync(BuildFixtureModel("1.0.0"));
        Assert.True((await GetBaselineAsync()).Exists);

        // Disable the local catalog AFTER the catalog singleton has been constructed and used.
        _serviceProvider.GetRequiredService<IOptions<LocalFileSystemCatalogOptions>>().Value.IsEnabled = false;

        var localCatalog = _serviceProvider.GetServices<ICatalog>()
            .Single(c => c.CatalogName == LocalFileSystemCatalog.Name);
        Assert.False(localCatalog.CanRead);
        Assert.False(localCatalog.CanWrite);

        // With the only enabled catalog that holds the model now disabled, the baseline must no longer
        // resolve — the CatalogManager must skip the disabled catalog instead of querying it.
        Assert.False((await GetBaselineAsync()).Exists);
    }

    [Fact]
    public async Task RoundTrippedBaseline_ComparesEqualToInMemoryModel()
    {
        // The baseline goes through JSON serialization in the catalog — the diff against an
        // identical in-memory model must still be empty (canonical value comparison).
        await PublishAsync(BuildFixtureModel("1.0.0"));
        var baselineResult = await GetBaselineAsync();
        var operationResult = new OperationResult();
        var baseline = await _catalogService.GetAsync(baselineResult.CatalogName!, baselineResult.ModelId!,
            operationResult);

        var changes = _diffService.Diff(baseline!, BuildFixtureModel("1.0.0"));

        Assert.Empty(changes);
    }
}
