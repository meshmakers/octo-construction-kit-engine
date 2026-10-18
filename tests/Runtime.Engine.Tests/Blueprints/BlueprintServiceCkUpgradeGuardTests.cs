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
///     AB#6871 (CK v2 F2.5): a blueprint apply or update judges the CK models it installs or raises as ONE set against
///     the tenant (the guard is the host's <see cref="IBlueprintCkUpgradeGuard" />). A blocked set is refused before the
///     first change unless the caller forces it; the preview and a dry run only report the blockers; callers that do not
///     ask for the guard (platform-internal blueprint applies) and a host without a guard behave as before.
/// </summary>
public class BlueprintServiceCkUpgradeGuardTests
{
    private const string TenantId = "tenant-a";
    private static readonly BlueprintId Current = new("Shop-1.0.0");
    private static readonly BlueprintId Target = new("Shop-2.0.0");

    private readonly IBlueprintCatalogManager _catalog = A.Fake<IBlueprintCatalogManager>();
    private readonly ITenantBlueprintHistory _history = A.Fake<ITenantBlueprintHistory>();
    private readonly IRuntimeRepositoryProvider _provider = A.Fake<IRuntimeRepositoryProvider>();
    private readonly ICkModelUpgradeService _upgrade = A.Fake<ICkModelUpgradeService>();
    private readonly IBlueprintDependencyResolver _resolver = A.Fake<IBlueprintDependencyResolver>();
    private readonly ITenantBlueprintInstallations _installations = A.Fake<ITenantBlueprintInstallations>();
    private readonly IBlueprintCkUpgradeGuard _guard = A.Fake<IBlueprintCkUpgradeGuard>();

    private static BlueprintCkUpgradeBlocker RegistrationBlocker() => new()
    {
        Dependent = "EnergyCommunity.Registration-2.8.0",
        Update = "Basic-2.5.0",
        Kind = "ExactPin",
        Subject = "Basic-2.4.0",
        Code = "CK_UPGRADE_EXACT_PIN",
        Message = "EnergyCommunity.Registration 2.8.0 would break: exact pin Basic-2.4.0: Basic-2.5.0 replaces it"
    };

