using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Secrets;
using Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     AB#5945: <see cref="RuntimeRepositoryBase.BulkInsertRtEntitiesAsync" /> - the write path behind
///     ImportRt and every blueprint install/update - bypasses the pre-document modifications, so it
///     must compute rtDisplayName / rtDisplayDescription itself. Before the fix every seeded entity
///     was written without them and an upsert (full replace) wiped a previously computed value.
/// </summary>
public class BulkInsertDisplayFieldTests
{
    private readonly SecretTestModel _model = new();

    [Theory]
    [InlineData(BulkInsertStrategies.InsertOnly)]
    [InlineData(BulkInsertStrategies.Upsert)]
    public async Task BulkInsert_TypeWithDisplayRules_ComputesDisplayFields(BulkInsertStrategies strategy)
    {
        var (repository, imported) = CreateBulkRepository();
        var entity = NewNamed("FY", 2026, timeout: 30);

        await repository.BulkInsertRtEntitiesAsync(A.Fake<IOctoSession>(), [entity],
            new BulkOperationOptions { InsertStrategy = strategy });

        var written = Assert.Single(imported);
        Assert.Equal("FY (2026)", written.RtDisplayName);
        Assert.Equal("Timeout 30", written.RtDisplayDescription);
    }

    [Fact]
    public async Task BulkInsert_CallerSuppliedDisplayName_IsOverwrittenByRule()
    {
        // The display fields are engine-computed (read-only system fields) - a stale or forged
        // value carried by the import must not survive, exactly like on the save path.
        var (repository, imported) = CreateBulkRepository();
        var entity = NewNamed("FY", 2025);
        entity.RtDisplayName = "Meshmakers.Accounting/FiscalYear@aa";

        await repository.BulkInsertRtEntitiesAsync(A.Fake<IOctoSession>(), [entity], new BulkOperationOptions());

        Assert.Equal("FY (2025)", Assert.Single(imported).RtDisplayName);
    }

    [Fact]
    public async Task BulkInsert_AllReferencedAttributesEmpty_YieldsNull()
    {
        var (repository, imported) = CreateBulkRepository();
        var entity = new RtEntity(_model.Named.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId());

        await repository.BulkInsertRtEntitiesAsync(A.Fake<IOctoSession>(), [entity], new BulkOperationOptions());

        var written = Assert.Single(imported);
        Assert.Null(written.RtDisplayName);
        Assert.Null(written.RtDisplayDescription);
    }

    [Fact]
    public async Task BulkInsert_TypeWithoutRules_LeavesDisplayFieldsNull()
    {
        var (repository, imported) = CreateBulkRepository();
        var entity = new RtEntity(_model.Plain.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId());
        entity.SetAttributeRawValue("Name", "plain");
        entity.RtDisplayName = "stale";

        await repository.BulkInsertRtEntitiesAsync(A.Fake<IOctoSession>(), [entity], new BulkOperationOptions());

        Assert.Null(Assert.Single(imported).RtDisplayName);
    }

    [Fact]
    public async Task BulkInsert_MixedTypes_EachEvaluatedWithItsOwnRules()
    {
        var (repository, imported) = CreateBulkRepository();
        var plain = new RtEntity(_model.Plain.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId());
        plain.SetAttributeRawValue("Name", "plain");
        var named1 = NewNamed("A", 1);
        var named2 = NewNamed("B", 2);

        await repository.BulkInsertRtEntitiesAsync(A.Fake<IOctoSession>(), [named1, plain, named2],
            new BulkOperationOptions());

        Assert.Equal(3, imported.Count);
        Assert.Equal("A (1)", imported.Single(e => e.RtId == named1.RtId).RtDisplayName);
        Assert.Equal("B (2)", imported.Single(e => e.RtId == named2.RtId).RtDisplayName);
        Assert.Null(imported.Single(e => e.RtId == plain.RtId).RtDisplayName);
    }

    private RtEntity NewNamed(string name, int year, int? timeout = null)
    {
        var entity = new RtEntity(_model.Named.CkTypeId.ToRtCkId(), OctoObjectId.GenerateNewId());
        entity.SetAttributeRawValue("Name", name);
        entity.SetAttributeRawValue("Year", year);
        if (timeout != null)
        {
            var settings = new RtRecord { CkRecordId = _model.Settings.CkRecordId.ToRtCkId() };
            settings.SetAttributeRawValue("Timeout", timeout);
            entity.SetAttributeRawValue("Settings", settings);
        }

        return entity;
    }

    private (RuntimeRepositoryBase Repository, List<RtEntity> Imported) CreateBulkRepository()
    {
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
        var mutation = new BulkRtMutation(new EntityRuleEngine(_model.Cache),
            new BulkRtMutationSecretTests.NoAssociationsGraphRuleEngine(), [],
            new SecretWriteNormalizer(SecretTestModel.CreateProtector()));
        return (new SecretBypassWritePathTests.StubRepository(_model.Cache, dataSource, mutation), imported);
    }
}
