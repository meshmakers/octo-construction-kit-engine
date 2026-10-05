using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Secrets;
using Meshmakers.Octo.Runtime.Engine.CkModelMigrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.CkModelMigrations;

/// <summary>
///     AB#5532 (concept §5.2 phase 3): after a successful CK model migration the migration service asks the
///     secret maintenance service to turn stored placeholders of that model into "not set".
/// </summary>
public class CkModelMigrationSecretHookTests
{
    private readonly ICkMigrationContentProvider _contentProvider = A.Fake<ICkMigrationContentProvider>();
    private readonly IRuntimeRepositoryProvider _repositoryProvider = A.Fake<IRuntimeRepositoryProvider>();
    private readonly ISecretMaintenanceService _maintenance = A.Fake<ISecretMaintenanceService>();

    private CkModelMigrationService CreateSut(ISecretMaintenanceService? maintenance) =>
        new(A.Fake<ICkMigrationParser>(), _contentProvider, _repositoryProvider, A.Fake<ICatalogService>(),
            A.Fake<ICkModelImportAuditTrail>(), NullLogger<CkModelMigrationService>.Instance,
            secretMaintenanceService: maintenance);

    private static readonly CkModelId FromModel = new("System.Communication", "3.39.0");
    private static readonly CkModelId ToModel = new("System.Communication", "3.40.0");

    public CkModelMigrationSecretHookTests()
    {
        // No migration scripts: the String -> Secret switch is a schema-only bump.
        A.CallTo(() => _contentProvider.HasMigrationsAsync(ToModel, A<CancellationToken>._)).Returns(false);
        A.CallTo(() => _repositoryProvider.GetRepositoryAsync("tenant1", A<CancellationToken>._))
            .Returns((IRuntimeRepository?)null);
        var normalised = new SecretSweepResult("tenant1", SecretSweepMode.Verify) { ValuesRewritten = 3, EntitiesRewritten = 2 };
        A.CallTo(() => _maintenance.NormalizePlaceholdersAsync("tenant1", "System.Communication", A<CancellationToken>._))
            .Returns(normalised);
    }

    [Fact]
    public async Task SuccessfulMigration_NormalisesSecretPlaceholdersOfTheModel()
    {
        var result = await CreateSut(_maintenance).MigrateAsync("tenant1", FromModel, ToModel,
            new CkMigrationOptions(), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        A.CallTo(() => _maintenance.NormalizePlaceholdersAsync("tenant1", "System.Communication", A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        Assert.Equal(2, result.EntitiesUpdated);
    }

    [Fact]
    public async Task DryRun_DoesNotNormalise()
    {
        await CreateSut(_maintenance).MigrateAsync("tenant1", FromModel, ToModel,
            new CkMigrationOptions { DryRun = true }, TestContext.Current.CancellationToken);

        A.CallTo(() => _maintenance.NormalizePlaceholdersAsync(A<string>._, A<string?>._, A<CancellationToken>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task NormalisationFailure_IsAWarning_NotAFailedMigration()
    {
        A.CallTo(() => _maintenance.NormalizePlaceholdersAsync("tenant1", "System.Communication", A<CancellationToken>._))
            .Throws(new InvalidOperationException("boom"));

        var result = await CreateSut(_maintenance).MigrateAsync("tenant1", FromModel, ToModel,
            new CkMigrationOptions(), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, w => w.Contains("Secret placeholder normalisation failed"));
    }

    [Fact]
    public async Task WithoutMaintenanceService_MigrationStillSucceeds()
    {
        var result = await CreateSut(null).MigrateAsync("tenant1", FromModel, ToModel,
            new CkMigrationOptions(), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
    }
}