    public BlueprintServiceCkUpgradeGuardTests()
    {
        A.CallTo(() => _catalog.IsExistingAsync(A<BlueprintId>._, A<object?>._)).Returns(true);
        A.CallTo(() => _catalog.GetAsync(A<BlueprintId>._, A<OperationResult>._, A<object?>._, A<CancellationToken?>._))
            .Returns(new BlueprintMetaRootDto
            {
                BlueprintId = Target, CkModelDependencies = [new CkModelIdVersionRange("Basic-[2.5,3.0)")]
            });
        A.CallTo(() => _history.GetCurrentByBlueprintNameAsync(TenantId, "Shop", A<CancellationToken>._))
            .Returns(new TenantBlueprintInfo { BlueprintId = Current, AppliedAt = DateTime.UtcNow });
        A.CallTo(() => _installations.GetByBlueprintNameAsync(TenantId, A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<BlueprintInstallation?>(null));
        A.CallTo(() => _upgrade.UpgradeModelsAsync(TenantId, A<IEnumerable<CkModelIdVersionRange>>._,
                A<CkMigrationOptions?>._, A<IReadOnlyDictionary<string, string>?>._, A<CancellationToken>._))
            .Returns(new CkModelUpgradeResult { Success = true });
        A.CallTo(() => _resolver.ResolveAsync(Target, A<CancellationToken>._))
            .Returns(new BlueprintResolutionResult
            {
                RootBlueprintId = Target,
                Success = true,
                InstallOrder =
                [
                    new BlueprintMetaRootDto
                    {
                        BlueprintId = Target, CkModelDependencies = [new CkModelIdVersionRange("Basic-[2.5,3.0)")]
                    }
                ],
                Conflicts = [],
                DependencyRanges = new Dictionary<string, List<BlueprintIdVersionRange>>()
            });
        GivenInstalledBasic("2.4.0");
        GivenGuardBlocks();
    }

    private void GivenInstalledBasic(string version) =>
        A.CallTo(() => _provider.GetSchemaVersionsAsync(TenantId, A<CancellationToken>._))
            .Returns(new Dictionary<string, string> { ["Basic"] = version });

    private void GivenGuardBlocks() =>
        A.CallTo(() => _guard.EvaluateAsync(TenantId, A<IReadOnlyList<CkModelId>>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<BlueprintCkUpgradeBlocker>>([RegistrationBlocker()]));

    private void GivenGuardAllows() =>
        A.CallTo(() => _guard.EvaluateAsync(TenantId, A<IReadOnlyList<CkModelId>>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<BlueprintCkUpgradeBlocker>>([]));

    private BlueprintService CreateService(bool withGuard = true) => new(
        A.Fake<ICkCacheService>(), _catalog, _history, A.Fake<IBlueprintMigrationExecutor>(),
        A.Fake<IBlueprintMigrationParser>(), _upgrade, _provider, A.Fake<IImportRtModelCommand>(),
        A.Fake<IRtYamlSerializer>(), A.Fake<IBlueprintNotifications>(), _resolver, _installations,
        A.Fake<IBlueprintVariableProvider>(), NullLogger<BlueprintService>.Instance, withGuard ? _guard : null);

    private void AssertNothingInstalled()
    {
        A.CallTo(() => _provider.EnsureCkModelInstalledAsync(A<string>._, A<CkModelId>._, A<OperationResult>._,
            A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => _upgrade.UpgradeModelsAsync(A<string>._, A<IEnumerable<CkModelIdVersionRange>>._,
            A<CkMigrationOptions?>._, A<IReadOnlyDictionary<string, string>?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _installations.UpsertAsync(A<string>._, A<BlueprintInstallation>._, A<CancellationToken>._))
            .MustNotHaveHappened();
        A.CallTo(() => _history.AddEntryAsync(A<string>._, A<TenantBlueprintInfo>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    // ---- preview ---------------------------------------------------------------------------------

    [Fact]
    public async Task Preview_ListsTheBlockersAndTheJudgedSetAndWritesNothing()
    {
        var preview = await CreateService().PreviewUpdateAsync(TenantId, Target, BlueprintUpdateMode.Merge,
            TestContext.Current.CancellationToken);

        Assert.True(preview.IsCkUpgradeBlocked);
        Assert.Equal(["Basic-2.5.0"], preview.CkUpdateSet);
        var blocker = Assert.Single(preview.CkUpgradeBlockers);
        Assert.Equal("EnergyCommunity.Registration-2.8.0", blocker.Dependent);
        Assert.Equal("Basic-2.4.0", blocker.Subject);
        Assert.Null(preview.CkUpgradeGuardError);
        Assert.Contains(preview.Warnings, w => w.Contains("CK upgrade guard"));
        AssertNothingInstalled();
    }

    [Fact]
    public async Task Preview_TheTenantAlreadyHoldsTheFloor_IsNotJudgedAndHasNoBlockers()
    {
        GivenInstalledBasic("2.6.0");

        var preview = await CreateService().PreviewUpdateAsync(TenantId, Target, BlueprintUpdateMode.Merge,
            TestContext.Current.CancellationToken);

        Assert.False(preview.IsCkUpgradeBlocked);
        Assert.Empty(preview.CkUpdateSet);
        A.CallTo(() => _guard.EvaluateAsync(A<string>._, A<IReadOnlyList<CkModelId>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Preview_TheGuardFails_StillAnswersAndReportsTheFailure()
    {
        A.CallTo(() => _guard.EvaluateAsync(A<string>._, A<IReadOnlyList<CkModelId>>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("catalog unreachable"));

        var preview = await CreateService().PreviewUpdateAsync(TenantId, Target, BlueprintUpdateMode.Merge,
            TestContext.Current.CancellationToken);

        Assert.Equal("catalog unreachable", preview.CkUpgradeGuardError);
        Assert.Empty(preview.CkUpgradeBlockers);
    }

    [Fact]
    public async Task Preview_WithoutAHostGuard_IsUnchanged()
    {
        var preview = await CreateService(withGuard: false).PreviewUpdateAsync(TenantId, Target,
            BlueprintUpdateMode.Merge, TestContext.Current.CancellationToken);

        Assert.False(preview.IsCkUpgradeBlocked);
        Assert.Empty(preview.CkUpdateSet);
        Assert.Null(preview.CkUpgradeGuardError);
    }

    // ---- update ----------------------------------------------------------------------------------

    [Fact]
    public async Task Update_Enforce_BlockedSet_IsRefusedBeforeTheFirstChange()
    {
        var result = await CreateService().ApplyUpdateAsync(TenantId, Target, BlueprintUpdateMode.Merge,
            new BlueprintUpdateOptions { CkUpgradeGuard = BlueprintCkUpgradeGuardMode.Enforce },
            TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.True(result.IsCkUpgradeBlocked);
        Assert.False(result.CkUpgradeGuardFailed);
        Assert.Equal(["Basic-2.5.0"], result.CkUpdateSet);
        Assert.Single(result.CkUpgradeBlockers);
        Assert.Contains(result.Errors, e => e.Contains("Nothing was changed") && e.Contains("ForceBreakingUpdate"));
        AssertNothingInstalled();
    }

    [Fact]
    public async Task Update_Enforce_NothingBreaks_ProceedsAsBefore()
    {
        GivenGuardAllows();

        var result = await CreateService().ApplyUpdateAsync(TenantId, Target, BlueprintUpdateMode.Merge,
            new BlueprintUpdateOptions { CkUpgradeGuard = BlueprintCkUpgradeGuardMode.Enforce },
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(result.IsCkUpgradeBlocked);
        Assert.Empty(result.CkUpgradeBlockers);
        A.CallTo(() => _provider.EnsureCkModelInstalledAsync(TenantId,
                A<CkModelId>.That.Matches(m => m.FullName == "Basic-2.5.0"), A<OperationResult>._,
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Update_Force_BlockedSet_ProceedsAndReportsTheForcedBlockers()
    {
        var result = await CreateService().ApplyUpdateAsync(TenantId, Target, BlueprintUpdateMode.Merge,
            new BlueprintUpdateOptions { CkUpgradeGuard = BlueprintCkUpgradeGuardMode.ForceBreakingUpdate },
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(result.IsCkUpgradeBlocked);
        var blocker = Assert.Single(result.CkUpgradeBlockers);
        Assert.Equal("EnergyCommunity.Registration-2.8.0", blocker.Dependent);
        A.CallTo(() => _provider.EnsureCkModelInstalledAsync(TenantId, A<CkModelId>._, A<OperationResult>._,
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData(BlueprintCkUpgradeGuardMode.Off)]
    [InlineData(BlueprintCkUpgradeGuardMode.Enforce)]
    public async Task Update_DryRun_ReportsTheBlockersAndChangesNothing(BlueprintCkUpgradeGuardMode mode)
    {
        var result = await CreateService().ApplyUpdateAsync(TenantId, Target, BlueprintUpdateMode.Merge,
            new BlueprintUpdateOptions { DryRun = true, CkUpgradeGuard = mode }, TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.True(result.IsCkUpgradeBlocked);
        Assert.Single(result.CkUpgradeBlockers);
        Assert.Equal(["Basic-2.5.0"], result.CkUpdateSet);
        Assert.Contains("DryRun: No changes were made", result.Warnings);
        A.CallTo(() => _provider.EnsureCkModelInstalledAsync(A<string>._, A<CkModelId>._, A<OperationResult>._,
            A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task Update_GuardOff_IsUnchangedAndIgnoresTheBlockers()
    {
        var result = await CreateService().ApplyUpdateAsync(TenantId, Target, BlueprintUpdateMode.Merge, null,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(result.IsCkUpgradeBlocked);
        Assert.Empty(result.CkUpgradeBlockers);
        A.CallTo(() => _provider.EnsureCkModelInstalledAsync(TenantId, A<CkModelId>._, A<OperationResult>._,
            A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Update_Enforce_WithoutAHostGuard_ProceedsAsBefore()
    {
        var result = await CreateService(withGuard: false).ApplyUpdateAsync(TenantId, Target,
            BlueprintUpdateMode.Merge,
            new BlueprintUpdateOptions { CkUpgradeGuard = BlueprintCkUpgradeGuardMode.Enforce },
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(result.IsCkUpgradeBlocked);
    }

    [Theory]
    [InlineData(BlueprintCkUpgradeGuardMode.Enforce)]
    [InlineData(BlueprintCkUpgradeGuardMode.ForceBreakingUpdate)]
    public async Task Update_TheGuardFails_FailsClosedEvenWhenForced(BlueprintCkUpgradeGuardMode mode)
    {
        A.CallTo(() => _guard.EvaluateAsync(A<string>._, A<IReadOnlyList<CkModelId>>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("catalog unreachable"));

        var result = await CreateService().ApplyUpdateAsync(TenantId, Target, BlueprintUpdateMode.Merge,
            new BlueprintUpdateOptions { CkUpgradeGuard = mode }, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.True(result.CkUpgradeGuardFailed);
        Assert.False(result.IsCkUpgradeBlocked);
        Assert.Contains(result.Errors, e => e.Contains("catalog unreachable") && e.Contains("Nothing was changed"));
        AssertNothingInstalled();
    }

    // ---- first-time apply ------------------------------------------------------------------------

    [Fact]
    public async Task Apply_Enforce_BlockedSet_IsRefusedBeforeTheFirstChange()
    {
        var result = await CreateService().ApplyBlueprintAsync(TenantId, Target,
            new BlueprintApplyOptions { CkUpgradeGuard = BlueprintCkUpgradeGuardMode.Enforce },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.True(result.IsCkUpgradeBlocked);
        Assert.False(result.CkUpgradeGuardFailed);
        Assert.Equal(["Basic-2.5.0"], result.CkUpdateSet);
        Assert.Single(result.CkUpgradeBlockers);
        var error = Assert.Single(result.OperationResult.Messages, m => m.MessageLevel == MessageLevel.Error);
        Assert.Contains("Nothing was changed", error.MessageText);
        AssertNothingInstalled();
    }

    [Fact]
    public async Task Apply_Enforce_NothingBreaks_ProceedsAsBefore()
    {
        GivenGuardAllows();

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Target,
            new BlueprintApplyOptions { CkUpgradeGuard = BlueprintCkUpgradeGuardMode.Enforce },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsCkUpgradeBlocked);
        Assert.Empty(result.CkUpgradeBlockers);
    }

    [Fact]
    public async Task Apply_Enforce_TenantAlreadyHoldsTheFloor_GuardIsNotAsked()
    {
        GivenInstalledBasic("2.7.0");

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Target,
            new BlueprintApplyOptions { CkUpgradeGuard = BlueprintCkUpgradeGuardMode.Enforce },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        A.CallTo(() => _guard.EvaluateAsync(A<string>._, A<IReadOnlyList<CkModelId>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Apply_Force_BlockedSet_ProceedsAndReportsTheForcedBlockers()
    {
        var result = await CreateService().ApplyBlueprintAsync(TenantId, Target,
            new BlueprintApplyOptions { CkUpgradeGuard = BlueprintCkUpgradeGuardMode.ForceBreakingUpdate },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsCkUpgradeBlocked);
        Assert.Single(result.CkUpgradeBlockers);
        Assert.Equal(["Basic-2.5.0"], result.CkUpdateSet);
    }

    [Fact]
    public async Task Apply_LegacyOverload_NeverAsksTheGuard()
    {
        var result = await CreateService().ApplyBlueprintAsync(TenantId, Target, false,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        A.CallTo(() => _guard.EvaluateAsync(A<string>._, A<IReadOnlyList<CkModelId>>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Apply_TheGuardFails_FailsClosed()
    {
        A.CallTo(() => _guard.EvaluateAsync(A<string>._, A<IReadOnlyList<CkModelId>>._, A<CancellationToken>._))
            .Throws(new InvalidOperationException("catalog unreachable"));

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Target,
            new BlueprintApplyOptions { CkUpgradeGuard = BlueprintCkUpgradeGuardMode.ForceBreakingUpdate },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.True(result.CkUpgradeGuardFailed);
        AssertNothingInstalled();
    }
}

/// <summary>
///     AB#6871: the six-parameter <c>BlueprintApplicationResult.Success</c> that earlier packages exposed still exists
///     (a consumer compiled against it must not fail with a missing method).
/// </summary>
public class BlueprintApplicationResultCompatibilityTests
{
    [Fact]
    public void Success_SixParameterForm_StillExists_AndCarriesNoGuardReport()
    {
        var method = typeof(BlueprintApplicationResult).GetMethod("Success",
            [typeof(string), typeof(BlueprintId), typeof(List<CkModelIdVersionRange>), typeof(List<string>), typeof(int),
                typeof(OperationResult)]);
        Assert.NotNull(method);

        var result = BlueprintApplicationResult.Success("t", new BlueprintId("Shop-1.0.0"), [], [], 0, new OperationResult());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.CkUpgradeBlockers);
        Assert.Empty(result.CkUpdateSet);
    }
}
