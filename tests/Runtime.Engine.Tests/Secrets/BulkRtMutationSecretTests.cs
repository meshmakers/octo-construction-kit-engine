using System.Linq.Expressions;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Repositories;
using Meshmakers.Octo.Runtime.Engine.RuleEngine;
using Meshmakers.Octo.Runtime.Engine.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

/// <summary>
///     AB#5532: the Secret write step inside <see cref="BulkRtMutation" /> (insert, update, replace) together
///     with the rule engine (clear list, required secrets). The repository collection is faked and captures
///     what would be written - no plaintext may ever reach it.
/// </summary>
public class BulkRtMutationSecretTests
{
    private const string Plain = "Hunter2-Plaintext!";
    private readonly SecretTestModel _model = new();
    private readonly SecretAttributeProtector _protector = SecretTestModel.CreateProtector();
    private readonly IDataSourceCollection<OctoObjectId, RtEntity> _collection =
        A.Fake<IDataSourceCollection<OctoObjectId, RtEntity>>();
    private readonly IRepositoryDataSource _dataSource = A.Fake<IRepositoryDataSource>();
    private readonly IOctoSession _session = A.Fake<IOctoSession>();
    private readonly List<RtEntity> _written = [];
    private readonly List<RtEntity> _stored = [];
    private readonly BulkRtMutation _mutation;

