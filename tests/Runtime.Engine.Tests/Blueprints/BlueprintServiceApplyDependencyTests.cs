using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
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
/// A dependency range floor is a minimum, never a target: (force-)installing a blueprint must not
/// downgrade a dependency the tenant already runs in a newer in-range version
/// (test-2: Simulation-2.8.1 with Base-[2.10,3.0) moved Base-2.11.0 back to 2.10.0).
/// </summary>
public class BlueprintServiceApplyDependencyTests
{
    private const string TenantId = "tenant-a";
    private static readonly BlueprintId Root = new("EnergyCommunity.Simulation-2.8.1");

    private readonly IBlueprintDependencyResolver _resolver = A.Fake<IBlueprintDependencyResolver>();
    private readonly ITenantBlueprintInstallations _installations = A.Fake<ITenantBlueprintInstallations>();
    private readonly List<BlueprintInstallation> _upserts = [];

    public BlueprintServiceApplyDependencyTests()
    {
        A.CallTo(() => _installations.GetByBlueprintNameAsync(TenantId, A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<BlueprintInstallation?>(null));
        A.CallTo(() => _installations.UpsertAsync(TenantId, A<BlueprintInstallation>._, A<CancellationToken>._))
            .Invokes((string _, BlueprintInstallation i, CancellationToken _) => _upserts.Add(i))
            .Returns(Task.CompletedTask);
    }

    private BlueprintService CreateService() => new(
        A.Fake<ICkCacheService>(),
        A.Fake<IBlueprintCatalogManager>(),
        A.Fake<ITenantBlueprintHistory>(),
        A.Fake<IBlueprintMigrationExecutor>(),
        A.Fake<IBlueprintMigrationParser>(),
        A.Fake<ICkModelUpgradeService>(),
        A.Fake<IRuntimeRepositoryProvider>(),
        A.Fake<IImportRtModelCommand>(),
        A.Fake<IRtYamlSerializer>(),
        A.Fake<IBlueprintNotifications>(),
        _resolver,
        _installations,
        A.Fake<IBlueprintVariableProvider>(),
        _logger);

    private readonly CapturingLogger _logger = new();

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<BlueprintService>
    {
        public List<Exception> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception != null) Exceptions.Add(exception);
        }
    }

    private void GivenResolution(string resolvedBase, string baseRange)
    {
        A.CallTo(() => _resolver.ResolveAsync(Root, A<CancellationToken>._))
            .Returns(new BlueprintResolutionResult
            {
                RootBlueprintId = Root,
                Success = true,
                InstallOrder =
                [
                    new BlueprintMetaRootDto { BlueprintId = new BlueprintId(resolvedBase) },
                    new BlueprintMetaRootDto { BlueprintId = Root }
                ],
                Conflicts = [],
                DependencyRanges = new Dictionary<string, List<BlueprintIdVersionRange>>
                {
                    ["EnergyCommunity.Base"] = [new BlueprintIdVersionRange(baseRange)]
                }
            });
    }

    private void GivenInstalled(string fullName)
    {
        var id = new BlueprintId(fullName);
        A.CallTo(() => _installations.GetByBlueprintNameAsync(TenantId, id.Name, A<CancellationToken>._))
            .Returns(new BlueprintInstallation
            {
                BlueprintId = id, InstalledAt = DateTime.UtcNow, LastUpdatedAt = DateTime.UtcNow, IsDependency = false
            });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_NewerInRangeDependencyInstalled_KeepsIt(bool force)
    {
        GivenResolution("EnergyCommunity.Base-2.10.0", "EnergyCommunity.Base-[2.10,3.0)");
        GivenInstalled("EnergyCommunity.Base-2.11.0");

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Root, force, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, string.Join("; ", result.OperationResult.Messages.Select(m => m.MessageText)) + string.Join("\n", _logger.Exceptions));
        Assert.DoesNotContain(_upserts, i => i.BlueprintId.Name == "EnergyCommunity.Base");
        var root = Assert.Single(_upserts, i => i.BlueprintId.Equals(Root));
        Assert.Equal(["EnergyCommunity.Base-2.11.0"], root.ResolvedDependencies.Select(d => d.FullName).ToArray());
    }

    [Fact]
    public async Task Apply_NewerOutOfRangeDependencyInstalled_FailsInsteadOfDowngrading()
    {
        GivenResolution("EnergyCommunity.Base-2.10.0", "EnergyCommunity.Base-[2.10,3.0)");
        GivenInstalled("EnergyCommunity.Base-3.0.0");

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Root, true, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.OperationResult.Messages, m => m.MessageText.Contains("Refusing to downgrade"));
        Assert.Empty(_upserts);
    }

    [Fact]
    public async Task Apply_OlderDependencyInstalled_StillUpgradesToResolvedVersion()
    {
        GivenResolution("EnergyCommunity.Base-2.11.0", "EnergyCommunity.Base-[2.10,3.0)");
        GivenInstalled("EnergyCommunity.Base-2.10.0");

        var result = await CreateService().ApplyBlueprintAsync(TenantId, Root, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Contains(_upserts, i => i.BlueprintId.FullName == "EnergyCommunity.Base-2.11.0" && i.IsDependency);
    }
}
