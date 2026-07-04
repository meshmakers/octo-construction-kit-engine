using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
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
/// Unit-test coverage for the <see cref="CkMigrationTransformType.WrapScalarInRecord"/>
/// transform action introduced for AB#4209 (Phase 3 follow-up).
/// </summary>
public class WrapScalarInRecordTransformTests
{
    private const string TestTenantId = "tenant-1";
    private static readonly RtCkId<CkTypeId> ClientTypeId = new("System.Identity", "Client");
    private static readonly RtCkId<CkRecordId> ClientUriEntryRecordId = new("System.Identity", "ClientUriEntry");
    private const string RedirectUrisAttr = "System.Identity/RedirectUris";
    private const string UriAttr = "System.Identity/Uri";
    private const string SourceAttr = "System.Identity/Source";

    private readonly ICkModelMigrationService _sut;
    private readonly ICkMigrationContentProvider _contentProvider;
    private readonly IRuntimeRepositoryProvider _repositoryProvider;
    private readonly IRuntimeRepository _repository;
    private readonly IOctoSession _session;
    private readonly ICkModelImportAuditTrail _auditTrail;
    private readonly List<(OctoObjectId RtId, object? Value)> _rewriteCaptures = [];

    public WrapScalarInRecordTransformTests()
    {
        _contentProvider = A.Fake<ICkMigrationContentProvider>();
        _repositoryProvider = A.Fake<IRuntimeRepositoryProvider>();
        _repository = A.Fake<IRuntimeRepository>();
        _session = A.Fake<IOctoSession>();
        _auditTrail = A.Fake<ICkModelImportAuditTrail>();
        var parser = A.Fake<ICkMigrationParser>();
        var catalogService = A.Fake<ICatalogService>();

        A.CallTo(() => _repositoryProvider.GetRepositoryAsync(A<string>._, A<CancellationToken>._))
            .Returns(_repository);
        A.CallTo(() => _repository.GetSessionAsync()).Returns(_session);

        // Capture every rewrite so the test can inspect what landed in storage.
        A.CallTo(() => _repository.RewriteAttributeValueForMigrationAsync(
                A<IOctoSession>._, A<RtCkId<CkTypeId>>._, A<OctoObjectId>._,
                A<string>._, A<object?>._))
            .Invokes(call =>
            {
                _rewriteCaptures.Add(((OctoObjectId)call.Arguments[2]!, call.Arguments[4]));
            });

        _sut = new CkModelMigrationService(
            parser,
            _contentProvider,
            _repositoryProvider,
            catalogService,
            _auditTrail,
            NullLogger<CkModelMigrationService>.Instance);
    }

