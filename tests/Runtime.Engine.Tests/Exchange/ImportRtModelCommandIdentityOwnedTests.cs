using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Exchange;
using Meshmakers.Octo.Runtime.Engine.Repositories.Query;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
///     AB#8042 / AB#8053 - a user-initiated <c>ImportRt</c> (caller path) cannot set an edge of an identity association role
///     (<see cref="IdentityOwnedWrites" />); the whole file is refused before the first write with one
///     <see cref="ExchangeException" /> carrying message 140 and, per offender, rtId, ckTypeId and path. The identity-owned
///     attributes of <c>System.Identity/User</c> are <c>access: MethodOnly</c> in the model since System.Identity 3.0.0 and refused
///     by the general access rule (message 139, every path), and a caller upsert keeps them by omission as well. The system path
///     (blueprint seed, <c>ImportModelAsync</c>, <c>ImportAsync</c> without caller) still accepts the association edges, and a
///     caller file with other User attributes imports as before. Synthetic graph, no published model.
/// </summary>
public class ImportRtModelCommandIdentityOwnedTests : IDisposable
{
    private const string Tenant = "tenant-id";
    private const string Model = "System.Identity-3.0.0";
    private static readonly RtCkId<CkTypeId> UserType = new($"{Model}/User");
    private static readonly RtCkId<CkTypeId> OtherType = new("Test-1.0.0/Plain");
    private static readonly RtImportCallerContext Caller = RtImportCallerContext.UserInitiated("user-1");

