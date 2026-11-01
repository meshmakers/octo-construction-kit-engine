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
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Exchange;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
///     AB#8042 - a user-initiated <c>ImportRt</c> (caller path) cannot set an identity-owned field of <c>System.Identity/User</c>
///     or an edge of an identity association role (<see cref="IdentityOwnedWrites" />); the whole file is refused before the
///     first write with one <see cref="ExchangeException" /> carrying message 140 and, per offender, rtId, ckTypeId and path.
///     The system path (blueprint seed, <c>ImportModelAsync</c>, <c>ImportAsync</c> without caller) is unchanged, and a caller
///     file with other User attributes imports as before. Synthetic graph, no published model.
/// </summary>
public class ImportRtModelCommandIdentityOwnedTests : IDisposable
{
    private const string Tenant = "tenant-id";
    private const string Model = "System.Identity-2.30.0";
    private static readonly RtCkId<CkTypeId> UserType = new($"{Model}/User");
    private static readonly RtCkId<CkTypeId> OtherType = new("Test-1.0.0/Plain");
    private static readonly RtImportCallerContext Caller = RtImportCallerContext.UserInitiated("user-1");

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
    public async Task Yaml_CallerSetsAnIdentityOwnedUserAttribute_IsRefused_AndWritesNothing(string attribute)
    {
        var h = new Harness();
        var bad = Dto(UserType, ("FirstName", "f"), (attribute, "x"));
        h.YamlReturns(Dto(OtherType, ("Name", "p")), bad);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsCallerAsync(h.Repository,
            TempFile(), ExchangeMimeTypes.MimeTypeYaml, ImportStrategy.Upsert, Caller));

        AssertRefusal(ex, (bad.RtId, Model + "/User", attribute));
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
    public async Task Json_IdentityOwnedValueInALaterBulk_IsRefusedBeforeTheFirstBulkIsWritten()
    {
        var h = new Harness();
        var bad = Dto(UserType, ("LockoutEnabled", false));
        h.JsonReturnsBulks([Dto(OtherType, ("Name", "a"))], [bad]);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsCallerAsync(h.Repository,
            TempFile(), "application/json", ImportStrategy.Insert, Caller));

