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
///     AB#7126 — the write guard applies the read visibility of the shared rules before the write/delete
///     decision for update, replace, delete and association changes (not for inserts): an entity the caller
///     cannot see is refused as if it did not exist, with no hint that it exists. AuditOnly policies audit and
///     never refuse; system callers and tenants without policies are unchanged.
/// </summary>
public class DataPermissionWriteGuardVisibilityTests
{
    private const string Tenant = "tenant1";
    private const string DocType = "Test/Document";
    private const string OpenType = "Test/Contact";

    private static readonly RtSecurityContext WriterOwnedRead = RtSecurityContext.ForUser("user-w", ["WriterOwnedRead"]);
    private static readonly RtSecurityContext WriteOnly = RtSecurityContext.ForUser("user-n", ["WriteOnly"]);
    private static readonly RtSecurityContext Full = RtSecurityContext.ForUser("user-f", ["Full"]);
    private static readonly RtSecurityContext AuditedWriter = RtSecurityContext.ForUser("user-a", ["Audited"]);

    private static RtCkId<CkTypeId> Rt(string id) => new(id);

    // The repro of AB#7126: P1 = Write+Delete all, P2 = Read owned-only, role holds both.
    // WriteOnly: Write+Delete without any Read grant. Full: Read+Write+Delete all.
    private static RtDataPolicyTable Table(bool withAuditOnlyPolicies = false)
    {
        if (withAuditOnlyPolicies)
        {
            // Only AuditOnly policies on the type: open in the enforcement view. The would-be policy of the
            // "Audited" role grants Write+Delete on all records but Read on its own records only.
            return new RtDataPolicyTable(
            [
                new RtDataPolicyRule("p4", new HashSet<string> { DocType }, [RtDataAction.Write, RtDataAction.Delete],
                    false, true, new HashSet<string> { "Audited" }),
                new RtDataPolicyRule("p5", new HashSet<string> { DocType }, [RtDataAction.Read], true, true,
                    new HashSet<string> { "Audited" })
            ]);
        }

        var rules = new List<RtDataPolicyRule>
        {
            new("p1", new HashSet<string> { DocType }, [RtDataAction.Write, RtDataAction.Delete], false, false,
                new HashSet<string> { "WriterOwnedRead", "WriteOnly" }),
            new("p2", new HashSet<string> { DocType }, [RtDataAction.Read], true, false,
                new HashSet<string> { "WriterOwnedRead" }),
            new("p3", new HashSet<string> { DocType }, [RtDataAction.Read, RtDataAction.Write, RtDataAction.Delete],
                false, false, new HashSet<string> { "Full" })
        };

        return new RtDataPolicyTable(rules);
    }

    private sealed class Harness
    {
        public IRuntimeRepository Repository { get; } = A.Fake<IRuntimeRepository>();
        public ICkCacheService CkCache { get; } = A.Fake<ICkCacheService>();
        public IAuditEventSink AuditSink { get; } = A.Fake<IAuditEventSink>();
        public Dictionary<OctoObjectId, RtEntity> Stored { get; } = new();

        public Harness()
        {
            A.CallTo(() => Repository.TenantId).Returns(Tenant);
            A.CallTo(() => Repository.GetRtEntityByRtIdAsync(A<IOctoSession>._, A<RtEntityId>._))
                .ReturnsLazily(call =>
                {
                    var id = call.GetArgument<RtEntityId>(1);
                    return Task.FromResult(Stored.TryGetValue(id.RtId, out var e) ? e : null);
                });
            var baseGraph = BuildGraph(DocType);
            var baseOut = (CkTypeGraph?)baseGraph;
            A.CallTo(() => CkCache.TryGetCkType(Tenant, A<CkId<CkTypeId>>.That.Matches(c =>
                c.ElementId.SemanticVersionedFullName == "Document"), out baseOut)).Returns(true)
                .AssignsOutAndRefParameters(baseGraph);
        }

