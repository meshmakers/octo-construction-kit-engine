using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
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
using static Meshmakers.Octo.Runtime.Engine.Tests.Access.AccessTestModel;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
///     AB#7767 (H3) — no RT import path (YAML file, JSON stream, text, model; user-initiated or system) can write an
///     attribute that only a CK method may write (Hidden or MethodOnly). The whole file is refused before the first
///     write with one <see cref="ExchangeException"/> carrying message 139 and, per offender, ckTypeId,
///     attributePath and rtId. ReadOnly stays importable; a file without such a value imports as before.
/// </summary>
public class ImportRtModelCommandAttributeAccessTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private static readonly RtImportCallerContext User = RtImportCallerContext.UserInitiated("user-1");

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
        public Harness(bool withRestricted = true)
        {
            CkCache = Cache(withRestricted: withRestricted);
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

        public IRuntimeRepository Repository { get; } = A.Fake<IRuntimeRepository>();
        public ICkCacheService CkCache { get; }
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
            A.CallTo(() => Yaml.DeserializeAsync(A<string>._, A<string>._, A<OperationResult>._))
                .Returns(Task.FromResult(root));
        }

        public void JsonReturnsBulks(params RtEntityTcDto[][] bulks)
        {
            A.CallTo(() => Json.DeserializeStreamAsync(A<Stream>._, A<CancellationToken?>._))
                .ReturnsLazily(() => Task.FromResult<IRtDeserializeStream>(new FakeDeserializeStream(bulks)));
        }

        public void NothingMayBeWritten()
        {
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

    private static void AssertRefusal(ExchangeException ex, params (OctoObjectId RtId, string Path)[] offenders)
    {
        Assert.Equal(139, ex.MessageNumber);
        Assert.Equal(offenders.Length, ex.Messages.Count);
        foreach (var (rtId, path) in offenders)
        {
            var message = Assert.Single(ex.Messages,
                m => m.Parameters["rtId"] == rtId.ToString() && m.Parameters["attributePath"] == path);
            Assert.Equal(139, message.MessageNumber);
            Assert.Equal("Test-1.0.0/Account", message.Parameters["ckTypeId"]);
        }
    }

    [Theory]
    [InlineData("PasswordHash")]
    [InlineData("Email")]
    public async Task Yaml_HiddenOrMethodOnlyValue_IsRefused_ForUserAndSystemPath_AndWritesNothing(string attribute)
    {
        foreach (var asUser in new[] { true, false })
        {
            var h = new Harness();
            var bad = Dto(AccountType, null, ("Name", "n"), (attribute, "x"));
            h.YamlReturns(Dto(PlainType, null, ("Name", "p")), bad);

            var command = h.CreateCommand();
            var ex = await Assert.ThrowsAsync<ExchangeException>(() => asUser
                ? command.ImportAsCallerAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
                    ImportStrategy.Upsert, User)
                : command.ImportAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
                    ImportStrategy.Upsert));

            AssertRefusal(ex, (bad.RtId, attribute));
            Assert.Empty(h.Written);
            h.NothingMayBeWritten();
        }
    }

    [Fact]
    public async Task Yaml_SeveralOffendersIncludingRecordMembers_AreListedInOneError()
    {
        var h = new Harness();
        var first = Dto(AccountType, null, ("PasswordHash", "h"), ("Profile", RecordDto(("Token", "t"))));
        var second = Dto(AccountType, null, ("Credentials", new List<object> { RecordDto(("Label", "ok")), RecordDto(("Token", "t")) }));
        h.YamlReturns(first, second);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand()
            .ImportAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml, ImportStrategy.Insert));

        AssertRefusal(ex, (first.RtId, "PasswordHash"), (first.RtId, "Profile.Token"),
            (second.RtId, "Credentials[1].Token"));
        Assert.Empty(h.Written);
    }

    [Fact]
    public async Task Json_RestrictedValueInALaterBulk_IsRefusedBeforeTheFirstBulkIsWritten()
    {
        var h = new Harness();
        var bad = Dto(AccountType, null, ("Email", "e"));
        h.JsonReturnsBulks([Dto(PlainType, null, ("Name", "a")), Dto(PlainType, null, ("Name", "b"))], [bad]);

        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand()
            .ImportAsync(h.Repository, TempFile(), "application/json", ImportStrategy.Insert));

        AssertRefusal(ex, (bad.RtId, "Email"));
        Assert.Empty(h.Written);
        h.NothingMayBeWritten();
    }

    [Fact]
    public async Task Text_AndModel_Imports_AreRefused_Too()
    {
        var h = new Harness();
        var bad = Dto(AccountType, null, ("PasswordHash", "h"));
        h.YamlReturns(bad);

        var textEx = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand()
            .ImportTextAsync(h.Repository, "ignored", ImportStrategy.Upsert));
        var modelEx = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand()
            .ImportModelAsync(h.Repository, new RtModelRootTcDto { Entities = [bad] }, ImportStrategy.Upsert));

        AssertRefusal(textEx, (bad.RtId, "PasswordHash"));
        AssertRefusal(modelEx, (bad.RtId, "PasswordHash"));
        h.NothingMayBeWritten();
    }

    [Fact]
    public async Task ReadOnlyAndReadWriteValues_AndFilesWithoutRestrictedValues_ImportAsBefore()
    {
        var h = new Harness();
        h.YamlReturns(Dto(AccountType, null, ("Name", "n"), ("Created", "c"),
            ("Profile", RecordDto(("Label", "l")))), Dto(PlainType, null, ("Name", "p")));

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), ExchangeMimeTypes.MimeTypeYaml,
            ImportStrategy.Insert, User);

        Assert.Equal(2, h.Written.Count);
    }

    [Fact]
    public async Task TenantWithoutRestrictedAttribute_ImportsAsBefore_AndReadsTheJsonFileOnlyOnce()
    {
        var h = new Harness(withRestricted: false);
        h.JsonReturnsBulks([Dto(AccountType, null, ("Name", "a"), ("Created", "c"))], [Dto(PlainType, null, ("Name", "p"))]);

        await h.CreateCommand().ImportAsync(h.Repository, TempFile(), "application/json", ImportStrategy.Insert);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (h.Written.ToArray().Length < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, h.Written.ToArray().Length);
        A.CallTo(() => h.Json.DeserializeStreamAsync(A<Stream>._, A<CancellationToken?>._))
            .MustHaveHappenedOnceExactly();
    }
}
