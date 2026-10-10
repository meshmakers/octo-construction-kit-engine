using FakeItEasy;

using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Repositories.Query;
using Meshmakers.Octo.Runtime.Engine.Security;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     AB#6384 — blueprint-lock restriction of the write guard: locked / unlocked / absent flag, derived
///     types, system caller, Enforce vs AuditOnly, protected attributes on insert and update, atomic
///     rejection, and the read budget (none for non-opted-in types, one batch per opted-in type).
/// </summary>
public class DataPermissionWriteGuardBlueprintLockTests
{
    private const string Tenant = "tenant1";
    private const string RuleType = "Test/CategorizationRule";
    private const string DerivedRuleType = "Test/SpecialRule";
    private const string OtherType = "Test/Contact";

    private static readonly RtSecurityContext User = RtSecurityContext.ForUser("user-1", ["Accountant"]);

    private static RtCkId<CkTypeId> Rt(string id) => new(id);

    private static RtDataPolicyTable Table(bool protect = true, bool auditOnly = false, params string[] targets)
    {
        return new RtDataPolicyTable(
        [
            new RtDataPolicyRule("rules", new HashSet<string>(targets.Length == 0 ? [RuleType] : targets),
                [RtDataAction.Read, RtDataAction.Write, RtDataAction.Delete], OwnedOnly: false,
                AuditOnly: auditOnly, new HashSet<string> { "Accountant" }, protect)
        ]);
    }

    private sealed class Harness
    {
        public IRuntimeRepository Repository { get; } = A.Fake<IRuntimeRepository>();
        public ICkCacheService CkCache { get; } = A.Fake<ICkCacheService>();
        public IAuditEventSink AuditSink { get; } = A.Fake<IAuditEventSink>();
        public IOctoSession Session { get; } = A.Fake<IOctoSession>();
        public Dictionary<OctoObjectId, RtEntity> Stored { get; } = new();

        public Harness()
        {
            A.CallTo(() => Repository.TenantId).Returns(Tenant);
            A.CallTo(() => Repository.GetSessionAsync()).Returns(Task.FromResult(A.Fake<IOctoSession>()));
            A.CallTo(() => Repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                    A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
                .ReturnsLazily(call =>
                {
                    var ids = call.GetArgument<IReadOnlyList<OctoObjectId>>(2)!;
                    var items = ids.Where(Stored.ContainsKey).Select(id => Stored[id]).ToList();
                    return Task.FromResult<IResultSet<RtEntity>>(new ResultSet<RtEntity>(items, items.Count, null,
                        null));
                });

            // Derived rule type -> base rule type (inheritance chain for policy targets).
            var derived = BuildGraph(DerivedRuleType, RuleType);
            var derivedOut = (CkTypeGraph?)derived;
            A.CallTo(() => CkCache.TryGetRtCkType(Tenant, Rt(DerivedRuleType), out derivedOut)).Returns(true)
                .AssignsOutAndRefParameters(derived);
            var baseGraph = BuildGraph(RuleType, null);
            var baseOut = (CkTypeGraph?)baseGraph;
            A.CallTo(() => CkCache.TryGetCkType(Tenant, A<CkId<CkTypeId>>.That.Matches(c =>
                c.ElementId.SemanticVersionedFullName == "CategorizationRule"), out baseOut)).Returns(true)
                .AssignsOutAndRefParameters(baseGraph);
        }

        public RtEntity AddStored(string ckType, bool? locked, string? source = null)
        {
            var entity = new RtEntity(Rt(ckType), OctoObjectId.GenerateNewId());
            if (locked != null)
            {
                entity.SetAttributeRawValue("RtBlueprintLocked", locked);
            }

            if (source != null)
            {
                entity.SetAttributeRawValue("RtBlueprintSource", source);
            }

            Stored[entity.RtId] = entity;
            return entity;
        }

        public async Task<OperationResult> RunAsync(RtSecurityContext context, RtDataPolicyTable table,
            params IEntityUpdateInfo<RtEntity>[] updates)
        {
            var result = new OperationResult();
            await DataPermissionWriteGuard.CheckAsync(Repository, CkCache, AuditSink, Session, context, table,
                updates, [], result);
            return result;
        }

        public int BatchReads => Fake.GetCalls(Repository)
            .Count(c => c.Method.Name == nameof(IRuntimeRepository.GetRtEntitiesByIdAsync));

        public int AnyReads => Fake.GetCalls(Repository).Count(c =>
            c.Method.Name.StartsWith("GetRtEntit", StringComparison.Ordinal) ||
            c.Method.Name == nameof(IRuntimeRepository.GetSessionAsync));
    }

