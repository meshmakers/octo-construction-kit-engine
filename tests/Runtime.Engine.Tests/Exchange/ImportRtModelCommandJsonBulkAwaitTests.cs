using System.Collections.Concurrent;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
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
///     AB#8325 - a JSON ImportRt writes its bulks inside the awaited import: the call returns only after every bulk is
///     written, the transaction is committed after the writes, and a failing write fails the import and commits nothing.
///     The repository of these tests completes its writes asynchronously, as MongoDB does; a repository that completes
///     synchronously hid the fire-and-forget write of the former <c>async void</c> bulk handler.
/// </summary>
public class ImportRtModelCommandJsonBulkAwaitTests : IDisposable
{
    private const string JsonMimeType = "application/json";
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
        public Harness(Exception? writeFailure = null)
        {
            CkCache = Cache(withRestricted: false);
            A.CallTo(() => Repository.TenantId).Returns(Tenant);
            A.CallTo(() => Repository.GetSessionAsync()).Returns(Task.FromResult(Session));
            A.CallTo(() => Resolver.GetPolicyTableAsync(A<IRuntimeRepository>._))
                .Returns(Task.FromResult(new RtDataPolicyTable([])));
            A.CallTo(() => Repository.CreateTransientRtEntityByRtCkIdAsync(A<RtCkId<CkTypeId>>._))
                .ReturnsLazily(call => Task.FromResult(new RtEntity(call.GetArgument<RtCkId<CkTypeId>>(0)!,
                    OctoObjectId.GenerateNewId())));
            A.CallTo(() => Repository.BulkInsertRtEntitiesAsync(A<IOctoSession>._, A<IEnumerable<RtEntity>>._,
                    A<BulkOperationOptions>._))
                .ReturnsLazily(async call =>
                {
                    var entities = call.GetArgument<IEnumerable<RtEntity>>(1)!.ToList();
                    // A real database answers later: the write completes after the caller's first await.
                    await Task.Delay(50).ConfigureAwait(false);
                    if (writeFailure != null)
                    {
                        Events.Enqueue("write-failed");
                        throw writeFailure;
                    }

                    foreach (var entity in entities)
                    {
                        Written.Enqueue(entity);
                    }

                    Events.Enqueue($"write:{entities.Count}");
                    return new AggregatedBulkImportResult([]);
                });
            A.CallTo(() => Session.CommitTransactionAsync()).Invokes(() => Events.Enqueue("commit"));
        }

        public IRuntimeRepository Repository { get; } = A.Fake<IRuntimeRepository>();
        public ICkCacheService CkCache { get; }
        public IDataPermissionResolver Resolver { get; } = A.Fake<IDataPermissionResolver>();
        public IRtJsonSerializer Json { get; } = A.Fake<IRtJsonSerializer>();
        public IOctoSession Session { get; } = A.Fake<IOctoSession>();
        public ConcurrentQueue<RtEntity> Written { get; } = new();
        public ConcurrentQueue<string> Events { get; } = new();

        public ImportRtModelCommand CreateCommand() => new(NullLogger<ImportRtModelCommand>.Instance, CkCache,
            A.Fake<IRtYamlSerializer>(), Json, A.Fake<IRtImportAuditTrail>(), new RtImportOptions(), Resolver,
            A.Fake<IAuditEventSink>());

        public void JsonReturnsBulks(params RtEntityTcDto[][] bulks)
        {
            A.CallTo(() => Json.DeserializeStreamAsync(A<Stream>._, A<CancellationToken?>._))
                .ReturnsLazily(() => Task.FromResult<IRtDeserializeStream>(new FakeRtDeserializeStream(bulks)));
        }
    }

    [Fact]
    public async Task JsonImport_ReturnsOnlyAfterEveryBulkIsWritten_AndCommitsAfterTheWrites()
    {
        var h = new Harness();
        h.JsonReturnsBulks([Dto(PlainType, null, ("Name", "a")), Dto(PlainType, null, ("Name", "b"))],
            [Dto(PlainType, null, ("Name", "c"))]);

        await h.CreateCommand().ImportAsync(h.Repository, TempFile(), JsonMimeType,
            ImportStrategy.Insert);

        // No polling: the entities are written when the call returns.
        Assert.Equal(3, h.Written.Count);
        Assert.Equal(["write:2", "write:1", "commit"], h.Events.ToArray());
    }

    [Fact]
    public async Task JsonCallerImport_ReturnsOnlyAfterEveryBulkIsWritten()
    {
        var h = new Harness();
        h.JsonReturnsBulks([Dto(PlainType, null, ("Name", "a"))]);

        await h.CreateCommand().ImportAsCallerAsync(h.Repository, TempFile(), JsonMimeType,
            ImportStrategy.Insert, RtImportCallerContext.UserInitiated("user-1"));

        Assert.Single(h.Written);
        Assert.Equal(["write:1", "commit"], h.Events.ToArray());
    }

    [Fact]
    public async Task JsonImport_FailingWrite_FailsTheImport_CommitsNothing_AndEndsTheSession()
    {
        var failure = new InvalidOperationException("write refused");
        var h = new Harness(failure);
        h.JsonReturnsBulks([Dto(PlainType, null, ("Name", "a"))], [Dto(PlainType, null, ("Name", "b"))]);

        // The write error reaches the caller the same way as on the YAML path (wrapped by the bulk import).
        var ex = await Assert.ThrowsAsync<ExchangeException>(() => h.CreateCommand().ImportAsync(h.Repository,
            TempFile(), JsonMimeType, ImportStrategy.Insert));

        Assert.Same(failure, ex.InnerException);
        Assert.Empty(h.Written);
        // The first failing bulk stops the import: the second bulk is not written.
        Assert.Equal(["write-failed"], h.Events.ToArray());
        A.CallTo(() => h.Session.CommitTransactionAsync()).MustNotHaveHappened();
        // Disposing the session aborts its open transaction.
        A.CallTo(() => h.Session.Dispose()).MustHaveHappened();
    }
}