        AssertRefusal(ex, (bad.RtId, Model + "/User", "LockoutEnabled"));
        h.NothingWritten();
    }

    [Fact]
    public async Task Yaml_SeveralOffenders_AreListedInOneError()
    {
        var h = new Harness();
        var first = Dto(UserType, ("TwoFactorEnabled", false), ("ResetPasswordOnLogin", false));
        var second = Dto(UserType, ("PhoneNumberConfirmed", true));
        h.YamlReturns(first, second);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsCallerAsync(h.Repository,
            TempFile(), ExchangeMimeTypes.MimeTypeYaml, ImportStrategy.Insert, Caller));

        AssertRefusal(ex, (first.RtId, Model + "/User", "TwoFactorEnabled"),
            (first.RtId, Model + "/User", "ResetPasswordOnLogin"), (second.RtId, Model + "/User", "PhoneNumberConfirmed"));
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
    public async Task SystemPath_IdentityOwnedAttributes_ImportAsBefore_TheBlueprintSeedIsNotAffected()
    {
        var h = new Harness();
        h.YamlReturns(Dto(UserType, ("FirstName", "f"), ("TwoFactorEnabled", false), ("EmailConfirmed", true)));

        await h.CreateCommand().ImportAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
            ImportStrategy.Insert);
        await h.CreateCommand().ImportModelAsync(h.Repository,
            new RtModelRootTcDto { Entities = [Dto(UserType, ("TwoFactorEnabled", false))] }, ImportStrategy.Insert);

        Assert.Equal(2, h.Written.Count);
    }

    [Fact]
    public async Task TenantWithoutSystemIdentity_IsNotChecked_AndReadsTheJsonFileOnlyOnce()
    {
        var h = new Harness(withIdentity: false);
        h.JsonReturnsBulks([Dto(OtherType, ("Name", "a"))]);

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), "application/json",
            ImportStrategy.Insert, Caller);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (h.Written.ToArray().Length < 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Single(h.Written.ToArray());
        A.CallTo(() => h.Json.DeserializeStreamAsync(A<Stream>._, A<CancellationToken?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public void TheList_MatchesVersionLess_AndIgnoresCase()
    {
        Assert.True(IdentityOwnedWrites.IsIdentityOwnedAttribute(new RtCkId<CkTypeId>("System.Identity-2.29.0/User"),
            "twoFactorEnabled"));
        Assert.True(IdentityOwnedWrites.IsIdentityOwnedAttribute(new RtCkId<CkTypeId>("System.Identity/User"), "claims"));
        Assert.False(IdentityOwnedWrites.IsIdentityOwnedAttribute(new RtCkId<CkTypeId>("System.Identity/User"),
            "FirstName"));
        Assert.False(IdentityOwnedWrites.IsIdentityOwnedAttribute(new RtCkId<CkTypeId>("Other/User"), "TwoFactorEnabled"));
        Assert.False(IdentityOwnedWrites.IsIdentityOwnedAttribute(new RtCkId<CkTypeId>("System.Identity/Group"),
            "TwoFactorEnabled"));
        Assert.True(IdentityOwnedWrites.IsIdentityOwnedAssociation(
            new RtCkId<CkAssociationRoleId>("System.Identity-2.30.0/AssignedRole")));
        Assert.False(IdentityOwnedWrites.IsIdentityOwnedAssociation(
            new RtCkId<CkAssociationRoleId>("System.Identity/PolicyPermission")));
        Assert.False(IdentityOwnedWrites.IsIdentityOwnedAssociation(new RtCkId<CkAssociationRoleId>("System/ParentChild")));
        Assert.True(IdentityOwnedWrites.IsUserType(new RtCkId<CkTypeId>("System.Identity@2/User")));
        Assert.False(IdentityOwnedWrites.IsUserType(new RtCkId<CkTypeId>("System.Identity.Extra/User")));
    }

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

    private static RtEntityTcDto Dto(RtCkId<CkTypeId> type, params (string Name, object? Value)[] attributes)
    {
        var dto = new RtEntityTcDto { RtId = OctoObjectId.GenerateNewId(), CkTypeId = type };
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
                new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = a, IsOptional = true }, definition);
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
                "Claims", "UserLogins", "PreferredChannelBindingId");
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
        }

        public ICkCacheService CkCache { get; } = A.Fake<ICkCacheService>();
        public IRuntimeRepository Repository { get; } = A.Fake<IRuntimeRepository>();
        public IDataPermissionResolver Resolver { get; } = A.Fake<IDataPermissionResolver>();
        public IRtYamlSerializer Yaml { get; } = A.Fake<IRtYamlSerializer>();
        public IRtJsonSerializer Json { get; } = A.Fake<IRtJsonSerializer>();
        public IOctoSession Session { get; } = A.Fake<IOctoSession>();
        public List<RtEntity> Written { get; } = [];

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
                .ReturnsLazily(() => Task.FromResult<IRtDeserializeStream>(new FakeDeserializeStream(bulks)));
        }

        public void NothingWritten()
        {
            Assert.Empty(Written);
            A.CallTo(() => Repository.BulkInsertRtEntitiesAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
                A<BulkOperationOptions>._)).MustNotHaveHappened();
            A.CallTo(() => Session.CommitTransactionAsync()).MustNotHaveHappened();
        }
    }

    private sealed class FakeDeserializeStream(RtEntityTcDto[][] bulks) : IRtDeserializeStream
    {
        public IReadOnlyCollection<CkModelIdVersionRange> Dependencies { get; } = [];
        public event EventHandler<RtDeserializeEventArgs>? BulkDeserialized;

        public Task ReadAsync(CancellationToken? cancellationToken = null)
        {
            foreach (var bulk in bulks)
            {
                BulkDeserialized?.Invoke(this, new RtDeserializeEventArgs(bulk));
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