    [Fact]
    public async Task WrapScalarInRecord_PopulatedScalarList_LiftsAllEntriesToRecords()
    {
        // Arrange: one entity with three scalar URIs.
        var rtId = OctoObjectId.GenerateNewId();
        var entity = BuildClientEntity(rtId, new List<object?>
        {
            "https://refinery.test.octo-mesh.com/",
            "https://refinery.test.octo-mesh.com/silent-renew",
            "https://refinery.test.octo-mesh.com/auth-callback",
        });

        ArrangeFixedMigration([entity]);

        // Act
        var result = await Migrate();

        // Assert: one entity updated.
        Assert.True(result.Success);
        Assert.Equal(1, result.EntitiesUpdated);
        Assert.Single(_rewriteCaptures);

        var (capturedId, capturedValue) = _rewriteCaptures[0];
        Assert.Equal(rtId, capturedId);

        var records = AssertIsRecordList(capturedValue);
        Assert.Equal(3, records.Count);
        Assert.All(records, r =>
        {
            Assert.Equal(ClientUriEntryRecordId, r.CkRecordId);
            Assert.Equal("base", r.Attributes[SourceAttr]);
        });
        Assert.Equal("https://refinery.test.octo-mesh.com/", records[0].Attributes[UriAttr]);
        Assert.Equal("https://refinery.test.octo-mesh.com/silent-renew", records[1].Attributes[UriAttr]);
        Assert.Equal("https://refinery.test.octo-mesh.com/auth-callback", records[2].Attributes[UriAttr]);

        // Audit-trail records one event per mutated entity.
        A.CallTo(() => _auditTrail.RecordWrapScalarInRecordAsync(
                TestTenantId,
                ClientTypeId,
                rtId,
                RedirectUrisAttr,
                ClientUriEntryRecordId,
                3,
                "wrap-redirect-uris"))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task WrapScalarInRecord_EmptyList_IsNoOp()
    {
        // Arrange
        var rtId = OctoObjectId.GenerateNewId();
        var entity = BuildClientEntity(rtId, new List<object?>());
        ArrangeFixedMigration([entity]);

        // Act
        var result = await Migrate();

        // Assert: no rewrite, no audit event.
        Assert.True(result.Success);
        Assert.Equal(0, result.EntitiesUpdated);
        Assert.Empty(_rewriteCaptures);
        A.CallTo(() => _auditTrail.RecordWrapScalarInRecordAsync(
                A<string?>._, A<RtCkId<CkTypeId>>._, A<OctoObjectId>._,
                A<string>.Ignored, A<RtCkId<CkRecordId>>._, A<int>._, A<string>.Ignored))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task WrapScalarInRecord_AlreadyRecords_IsIdempotentNoOp()
    {
        // Arrange: entity carries the post-lift shape already (e.g. a prior run completed).
        var rtId = OctoObjectId.GenerateNewId();
        var existing = new RtRecord(ClientUriEntryRecordId,
            new Dictionary<string, object?>
            {
                [UriAttr] = "https://refinery.test.octo-mesh.com/",
                [SourceAttr] = "base",
            });

        var entity = BuildClientEntity(rtId, new List<object?> { existing });
        ArrangeFixedMigration([entity]);

        // Act
        var result = await Migrate();

        // Assert: no rewrite — the step is fully idempotent, no audit event either.
        Assert.True(result.Success);
        Assert.Equal(0, result.EntitiesUpdated);
        Assert.Empty(_rewriteCaptures);
        A.CallTo(() => _auditTrail.RecordWrapScalarInRecordAsync(
                A<string?>._, A<RtCkId<CkTypeId>>._, A<OctoObjectId>._,
                A<string>.Ignored, A<RtCkId<CkRecordId>>._, A<int>._, A<string>.Ignored))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task WrapScalarInRecord_MixedList_WrapsOnlyTheScalars()
    {
        // Arrange: one already-wrapped record, two raw strings. Should produce a 3-entry
        // list with the existing record left untouched.
        var rtId = OctoObjectId.GenerateNewId();
        var existing = new RtRecord(ClientUriEntryRecordId,
            new Dictionary<string, object?>
            {
                [UriAttr] = "https://existing.example/",
                [SourceAttr] = "overlay:local-dev",
            });

        var entity = BuildClientEntity(rtId, new List<object?>
        {
            existing,
            "https://refinery.test.octo-mesh.com/",
            "https://refinery.test.octo-mesh.com/silent-renew",
        });

        ArrangeFixedMigration([entity]);

        // Act
        var result = await Migrate();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.EntitiesUpdated);

        var records = AssertIsRecordList(_rewriteCaptures[0].Value);
        Assert.Equal(3, records.Count);

        // Existing record is preserved verbatim — overlay marker survives. (RecordArray
        // serialisation in AttributeValueConverter clones the RtRecord, so the assertion is
        // on attribute content, not on reference identity.)
        Assert.Equal(ClientUriEntryRecordId, records[0].CkRecordId);
        Assert.Equal("https://existing.example/", records[0].Attributes[UriAttr]);
        Assert.Equal("overlay:local-dev", records[0].Attributes[SourceAttr]);

        // The two scalars get wrapped with the base source.
        Assert.Equal("https://refinery.test.octo-mesh.com/", records[1].Attributes[UriAttr]);
        Assert.Equal("base", records[1].Attributes[SourceAttr]);
        Assert.Equal("https://refinery.test.octo-mesh.com/silent-renew", records[2].Attributes[UriAttr]);
        Assert.Equal("base", records[2].Attributes[SourceAttr]);

        // The audit event reports the number of scalars actually wrapped, not the total list size.
        A.CallTo(() => _auditTrail.RecordWrapScalarInRecordAsync(
                TestTenantId,
                ClientTypeId,
                rtId,
                RedirectUrisAttr,
                ClientUriEntryRecordId,
                2,
                "wrap-redirect-uris"))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task WrapScalarInRecord_MissingSourceAttributeOnSubsetOfEntities_GracefullySkipsThem()
    {
        // Arrange: three entities — first has a populated list, second has the attribute set to
        // null, third doesn't have the attribute at all. Only the first should be rewritten.
        var rtIdWithList = OctoObjectId.GenerateNewId();
        var rtIdWithNull = OctoObjectId.GenerateNewId();
        var rtIdNoAttr = OctoObjectId.GenerateNewId();

        var entityWithList = BuildClientEntity(rtIdWithList, new List<object?>
        {
            "https://refinery.test.octo-mesh.com/",
        });
        var entityWithNull = BuildClientEntity(rtIdWithNull, null);
        var entityNoAttr = new RtEntity(ClientTypeId, rtIdNoAttr); // attribute slot never set.

        ArrangeFixedMigration([entityWithList, entityWithNull, entityNoAttr]);

        // Act
        var result = await Migrate();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.EntitiesUpdated);
        Assert.Single(_rewriteCaptures);
        Assert.Equal(rtIdWithList, _rewriteCaptures[0].RtId);
    }

    [Fact]
    public async Task WrapScalarInRecord_RespectsStandardStepMetadata_DescriptionAndContinueOnError()
    {
        // Arrange: a step that mixes a non-list entry into the source attribute with
        // onConflict=Skip should warn and continue rather than fail the migration.
        var rtIdBad = OctoObjectId.GenerateNewId();
        var entityWithScalar = new RtEntity(ClientTypeId, rtIdBad);
        entityWithScalar.SetAttributeValue(RedirectUrisAttr, AttributeValueTypesDto.String,
            "not-a-list");

        var rtIdGood = OctoObjectId.GenerateNewId();
        var entityGood = BuildClientEntity(rtIdGood, new List<object?>
        {
            "https://ok.example/",
        });

        ArrangeFixedMigration([entityWithScalar, entityGood],
            stepCustomiser: step =>
            {
                step.Description = "Lift RedirectUris strings into ClientUriEntry records";
                step.OnConflict = CkMigrationConflictBehavior.Skip;
                step.ContinueOnError = true;
            });

        // Act
        var result = await Migrate();

        // Assert: the bad entity is skipped, the good entity is rewritten.
        Assert.True(result.Success);
        Assert.Equal(1, result.EntitiesUpdated);
        Assert.Single(_rewriteCaptures);
        Assert.Equal(rtIdGood, _rewriteCaptures[0].RtId);
    }

    [Fact]
    public async Task WrapScalarInRecord_MissingRequiredFields_FailsStep()
    {
        // Arrange: deliberately omit the targetRecordCkRecordId field.
        var rtId = OctoObjectId.GenerateNewId();
        var entity = BuildClientEntity(rtId, new List<object?> { "https://x/" });

        var script = BuildScript(step =>
        {
            step.Transform = new CkMigrationTransformDto
            {
                Type = CkMigrationTransformType.WrapScalarInRecord,
                SourceAttribute = RedirectUrisAttr,
                RecordValueAttribute = UriAttr,
                // TargetRecordCkRecordId left null on purpose.
            };
        });

        ArrangeMigration(script, [entity]);

        // Act
        var result = await Migrate();

        // Assert: step failed with a descriptive error, no rewrite happened.
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("targetRecordCkRecordId"));
        Assert.Empty(_rewriteCaptures);
    }

    #region Helpers

    private static RtEntity BuildClientEntity(OctoObjectId rtId, List<object?>? redirectUris)
    {
        var entity = new RtEntity(ClientTypeId, rtId);
        // Bypass AttributeValueConverter so the test can store the raw shape pre-migration.
        // SetAttributeRawValue mirrors what BSON deserialisation produces — a List<object> of
        // strings is exactly what Mongo would hand the engine before the lift.
        entity.SetAttributeRawValue(RedirectUrisAttr, redirectUris);
        return entity;
    }

    private void ArrangeFixedMigration(
        IReadOnlyList<RtEntity> entities,
        Action<CkMigrationStepDto>? stepCustomiser = null)
    {
        var script = BuildScript(step => stepCustomiser?.Invoke(step));
        ArrangeMigration(script, entities);
    }

    private static CkMigrationScriptDto BuildScript(Action<CkMigrationStepDto>? customiser = null)
    {
        var step = new CkMigrationStepDto
        {
            StepId = "wrap-redirect-uris",
            Action = CkMigrationActionType.Transform,
            Target = new CkMigrationTargetDto { CkTypeId = "System.Identity/Client" },
            Transform = new CkMigrationTransformDto
            {
                Type = CkMigrationTransformType.WrapScalarInRecord,
                SourceAttribute = RedirectUrisAttr,
                TargetRecordCkRecordId = "System.Identity/ClientUriEntry",
                RecordValueAttribute = UriAttr,
                RecordDefaults = new Dictionary<string, object> { [SourceAttr] = "base" },
            },
            OnConflict = CkMigrationConflictBehavior.Fail,
        };

        customiser?.Invoke(step);

        return new CkMigrationScriptDto
        {
            SourceVersion = "2.8.0",
            TargetVersion = "2.9.0",
            Steps = [step],
        };
    }

    private void ArrangeMigration(CkMigrationScriptDto script, IReadOnlyList<RtEntity> entities)
    {
        var toModel = new CkModelId("System.Identity", "2.9.0");
        var meta = new CkMigrationMetaDto
        {
            CkModelId = "System.Identity-2.9.0",
            Migrations =
            [
                new CkMigrationReferenceDto
                {
                    FromVersion = "2.8.0",
                    ToVersion = "2.9.0",
                    ScriptPath = "2.8.0-to-2.9.0.yaml",
                },
            ],
        };

        A.CallTo(() => _contentProvider.HasMigrationsAsync(toModel, A<CancellationToken>._))
            .Returns(true);
        A.CallTo(() => _contentProvider.GetMigrationMetaAsync(toModel, A<CancellationToken>._))
            .Returns(meta);
        A.CallTo(() => _contentProvider.GetMigrationAsync(toModel, "2.8.0", "2.9.0", A<CancellationToken>._))
            .Returns(script);
        A.CallTo(() => _repository.GetRtEntitiesByTypeForMigrationAsync(
                A<IOctoSession>._, ClientTypeId))
            .Returns((entities, false));
    }

    private Task<CkMigrationResult> Migrate()
    {
        var fromModel = new CkModelId("System.Identity", "2.8.0");
        var toModel = new CkModelId("System.Identity", "2.9.0");
        return _sut.MigrateAsync(TestTenantId, fromModel, toModel,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static IReadOnlyList<RtRecord> AssertIsRecordList(object? value)
    {
        Assert.NotNull(value);
        var list = Assert.IsType<List<RtRecord>>(value);
        return list;
    }

    #endregion

    #region BuildWrapperRecord direct tests

    [Fact]
    public void BuildWrapperRecord_PutsScalarInValueSlot()
    {
        // Arrange / Act
        var record = CkModelMigrationService.BuildWrapperRecord(
            ClientUriEntryRecordId,
            UriAttr,
            "https://refinery.test.octo-mesh.com/",
            new Dictionary<string, object> { [SourceAttr] = "base" });

        // Assert
        Assert.Equal(ClientUriEntryRecordId, record.CkRecordId);
        Assert.Equal("https://refinery.test.octo-mesh.com/", record.Attributes[UriAttr]);
        Assert.Equal("base", record.Attributes[SourceAttr]);
    }

    [Fact]
    public void BuildWrapperRecord_RecordDefaultsCannotOverwriteValueSlot()
    {
        // Arrange / Act: an author accidentally listed the value slot in recordDefaults.
        // The actual scalar must still win.
        var record = CkModelMigrationService.BuildWrapperRecord(
            ClientUriEntryRecordId,
            UriAttr,
            "https://real.example/",
            new Dictionary<string, object> { [UriAttr] = "https://fallback.example/" });

        // Assert
        Assert.Equal("https://real.example/", record.Attributes[UriAttr]);
    }

    [Fact]
    public void BuildWrapperRecord_NoDefaults_OnlyValueAttributeIsSet()
    {
        // Arrange / Act
        var record = CkModelMigrationService.BuildWrapperRecord(
            ClientUriEntryRecordId,
            UriAttr,
            "https://only-value/",
            new Dictionary<string, object>());

        // Assert
        Assert.Single(record.Attributes);
        Assert.Equal("https://only-value/", record.Attributes[UriAttr]);
    }

    #endregion
}
