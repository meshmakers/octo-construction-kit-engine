using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.CkModelMigrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.CkModelMigrations;

/// <summary>
///     AB#4924 increment 9 (leasing implementation plan §11.6) — post-migration validations.
/// </summary>
/// <remarks>
///     🔴 <b>These validations used to be a stub that returned "passed" without reading anything.</b>
///     Every <c>postValidations</c> block in every migration script in the estate therefore passed,
///     including the <c>severity: Error</c> entries whose whole purpose is to stop a migration that
///     left data behind — such as <c>System.Communication</c> 4.0.0's <c>no-legacy-pools</c>, which
///     the leasing rollout's wave 2 relies on. A guard that always passes is worse than no guard,
///     because the script reads as if it is guarded and the runbook says so too.
/// </remarks>
public class CkMigrationPostValidationTests
{
    private const string TestTenantId = "tenant-1";
    private static readonly RtCkId<CkTypeId> LegacyTypeId = new("System.Communication", "Pool");

    private readonly ICkModelMigrationService _sut;
    private readonly ICkMigrationContentProvider _contentProvider;
    private readonly IRuntimeRepository _repository;

    public CkMigrationPostValidationTests()
    {
        _contentProvider = A.Fake<ICkMigrationContentProvider>();
        var repositoryProvider = A.Fake<IRuntimeRepositoryProvider>();
        _repository = A.Fake<IRuntimeRepository>();
        var session = A.Fake<IOctoSession>();

        A.CallTo(() => repositoryProvider.GetRepositoryAsync(A<string>._, A<CancellationToken>._))
            .Returns(_repository);
        A.CallTo(() => _repository.GetSessionAsync()).Returns(session);

        _sut = new CkModelMigrationService(
            A.Fake<ICkMigrationParser>(),
            _contentProvider,
            repositoryProvider,
            A.Fake<ICatalogService>(),
            A.Fake<ICkModelImportAuditTrail>(),
            NullLogger<CkModelMigrationService>.Instance);
    }

    /// <summary>
    ///     The happy case, and the one the leasing rollout depends on: after the rename no entity of
    ///     the old type is left, and the migration is allowed to succeed.
    /// </summary>
    [Fact]
    public async Task NoEntitiesOfType_WithNothingLeftBehind_Passes()
    {
        ArrangeMigration(remainingLegacyEntities: 0,
            Validation(CkMigrationValidationType.NoEntitiesOfType));

        var result = await MigrateAsync();

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
    }

