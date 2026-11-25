using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
///     AB#9371: the install path runs the shared blueprint draft validator before anything is installed. Only
///     findings that would make the install fail anyway refuse it; every other finding becomes a warning, so a
///     blueprint that installed before still installs. <c>ValidateBlueprintAsync</c> reports all findings.
/// </summary>
public class BlueprintServiceDraftValidationTests
{
    private const string TenantId = "tenant-a";
    private static readonly BlueprintId Root = new("Samples.Draft-1.0.0");

    private readonly IBlueprintDependencyResolver _resolver = A.Fake<IBlueprintDependencyResolver>();
    private readonly ITenantBlueprintInstallations _installations = A.Fake<ITenantBlueprintInstallations>();
    private readonly IRuntimeRepositoryProvider _provider = A.Fake<IRuntimeRepositoryProvider>();
    private readonly ICkModelUpgradeService _upgrade = A.Fake<ICkModelUpgradeService>();
    private readonly IBlueprintCatalogManager _catalog = A.Fake<IBlueprintCatalogManager>();
    private readonly IBlueprintDraftValidator _validator = A.Fake<IBlueprintDraftValidator>();

    public BlueprintServiceDraftValidationTests()
    {
        A.CallTo(() => _installations.GetByBlueprintNameAsync(TenantId, A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<BlueprintInstallation?>(null));
        A.CallTo(() => _upgrade.UpgradeModelsAsync(TenantId, A<IEnumerable<CkModelIdVersionRange>>._,
                A<CkMigrationOptions?>._, A<IReadOnlyDictionary<string, string>?>._, A<CancellationToken>._))
            .Returns(new CkModelUpgradeResult { Success = true });
        A.CallTo(() => _provider.GetSchemaVersionsAsync(TenantId, A<CancellationToken>._))
            .Returns(new Dictionary<string, string>());
        A.CallTo(() => _resolver.ResolveAsync(Root, A<CancellationToken>._))
            .Returns(new BlueprintResolutionResult
            {
                RootBlueprintId = Root,
                Success = true,
                InstallOrder =
                [
                    new BlueprintMetaRootDto
                    {
                        BlueprintId = Root, CkModelDependencies = [new CkModelIdVersionRange("Acme.Model-[1.0,2.0)")]
                    }
                ],
                Conflicts = [],
                DependencyRanges = new Dictionary<string, List<BlueprintIdVersionRange>>()
            });
    }

    private BlueprintService CreateService() => new(
        A.Fake<ICkCacheService>(), _catalog, A.Fake<ITenantBlueprintHistory>(),
        A.Fake<IBlueprintMigrationExecutor>(), A.Fake<IBlueprintMigrationParser>(), _upgrade, _provider,
        A.Fake<IImportRtModelCommand>(), A.Fake<IRtYamlSerializer>(), A.Fake<IBlueprintNotifications>(), _resolver,
        _installations, A.Fake<IBlueprintVariableProvider>(), NullLogger<BlueprintService>.Instance,
        draftValidator: _validator);

    private void Findings(params OperationMessage[] findings)
    {
        A.CallTo(() => _validator.ValidateAsync(A<IBlueprintFileSource>._, A<BlueprintValidationOptions?>._,
                A<CancellationToken>._))
            .Returns(new BlueprintDraftValidationResult(
                new BlueprintMetaRootDto
                {
                    BlueprintId = Root, CkModelDependencies = [new CkModelIdVersionRange("Acme.Model-[1.0,2.0)")]
                }, findings));
    }

    private static OperationMessage Error(int code, Dictionary<string, string>? parameters = null) =>
        new(MessageLevel.Error, "blueprint.yaml", code, $"finding {code}", parameters ?? []);

    private static OperationMessage NotInCatalogs(string model) =>
        Error(BlueprintDraftValidationCodes.CkModelDependencyNotInCatalogs,
            new Dictionary<string, string> { ["modelId"] = model, ["versionRange"] = "[1.0,2.0)" });

    private void AssertNothingInstalled()
    {
        A.CallTo(() => _provider.EnsureCkModelInstalledAsync(A<string>._, A<CkModelId>._, A<OperationResult>._,
            A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => _installations.UpsertAsync(A<string>._, A<BlueprintInstallation>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Apply_NonBlockingError_IsAWarning_AndTheBlueprintInstalls()
    {
        Findings(Error(BlueprintDraftValidationCodes.SeedSecretValue),
            Error(BlueprintDraftValidationCodes.SeedEntityDuplicateAcrossFiles));

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Root, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.OperationResult.Messages, m =>
            m.MessageNumber == BlueprintDraftValidationCodes.SeedSecretValue && m.MessageLevel == MessageLevel.Warning);
        A.CallTo(() => _validator.ValidateAsync(A<IBlueprintFileSource>._,
                A<BlueprintValidationOptions?>.That.Matches(o => o!.CkModelCatalogs != null), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData(BlueprintDraftValidationCodes.SeedDataFileOfSeveralMissing)]
    [InlineData(BlueprintDraftValidationCodes.SeedDataFileUnparsable)]
    [InlineData(BlueprintDraftValidationCodes.CkModelDependencyNoMinimumVersion)]
    public async Task Apply_InstallBlockingError_RefusesBeforeAnythingIsInstalled(int code)
    {
        Findings(Error(code));

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Root, false, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.OperationResult.Messages, m => m.MessageNumber == code && m.MessageLevel == MessageLevel.Error);
        AssertNothingInstalled();
    }

    [Fact]
    public async Task Apply_DependencyInNoCatalog_AndNotInstalled_IsAWarning_TheInstallStepDecides()
    {
        // A service-managed model is installed from its descriptor even when no catalog offers the range.
        Findings(NotInCatalogs("Acme.Model"));

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Root, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.OperationResult.Messages, m =>
            m.MessageNumber == BlueprintDraftValidationCodes.CkModelDependencyNotInCatalogs
            && m.MessageLevel == MessageLevel.Warning);
    }

    [Fact]
    public async Task Apply_DependencyInNoCatalog_ButInstalledInTheTenant_IsAWarning()
    {
        // A model imported from a file (ImportCk) is in no catalog but the tenant runs it: installs as before.
        A.CallTo(() => _provider.GetSchemaVersionsAsync(TenantId, A<CancellationToken>._))
            .Returns(new Dictionary<string, string> { ["Acme.Model"] = "1.2.0" });
        Findings(NotInCatalogs("Acme.Model"));

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Root, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.OperationResult.Messages, m =>
            m.MessageNumber == BlueprintDraftValidationCodes.CkModelDependencyNotInCatalogs
            && m.MessageLevel == MessageLevel.Warning);
    }

    [Fact]
    public async Task Apply_DependencyLeftOutOfTheApply_IsAWarning()
    {
        // e.g. System.StreamData on a tenant without stream data: not part of the CK models this apply installs.
        Findings(NotInCatalogs("System.StreamData"));

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Root, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Validate_ReportsAllFindings_AndTheMissingModel()
    {
        A.CallTo(() => _catalog.IsExistingAsync(Root, A<object?>._)).Returns(true);
        A.CallTo(() => _catalog.GetAsync(Root, A<OperationResult>._, A<object?>._, A<CancellationToken?>._))
            .Returns(new BlueprintMetaRootDto
            {
                BlueprintId = Root, CkModelDependencies = [new CkModelIdVersionRange("Acme.Model-[1.0,2.0)")]
            });
        Findings(NotInCatalogs("Acme.Model"), Error(BlueprintDraftValidationCodes.SeedSecretValue));

        var result = await CreateService().ValidateBlueprintAsync(Root, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.Equal("Acme.Model", Assert.Single(result.MissingCkModels).Name);
        Assert.Contains(result.OperationResult.Messages, m =>
            m.MessageNumber == BlueprintDraftValidationCodes.SeedSecretValue && m.MessageLevel == MessageLevel.Error);
    }
}
