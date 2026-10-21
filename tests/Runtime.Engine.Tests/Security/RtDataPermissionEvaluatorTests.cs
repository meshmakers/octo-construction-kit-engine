using FakeItEasy;

using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Security;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     AB#7035 — the public data-permission check: matrix over caller x entity x action, inheritance via base
///     types, owned-only (not found = not visible), AuditOnly (flag, optional event), blueprint lock, type-level
///     calls, read budget, and agreement with <see cref="DataPermissionWriteGuard" /> for the same inputs.
/// </summary>
public class RtDataPermissionEvaluatorTests
{
    private const string Tenant = "tenant1";
    private const string DocType = "Test/Document";
    private const string DerivedDocType = "Test/SpecialDocument";
    private const string OpenType = "Test/Contact";

    private static readonly RtSecurityContext Manager = RtSecurityContext.ForUser("user-m", ["Manager"]);
    private static readonly RtSecurityContext Employee = RtSecurityContext.ForUser("user-1", ["Employee"]);
    private static readonly RtSecurityContext Reader = RtSecurityContext.ForUser("user-r", ["Reader"]);
    private static readonly RtSecurityContext Writer = RtSecurityContext.ForUser("user-w", ["Writer"]);
    private static readonly RtSecurityContext Outsider = RtSecurityContext.ForUser("user-x", ["Other"]);
    private static readonly RtSecurityContext NoSubject = RtSecurityContext.ForUser(null, ["Employee"]);

    private static RtCkId<CkTypeId> Rt(string id) => new(id);

    // Manager: full read/write/delete. Employee: owned-only read+write (no delete). Reader: full read only.
    // Writer: full write, owned-only read.
    private static RtDataPolicyTable Table(bool auditOnly = false, string target = DocType,
        bool protectBlueprintLocked = false)
    {
        return new RtDataPolicyTable(
        [
            new RtDataPolicyRule("docs", new HashSet<string> { target },
                [RtDataAction.Read, RtDataAction.Write, RtDataAction.Delete], false, auditOnly,
                new HashSet<string> { "Manager" }, protectBlueprintLocked),
            new RtDataPolicyRule("docs", new HashSet<string> { target },
                [RtDataAction.Read, RtDataAction.Write], true, auditOnly, new HashSet<string> { "Employee" }),
            new RtDataPolicyRule("docs", new HashSet<string> { target },
                [RtDataAction.Read], false, auditOnly, new HashSet<string> { "Reader" }),
            new RtDataPolicyRule("docs", new HashSet<string> { target },
                [RtDataAction.Write], false, auditOnly, new HashSet<string> { "Writer" }),
            new RtDataPolicyRule("docs", new HashSet<string> { target },
                [RtDataAction.Read], true, auditOnly, new HashSet<string> { "Writer" })
        ]);
    }

    private sealed class Harness
    {
        public IRuntimeRepository Repository { get; } = A.Fake<IRuntimeRepository>();
        public ICkCacheService CkCache { get; } = A.Fake<ICkCacheService>();
        public IAuditEventSink AuditSink { get; } = A.Fake<IAuditEventSink>();
        public IDataPermissionResolver Resolver { get; } = A.Fake<IDataPermissionResolver>();
        public Dictionary<OctoObjectId, RtEntity> Stored { get; } = new();

        public Harness(RtDataPolicyTable table)
        {
            A.CallTo(() => Repository.TenantId).Returns(Tenant);
            A.CallTo(() => Repository.GetSessionAsync()).ReturnsLazily(() => Task.FromResult(A.Fake<IOctoSession>()));
            A.CallTo(() => Resolver.GetPolicyTableAsync(Repository)).Returns(Task.FromResult(table));
            A.CallTo(() => Repository.GetRtEntityByRtIdAsync(A<IOctoSession>._, A<RtEntityId>._))
                .ReturnsLazily(call =>
                {
                    var id = call.GetArgument<RtEntityId>(1);
                    return Task.FromResult(Stored.TryGetValue(id.RtId, out var e) ? e : null);
                });

            var derived = BuildGraph(DerivedDocType, DocType);
            var derivedOut = (CkTypeGraph?)derived;
            A.CallTo(() => CkCache.TryGetRtCkType(Tenant, Rt(DerivedDocType), out derivedOut)).Returns(true)
                .AssignsOutAndRefParameters(derived);
            var baseGraph = BuildGraph(DocType, null);
            var baseOut = (CkTypeGraph?)baseGraph;
            A.CallTo(() => CkCache.TryGetCkType(Tenant, A<CkId<CkTypeId>>.That.Matches(c =>
                c.ElementId.SemanticVersionedFullName == "Document"), out baseOut)).Returns(true)
                .AssignsOutAndRefParameters(baseGraph);

            Evaluator = new RtDataPermissionEvaluator(Resolver, CkCache, AuditSink);
        }