        public RtEntity Add(string type, string? createdBy)
        {
            var entity = new RtEntity(Rt(type), OctoObjectId.GenerateNewId()) { RtCreatedBy = createdBy };
            Stored[entity.RtId] = entity;
            return entity;
        }

        public async Task<OperationResult> RunAsync(RtSecurityContext caller, RtDataPolicyTable table,
            IReadOnlyList<IEntityUpdateInfo<RtEntity>> updates, IReadOnlyList<AssociationUpdateInfo>? associations = null)
        {
            var result = new OperationResult();
            await DataPermissionWriteGuard.CheckAsync(Repository, CkCache, AuditSink, A.Fake<IOctoSession>(), caller,
                table, updates, associations ?? [], result);
            return result;
        }

        public int EntityReads => Fake.GetCalls(Repository)
            .Count(c => c.Method.Name == nameof(IRuntimeRepository.GetRtEntityByRtIdAsync));

        public int AuditEvents => Fake.GetCalls(AuditSink)
            .Count(c => c.Method.Name == nameof(IAuditEventSink.PublishAsync));
    }

    private static CkTypeGraph BuildGraph(string rtId)
    {
        return new CkTypeGraph(
            new CkId<CkTypeId>(rtId.Replace("Test/", "Test-1.0.0/")),
            isAbstract: false, isFinal: false, isCollectionRoot: false, baseTypes: [],
            derivedFromCkTypeId: null, definingCollectionRootCkTypeId: null, derivedTypes: [], definedAttributes: [],
            allAttributes: new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>(), indexes: [],
            associations: new CkGraphDirectedAssociations([]), description: "test",
            enableChangeStreamPreAndPostImages: false);
    }

    private static EntityUpdateInfo<RtEntity> Update(RtEntity stored) =>
        EntityUpdateInfo<RtEntity>.CreateUpdate(new RtEntityId(stored.CkTypeId!, stored.RtId),
            new RtEntity(stored.CkTypeId!, stored.RtId));

    private static EntityUpdateInfo<RtEntity> Replace(RtEntity stored) =>
        EntityUpdateInfo<RtEntity>.CreateReplace(new RtEntityId(stored.CkTypeId!, stored.RtId),
            new RtEntity(stored.CkTypeId!, stored.RtId));

    private static EntityUpdateInfo<RtEntity> Delete(RtEntity stored) =>
        EntityUpdateInfo<RtEntity>.CreateDelete(new RtEntityId(stored.CkTypeId!, stored.RtId));

    private static EntityUpdateInfo<RtEntity> Insert() =>
        EntityUpdateInfo<RtEntity>.CreateInsert(new RtEntity(Rt(DocType), OctoObjectId.GenerateNewId()));

    private static void AssertRefusedAsNotFound(OperationResult result, RtEntity entity)
    {
        var message = Assert.Single(result.Messages);
        Assert.Equal(MessageLevel.Error, message.MessageLevel);
        Assert.Contains("not found", message.MessageText);
        Assert.DoesNotContain("Access denied", message.MessageText);
        Assert.DoesNotContain("permission", message.MessageText);
        Assert.Contains(entity.RtId.ToString(), message.MessageText);
    }

    public static TheoryData<string> Actions() => ["update", "replace", "delete"];

