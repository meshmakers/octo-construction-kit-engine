using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Secrets;
using Meshmakers.Octo.Runtime.Engine.Tests.Fixtures;
using Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     AB#7445: <see cref="BulkRtMutation" /> hands record array item updates to the collection as ONE statement
///     (no read of the stored entity) and reports an update that matched no item as a warning.
/// </summary>
public class BulkRtMutationRecordArrayItemTests(CacheServiceFixture fixture) : IClassFixture<CacheServiceFixture>
{
    private static readonly OctoObjectId EntityId = OctoObjectId.Parse("67628789d10f47d162164d34");
    private static readonly RtEntityId Id = new("Test/Country", EntityId);

    private readonly IDataSourceCollection<OctoObjectId, RtEntity> _collection =
        A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();

    private readonly IRepositoryDataSource _dataSource = A.Fake<IRepositoryDataSource>();
    private readonly IOctoSession _session = A.Fake<IOctoSession>();

    private static RtRecordArrayItemUpdate Item() =>
        new("RecordArrayTests", "Designation", "A", new Dictionary<string, object?> { ["DataCount"] = 5 });

    private async Task<OperationResult> ApplyAsync(Dictionary<string, object?> regularAttributes,
        bool written = true)
    {
        var cache = await fixture.GetCacheServiceAsync();
        A.CallTo(() => _dataSource.TenantId).Returns(fixture.TenantId);
        A.CallTo(() => _dataSource.GetRtCollection<RtEntity>(
                A<Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph.CkTypeGraph>._))
            .Returns(_collection);
        A.CallTo(() => _collection.UpdateOneWithRecordArrayItemsAsync(A<IOctoSession>._, A<RtEntity>._,
            A<IReadOnlyCollection<RtRecordArrayItemUpdate>>._, A<bool>._)).Returns(written);

        var mutation = new BulkRtMutation(new EntityRuleEngine(cache),
            new BulkRtMutationSecretTests.NoAssociationsGraphRuleEngine(), [],
            new SecretWriteNormalizer(SecretTestModel.CreateProtector()));
        var callerResult = new OperationResult();
        var entity = new RtEntity("Test/Country", EntityId, regularAttributes);
        await mutation.ApplyChangesAsync(_session, _dataSource, cache,
            [EntityUpdateInfo<RtEntity>.CreateRecordArrayItemUpdate(Id, entity, [Item()])], [],
            BulkRtMutationOptions.Default, callerResult);
        return callerResult;
    }

    [Fact]
    public async Task ItemOnlyUpdate_IsOneStatement_RequiringAnItemMatch_AndReadsNothing()
    {
        var result = await ApplyAsync(new Dictionary<string, object?>());

        A.CallTo(() => _collection.UpdateOneWithRecordArrayItemsAsync(_session, A<RtEntity>.That.Matches(e => e.RtId == EntityId),
                A<IReadOnlyCollection<RtRecordArrayItemUpdate>>.That.Matches(u => u.Count == 1), true))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _collection.UpdateOneAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._)).MustNotHaveHappened();
        A.CallTo(_collection).Where(call => call.Method.Name.StartsWith("Get") || call.Method.Name.StartsWith("Find")
                                                                                || call.Method.Name.StartsWith("Query"))
            .MustNotHaveHappened();
        Assert.Empty(result.Messages);
    }

    [Fact]
    public async Task MixedUpdate_WritesTheRegularAttributesRegardlessOfTheItemMatch()
    {
        await ApplyAsync(new Dictionary<string, object?> { ["IsEnabled"] = true });

        A.CallTo(() => _collection.UpdateOneWithRecordArrayItemsAsync(A<IOctoSession>._, A<RtEntity>._,
            A<IReadOnlyCollection<RtRecordArrayItemUpdate>>._, false)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task NoMatch_IsAWarningOnTheCallersResult_NotAnException()
    {
        var result = await ApplyAsync(new Dictionary<string, object?>(), written: false);

        var message = Assert.Single(result.Messages);
        Assert.Equal(MessageLevel.Warning, message.MessageLevel);
        Assert.Equal(27, message.MessageNumber);
        Assert.Contains("Test/Country", message.ToString());
        Assert.Contains("RecordArrayTests", message.ToString());
        Assert.False(result.HasErrors);
        Assert.False(result.HasFatalErrors);
    }
}
