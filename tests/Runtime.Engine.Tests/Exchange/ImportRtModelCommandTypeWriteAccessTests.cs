using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
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
using static Meshmakers.Octo.Runtime.Engine.Tests.Security.TypeWriteAccessTestModel;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
///     AB#8574: a user-initiated RT import (caller path) cannot write entities, or association edges at either end, of
///     a type whose <c>writeAccess</c> block does not admit the initiating caller: the whole file fails before the first
///     write with one <see cref="ExchangeException" /> (message 144). An admitted principal or channel imports as before;
///     the system path (blueprint seed, no caller) is never restricted; tenants without restricted types are unaffected.
/// </summary>
public class ImportRtModelCommandTypeWriteAccessTests : IDisposable
{
    private const string Tenant = "tenant1";
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

    private sealed class Harness
    {
        public Harness(bool restricted = true)
        {
            A.CallTo(() => Repository.TenantId).Returns(Tenant);
            A.CallTo(() => Repository.GetSessionAsync()).Returns(Task.FromResult(Session));
            A.CallTo(() => Resolver.GetPolicyTableAsync(A<IRuntimeRepository>._))
                .Returns(Task.FromResult(new RtDataPolicyTable([])));
            A.CallTo(() => CkCache.IsTenantLoaded(Tenant)).Returns(true);
            A.CallTo(() => CkCache.EnsureModelIdRanges(Tenant, A<IEnumerable<CkModelIdVersionRange>>._))
                .Returns(new List<CkModelIdVersionRange>());
            Register(CkCache, Tenant, restricted);
            A.CallTo(() => Repository.CreateTransientRtEntityByRtCkIdAsync(A<RtCkId<CkTypeId>>._))
                .ReturnsLazily(call => Task.FromResult(new RtEntity(call.GetArgument<RtCkId<CkTypeId>>(0)!,
                    OctoObjectId.GenerateNewId())));
            A.CallTo(() => Repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                    A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
                .Returns(Task.FromResult<IResultSet<RtEntity>>(new ResultSet<RtEntity>([], 0, null, null)));
            A.CallTo(() => Repository.BulkInsertRtEntitiesAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
                    A<BulkOperationOptions>._))
                .Invokes(call => Written.AddRange(call.GetArgument<IEnumerable<RtEntity>>(1)!));
        }

        public IRuntimeRepository Repository { get; } = A.Fake<IRuntimeRepository>();
        public ICkCacheService CkCache { get; } = A.Fake<ICkCacheService>();
        public IDataPermissionResolver Resolver { get; } = A.Fake<IDataPermissionResolver>();
        public IRtYamlSerializer Yaml { get; } = A.Fake<IRtYamlSerializer>();
        public IRtJsonSerializer Json { get; } = A.Fake<IRtJsonSerializer>();
        public IOctoSession Session { get; } = A.Fake<IOctoSession>();
        public List<RtEntity> Written { get; } = [];

        public ImportRtModelCommand CreateCommand() =>
            new(NullLogger<ImportRtModelCommand>.Instance, CkCache, Yaml, Json, A.Fake<IRtImportAuditTrail>(),
                new RtImportOptions(), Resolver, A.Fake<IAuditEventSink>());

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