    private static IEntityUpdateInfo<RtEntity> Change(string action, RtEntity entity) => action switch
    {
        "update" => Update(entity),
        "replace" => Replace(entity),
        _ => Delete(entity)
    };

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Repro_WriteAllReadOwnedOnly_ForeignRecord_IsRefusedAsNotFound(string action)
    {
        var h = new Harness();
        var foreign = h.Add(DocType, "user-a-creator");

        var result = await h.RunAsync(WriterOwnedRead, Table(), [Change(action, foreign)]);

        AssertRefusedAsNotFound(result, foreign);
        Assert.Equal(0, h.AuditEvents);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Repro_WriteAllReadOwnedOnly_OwnRecord_IsAllowed(string action)
    {
        var h = new Harness();
        var own = h.Add(DocType, "user-w");

        var result = await h.RunAsync(WriterOwnedRead, Table(), [Change(action, own)]);

        Assert.Empty(result.Messages);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task NoReadGrant_ExistingRecord_IsRefusedAsNotFound_WithoutReadingIt(string action)
    {
        var h = new Harness();
        var existing = h.Add(DocType, "user-n");

        var result = await h.RunAsync(WriteOnly, Table(), [Change(action, existing)]);

        AssertRefusedAsNotFound(result, existing);
        Assert.Equal(0, h.EntityReads); // no read grant: invisible whether or not it exists, nothing to read
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task NoReadGrant_IdWithoutStoredEntity_IsAnsweredLikeAnInvisibleOne(string action)
    {
        // A missing id and a hidden one are the same "not found" for a caller who may not read the type:
        // the answer must not reveal which of the two it is.
        var h = new Harness();
        var ghost = new RtEntity(Rt(DocType), OctoObjectId.GenerateNewId());

        var result = await h.RunAsync(WriteOnly, Table(), [Change(action, ghost)]);

        AssertRefusedAsNotFound(result, ghost);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task FullReadAndWrite_ForeignRecord_IsAllowed_WithoutReadingIt(string action)
    {
        var h = new Harness();
        var foreign = h.Add(DocType, "someone-else");

        var result = await h.RunAsync(Full, Table(), [Change(action, foreign)]);

        Assert.Empty(result.Messages);
        Assert.Equal(0, h.EntityReads);
    }

    [Fact]
    public async Task OwnedOnlyRead_IdWithoutStoredEntity_IsAnsweredLikeAnInvisibleOne()
    {
        var h = new Harness();
        var ghost = new RtEntity(Rt(DocType), OctoObjectId.GenerateNewId());

        var result = await h.RunAsync(WriterOwnedRead, Table(), [Update(ghost)]);

        AssertRefusedAsNotFound(result, ghost);
    }

    [Fact]
    public async Task FullRead_IdWithoutStoredEntity_IsLeftToTheNormalWritePath()
    {
        var h = new Harness();
        var ghost = new RtEntity(Rt(DocType), OctoObjectId.GenerateNewId());

        var result = await h.RunAsync(Full, Table(), [Update(ghost)]);

        Assert.Empty(result.Messages);
    }

    [Fact]
    public async Task OneInvisibleRecordAmongVisibleOnes_RejectsTheWholeSet_AsNotFound()
    {
        var h = new Harness();
        var own = h.Add(DocType, "user-w");
        var foreign = h.Add(DocType, "user-x");

        var result = await h.RunAsync(WriterOwnedRead, Table(), [Update(own), Delete(foreign)]);

        AssertRefusedAsNotFound(result, foreign);
    }

    [Fact]
    public async Task InvisibleRecord_IsAnsweredAsNotFound_NotAsMissingWritePermission()
    {
        // Foreign record, caller has no Write grant either: visibility is answered first, so the answer does
        // not reveal that the record exists (a "missing Write permission" would).
        var h = new Harness();
        var readerRules = new RtDataPolicyTable(
        [
            new RtDataPolicyRule("p", new HashSet<string> { DocType }, [RtDataAction.Read], true, false,
                new HashSet<string> { "ReaderOwned" })
        ]);
        var foreign = h.Add(DocType, "someone-else");

        var result = await h.RunAsync(RtSecurityContext.ForUser("user-r", ["ReaderOwned"]), readerRules,
            [Update(foreign)]);

        AssertRefusedAsNotFound(result, foreign);
    }

    [Fact]
    public async Task OwnedOnlyWrite_VisibleForeignRecord_StillAnswersAccessDenied()
    {
        // Read all but Write owned-only: the foreign record is visible, so the write refusal stays an
        // honest "access denied".
        var h = new Harness();
        var table = new RtDataPolicyTable(
        [
            new RtDataPolicyRule("p", new HashSet<string> { DocType }, [RtDataAction.Read], false, false,
                new HashSet<string> { "Mixed" }),
            new RtDataPolicyRule("q", new HashSet<string> { DocType }, [RtDataAction.Write], true, false,
                new HashSet<string> { "Mixed" })
        ]);
        var foreign = h.Add(DocType, "someone-else");

        var result = await h.RunAsync(RtSecurityContext.ForUser("user-m", ["Mixed"]), table, [Update(foreign)]);

        var message = Assert.Single(result.Messages);
        Assert.Contains("Access denied", message.MessageText);
    }

    [Fact]
    public async Task Association_OriginNotVisible_IsRefusedAsNotFound()
    {
        var h = new Harness();
        var foreignOrigin = h.Add(DocType, "someone-else");
        var target = h.Add(DocType, "user-w");
        var association = AssociationUpdateInfo.CreateInsert(new RtEntityId(foreignOrigin.CkTypeId!, foreignOrigin.RtId),
            new RtEntityId(target.CkTypeId!, target.RtId), new RtCkId<CkAssociationRoleId>("Test/Relates"));

        var result = await h.RunAsync(WriterOwnedRead, Table(), [], [association]);

        AssertRefusedAsNotFound(result, foreignOrigin);
    }

    [Fact]
    public async Task Association_OriginVisible_IsAllowed()
    {
        var h = new Harness();
        var ownOrigin = h.Add(DocType, "user-w");
        var foreignTarget = h.Add(DocType, "someone-else");
        var association = AssociationUpdateInfo.CreateInsert(new RtEntityId(ownOrigin.CkTypeId!, ownOrigin.RtId),
            new RtEntityId(foreignTarget.CkTypeId!, foreignTarget.RtId), new RtCkId<CkAssociationRoleId>("Test/Relates"));

        var result = await h.RunAsync(WriterOwnedRead, Table(), [], [association]);

        Assert.Empty(result.Messages);
    }

    [Fact]
    public async Task SystemCaller_EmptyTable_AndUnprotectedType_AreUnchanged()
    {
        var h = new Harness();
        var foreign = h.Add(DocType, "someone-else");
        var contact = h.Add(OpenType, "someone-else");

        Assert.Empty((await h.RunAsync(RtSecurityContext.System, Table(), [Delete(foreign)])).Messages);
        Assert.Empty((await h.RunAsync(WriteOnly, RtDataPolicyTable.Empty, [Delete(foreign)])).Messages);
        Assert.Empty((await h.RunAsync(WriteOnly, Table(), [Delete(contact)])).Messages);
        Assert.Equal(0, h.EntityReads);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task AuditOnlyReadPolicy_ForeignRecord_IsAllowed_AndAuditsOnce(string action)
    {
        // "Audited" holds only AuditOnly policies: the type is open in the enforcement view. The would-be read
        // policy is owned-only, the record is foreign -> exactly one audit event, no refusal.
        var h = new Harness();
        var foreign = h.Add(DocType, "someone-else");

        var result = await h.RunAsync(AuditedWriter, Table(withAuditOnlyPolicies: true), [Change(action, foreign)]);

        Assert.Empty(result.Messages);
        Assert.Equal(1, h.AuditEvents);
    }

    [Fact]
    public async Task AuditOnlyReadPolicy_OwnRecord_NeitherRefusesNorAudits()
    {
        var h = new Harness();
        var own = h.Add(DocType, "user-a");

        var result = await h.RunAsync(AuditedWriter, Table(withAuditOnlyPolicies: true), [Update(own)]);

        Assert.Empty(result.Messages);
        Assert.Equal(0, h.AuditEvents);
    }
}
