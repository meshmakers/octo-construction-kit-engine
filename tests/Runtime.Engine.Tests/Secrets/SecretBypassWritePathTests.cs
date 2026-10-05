using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.CkModelMigrations;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

/// <summary>
///     AB#5532: the write paths that bypass <see cref="BulkRtMutation" /> run the Secret write step
///     themselves - <see cref="RuntimeRepositoryBase.BulkInsertRtEntitiesAsync" /> (bulk import) and the
///     two CK-cache-free CK migration writes (<c>InsertOneRtEntityForMigrationAsync</c> after a
///     <c>ChangeCkType</c>, <c>RewriteAttributeValueForMigrationAsync</c> after a <c>WrapScalarInRecord</c>).
///     No plaintext may reach the repository on any of them.
/// </summary>
public class SecretBypassWritePathTests
{
    private const string Plain = "Hunter2-Plaintext!";
    private const string RecordPlain = "Record-Plaintext!";

    private readonly SecretTestModel _model = new();
    private readonly SecretAttributeProtector _protector = SecretTestModel.CreateProtector();

    #region BulkInsertRtEntitiesAsync

    [Fact]
    public async Task BulkInsert_EncryptsPlaintext_DropsPlaceholders_AndKeepsProtectedValues()
    {
        var (repository, imported) = CreateBulkRepository();
        var preserved = _protector.Protect("preserved-by-upsert");
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("Name", "cfg");
        entity.SetAttributeRawValue("Password", Plain);
        entity.SetAttributeRawValue("ApiKey", preserved);
        entity.SetAttributeRawValue("Credentials", new List<RtRecord>
        {
            _model.CredentialRecord("a", RecordPlain),
            _model.CredentialRecord("b", "<SET_ME>")
        });
        var placeholderOnly = _model.NewOptionalOnly();
        placeholderOnly.SetAttributeRawValue("Password", "TODO_SET_PASSWORD");

        await repository.BulkInsertRtEntitiesAsync(A.Fake<IOctoSession>(), [entity, placeholderOnly],
            new BulkOperationOptions());

        Assert.Equal(2, imported.Count);
        var config = imported.Single(e => e.RtId == entity.RtId);
        Assert.Equal(Plain, _protector.Unprotect((RtSecretValue)config.Attributes["Password"]!));
        Assert.Same(preserved, config.Attributes["ApiKey"]);
        var credentials = ((IEnumerable<RtRecord>)config.Attributes["Credentials"]!).ToList();
        Assert.Equal(RecordPlain, _protector.Unprotect((RtSecretValue)credentials[0].Attributes["Value"]!));
        Assert.Null(credentials[1].Attributes.GetValueOrDefault("Value"));
        Assert.Equal("cfg", config.Attributes["Name"]);

        var optional = imported.Single(e => e.RtId == placeholderOnly.RtId);
        Assert.Null(optional.Attributes.GetValueOrDefault("Password"));

        AssertNoPlaintext(imported, Plain, RecordPlain, "TODO_SET_PASSWORD", "<SET_ME>");
    }

    [Fact]
    public async Task BulkInsert_DoesNotEnforceRequiredSecrets()
    {
        // AB#4772: the import reports missing mandatory attributes itself; the write step must not throw.
        var (repository, imported) = CreateBulkRepository();
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", "<SET>");

        await repository.BulkInsertRtEntitiesAsync(A.Fake<IOctoSession>(), [entity], new BulkOperationOptions());

        Assert.Null(Assert.Single(imported).Attributes.GetValueOrDefault("ApiKey"));
    }

    [Fact]
    public async Task BulkInsert_TypesWithoutSecrets_AreWrittenUnchanged()
    {
        var (repository, imported) = CreateBulkRepository();
        var entity = new RtEntity(_model.Plain.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId());
        entity.SetAttributeRawValue("Name", "<looks-like-a-placeholder>");

        await repository.BulkInsertRtEntitiesAsync(A.Fake<IOctoSession>(), [entity], new BulkOperationOptions());

        Assert.Equal("<looks-like-a-placeholder>", Assert.Single(imported).Attributes["Name"]);
    }

    [Fact]
    public async Task BulkInsert_PlaintextWithoutKeys_Throws_AndWritesNothing()
    {
        var (repository, imported) = CreateBulkRepository(SecretTestModel.CreateProtector(configured: false));
        var entity = _model.NewOptionalOnly();
        entity.SetAttributeRawValue("Password", Plain);

        await Assert.ThrowsAsync<Contracts.Secrets.SecretEncryptionNotConfiguredException>(() =>
            repository.BulkInsertRtEntitiesAsync(A.Fake<IOctoSession>(), [entity], new BulkOperationOptions()));
        Assert.Empty(imported);
    }