        public void NothingMayBeWritten()
        {
            Assert.Empty(Written);
            A.CallTo(() => Repository.BulkInsertRtEntitiesAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
                A<BulkOperationOptions>._)).MustNotHaveHappened();
            A.CallTo(() => Session.CommitTransactionAsync()).MustNotHaveHappened();
        }
    }

    private static RtEntityTcDto Dto(string ckType)
    {
        var dto = new RtEntityTcDto { RtId = OctoObjectId.GenerateNewId(), CkTypeId = Rt(ckType) };
        dto.Attributes.Add(new RtAttributeTcDto
        {
            Id = new CkId<CkAttributeId>($"{Model}/Name").ToRtCkId(), Value = "incoming"
        });
        return dto;
    }

    private static RtImportCallerContext Caller(RtPrincipalKind kind, string? channel = null) =>
        RtImportCallerContext.UserInitiated("caller", kind) with { WriteChannel = channel };

    [Theory]
    [InlineData(RtPrincipalKind.Unknown)]
    [InlineData(RtPrincipalKind.Client)]
    public async Task ClientOrUnknownCaller_Yaml_IsRefusedWith144_AndWritesNothing(RtPrincipalKind kind)
    {
        var h = new Harness();
        var restricted = Dto(KnowledgeType);
        h.YamlReturns(Dto(NoteType), restricted);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsCallerAsync(h.Repository,
            TempFile(), ExchangeMimeTypes.MimeTypeYaml, ImportStrategy.Upsert, Caller(kind)));

        Assert.Equal(144, ex.MessageNumber);
        var message = Assert.Single(ex.Messages);
        Assert.Equal(restricted.RtId.ToString(), message.Parameters["rtId"]);
        Assert.Equal(KnowledgeType, message.Parameters["ckTypeId"]);
        Assert.Equal(kind.ToString(), message.Parameters["principalKind"]);
        Assert.Contains("writeAccess", ex.Message);
        h.NothingMayBeWritten();
    }

    [Fact]
    public async Task LegacyCallerContext_WithoutPrincipalKind_IsRefused_FailClosed()
    {
        var h = new Harness();
        h.YamlReturns(Dto(SpecialKnowledgeType));

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsCallerAsync(h.Repository,
            TempFile(), ExchangeMimeTypes.MimeTypeYaml, ImportStrategy.Upsert, RtImportCallerContext.UserInitiated("x")));

        Assert.Equal(KnowledgeType, Assert.Single(ex.Messages).Parameters["restrictingCkTypeId"]);
        h.NothingMayBeWritten();
    }

    [Fact]
    public async Task InteractiveCaller_ImportsAsBefore()
    {
        var h = new Harness();
        h.YamlReturns(Dto(KnowledgeType), Dto(NoteType));

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
            ImportStrategy.Upsert, Caller(RtPrincipalKind.Interactive));

        Assert.Equal(2, h.Written.Count);
    }

    [Fact]
    public async Task ClientOnTheDeclaredChannel_ImportsAsBefore()
    {
        var h = new Harness();
        h.YamlReturns(Dto(LockedKnowledgeType));

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
            ImportStrategy.Upsert, Caller(RtPrincipalKind.Client, Channel));

        Assert.Single(h.Written);
    }

    [Fact]
    public async Task SystemPath_BlueprintSeed_IsNeverRestricted()
    {
        var h = new Harness();
        h.YamlReturns(Dto(KnowledgeType), Dto(LockedKnowledgeType));

        await h.CreateCommand().ImportAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
            ImportStrategy.Upsert);

        Assert.Equal(2, h.Written.Count);
    }

    [Fact]
    public async Task AssociationEdgeToARestrictedEntity_IsRefused()
    {
        var h = new Harness();
        var note = Dto(NoteType);
        note.Associations =
        [
            new RtAssociationTcDto
            {
                RoleId = new RtCkId<CkAssociationRoleId>("Test/Related"), TargetRtId = OctoObjectId.GenerateNewId(),
                TargetCkTypeId = Rt(KnowledgeType)
            }
        ];
        h.YamlReturns(note);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsCallerAsync(h.Repository,
            TempFile(), ExchangeMimeTypes.MimeTypeYaml, ImportStrategy.Upsert, Caller(RtPrincipalKind.Client)));

        var message = Assert.Single(ex.Messages);
        Assert.Equal("association change", message.Parameters["operation"]);
        Assert.Equal(KnowledgeType, message.Parameters["ckTypeId"]);
        h.NothingMayBeWritten();
    }

    [Fact]
    public async Task JsonBulks_RestrictedEntityInALaterBulk_IsRefusedBeforeTheFirstBulkIsWritten()
    {
        var h = new Harness();
        h.JsonReturnsBulks([Dto(NoteType), Dto(NoteType)], [Dto(KnowledgeType)]);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsCallerAsync(h.Repository,
            TempFile(), "application/json", ImportStrategy.Upsert, Caller(RtPrincipalKind.Client)));

        Assert.Equal(144, ex.MessageNumber);
        h.NothingMayBeWritten();
    }

    [Fact]
    public async Task TenantWithoutRestrictedTypes_UnknownCaller_ImportsAsBefore()
    {
        var h = new Harness(restricted: false);
        h.JsonReturnsBulks([Dto(KnowledgeType)], [Dto(NoteType)]);

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), "application/json",
            ImportStrategy.Upsert, Caller(RtPrincipalKind.Unknown));

        Assert.Equal(2, h.Written.Count);
    }
}