        public RtDataPermissionEvaluator Evaluator { get; }

        public RtEntity Add(string type, string? createdBy, bool? locked = null)
        {
            var entity = new RtEntity(Rt(type), OctoObjectId.GenerateNewId()) { RtCreatedBy = createdBy };
            if (locked != null)
            {
                entity.SetAttributeRawValue("RtBlueprintLocked", locked);
            }

            Stored[entity.RtId] = entity;
            return entity;
        }

        public Task<RtDataPermissionVerdict> EvalAsync(RtSecurityContext caller, RtEntity entity,
            RtDataAction action, bool publishAudit = false)
        {
            return Evaluator.EvaluateAsync(Repository, caller, new RtEntityId(entity.CkTypeId!, entity.RtId),
                action, publishAudit, TestContext.Current.CancellationToken);
        }

        public async Task<bool> GuardAllowsAsync(RtSecurityContext caller, RtDataPolicyTable table, RtEntity entity,
            RtDataAction action)
        {
            var id = new RtEntityId(entity.CkTypeId!, entity.RtId);
            IEntityUpdateInfo<RtEntity> update = action == RtDataAction.Delete
                ? EntityUpdateInfo<RtEntity>.CreateDelete(id)
                : EntityUpdateInfo<RtEntity>.CreateUpdate(id, new RtEntity(entity.CkTypeId!, entity.RtId));
            var result = new OperationResult();
            await DataPermissionWriteGuard.CheckAsync(Repository, CkCache, null, A.Fake<IOctoSession>(), caller,
                table, [update], [], result);
            return !result.Messages.Any(m => m.MessageLevel == MessageLevel.Error);
        }

        public int EntityReads => Fake.GetCalls(Repository)
            .Count(c => c.Method.Name == nameof(IRuntimeRepository.GetRtEntityByRtIdAsync));
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

    private const RtDataPermissionDecision Ok = RtDataPermissionDecision.Allowed;
    private const RtDataPermissionDecision Hidden = RtDataPermissionDecision.NotVisible;
    private const RtDataPermissionDecision Refused = RtDataPermissionDecision.NotPermitted;

    // (caller, entity owner, action) -> (decision, reason). Owner "user-1" = Employee's own entity.
    public static TheoryData<string, string, RtDataAction, RtDataPermissionDecision, RtDataPermissionReason> Matrix()
    {
        var d = new TheoryData<string, string, RtDataAction, RtDataPermissionDecision, RtDataPermissionReason>();
        foreach (var action in Enum.GetValues<RtDataAction>())
        {
            d.Add("Manager", "user-1", action, Ok, RtDataPermissionReason.Granted);
            d.Add("Manager", "user-m", action, Ok, RtDataPermissionReason.Granted);
            d.Add("Outsider", "user-1", action, Hidden, RtDataPermissionReason.NoGrant);
            d.Add("Employee", "user-2", action, Hidden, RtDataPermissionReason.NotOwner);
            d.Add("NoSubject", "user-1", action, Hidden, RtDataPermissionReason.NoGrant);
        }

        d.Add("Employee", "user-1", RtDataAction.Read, Ok, RtDataPermissionReason.GrantedAsOwner);
        d.Add("Employee", "user-1", RtDataAction.Write, Ok, RtDataPermissionReason.GrantedAsOwner);
        d.Add("Employee", "user-1", RtDataAction.Delete, Refused, RtDataPermissionReason.NoGrant);
        d.Add("Reader", "user-1", RtDataAction.Read, Ok, RtDataPermissionReason.Granted);
        d.Add("Reader", "user-1", RtDataAction.Write, Refused, RtDataPermissionReason.NoGrant);
        d.Add("Reader", "user-1", RtDataAction.Delete, Refused, RtDataPermissionReason.NoGrant);
        // Full write grant but owned-only read: a foreign entity is invisible, an own one is writable.
        d.Add("Writer", "user-1", RtDataAction.Read, Hidden, RtDataPermissionReason.NotOwner);
        d.Add("Writer", "user-1", RtDataAction.Write, Hidden, RtDataPermissionReason.NotOwner);
        d.Add("Writer", "user-w", RtDataAction.Read, Ok, RtDataPermissionReason.GrantedAsOwner);
        d.Add("Writer", "user-w", RtDataAction.Write, Ok, RtDataPermissionReason.Granted);
        d.Add("Writer", "user-w", RtDataAction.Delete, Refused, RtDataPermissionReason.NoGrant);
        return d;
    }