    /// <summary>
    ///     🔴 The case the stub could never produce. A surviving entity of the renamed type means a
    ///     workload whose edge points at a type the model no longer defines, and nothing downstream
    ///     would report it — which is exactly why the script marks this <c>severity: Error</c>.
    /// </summary>
    [Fact]
    public async Task NoEntitiesOfType_WithEntitiesLeftBehind_FailsTheMigrationAndSaysHowMany()
    {
        ArrangeMigration(remainingLegacyEntities: 3,
            Validation(CkMigrationValidationType.NoEntitiesOfType));

        var result = await MigrateAsync();

        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Contains("no-legacy-pools", error, StringComparison.Ordinal);
        Assert.Contains("3", error, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A <c>Warning</c> validation is reported and does not fail the run, which is what the three
    ///     3.0.x retry scripts in <c>System.Communication</c> rely on.
    /// </summary>
    [Fact]
    public async Task AFailedWarningValidation_IsReportedWithoutFailingTheMigration()
    {
        var validation = Validation(CkMigrationValidationType.NoEntitiesOfType);
        validation.Severity = CkMigrationValidationSeverity.Warning;
        ArrangeMigration(remainingLegacyEntities: 1, validation);

        var result = await MigrateAsync();

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task EntityExists_WithNoEntities_Fails()
    {
        ArrangeMigration(remainingLegacyEntities: 0, Validation(CkMigrationValidationType.EntityExists));

        var result = await MigrateAsync();

        Assert.False(result.Success);
    }

    [Fact]
    public async Task EntityCount_ComparesAgainstTheExpectedCount()
    {
        var validation = Validation(CkMigrationValidationType.EntityCount);
        validation.ExpectedCount = 2;
        ArrangeMigration(remainingLegacyEntities: 5, validation);

        var result = await MigrateAsync();

        Assert.False(result.Success);
        Assert.Contains("found 5", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    /// <summary>
    ///     🔴 "I could not check" must never read as "it is fine". That equivalence is the behaviour
    ///     being removed here, so a validation the engine cannot evaluate fails rather than passing.
    /// </summary>
    [Fact]
    public async Task AValidationThatNamesNoTarget_FailsRatherThanPassingQuietly()
    {
        var validation = Validation(CkMigrationValidationType.NoEntitiesOfType);
        validation.Target = null;
        ArrangeMigration(remainingLegacyEntities: 0, validation);

        var result = await MigrateAsync();

        Assert.False(result.Success);
        Assert.Contains("cannot be evaluated", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    /// <summary>
    ///     AB#4924 increment 9. The rollout runbook asks an operator to dry-run a major migration
    ///     against a copy of a production tenant database and check that the counts survive — and a
    ///     dry run used to report nothing at all. It now reads the entities it would touch, which
    ///     also means it must not then fail on a post-validation about the state it did not change.
    /// </summary>
    [Fact]
    public async Task ADryRun_ReadsWhatTheStepWouldTouchAndChangesNothing()
    {
        ArrangeMigration(remainingLegacyEntities: 4,
            Validation(CkMigrationValidationType.EntityExists));

        var result = await MigrateAsync(new CkMigrationOptions { DryRun = true });

        Assert.True(result.Success);
        Assert.Equal(0, result.EntitiesUpdated);
        Assert.Equal(0, result.EntitiesDeleted);
        Assert.Equal(0, result.EntitiesAdded);
        // Read twice: once to report what the step would affect, once for the post-validation.
        A.CallTo(() => _repository.GetRtEntitiesByTypeForMigrationAsync(A<IOctoSession>._, LegacyTypeId))
            .MustHaveHappened(2, Times.Exactly);
        // And nothing was written.
        A.CallTo(() => _repository.UpdateCkTypeIdForMigrationAsync(A<IOctoSession>._, A<OctoObjectId>._,
            A<RtCkId<CkTypeId>>._)).MustNotHaveHappened();
    }

    private static CkMigrationPostValidationDto Validation(CkMigrationValidationType type) => new()
    {
        ValidationId = "no-legacy-pools",
        Description = "Ensure no Pool entities remain",
        Type = type,
        Target = new CkMigrationTargetDto { CkTypeId = "System.Communication/Pool" },
        Severity = CkMigrationValidationSeverity.Error
    };

    private void ArrangeMigration(int remainingLegacyEntities, CkMigrationPostValidationDto validation)
    {
        var toModel = new CkModelId("System.Communication", "4.0.0");
        var meta = new CkMigrationMetaDto
        {
            CkModelId = "System.Communication-4.0.0",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "3.35.0",
                    ToVersion = "4.0.0",
                    ScriptPath = "3.35.0-to-4.0.0.yaml"
                }
            ]
        };

        var script = new CkMigrationScriptDto
        {
            SourceVersion = "3.35.0",
            TargetVersion = "4.0.0",
            Steps =
            [
                new CkMigrationStepDto
                {
                    StepId = "migrate-pool-to-deployment-site",
                    Action = CkMigrationActionType.Transform,
                    Target = new CkMigrationTargetDto { CkTypeId = "System.Communication/Pool" },
                    Transform = new CkMigrationTransformDto
                    {
                        Type = CkMigrationTransformType.ChangeCkType,
                        NewCkTypeId = "System.Communication/DeploymentSite"
                    },
                    OnConflict = CkMigrationConflictBehavior.Skip
                }
            ],
            PostValidations = [validation]
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._)).Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "3.35.0", "4.0.0", A<CancellationToken>._))
            .Returns(script);

        // What the post-validation finds afterwards. The step itself is a no-op against this fake —
        // the subject here is the validation, not the transform.
        var entities = Enumerable.Range(0, remainingLegacyEntities)
            .Select(_ => new RtEntity(LegacyTypeId, OctoObjectId.GenerateNewId()))
            .ToList();
        A.CallTo(() => _repository.GetRtEntitiesByTypeForMigrationAsync(A<IOctoSession>._, LegacyTypeId))
            .Returns(((IReadOnlyList<RtEntity>)entities, false));
    }

    private Task<CkMigrationResult> MigrateAsync(CkMigrationOptions? options = null) =>
        _sut.MigrateAsync(TestTenantId,
            new CkModelId("System.Communication", "3.35.0"),
            new CkModelId("System.Communication", "4.0.0"),
            options,
            TestContext.Current.CancellationToken);
}