    private (RuntimeRepositoryBase Repository, List<RtEntity> Imported) CreateBulkRepository(
        SecretAttributeProtector? protector = null)
    {
        protector ??= _protector;
        var imported = new List<RtEntity>();
        var collection = A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
        A.CallTo(() => collection.BulkImportAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
                A<BulkOperationOptions>._))
            .ReturnsLazily(call =>
            {
                imported.AddRange(call.GetArgument<IEnumerable<RtEntity>>(1)!);
                return Task.FromResult(A.Fake<IBulkImportResult>());
            });
        var dataSource = A.Fake<IRepositoryDataSource>();
        A.CallTo(() => dataSource.TenantId).Returns(SecretTestModel.TenantId);
        A.CallTo(() => dataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>._)).Returns(collection);
        var mutation = new BulkRtMutation(new EntityRuleEngine(_model.Cache), new BulkRtMutationSecretTests.NoAssociationsGraphRuleEngine(), [],
            new SecretWriteNormalizer(protector));
        return (new StubRepository(_model.Cache, dataSource, mutation), imported);
    }

    #endregion

    #region CK migration writes

    private static readonly CkModelId FromModel = new("Test", "1.0.0");
    private static readonly CkModelId ToModel = new("Test", "1.1.0");

    [Fact]
    public async Task ChangeCkTypeMigration_NormalisesSecretsOfTheInsertedEntity_AsStoredValues()
    {
        var repository = A.Fake<IRuntimeRepository>();
        var inserted = new List<RtEntity>();
        A.CallTo(() => repository.InsertOneRtEntityForMigrationAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<RtEntity>._))
            .Invokes(call => inserted.Add(call.GetArgument<RtEntity>(2)!));

        // Read from the old collection: strings in Secret slots are stored legacy values.
        var oldTypeId = new RtCkId<CkTypeId>("Test/OldConfig");
        var entity = new RtEntity(oldTypeId, OctoObjectId.GenerateNewId());
        entity.SetAttributeRawValue("Password", Plain);
        entity.SetAttributeRawValue("ApiKey", SecretTestModel.V1Vector);
        entity.SetAttributeRawValue("Credentials", new List<RtRecord>
        {
            _model.CredentialRecord("a", RecordPlain),
            _model.CredentialRecord("b", "TODO_SET_B")
        });
        A.CallTo(() => repository.GetRtEntitiesByTypeForMigrationAsync(A<IOctoSession>._,
                A<RtCkId<CkTypeId>>.That.Matches(id => id.FullName == oldTypeId.FullName)))
            .Returns(Task.FromResult<(IReadOnlyList<RtEntity>, bool)>(([entity], false)));

        var result = await MigrateAsync(repository, new CkMigrationStepDto
        {
            StepId = "move-config",
            Action = CkMigrationActionType.Transform,
            Target = new CkMigrationTargetDto { CkTypeId = "Test/OldConfig" },
            Transform = new CkMigrationTransformDto
            {
                Type = CkMigrationTransformType.ChangeCkType, NewCkTypeId = "Test/Config"
            }
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var written = Assert.Single(inserted);
        Assert.Equal(Plain, _protector.Unprotect((RtSecretValue)written.Attributes["Password"]!));
        var apiKey = (RtSecretValue)written.Attributes["ApiKey"]!;
        Assert.True(apiKey.IsProtected); // enc:v1 moved to enc:v2
        Assert.Equal(SecretTestModel.V1VectorPlaintext, _protector.Unprotect(apiKey));
        var credentials = ((IEnumerable<RtRecord>)written.Attributes["Credentials"]!).ToList();
        Assert.Equal(RecordPlain, _protector.Unprotect((RtSecretValue)credentials[0].Attributes["Value"]!));
        Assert.Null(credentials[1].Attributes.GetValueOrDefault("Value"));
        AssertNoPlaintext(inserted, Plain, RecordPlain, SecretTestModel.V1Vector, "TODO_SET_B");
    }

    [Fact]
    public async Task ChangeCkTypeMigration_WithoutKeys_KeepsLegacyValuesAsStored()
    {
        // Without a key ring a stored legacy value is kept (it was readable before and stays so);
        // only new input needs a key. The migration must not fail over it.
        var repository = A.Fake<IRuntimeRepository>();
        var inserted = new List<RtEntity>();
        A.CallTo(() => repository.InsertOneRtEntityForMigrationAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<RtEntity>._))
            .Invokes(call => inserted.Add(call.GetArgument<RtEntity>(2)!));
        var entity = new RtEntity(new RtCkId<CkTypeId>("Test/OldConfig"), OctoObjectId.GenerateNewId());
        entity.SetAttributeRawValue("Password", Plain);
        A.CallTo(() => repository.GetRtEntitiesByTypeForMigrationAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._))
            .Returns(Task.FromResult<(IReadOnlyList<RtEntity>, bool)>(([entity], false)));

        var result = await MigrateAsync(repository, new CkMigrationStepDto
        {
            StepId = "move-config",
            Action = CkMigrationActionType.Transform,
            Target = new CkMigrationTargetDto { CkTypeId = "Test/OldConfig" },
            Transform = new CkMigrationTransformDto
            {
                Type = CkMigrationTransformType.ChangeCkType, NewCkTypeId = "Test/Config"
            }
        }, SecretTestModel.CreateProtector(configured: false));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var password = Assert.IsType<RtSecretValue>(Assert.Single(inserted).Attributes["Password"]);
        Assert.True(password.IsLegacyPlaintext);
    }

    [Fact]
    public async Task WrapScalarInRecordMigration_NormalisesSecretsOfTheRewrittenValue()
    {
        var repository = A.Fake<IRuntimeRepository>();
        var rewrites = new List<object?>();
        A.CallTo(() => repository.RewriteAttributeValueForMigrationAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<OctoObjectId>._, "Credentials", A<object?>._))
            .Invokes(call => rewrites.Add(call.GetArgument<object?>(4)));

        // Credentials used to be a list of bare strings; the step lifts each into a Credential record
        // whose Value is a Secret. Mixed with an already lifted record that still holds plaintext.
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("Credentials", new List<object?>
        {
            _model.CredentialRecord("existing", RecordPlain),
            Plain
        });
        A.CallTo(() => repository.GetRtEntitiesByTypeForMigrationAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._))
            .Returns(Task.FromResult<(IReadOnlyList<RtEntity>, bool)>(([entity], false)));

        var result = await MigrateAsync(repository, new CkMigrationStepDto
        {
            StepId = "wrap-credentials",
            Action = CkMigrationActionType.Transform,
            Target = new CkMigrationTargetDto { CkTypeId = "Test/Config" },
            Transform = new CkMigrationTransformDto
            {
                Type = CkMigrationTransformType.WrapScalarInRecord,
                SourceAttribute = "Credentials",
                TargetRecordCkRecordId = _model.Credential.CkRecordId.ToRtCkId().FullName,
                RecordValueAttribute = "Value",
                RecordDefaults = new Dictionary<string, object> { ["Key"] = "lifted" }
            },
            OnConflict = CkMigrationConflictBehavior.Fail
        });

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var records = ((IEnumerable<object?>)Assert.Single(rewrites)!).Cast<RtRecord>().ToList();
        Assert.Equal(2, records.Count);
        Assert.Equal(RecordPlain, _protector.Unprotect((RtSecretValue)records[0].Attributes["Value"]!));
        Assert.Equal(Plain, _protector.Unprotect((RtSecretValue)records[1].Attributes["Value"]!));
        Assert.Equal("lifted", records[1].Attributes["Key"]);
        foreach (var record in records)
        {
            Assert.DoesNotContain(SecretTestModel.AllStrings(record),
                s => s.Contains(Plain, StringComparison.Ordinal) || s.Contains(RecordPlain, StringComparison.Ordinal));
        }
    }

    private async Task<CkMigrationResult> MigrateAsync(IRuntimeRepository repository, CkMigrationStepDto step,
        SecretAttributeProtector? protector = null)
    {
        protector ??= _protector;
        var contentProvider = A.Fake<ICkMigrationContentProvider>();
        var repositoryProvider = A.Fake<IRuntimeRepositoryProvider>();
        A.CallTo(() => repositoryProvider.GetRepositoryAsync(SecretTestModel.TenantId, A<CancellationToken>._))
            .Returns(repository);
        A.CallTo(() => repository.GetSessionAsync()).Returns(A.Fake<IOctoSession>());
        A.CallTo(() => contentProvider.HasMigrationsAsync(ToModel, A<CancellationToken>._)).Returns(true);
        A.CallTo(() => contentProvider.GetMigrationMetaAsync(ToModel, A<CancellationToken>._))
            .Returns(new CkMigrationMetaDto
            {
                CkModelId = ToModel.ToString(),
                Migrations =
                [
                    new CkMigrationReferenceDto { FromVersion = "1.0.0", ToVersion = "1.1.0", ScriptPath = "1.0.0-to-1.1.0.yaml" }
                ]
            });
        A.CallTo(() => contentProvider.GetMigrationAsync(ToModel, "1.0.0", "1.1.0", A<CancellationToken>._))
            .Returns(new CkMigrationScriptDto { SourceVersion = "1.0.0", TargetVersion = "1.1.0", Steps = [step] });

        var service = new CkModelMigrationService(A.Fake<ICkMigrationParser>(), contentProvider, repositoryProvider,
            A.Fake<ICatalogService>(), A.Fake<ICkModelImportAuditTrail>(), NullLogger<CkModelMigrationService>.Instance,
            _model.Cache, new SecretWriteNormalizer(protector));
        return await service.MigrateAsync(SecretTestModel.TenantId, FromModel, ToModel,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    #endregion

    private static void AssertNoPlaintext(IEnumerable<RtEntity> entities, params string[] plaintexts)
    {
        foreach (var entity in entities)
        {
            var strings = SecretTestModel.AllStrings(entity).ToList();
            foreach (var plaintext in plaintexts)
            {
                Assert.DoesNotContain(strings, s => s.Contains(plaintext, StringComparison.Ordinal));
            }
        }
    }

    /// <summary>
    ///     Only <see cref="RuntimeRepositoryBase.BulkInsertRtEntitiesAsync" /> is exercised; everything the
    ///     base needs from a concrete repository beyond the data source is unsupported.
    /// </summary>
    private sealed class StubRepository(ICkCacheService cache, IRepositoryDataSource dataSource, IBulkRtMutation mutation)
        : RuntimeRepositoryBase(SecretTestModel.TenantId, cache, dataSource, mutation)
    {
        protected override Task RefreshCkCacheServiceAsync(ICkCacheService ckCacheService) => Task.CompletedTask;

        public override Task<IOctoSession> GetSessionAsync() => throw new NotSupportedException();

        public override Task<IResultSet<RtEntityGraphItem>> GetRtEntitiesGraphByTypeAsync(IOctoSession session,
            RtCkId<CkTypeId> ckTypeId, RtEntityQueryOptions rtEntityQueryOptions,
            ICollection<NavigationPair> roleIdDirectionPairs, int? skip = null, int? take = null) =>
            throw new NotSupportedException();

        public override Task<IResultSet<RtEntityGraphItem>> GetRtEntitiesGraphByIdAsync(IOctoSession session,
            RtCkId<CkTypeId> ckTypeId, IReadOnlyList<OctoObjectId> rtIds,
            RtEntityQueryOptions rtEntityQueryOptions, IEnumerable<NavigationPair> roleIdDirectionPairs,
            int? skip = null, int? take = null) => throw new NotSupportedException();

        protected override Task<IResultSet<TEntity>> GetRtEntitiesByIdAsync<TEntity>(IOctoSession session,
            RtCkId<CkTypeId> ckTypeId, IReadOnlyList<OctoObjectId> rtIds,
            RtEntityQueryOptions rtEntityQueryOptions, int? skip = null, int? take = null) =>
            throw new NotSupportedException();

        protected override Task DeleteManyRtEntitiesAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
            FieldFilterCriteria fieldFilterCriteria, DeleteOptions deleteOptions) => throw new NotSupportedException();

        protected override Task DeleteOneRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
            FieldFilterCriteria fieldFilterCriteria, DeleteOptions deleteOptions) => throw new NotSupportedException();

        protected override Task UpdateOneRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
            FieldFilterCriteria fieldFilterCriteria, TEntity rtEntity) => throw new NotSupportedException();

        protected override Task UpdateManyRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
            FieldFilterCriteria fieldFilterCriteria, TEntity rtEntity) => throw new NotSupportedException();

        protected override Task ReplaceOneRtEntityAsync<TEntity>(IOctoSession session, RtCkId<CkTypeId> ckTypeId,
            FieldFilterCriteria fieldFilterCriteria, TEntity rtEntity) => throw new NotSupportedException();

        protected override Task<IResultSet<TEntity>> GetRtEntitiesByTypeAsync<TEntity>(IOctoSession session,
            RtCkId<CkTypeId> ckTypeId, RtEntityQueryOptions rtEntityQueryOptions, int? skip = null,
            int? take = null) => throw new NotSupportedException();
    }
}