    private static RtSecurityContext Caller(string name) => name switch
    {
        "Manager" => Manager,
        "Employee" => Employee,
        "Reader" => Reader,
        "Writer" => Writer,
        "NoSubject" => NoSubject,
        _ => Outsider
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Entity_Matrix(string caller, string owner, RtDataAction action,
        RtDataPermissionDecision decision, RtDataPermissionReason reason)
    {
        foreach (var type in new[] { DocType, DerivedDocType }) // derived type inherits the base-type policy
        {
            var h = new Harness(Table());
            var entity = h.Add(type, owner);

            var verdict = await h.EvalAsync(Caller(caller), entity, action);

            Assert.Equal(decision, verdict.Decision);
            Assert.Equal(reason, verdict.Reason);
            Assert.False(verdict.AuditOnlyViolation);
        }
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Entity_AgreesWithWriteGuard(string caller, string owner, RtDataAction action,
        RtDataPermissionDecision decision, RtDataPermissionReason reason)
    {
        _ = decision;
        _ = reason;
        if (action == RtDataAction.Read)
        {
            return; // the guard only covers writes and deletes; reads are filtered (mongodb differential test)
        }

        var table = Table();
        var h = new Harness(table);
        var entity = h.Add(DocType, owner);
        var verdict = await h.EvalAsync(Caller(caller), entity, action);
        var guardAllows = await h.GuardAllowsAsync(Caller(caller), table, entity, action);

        // Guard permits <=> evaluator Allowed, for every cell including the entities the caller cannot see:
        // the guard applies the same read visibility first (AB#7126), so NotVisible is refused there too.
        Assert.Equal(guardAllows, verdict.IsAllowed);
    }

    [Fact]
    public async Task UnprotectedType_IsOpen_AndReadsNothing()
    {
        var h = new Harness(Table());
        var contact = h.Add(OpenType, "user-1");

        foreach (var action in Enum.GetValues<RtDataAction>())
        {
            var verdict = await h.EvalAsync(Outsider, contact, action);
            Assert.Equal(new RtDataPermissionVerdict(Ok, RtDataPermissionReason.Open), verdict);
        }

        Assert.Equal(0, h.EntityReads);
    }

    [Fact]
    public async Task SystemCaller_And_EmptyTable_AreOpen_WithoutResolvingOrReading()
    {
        var h = new Harness(Table());
        var doc = h.Add(DocType, "user-1");
        var system = await h.EvalAsync(RtSecurityContext.System, doc, RtDataAction.Delete);
        Assert.Equal(RtDataPermissionReason.Open, system.Reason);
        Assert.True(system.IsAllowed);
        A.CallTo(() => h.Resolver.GetPolicyTableAsync(A<IRuntimeRepository>._)).MustNotHaveHappened();

        var empty = new Harness(RtDataPolicyTable.Empty);
        var doc2 = empty.Add(DocType, "user-1");
        Assert.True((await empty.EvalAsync(Outsider, doc2, RtDataAction.Write)).IsAllowed);
        Assert.Equal(0, empty.EntityReads);
    }

    [Fact]
    public async Task MissingEntity_UnderOwnedOnly_IsNotVisible_IndistinguishableFromForeign()
    {
        var h = new Harness(Table());
        var ghost = new RtEntity(Rt(DocType), OctoObjectId.GenerateNewId());

        var missing = await h.EvalAsync(Employee, ghost, RtDataAction.Write);
        Assert.Equal(Hidden, missing.Decision);
        Assert.Equal(RtDataPermissionReason.EntityNotFound, missing.Reason);

        // A caller with full grants needs no stored entity: the verdict is the pure type verdict.
        Assert.True((await h.EvalAsync(Manager, ghost, RtDataAction.Write)).IsAllowed);
        Assert.Equal(1, h.EntityReads);
    }

    [Fact]
    public async Task OwnedOnly_WithoutSubject_FailsClosed()
    {
        var h = new Harness(Table());
        var doc = h.Add(DocType, null); // no stamped creator either

        var verdict = await h.EvalAsync(NoSubject, doc, RtDataAction.Write);
        Assert.Equal(Hidden, verdict.Decision);

        var noOwner = await h.EvalAsync(Employee, doc, RtDataAction.Write);
        Assert.Equal(Hidden, noOwner.Decision);
        Assert.Equal(RtDataPermissionReason.NotOwner, noOwner.Reason);
    }

    [Fact]
    public async Task EntityIsReadOnlyWhenTheVerdictDependsOnIt()
    {
        var h = new Harness(Table());
        var doc = h.Add(DocType, "user-1");

        await h.EvalAsync(Manager, doc, RtDataAction.Write);
        await h.EvalAsync(Reader, doc, RtDataAction.Read);
        await h.EvalAsync(Outsider, doc, RtDataAction.Read);
        Assert.Equal(0, h.EntityReads);

        await h.EvalAsync(Employee, doc, RtDataAction.Read);
        Assert.Equal(1, h.EntityReads);
    }

    [Fact]
    public async Task PublicCall_IsSideEffectFree_UnlessAuditRequested()
    {
        var table = Table(auditOnly: true);
        var h = new Harness(table);
        var doc = h.Add(DocType, "user-1");

        var write = await h.EvalAsync(Outsider, doc, RtDataAction.Write);
        var read = await h.EvalAsync(Outsider, doc, RtDataAction.Read);

        Assert.Equal(new RtDataPermissionVerdict(Ok, RtDataPermissionReason.AuditOnly, true), write);
        Assert.Equal(new RtDataPermissionVerdict(Ok, RtDataPermissionReason.AuditOnly, true), read);
        A.CallTo(() => h.AuditSink.PublishAsync(A<AuditEvent>._)).MustNotHaveHappened();

        await h.EvalAsync(Outsider, doc, RtDataAction.Write, publishAudit: true);
        await h.EvalAsync(Outsider, doc, RtDataAction.Read, publishAudit: true);
        A.CallTo(() => h.AuditSink.PublishAsync(A<AuditEvent>.That.Matches(e =>
            e.Category == "DataPermissions.WriteViolation"))).MustHaveHappenedOnceExactly();
        A.CallTo(() => h.AuditSink.PublishAsync(A<AuditEvent>.That.Matches(e =>
            e.Category == "DataPermissions.ReadViolation"))).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task AuditOnly_ForGrantedCaller_HasNoViolation()
    {
        var h = new Harness(Table(auditOnly: true));
        var doc = h.Add(DocType, "user-1");

        var verdict = await h.EvalAsync(Manager, doc, RtDataAction.Write, publishAudit: true);

        Assert.Equal(new RtDataPermissionVerdict(Ok, RtDataPermissionReason.Open), verdict);
        A.CallTo(() => h.AuditSink.PublishAsync(A<AuditEvent>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task BlueprintLock_Enforce_RefusesLockedEntityOnly_ForWriteAndDelete()
    {
        var h = new Harness(Table(protectBlueprintLocked: true));
        var locked = h.Add(DocType, "user-1", locked: true);
        var unlocked = h.Add(DocType, "user-1", locked: false);

        foreach (var action in new[] { RtDataAction.Write, RtDataAction.Delete })
        {
            var refused = await h.EvalAsync(Manager, locked, action);
            Assert.Equal(new RtDataPermissionVerdict(Refused, RtDataPermissionReason.BlueprintLocked), refused);
            Assert.True((await h.EvalAsync(Manager, unlocked, action)).IsAllowed);
        }

        // Reading a locked entity stays allowed.
        Assert.True((await h.EvalAsync(Manager, locked, RtDataAction.Read)).IsAllowed);
    }

    [Fact]
    public async Task BlueprintLock_AuditOnlyPolicy_AllowsWithFlag()
    {
        var h = new Harness(Table(auditOnly: true, protectBlueprintLocked: true));
        var locked = h.Add(DocType, "user-1", locked: true);

        var verdict = await h.EvalAsync(Manager, locked, RtDataAction.Write, publishAudit: true);

        Assert.True(verdict.IsAllowed);
        Assert.True(verdict.AuditOnlyViolation);
        A.CallTo(() => h.AuditSink.PublishAsync(A<AuditEvent>.That.Matches(e =>
            e.Category == "DataPermissions.BlueprintLockViolation"))).MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData("Manager", RtDataAction.Read, Ok, RtDataPermissionReason.Granted)]
    [InlineData("Manager", RtDataAction.Delete, Ok, RtDataPermissionReason.Granted)]
    [InlineData("Employee", RtDataAction.Read, Ok, RtDataPermissionReason.GrantedAsOwner)]
    [InlineData("Employee", RtDataAction.Write, Ok, RtDataPermissionReason.GrantedAsOwner)]
    [InlineData("Employee", RtDataAction.Delete, Refused, RtDataPermissionReason.NoGrant)]
    [InlineData("Reader", RtDataAction.Write, Refused, RtDataPermissionReason.NoGrant)]
    [InlineData("Outsider", RtDataAction.Read, Hidden, RtDataPermissionReason.NoGrant)]
    [InlineData("Outsider", RtDataAction.Write, Refused, RtDataPermissionReason.NoGrant)]
    public async Task TypeLevel_Matrix(string caller, RtDataAction action, RtDataPermissionDecision decision,
        RtDataPermissionReason reason)
    {
        var h = new Harness(Table());

        var verdict = await h.Evaluator.EvaluateTypeAsync(h.Repository, Caller(caller), Rt(DerivedDocType), action,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(decision, verdict.Decision);
        Assert.Equal(reason, verdict.Reason);
        Assert.Equal(0, h.EntityReads);
    }

    [Fact]
    public async Task TypeLevel_UnprotectedAndSystem_AreOpen_AndAuditOnlyFlags()
    {
        var h = new Harness(Table());
        Assert.Equal(RtDataPermissionReason.Open,
            (await h.Evaluator.EvaluateTypeAsync(h.Repository, Outsider, Rt(OpenType), RtDataAction.Write,
                cancellationToken: TestContext.Current.CancellationToken)).Reason);
        Assert.True((await h.Evaluator.EvaluateTypeAsync(h.Repository, RtSecurityContext.System, Rt(DocType),
            RtDataAction.Delete, cancellationToken: TestContext.Current.CancellationToken)).IsAllowed);

        var audit = new Harness(Table(auditOnly: true));
        var verdict = await audit.Evaluator.EvaluateTypeAsync(audit.Repository, Outsider, Rt(DocType),
            RtDataAction.Write, publishAudit: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(verdict.AuditOnlyViolation);
        A.CallTo(() => audit.AuditSink.PublishAsync(A<AuditEvent>.That.Matches(e =>
            e.Category == "DataPermissions.WriteViolation"))).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task CancelledToken_Throws()
    {
        var h = new Harness(Table());
        var doc = h.Add(DocType, "user-1");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => h.Evaluator.EvaluateAsync(h.Repository, Manager,
            new RtEntityId(doc.CkTypeId!, doc.RtId), RtDataAction.Read, cancellationToken: cts.Token));
    }
}