    public BulkRtMutationSecretTests()
    {
        A.CallTo(() => _dataSource.TenantId).Returns(SecretTestModel.TenantId);
        A.CallTo(() => _dataSource.GetRtCollection<RtEntity>(A<CkTypeGraph>._)).Returns(_collection);
        A.CallTo(() => _collection.FindManyAsync(A<IOctoSession>._, A<Expression<Func<RtEntity, bool>>>._,
                A<int?>._, A<int?>._))
            .ReturnsLazily(call =>
            {
                var predicate = call.GetArgument<Expression<Func<RtEntity, bool>>>(1)!.Compile();
                return Task.FromResult<ICollection<RtEntity>>(_stored.Where(predicate).ToList());
            });
        A.CallTo(() => _collection.InsertManyAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .Invokes(call => _written.AddRange(call.GetArgument<IEnumerable<RtEntity>>(1)!));
        A.CallTo(() => _collection.ReplaceManyAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .Invokes(call => _written.AddRange(call.GetArgument<IEnumerable<RtEntity>>(1)!));
        A.CallTo(() => _collection.UpdateOneAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._))
            .Invokes(call => _written.AddRange(call.GetArgument<IEnumerable<RtEntity>>(1)!));

        _mutation = new BulkRtMutation(new EntityRuleEngine(_model.Cache), new NoAssociationsGraphRuleEngine(), [],
            new SecretWriteNormalizer(_protector));
    }

    private Task ApplyAsync(params IEntityUpdateInfo<RtEntity>[] updates)
    {
        return _mutation.ApplyChangesAsync(_session, _dataSource, _model.Cache, updates, [],
            BulkRtMutationOptions.Default);
    }

    private RtEntityId IdOf(RtEntity entity) => new(_model.Config.CkTypeId.ToRtCkId(), entity.RtId);

    private void AssertNoPlaintextWritten(params string[] plaintexts)
    {
        Assert.NotEmpty(_written);
        foreach (var entity in _written)
        {
            foreach (var slot in _model.SecretSlotValues(entity))
            {
                Assert.True(slot == null || slot is RtSecretValue { IsProtected: true },
                    $"A Secret slot reached the repository as {slot?.GetType().Name}");
            }

            var strings = SecretTestModel.AllStrings(entity).ToList();
            foreach (var plaintext in plaintexts)
            {
                Assert.DoesNotContain(strings, s => s.Contains(plaintext, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public async Task Insert_NoPlaintextReachesTheRepository()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", Plain);
        entity.SetAttributeRawValue("Password", RtSecretValue.Pending("pw-" + Plain));
        entity.SetAttributeRawValue("Credentials", new List<RtRecord> { _model.CredentialRecord("a", "rec-" + Plain) });
        entity.SetAttributeRawValue("Wrappers", new List<RtRecord>
        {
            _model.WrapperRecord("w", _model.CredentialRecord("i", "inner-" + Plain),
                _model.CredentialRecord("k", "item-" + Plain))
        });

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateInsert(entity));

        AssertNoPlaintextWritten(Plain);
        var written = Assert.Single(_written);
        Assert.Equal(Plain, _protector.Unprotect((RtSecretValue)written.Attributes["ApiKey"]!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<SET_AFTER_INSTALL>")]
    [InlineData("TODO_SET_API_KEY")]
    public async Task Insert_RequiredSecretWithoutValue_IsRejected(string value)
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", value);

        var ex = await Assert.ThrowsAsync<RuntimeRepositoryException>(() =>
            ApplyAsync(EntityUpdateInfo<RtEntity>.CreateInsert(entity)));

        Assert.Contains("ApiKey", ex.Message);
        Assert.Empty(_written);
    }

    [Fact]
    public async Task Update_EmptyStringLeavesStoredValue_ClearListClears()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", "");
        entity.SetAttributeRawValue("Name", "renamed");

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity, ["Password"]));

        var written = Assert.Single(_written);
        Assert.False(written.Attributes.ContainsKey("ApiKey"));
        Assert.True(written.Attributes.ContainsKey("Password"));
        Assert.Null(written.Attributes["Password"]);
    }

    [Fact]
    public async Task Update_RecordArray_CarriesOverByKey_WithoutPlaintext()
    {
        var stored = _model.NewConfig();
        var storedValue = _protector.Protect("stored-" + Plain);
        stored.SetAttributeRawValue("Credentials", new List<RtRecord> { _model.CredentialRecord("a", storedValue) });
        _stored.Add(stored);

        var entity = _model.NewConfig(stored.RtId);
        entity.SetAttributeRawValue("Credentials", new List<RtRecord>
        {
            _model.CredentialRecord("a", null), _model.CredentialRecord("b", "new-" + Plain)
        });

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity));

        AssertNoPlaintextWritten(Plain);
        var elements = ((IEnumerable<RtRecord>)_written.Single().Attributes["Credentials"]!).ToList();
        Assert.Equal(storedValue.Envelope, ((RtSecretValue)elements[0].Attributes["Value"]!).Envelope);
    }

    [Fact]
    public async Task Replace_EmptyAndOmitted_CarryOver()
    {
        var stored = _model.NewConfig();
        var storedApiKey = _protector.Protect("api-" + Plain);
        var storedPassword = _protector.Protect("pw-" + Plain);
        stored.SetAttributeRawValue("ApiKey", storedApiKey);
        stored.SetAttributeRawValue("Password", storedPassword);
        stored.RtCreatedBy = "creator";
        _stored.Add(stored);

        var entity = _model.NewConfig(stored.RtId);
        entity.SetAttributeRawValue("ApiKey", "");

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateReplace(IdOf(entity), entity));

        var written = Assert.Single(_written);
        Assert.Equal(storedApiKey.Envelope, ((RtSecretValue)written.Attributes["ApiKey"]!).Envelope);
        Assert.Equal(storedPassword.Envelope, ((RtSecretValue)written.Attributes["Password"]!).Envelope);
        Assert.Equal("creator", written.RtCreatedBy);
        AssertNoPlaintextWritten(Plain);
    }

    [Fact]
    public async Task Replace_WithClearList_ClearsInsteadOfCarryingOver()
    {
        var stored = _model.NewConfig();
        stored.SetAttributeRawValue("ApiKey", _protector.Protect("api"));
        stored.SetAttributeRawValue("Password", _protector.Protect("pw"));
        _stored.Add(stored);

        var entity = _model.NewConfig(stored.RtId);

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateReplace(IdOf(entity), entity, ["Password"]));