    private static CkTypeGraph BuildGraph(string rtId, string? baseRtId)
    {
        return new CkTypeGraph(
            new CkId<CkTypeId>(rtId.Replace("Test/", "Test-1.0.0/")),
            isAbstract: false, isFinal: false, isCollectionRoot: false, baseTypes: [],
            derivedFromCkTypeId: baseRtId == null ? null : new CkId<CkTypeId>(baseRtId.Replace("Test/", "Test-1.0.0/")),
            definingCollectionRootCkTypeId: null, derivedTypes: [], definedAttributes: [],
            allAttributes: new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(), indexes: [],
            associations: new CkGraphDirectedAssociations([]), description: "test",
            enableChangeStreamPreAndPostImages: false);
    }

    private static EntityUpdateInfo<RtEntity> Update(RtEntity stored, params (string, object?)[] attrs)
    {
        var payload = new RtEntity(stored.CkTypeId!, stored.RtId);
        foreach (var (name, value) in attrs)
        {
            payload.SetAttributeRawValue(name, value);
        }

        return EntityUpdateInfo<RtEntity>.CreateUpdate(new RtEntityId(stored.CkTypeId!, stored.RtId), payload);
    }

    private static EntityUpdateInfo<RtEntity> Delete(RtEntity stored) =>
        EntityUpdateInfo<RtEntity>.CreateDelete(new RtEntityId(stored.CkTypeId!, stored.RtId));

    private static EntityUpdateInfo<RtEntity> Insert(string ckType, params (string, object?)[] attrs)
    {
        var entity = new RtEntity(Rt(ckType), OctoObjectId.GenerateNewId());
        foreach (var (name, value) in attrs)
        {
            entity.SetAttributeRawValue(name, value);
        }

        return EntityUpdateInfo<RtEntity>.CreateInsert(entity);
    }