    // The identity-owned User attributes: access MethodOnly in System.Identity 3.0.0 (AB#8053).
    private static readonly HashSet<string> MethodOnlyInSystemIdentity =
    [
        "NormalizedUserName", "NormalizedEmail", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled",
        "LockoutEnabled", "LockoutEnd", "AccessFailedCount", "ResetPasswordOnLogin", "Claims", "UserLogins",
        "PreferredChannelBindingId"
    ];

    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            File.Delete(file);
        }
    }

    private string TempFile()
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);
        return path;
    }

    [Theory]
    [InlineData("TwoFactorEnabled")]
    [InlineData("NormalizedUserName")]
    [InlineData("EmailConfirmed")]
    [InlineData("Claims")]
    [InlineData("UserLogins")]
    [InlineData("PreferredChannelBindingId")]
    [InlineData("LockoutEnd")]
    [InlineData("AccessFailedCount")]
    public async Task Yaml_CallerSetsAnIdentityOwnedUserAttribute_IsRefusedByTheAccessRule_AndWritesNothing(string attribute)
    {
        var h = new Harness();
        var bad = Dto(UserType, ("FirstName", "f"), (attribute, "x"));
        h.YamlReturns(Dto(OtherType, ("Name", "p")), bad);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsCallerAsync(h.Repository,
            TempFile(), ExchangeMimeTypes.MimeTypeYaml, ImportStrategy.Upsert, Caller));

        AssertAccessRefusal(ex, (bad.RtId, attribute));
        h.NothingWritten();
    }

    [Theory]
    [InlineData("System.Identity/AssignedRole")]
    [InlineData("System.Identity/GroupMember")]
    [InlineData("System.Identity/ChildGroup")]
    [InlineData("System.Identity/MayActAs")]
    [InlineData("System.Identity/GrantsPermission")]
    public async Task Yaml_CallerWritesAnIdentityAssociation_OnAnyEntity_IsRefused(string roleId)
    {
        var h = new Harness();
        var target = OctoObjectId.GenerateNewId();
        var bad = Dto(OtherType, ("Name", "p"));
        bad.Associations = [new RtAssociationTcDto { RoleId = roleId, TargetRtId = target, TargetCkTypeId = UserType }];
        h.YamlReturns(bad);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsCallerAsync(h.Repository,
            TempFile(), ExchangeMimeTypes.MimeTypeYaml, ImportStrategy.Insert, Caller));

        AssertRefusal(ex, (bad.RtId, "Test-1.0.0/Plain",
            $"association {new RtCkId<CkAssociationRoleId>(roleId).FullName} -> {target}"));
        h.NothingWritten();
    }

    [Fact]
    public async Task Json_IdentityAssociationInALaterBulk_IsRefusedBeforeTheFirstBulkIsWritten()
    {
        var h = new Harness();
        var target = OctoObjectId.GenerateNewId();
        var bad = Dto(OtherType, ("Name", "b"));
        bad.Associations =
            [new RtAssociationTcDto { RoleId = "System.Identity/GroupMember", TargetRtId = target, TargetCkTypeId = UserType }];
        h.JsonReturnsBulks([Dto(OtherType, ("Name", "a"))], [bad]);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsCallerAsync(h.Repository,
            TempFile(), "application/json", ImportStrategy.Insert, Caller));

        AssertRefusal(ex, (bad.RtId, "Test-1.0.0/Plain", $"association {Role("GroupMember")} -> {target}"));
        h.NothingWritten();
    }

    [Fact]
    public async Task Yaml_SeveralAssociationOffenders_AreListedInOneError()
    {
        var h = new Harness();
        var first = Dto(OtherType, ("Name", "a"));
        var target = OctoObjectId.GenerateNewId();
        first.Associations =
        [
            new RtAssociationTcDto { RoleId = "System.Identity/AssignedRole", TargetRtId = target, TargetCkTypeId = UserType },
            new RtAssociationTcDto { RoleId = "System.Identity/MayActAs", TargetRtId = target, TargetCkTypeId = UserType }
        ];
        var second = Dto(OtherType, ("Name", "b"));
        second.Associations =
            [new RtAssociationTcDto { RoleId = "System.Identity/ChildGroup", TargetRtId = target, TargetCkTypeId = UserType }];
        h.YamlReturns(first, second);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsCallerAsync(h.Repository,
            TempFile(), ExchangeMimeTypes.MimeTypeYaml, ImportStrategy.Insert, Caller));

        AssertRefusal(ex, (first.RtId, "Test-1.0.0/Plain", $"association {Role("AssignedRole")} -> {target}"),
            (first.RtId, "Test-1.0.0/Plain", $"association {Role("MayActAs")} -> {target}"),
            (second.RtId, "Test-1.0.0/Plain", $"association {Role("ChildGroup")} -> {target}"));
    }

    [Fact]
    public async Task Caller_OtherUserAttributes_ImportAsBefore()
    {
        var h = new Harness();
        h.YamlReturns(Dto(UserType, ("FirstName", "f"), ("LastName", "l")), Dto(OtherType, ("Name", "p")));

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
            ImportStrategy.Insert, Caller);

        Assert.Equal(2, h.Written.Count);
    }

    [Fact]
    public async Task SystemPath_IdentityAssociations_ImportAsBefore_TheBootstrapSeedIsNotAffected()
    {
        var h = new Harness();
        var user = Dto(UserType, ("FirstName", "f"));
        var group = Dto(OtherType, ("Name", "g"));
        group.Associations =
        [
            new RtAssociationTcDto
            {
                RoleId = "System.Identity/AssignedRole", TargetRtId = user.RtId, TargetCkTypeId = UserType
            }
        ];

        // Both endpoints are part of the import, so the dangling-edge filter keeps the edge.
        await h.CreateCommand().ImportModelAsync(h.Repository,
            new RtModelRootTcDto { Entities = [group, user] }, ImportStrategy.Insert);

        Assert.Equal(2, h.Written.Count);
        var edge = Assert.Single(h.WrittenAssociations);
        Assert.Equal(group.RtId, edge.OriginRtId);
        Assert.Equal(user.RtId, edge.TargetRtId);
        Assert.Equal("System.Identity/AssignedRole", edge.AssociationRoleId!.FullName.Replace("-1", string.Empty));
    }

    [Fact]
    public async Task SystemPath_IdentityOwnedUserAttribute_IsRefusedByTheAccessRule_LikeEveryPath()
    {
        // AB#8053: MethodOnly is refused on the system path as well (rule 139 does not distinguish); the listed attributes used
        // to pass there while the list guarded the caller path only. No blueprint seeds a User.
        var h = new Harness();
        var bad = Dto(UserType, ("FirstName", "f"), ("TwoFactorEnabled", false));

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportModelAsync(h.Repository,
            new RtModelRootTcDto { Entities = [bad] }, ImportStrategy.Insert));

        AssertAccessRefusal(ex, (bad.RtId, "TwoFactorEnabled"));
        h.NothingWritten();
    }

    [Fact]
    public async Task TenantWithoutSystemIdentity_IsNotChecked_AndReadsTheJsonFileOnlyOnce()
    {
        var h = new Harness(withIdentity: false);
        h.JsonReturnsBulks([Dto(OtherType, ("Name", "a"))]);

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), "application/json",
            ImportStrategy.Insert, Caller);

        // AB#8325: the entities are written when the call returns, no polling.
        Assert.Single(h.Written.ToArray());
        A.CallTo(() => h.Json.DeserializeStreamAsync(A<Stream>._, A<CancellationToken?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Theory]
    [InlineData("LockoutEnd", "2000-01-01T00:00:00Z")]
    [InlineData("AccessFailedCount", 0)]
    public async Task Json_CallerResetsALockout_IsRefusedByTheAccessRule_AndWritesNothing(string attribute, object value)
    {
        // AB#8059: a user-started import cannot reset a lockout (MethodOnly since System.Identity 3.0.0).
        var h = new Harness();
        var bad = Dto(UserType, ("FirstName", "f"), (attribute, value));
        h.JsonReturnsBulks([bad]);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsCallerAsync(h.Repository,
            TempFile(), "application/json", ImportStrategy.Upsert, Caller));

        AssertAccessRefusal(ex, (bad.RtId, attribute));
        h.NothingWritten();
    }

    // AB#8059: a caller Upsert that OMITS a MethodOnly attribute (here the identity-owned ones) must not clear it either (the Upsert is a full
    // replace; the blanking guard alone lets an omitted SeedOwned value go under Allow or a confirmation).
    private static readonly string[] OmittedOwned = ["LockoutEnd", "AccessFailedCount", "TwoFactorEnabled"];

    [Fact]
    public async Task CallerUpsert_OmittedMethodOnlyValues_ArePreserved_UnderBlankingPolicyAllow()
    {
        var h = new Harness();
        var stored = h.StoreUser();
        h.YamlReturns(DtoWithId(stored.RtId, UserType, ("FirstName", "changed")));

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
            ImportStrategy.Upsert, Caller, blankingPolicy: RtImportBlankingPolicy.Allow);

        AssertStoredValuesKept(Assert.Single(h.Written), stored);
    }

    [Fact]
    public async Task CallerUpsert_OmittedMethodOnlyValues_ArePreserved_EvenWhenTheBlankingIsConfirmed()
    {
        var h = new Harness();
        var stored = h.StoreUser();
        h.YamlReturns(DtoWithId(stored.RtId, UserType, ("FirstName", "changed")));

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
            ImportStrategy.Upsert, Caller, confirmedBlankings:
            OmittedOwned.Select(a => new RtImportBlankingConfirmation(stored.RtId, a)).ToList());

        AssertStoredValuesKept(Assert.Single(h.Written), stored);
    }

    [Fact]
    public async Task CallerUpsert_Json_OmittedMethodOnlyValues_ArePreserved_UnderBlankingPolicyAllow()
    {
        var h = new Harness();
        var stored = h.StoreUser();
        h.JsonReturnsBulks([DtoWithId(stored.RtId, UserType, ("FirstName", "changed"))]);

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), "application/json",
            ImportStrategy.Upsert, Caller, blankingPolicy: RtImportBlankingPolicy.Allow);

        // AB#8325: the entities are written when the call returns, no polling.
        AssertStoredValuesKept(Assert.Single(h.Written.ToArray()), stored);
    }

    [Fact]
    public async Task SystemUpsert_UnderBlankingPolicyAllow_BehavesAsBefore_AndClearsOmittedValues()
    {
        // Positive control: the system path (blueprint seed, restore of a file) keeps the blanking semantics.
        var h = new Harness();
        var stored = h.StoreUser();
        h.YamlReturns(DtoWithId(stored.RtId, UserType, ("FirstName", "changed")));

        await h.CreateCommand().ImportAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
            ImportStrategy.Upsert, blankingPolicy: RtImportBlankingPolicy.Allow);

        var written = Assert.Single(h.Written);
        foreach (var attribute in OmittedOwned)
        {
            Assert.False(written.Attributes.TryGetValue(attribute, out var value) && value != null,
                $"{attribute} is cleared on the system path under Allow");
        }
    }

    private static void AssertStoredValuesKept(RtEntity written, RtEntity stored)
    {
        Assert.Equal("changed", written.Attributes["FirstName"]);
        foreach (var attribute in OmittedOwned)
        {
            Assert.True(written.Attributes.TryGetValue(attribute, out var value), $"{attribute} must be carried over");
            Assert.Equal(stored.Attributes[attribute]?.ToString(), value?.ToString());
        }
    }

    [Fact]
    public void TheList_HoldsTheAssociationRolesOnly_MatchesVersionLess_AndIgnoresCase()
    {
        Assert.True(IdentityOwnedWrites.IsIdentityOwnedAssociation(
            new RtCkId<CkAssociationRoleId>("System.Identity-2.30.0/AssignedRole")));
        Assert.True(IdentityOwnedWrites.IsIdentityOwnedAssociation(
            new RtCkId<CkAssociationRoleId>("System.Identity@3/groupMember")));
        Assert.False(IdentityOwnedWrites.IsIdentityOwnedAssociation(
            new RtCkId<CkAssociationRoleId>("System.Identity/PolicyPermission")));
        Assert.False(IdentityOwnedWrites.IsIdentityOwnedAssociation(new RtCkId<CkAssociationRoleId>("System/ParentChild")));
        Assert.Equal(5, IdentityOwnedWrites.AssociationRoleIds.Count);
    }

    private static void AssertAccessRefusal(ExchangeException ex, params (OctoObjectId RtId, string Path)[] offenders)
    {
        Assert.Equal(139, ex.MessageNumber);
        Assert.Equal(offenders.Length, ex.Messages.Count);
        foreach (var (rtId, path) in offenders)
        {
            var message = Assert.Single(ex.Messages,
                m => m.Parameters["rtId"] == rtId.ToString() && m.Parameters["attributePath"] == path);
            Assert.Equal(139, message.MessageNumber);
        }
    }

    private static string Role(string name) => new RtCkId<CkAssociationRoleId>($"System.Identity/{name}").FullName;

    private static void AssertRefusal(ExchangeException ex, params (OctoObjectId RtId, string CkTypeId, string Path)[] offenders)
    {
        Assert.Equal(140, ex.MessageNumber);
        Assert.Equal(offenders.Length, ex.Messages.Count);
        foreach (var (rtId, ckTypeId, path) in offenders)
        {
            var message = Assert.Single(ex.Messages,
                m => m.Parameters["rtId"] == rtId.ToString() && m.Parameters["path"] == path);
            Assert.Equal(140, message.MessageNumber);
            Assert.Equal(ckTypeId, message.Parameters["ckTypeId"]);
        }

        Assert.Contains("only the identity service writes", ex.Message);
    }

    private static RtEntityTcDto Dto(RtCkId<CkTypeId> type, params (string Name, object? Value)[] attributes) =>
        DtoWithId(OctoObjectId.GenerateNewId(), type, attributes);

    private static RtEntityTcDto DtoWithId(OctoObjectId rtId, RtCkId<CkTypeId> type,
        params (string Name, object? Value)[] attributes)
    {
        var dto = new RtEntityTcDto { RtId = rtId, CkTypeId = type };
        foreach (var (name, value) in attributes)
        {
            dto.Attributes.Add(new RtAttributeTcDto
            {
                Id = new CkId<CkAttributeId>($"{type.ModelId}/{name}").ToRtCkId(), Value = value
            });
        }

        return dto;
    }

    private static CkTypeGraph TypeGraph(string model, string name, params string[] attributeNames)
    {
        var attrs = attributeNames.Select(a =>
        {
            var attrId = new CkId<CkAttributeId>($"{model}/{a}");
            var definition = new CkAttributeGraph(attrId,
                new CkAttributeDto { AttributeId = a, ValueType = AttributeValueTypesDto.String });
            return new CkTypeAttributeGraph(attrId,
                new CkTypeAttributeDto
                {
                    CkAttributeId = attrId, AttributeName = a, IsOptional = true,
                    Access = MethodOnlyInSystemIdentity.Contains(a) ? CkAttributeAccessDto.MethodOnly : null
                }, definition);
        }).ToArray();
        return new CkTypeGraph(new CkId<CkTypeId>($"{model}/{name}"), isAbstract: false, isFinal: true,
            isCollectionRoot: false, baseTypes: [], derivedFromCkTypeId: null, definingCollectionRootCkTypeId: null,
            derivedTypes: [], definedAttributes: [], allAttributes: attrs.ToDictionary(a => a.CkAttributeId, a => a),
            indexes: [], associations: new CkGraphDirectedAssociations([]), description: "test",
            enableChangeStreamPreAndPostImages: false);
    }

    private sealed class Harness
    {
        public Harness(bool withIdentity = true)
        {
            var user = TypeGraph(Model, "User", "FirstName", "LastName", "NormalizedUserName", "NormalizedEmail",
                "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "ResetPasswordOnLogin",
                "Claims", "UserLogins", "PreferredChannelBindingId", "LockoutEnd", "AccessFailedCount");
            var other = TypeGraph("Test-1.0.0", "Plain", "Name");
            var userOut = (CkTypeGraph?)user;
            var otherOut = (CkTypeGraph?)other;
            A.CallTo(() => CkCache.IsTenantLoaded(Tenant)).Returns(true);
            A.CallTo(() => CkCache.GetRtCkType(Tenant, UserType)).Returns(user);
            A.CallTo(() => CkCache.GetRtCkType(Tenant, OtherType)).Returns(other);
            A.CallTo(() => CkCache.TryGetRtCkType(Tenant, UserType, out userOut)).Returns(true)
                .AssignsOutAndRefParameters(user);
            A.CallTo(() => CkCache.TryGetRtCkType(Tenant, OtherType, out otherOut)).Returns(true)
                .AssignsOutAndRefParameters(other);
            A.CallTo(() => CkCache.GetCkTypes(Tenant))
                .Returns(withIdentity ? new List<CkTypeGraph> { user, other } : new List<CkTypeGraph> { other });
            A.CallTo(() => CkCache.GetCkRecords(Tenant)).Returns(new List<CkRecordGraph>());
            A.CallTo(() => CkCache.EnsureModelIdRanges(Tenant, A<IEnumerable<CkModelIdVersionRange>>._))
                .Returns(new List<CkModelIdVersionRange>());

            A.CallTo(() => Repository.TenantId).Returns(Tenant);
            A.CallTo(() => Repository.GetSessionAsync()).Returns(Task.FromResult(Session));
            A.CallTo(() => Resolver.GetPolicyTableAsync(A<IRuntimeRepository>._))
                .Returns(Task.FromResult(new RtDataPolicyTable([])));
            A.CallTo(() => Repository.CreateTransientRtEntityByRtCkIdAsync(A<RtCkId<CkTypeId>>._))
                .ReturnsLazily(call => Task.FromResult(new RtEntity(call.GetArgument<RtCkId<CkTypeId>>(0)!,
                    OctoObjectId.GenerateNewId())));
            A.CallTo(() => Repository.BulkInsertRtEntitiesAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
                    A<BulkOperationOptions>._))
                .Invokes(call => Written.AddRange(call.GetArgument<IEnumerable<RtEntity>>(1)!));
            A.CallTo(() => Repository.BulkRtAssociationsAsync(A<IOctoSession>._, A<IEnumerable<RtAssociation>>._,
                    A<BulkOperationOptions>._))
                .Invokes(call => WrittenAssociations.AddRange(call.GetArgument<IEnumerable<RtAssociation>>(1)!));
            A.CallTo(() => Repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                    A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
                .ReturnsLazily(call =>
                {
                    var ids = call.GetArgument<IReadOnlyList<OctoObjectId>>(2)!;
                    var items = ids.Where(Stored.ContainsKey).Select(id => Stored[id]).ToList();
                    return Task.FromResult<IResultSet<RtEntity>>(new ResultSet<RtEntity>(items, items.Count, null,
                        null));
                });
        }

        public Dictionary<OctoObjectId, RtEntity> Stored { get; } = [];

        /// <summary>A stored user that is locked out, with failed attempts and 2FA on (identity service state).</summary>
        public RtEntity StoreUser()
        {
            var user = new RtEntity(UserType, OctoObjectId.GenerateNewId());
            user.SetAttributeRawValue("FirstName", "stored");
            user.SetAttributeRawValue("LockoutEnd", "2099-01-01T00:00:00Z");
            user.SetAttributeRawValue("AccessFailedCount", "5");
            user.SetAttributeRawValue("TwoFactorEnabled", "true");
            Stored[user.RtId] = user;
            return user;
        }

        public ICkCacheService CkCache { get; } = A.Fake<ICkCacheService>();
        public IRuntimeRepository Repository { get; } = A.Fake<IRuntimeRepository>();
        public IDataPermissionResolver Resolver { get; } = A.Fake<IDataPermissionResolver>();
        public IRtYamlSerializer Yaml { get; } = A.Fake<IRtYamlSerializer>();
        public IRtJsonSerializer Json { get; } = A.Fake<IRtJsonSerializer>();
        public IOctoSession Session { get; } = A.Fake<IOctoSession>();
        public List<RtEntity> Written { get; } = [];
        public List<RtAssociation> WrittenAssociations { get; } = [];

        public ImportRtModelCommand CreateCommand() => new(NullLogger<ImportRtModelCommand>.Instance, CkCache, Yaml,
            Json, A.Fake<IRtImportAuditTrail>(), new RtImportOptions(), Resolver, A.Fake<IAuditEventSink>());

        public void YamlReturns(params RtEntityTcDto[] entities)
        {
            var root = new RtModelRootTcDto { Entities = entities.ToList() };
            A.CallTo(() => Yaml.DeserializeAsync(A<Stream>._, A<string>._, A<OperationResult>._))
                .Returns(Task.FromResult(root));
        }

        public void JsonReturnsBulks(params RtEntityTcDto[][] bulks)
        {
            A.CallTo(() => Json.DeserializeStreamAsync(A<Stream>._, A<CancellationToken?>._))
                .ReturnsLazily(() => Task.FromResult<IRtDeserializeStream>(new FakeRtDeserializeStream(bulks)));
        }

        public void NothingWritten()
        {
            Assert.Empty(Written);
            A.CallTo(() => Repository.BulkInsertRtEntitiesAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
                A<BulkOperationOptions>._)).MustNotHaveHappened();
            A.CallTo(() => Session.CommitTransactionAsync()).MustNotHaveHappened();
        }
    }

}