        var written = Assert.Single(_written);
        Assert.Null(written.Attributes["Password"]);
        Assert.IsType<RtSecretValue>(written.Attributes["ApiKey"]);
    }

    [Fact]
    public async Task Replace_RequiredSecretWithNothingToCarry_IsRejectedBeforeAnyWrite()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("Name", Plain); // a non-secret value - still never echoed below

        var ex = await Assert.ThrowsAsync<RuntimeRepositoryException>(() =>
            ApplyAsync(EntityUpdateInfo<RtEntity>.CreateReplace(IdOf(entity), entity)));

        Assert.Contains("ApiKey", ex.Message);
        Assert.Empty(_written);
        A.CallTo(() => _dataSource.BinaryDataSource.DeleteAllFileSystemBinariesAsync(A<IOctoSession>._,
            A<RtEntityId>._, A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task ClearList_RequiredSecret_IsRejected()
    {
        var entity = _model.NewConfig();

        var ex = await Assert.ThrowsAsync<RuntimeRepositoryException>(() =>
            ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity, ["ApiKey"])));

        Assert.Contains(ex.OperationResult.Messages, m => m.MessageNumber == 22);
        Assert.Empty(_written);
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("DoesNotExist")]
    public async Task ClearList_NotASecretAttribute_IsRejected(string attributeName)
    {
        var entity = _model.NewConfig();

        var ex = await Assert.ThrowsAsync<RuntimeRepositoryException>(() =>
            ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity, [attributeName])));

        Assert.Contains(ex.OperationResult.Messages, m => m.MessageNumber == 21);
    }

    [Fact]
    public async Task ClearList_SetAndCleared_IsRejected_WithoutEchoingTheValue()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("Password", Plain);

        var ex = await Assert.ThrowsAsync<RuntimeRepositoryException>(() =>
            ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity, ["Password"])));

        Assert.Contains(ex.OperationResult.Messages, m => m.MessageNumber == 23);
        Assert.DoesNotContain(Plain, ex.Message);
        Assert.DoesNotContain(Plain, ex.ToString());
    }

    [Fact]
    public async Task ClearList_WithEmptyValue_IsAllowed()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("Password", "");

        await ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity, ["Password"]));

        Assert.Null(_written.Single().Attributes["Password"]);
    }

    [Fact]
    public async Task Update_PlaceholderOnRequiredSecret_IsRejected()
    {
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", "TODO_SET_API_KEY");

        var ex = await Assert.ThrowsAsync<RuntimeRepositoryException>(() =>
            ApplyAsync(EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity)));

        Assert.Contains(ex.OperationResult.Messages, m => m.MessageNumber == 5);
    }

    [Fact]
    public async Task NoKeyConfigured_WritingASecret_Throws_WithoutEchoingTheValue()
    {
        var mutation = new BulkRtMutation(new EntityRuleEngine(_model.Cache), new NoAssociationsGraphRuleEngine(), [],
            new SecretWriteNormalizer(SecretTestModel.CreateProtector(configured: false)));
        var entity = _model.NewConfig();
        entity.SetAttributeRawValue("ApiKey", Plain);

        var ex = await Assert.ThrowsAsync<Contracts.Secrets.SecretEncryptionNotConfiguredException>(() =>
            mutation.ApplyChangesAsync(_session, _dataSource, _model.Cache,
                [EntityUpdateInfo<RtEntity>.CreateInsert(entity)], [], BulkRtMutationOptions.Default));

        Assert.DoesNotContain(Plain, ex.ToString());
        Assert.Empty(_written);
    }

    [Fact]
    public void EntityUpdateInfo_ClearList_IsNormalised()
    {
        var entity = _model.NewConfig();
        var info = EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity, [" Password ", "Password", ""]);

        Assert.Equal(["Password"], info.ClearSecretAttributes);
        Assert.Null(EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity, []).ClearSecretAttributes);
        Assert.Null(EntityUpdateInfo<RtEntity>.CreateUpdate(IdOf(entity), entity).ClearSecretAttributes);
        Assert.Null(((IEntityUpdateInfo<RtEntity>)EntityUpdateInfo<RtEntity>.CreateInsert(entity)).ClearSecretAttributes);
    }

    /// <summary>
    ///     The graph rule engine is internal to the contracts; these tests write no associations.
    /// </summary>
    internal sealed class NoAssociationsGraphRuleEngine : IGraphRuleEngine
    {
        public Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
            IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList, IOriginFileResolver originFileResolver,
            OperationResult operationResult) => Task.FromResult(new GraphRuleEngineResult());

        public Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
            IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
            IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, IOriginFileResolver originFileResolver,
            OperationResult operationResult) => Task.FromResult(new GraphRuleEngineResult());

        public Task<GraphRuleEngineResult> ValidateAsync(IOctoSession session, IRepositoryDataSource repositoryDataSource,
            IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, IOriginFileResolver originFileResolver,
            OperationResult operationResult) => Task.FromResult(new GraphRuleEngineResult());
    }
}