    [Fact]
    public async Task LockedEntity_UpdateAndDelete_AreRefusedWithStableMessage()
    {
        var h = new Harness();
        var locked = h.AddStored(RuleType, true, "Acc-1");

        var update = await h.RunAsync(User, Table(), Update(locked, ("Name", "x")));
        var delete = await h.RunAsync(User, Table(), Delete(locked));

        foreach (var result in new[] { update, delete })
        {
            var message = Assert.Single(result.Messages);
            Assert.Equal(MessageLevel.Error, message.MessageLevel);
            Assert.Equal(6384, message.MessageNumber);
            Assert.Equal(RtBlueprintLockProtectionNames.ForbiddenMessageNumber, message.MessageNumber);
            Assert.Contains("locked by blueprint", message.MessageText);
            Assert.Contains(locked.RtId.ToString(), message.MessageText);
            Assert.Contains(RuleType, message.MessageText);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task UnlockedOrAbsentFlag_UpdateAndDelete_AreAllowed(bool? flag)
    {
        var h = new Harness();
        var entity = h.AddStored(RuleType, flag);

        var result = await h.RunAsync(User, Table(), Update(entity, ("Name", "x")), Delete(h.AddStored(RuleType, flag)));

        Assert.Empty(result.Messages);
    }

    [Fact]
    public async Task UnlockedEntity_FormRoundTripOfProtectedValues_IsNotAChange()
    {
        var h = new Harness();
        var entity = h.AddStored(RuleType, false, "Acc-1");

        var result = await h.RunAsync(User, Table(),
            Update(entity, ("RtBlueprintLocked", false), ("RtBlueprintSource", "Acc-1"), ("Name", "x")));

        Assert.Empty(result.Messages);
    }

    [Fact]
    public async Task DerivedType_InheritsTheRestriction()
    {
        var h = new Harness();
        var locked = h.AddStored(DerivedRuleType, true);

        var result = await h.RunAsync(User, Table(), Update(locked, ("Name", "x")));

        Assert.Equal(6384, Assert.Single(result.Messages).MessageNumber);
    }

    [Fact]
    public async Task SystemCaller_IsNeverRestricted_AndNothingIsRead()
    {
        var h = new Harness();
        var locked = h.AddStored(RuleType, true);

        var result = await h.RunAsync(RtSecurityContext.System, Table(), Update(locked, ("RtBlueprintLocked", false)),
            Delete(locked));

        Assert.Empty(result.Messages);
        Assert.Equal(0, h.AnyReads);
    }

    [Fact]
    public async Task AuditOnlyPolicy_LetsTheChangeThroughAndPublishesOneAuditEvent()
    {
        var h = new Harness();
        var locked1 = h.AddStored(RuleType, true);
        var locked2 = h.AddStored(RuleType, true);

        var result = await h.RunAsync(User, Table(auditOnly: true), Update(locked1, ("Name", "x")), Delete(locked2));

        Assert.Empty(result.Messages);
        A.CallTo(() => h.AuditSink.PublishAsync(
                A<AuditEvent>.That.Matches(e => e.Category == "DataPermissions.BlueprintLockViolation"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task EnforcePolicy_WinsOverAuditOnlyPolicyOnSameType()
    {
        var h = new Harness();
        var locked = h.AddStored(RuleType, true);
        var table = new RtDataPolicyTable(
        [
            new RtDataPolicyRule("a", new HashSet<string> { RuleType }, [RtDataAction.Write], false, true,
                new HashSet<string> { "Accountant" }, true),
            new RtDataPolicyRule("b", new HashSet<string> { RuleType }, [RtDataAction.Write], false, false,
                new HashSet<string> { "Accountant" }, true)
        ]);

        var result = await h.RunAsync(User, table, Update(locked, ("Name", "x")));

        Assert.Equal(6384, Assert.Single(result.Messages).MessageNumber);
    }

    [Theory]
    [InlineData("RtBlueprintLocked", true)]
    [InlineData("RtBlueprintSource", "Meshmakers.Accounting-1")]
    [InlineData("RtBlueprintAppliedAt", "2026-10-10T10:00:00Z")]
    public async Task Insert_WithProtectedAttribute_IsRefused(string attribute, object value)
    {
        var h = new Harness();

        var result = await h.RunAsync(User, Table(), Insert(RuleType, (attribute, value), ("Name", "x")));

        var message = Assert.Single(result.Messages);
        Assert.Equal(6384, message.MessageNumber);
        Assert.Contains(attribute, message.MessageText);
        Assert.Equal(0, h.AnyReads);
    }

    [Fact]
    public async Task Insert_WithoutProtectedAttributes_OrWithLockedFalse_IsAllowedAndReadsNothing()
    {
        var h = new Harness();

        var result = await h.RunAsync(User, Table(), Insert(RuleType, ("Name", "x")),
            Insert(RuleType, ("RtBlueprintLocked", false)));

        Assert.Empty(result.Messages);
        Assert.Equal(0, h.AnyReads);
    }

    [Theory]
    [InlineData("RtBlueprintLocked", true)]
    [InlineData("RtBlueprintSource", "Other-1")]
    [InlineData("RtBlueprintAppliedAt", "2026-10-10T10:00:00Z")]
    public async Task Update_ChangingProtectedAttributeOfUnlockedEntity_IsRefused(string attribute, object value)
    {
        var h = new Harness();
        var entity = h.AddStored(RuleType, false, "Acc-1");

        var result = await h.RunAsync(User, Table(), Update(entity, (attribute, value)));

        Assert.Contains(attribute, Assert.Single(result.Messages).MessageText);
    }

    [Fact]
    public async Task Replace_DroppingTheBlueprintSource_IsRefused()
    {
        var h = new Harness();
        var entity = h.AddStored(RuleType, false, "Acc-1");
        var payload = new RtEntity(entity.CkTypeId!, entity.RtId);
        payload.SetAttributeRawValue("Name", "x");

        var result = await h.RunAsync(User, Table(),
            EntityUpdateInfo<RtEntity>.CreateReplace(new RtEntityId(entity.CkTypeId!, entity.RtId), payload));

        Assert.Contains("RtBlueprintSource", Assert.Single(result.Messages).MessageText);
    }

    [Fact]
    public async Task Batch_IsRejectedAtomically_OneLockedAmongManyUnlocked()
    {
        var h = new Harness();
        var updates = Enumerable.Range(0, 20).Select(_ => Update(h.AddStored(RuleType, false), ("Name", "x")))
            .Cast<IEntityUpdateInfo<RtEntity>>().ToList();
        var locked = h.AddStored(RuleType, true);
        updates.Add(Update(locked, ("Name", "x")));

        var result = await h.RunAsync(User, Table(), updates.ToArray());

        // The guard reports exactly the violating item; the caller throws on any error and applies nothing.
        Assert.True(result.HasErrors);
        var message = Assert.Single(result.Messages);
        Assert.Contains(locked.RtId.ToString(), message.MessageText);
    }

    [Fact]
    public async Task NonOptedInTypes_CostNoReadAtAll_For1000Entities()
    {
        var h = new Harness();
        var updates = Enumerable.Range(0, 1000)
            .Select(_ => Update(h.AddStored(OtherType, true), ("Name", "x"))).Cast<IEntityUpdateInfo<RtEntity>>()
            .ToArray();

        // Policy opts in the rule type only; the 1000 entities are of another type.
        var result = await h.RunAsync(User, Table(), updates);

        Assert.Empty(result.Messages);
        Assert.Equal(0, h.AnyReads);
    }

    [Fact]
    public async Task PolicyWithoutOptIn_CostsNoRead_EvenForLockedEntities()
    {
        var h = new Harness();
        var locked = h.AddStored(RuleType, true);

        var result = await h.RunAsync(User, Table(protect: false), Update(locked, ("Name", "x")), Delete(locked));

        Assert.Empty(result.Messages);
        Assert.Equal(0, h.AnyReads);
    }

    [Fact]
    public async Task OptedInType_1000Updates_UseExactlyOneBatchedRead_AndInsertsAreNotRead()
    {
        var h = new Harness();
        var updates = Enumerable.Range(0, 1000).Select(_ => Update(h.AddStored(RuleType, false), ("Name", "x")))
            .Cast<IEntityUpdateInfo<RtEntity>>().ToList();
        updates.Add(Insert(RuleType, ("Name", "new")));

        var result = await h.RunAsync(User, Table(), updates.ToArray());

        Assert.Empty(result.Messages);
        Assert.Equal(1, h.BatchReads);
        A.CallTo(() => h.Repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<IReadOnlyList<OctoObjectId>>.That.Matches(ids => ids.Count == 1000), A<RtEntityQueryOptions>._,
                A<int?>._, A<int?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task OptedInType_InsertsOnly_ReadNothing()
    {
        var h = new Harness();

        var result = await h.RunAsync(User, Table(), Insert(RuleType, ("Name", "a")), Insert(RuleType, ("Name", "b")));

        Assert.Empty(result.Messages);
        Assert.Equal(0, h.AnyReads);
    }

    [Fact]
    public async Task LockedEntity_IsFoundEvenWhenTheCallersReadFilterWouldHideIt()
    {
        // The lock read runs on a fresh system session, not on the caller's (read-filtered) session.
        var h = new Harness();
        var locked = h.AddStored(RuleType, true);

        await h.RunAsync(User, Table(), Delete(locked));

        A.CallTo(() => h.Repository.GetRtEntitiesByIdAsync(h.Session, A<RtCkId<CkTypeId>>._,
            A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._)).MustNotHaveHappened();
        A.CallTo(() => h.Repository.GetSessionAsync()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task MissingStoredEntity_IsLeftToTheNormalWritePath()
    {
        var h = new Harness();
        var ghost = new RtEntity(Rt(RuleType), OctoObjectId.GenerateNewId());

        var result = await h.RunAsync(User, Table(), Delete(ghost));

        Assert.Empty(result.Messages);
    }

    [Fact]
    public void Evaluator_OldPolicyWithoutAttribute_ReadsAsNotProtecting()
    {
        // Rule built the pre-AB#6384 way (flag defaults to false): no restriction, table has no lock rules.
        var table = new RtDataPolicyTable(
        [
            new RtDataPolicyRule("a", new HashSet<string> { RuleType }, [RtDataAction.Write], false, false,
                new HashSet<string> { "Accountant" })
        ]);

        Assert.False(table.HasBlueprintLockProtection);
        Assert.Equal(RtBlueprintLockProtection.None,
            RtDataAccessEvaluator.ClassifyBlueprintLockProtection(table, [RuleType], User));
    }

    [Fact]
    public void Evaluator_ClassifiesEnforceAuditOnlyAndSystem()
    {
        Assert.Equal(RtBlueprintLockProtection.Enforce,
            RtDataAccessEvaluator.ClassifyBlueprintLockProtection(Table(), [DerivedRuleType, RuleType], User));
        Assert.Equal(RtBlueprintLockProtection.AuditOnly,
            RtDataAccessEvaluator.ClassifyBlueprintLockProtection(Table(auditOnly: true), [RuleType], User));
        Assert.Equal(RtBlueprintLockProtection.None,
            RtDataAccessEvaluator.ClassifyBlueprintLockProtection(Table(), [OtherType], User));
        Assert.Equal(RtBlueprintLockProtection.None,
            RtDataAccessEvaluator.ClassifyBlueprintLockProtection(Table(), [RuleType], RtSecurityContext.System));
    }
}
