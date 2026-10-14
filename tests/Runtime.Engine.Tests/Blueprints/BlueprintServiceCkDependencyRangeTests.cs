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
///     AB#6672: the CK model dependency range of a blueprint (<c>ckModelDependencies</c>). The floor is a minimum,
///     never a target: a tenant that runs a newer version inside the range keeps it, one below the floor is
///     installed, and a version OUTSIDE the range (above its upper bound) stops the apply before anything is
///     installed. Same rule for classic and range-retaining models (the range check is on the declared range, not on
///     how the models were compiled). The integration counterpart runs in engine-mongodb
///     (<c>BlueprintServiceIntegrationTests</c>).
/// </summary>
public class BlueprintServiceCkDependencyRangeTests
{
    private const string TenantId = "tenant-a";
    private static readonly BlueprintId Root = new("Samples.Range-1.0.0");

    private readonly IBlueprintDependencyResolver _resolver = A.Fake<IBlueprintDependencyResolver>();
    private readonly ITenantBlueprintInstallations _installations = A.Fake<ITenantBlueprintInstallations>();
    private readonly IRuntimeRepositoryProvider _provider = A.Fake<IRuntimeRepositoryProvider>();
    private readonly ICkModelUpgradeService _upgrade = A.Fake<ICkModelUpgradeService>();

    private BlueprintService CreateService() => new(
        A.Fake<ICkCacheService>(), A.Fake<IBlueprintCatalogManager>(), A.Fake<ITenantBlueprintHistory>(),
        A.Fake<IBlueprintMigrationExecutor>(), A.Fake<IBlueprintMigrationParser>(), _upgrade, _provider,
        A.Fake<IImportRtModelCommand>(), A.Fake<IRtYamlSerializer>(), A.Fake<IBlueprintNotifications>(), _resolver,
        _installations, A.Fake<IBlueprintVariableProvider>(), NullLogger<BlueprintService>.Instance);

    private void Given(string installedSystem, string dependencyRange)
    {
        A.CallTo(() => _installations.GetByBlueprintNameAsync(TenantId, A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<BlueprintInstallation?>(null));
        A.CallTo(() => _upgrade.UpgradeModelsAsync(TenantId, A<IEnumerable<CkModelIdVersionRange>>._,
                A<CkMigrationOptions?>._, A<IReadOnlyDictionary<string, string>?>._, A<CancellationToken>._))
            .Returns(new CkModelUpgradeResult { Success = true });
        A.CallTo(() => _provider.GetSchemaVersionsAsync(TenantId, A<CancellationToken>._))
            .Returns(new Dictionary<string, string> { ["System"] = installedSystem });
        A.CallTo(() => _resolver.ResolveAsync(Root, A<CancellationToken>._))
            .Returns(new BlueprintResolutionResult
            {
                RootBlueprintId = Root,
                Success = true,
                InstallOrder =
                [
                    new BlueprintMetaRootDto
                    {
                        BlueprintId = Root, CkModelDependencies = [new CkModelIdVersionRange(dependencyRange)]
                    }
                ],
                Conflicts = [],
                DependencyRanges = new Dictionary<string, List<BlueprintIdVersionRange>>()
            });
    }

    [Theory]
    [InlineData("2.6.0", true)] // newer inside the range: kept (the install step is a no-op), blueprint installs
    [InlineData("2.4.0", true)] // at the floor
    [InlineData("2.3.0", true)] // below the floor: the install step runs
    [InlineData("3.0.0", false)] // above the range: refused, nothing installed
    [InlineData("4.1.0", false)]
    public async Task Apply_AgainstTheInstalledVersion_KeepsInstallsOrRefuses(string installedSystem, bool expectedSuccess)
    {
        Given(installedSystem, "System-[2.4,3.0)");

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Root, true, TestContext.Current.CancellationToken);

        Assert.Equal(expectedSuccess, result.IsSuccess);
        if (expectedSuccess)
        {
            // The floor id is handed to the provider, which keeps a satisfying-or-newer installed version.
            A.CallTo(() => _provider.EnsureCkModelInstalledAsync(TenantId, A<CkModelId>._, A<OperationResult>._,
                A<CancellationToken>._)).MustHaveHappened(1, Times.Exactly);
        }
        else
        {
            var error = Assert.Single(result.OperationResult.Messages, m => m.MessageLevel == MessageLevel.Error);
            Assert.Contains("System-[2.4,3.0)", error.MessageText);
            Assert.Contains($"System-{installedSystem}", error.MessageText);
            Assert.Contains("nothing was installed", error.MessageText);
            A.CallTo(() => _provider.EnsureCkModelInstalledAsync(A<string>._, A<CkModelId>._, A<OperationResult>._,
                A<CancellationToken>._)).MustNotHaveHappened();
            A.CallTo(() => _installations.UpsertAsync(A<string>._, A<BlueprintInstallation>._, A<CancellationToken>._))
                .MustNotHaveHappened();
        }
    }

    [Fact]
    public async Task Apply_ModelNotInstalled_IsInstalledAsBefore()
    {
        Given("2.6.0", "System-[2.4,3.0)");
        A.CallTo(() => _provider.GetSchemaVersionsAsync(TenantId, A<CancellationToken>._))
            .Returns(new Dictionary<string, string>());

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Root, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        A.CallTo(() => _provider.EnsureCkModelInstalledAsync(TenantId, A<CkModelId>.That.Matches(m => m.FullName == "System-2.4.0"),
            A<OperationResult>._, A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }
}
