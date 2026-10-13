using FakeItEasy;

using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
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

using Microsoft.Extensions.Logging;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
///     AB#6652 — the per-import state of <see cref="ImportRtModelCommand" /> (duplicate-RtId set, mandatory
///     violations, guard entries, counters) must not survive from one import to the next on the same instance
///     (the scoped <c>DefaultConfigurationCreatorService</c> reuses one command for the imports of all tenants).
/// </summary>
public class ImportRtModelCommandStateResetTests
{
    private const string Model = "Test-1.0.0";
    private const string TypeId = "Test/Seed";

    private static RtCkId<CkTypeId> Rt(string id) => new(id);

    private static CkTypeAttributeGraph Attr(string name, bool isOptional)
    {
        var attrId = new CkId<CkAttributeId>($"{Model}/{name}");
        var definition = new CkAttributeGraph(attrId,
            new CkAttributeDto { AttributeId = name, ValueType = AttributeValueTypesDto.String });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = name, IsOptional = isOptional },
            definition);
    }

    private static CkTypeGraph Graph()
    {
        // "Name" optional, "Required" mandatory without default: an entity without it is a mandatory violation.
        var attrs = new[] { Attr("Name", true), Attr("Required", false) };
        return new CkTypeGraph(new CkId<CkTypeId>($"{Model}/Seed"),
            isAbstract: false, isFinal: false, isCollectionRoot: false, baseTypes: [],
            derivedFromCkTypeId: null, definingCollectionRootCkTypeId: null, derivedTypes: [], definedAttributes: [],
            allAttributes: attrs.ToDictionary(a => a.CkAttributeId, a => a), indexes: [],
            associations: new CkGraphDirectedAssociations([]), description: "test",
            enableChangeStreamPreAndPostImages: false);
    }

    private static RtEntityTcDto Dto(OctoObjectId rtId, bool withRequired)
    {
        var graph = Graph();
        RtCkId<CkAttributeId> Id(string n) => graph.AllAttributes.Values.First(a => a.AttributeName == n)
            .CkAttributeId.ToRtCkId();

        var dto = new RtEntityTcDto { RtId = rtId, CkTypeId = Rt(TypeId) };
        dto.Attributes.Add(new RtAttributeTcDto { Id = Id("Name"), Value = "seed" });
        if (withRequired)
        {
            dto.Attributes.Add(new RtAttributeTcDto { Id = Id("Required"), Value = "x" });
        }

        return dto;
    }

    private sealed record LogLine(LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger<ImportRtModelCommand>
    {
        private readonly List<LogLine> _lines = [];

        public IReadOnlyList<LogLine> Lines
        {
            get
            {
                lock (_lines)
                {
                    return _lines.ToArray();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_lines)
            {
                _lines.Add(new LogLine(logLevel, formatter(state, exception)));
            }
        }
    }

    private sealed class Harness
    {
        public Harness()
        {
            const string tenant = "tenant1";
            var graph = Graph();
            A.CallTo(() => Repository.TenantId).Returns(tenant);
            A.CallTo(() => Repository.GetSessionAsync()).Returns(Task.FromResult(Session));
            A.CallTo(() => CkCache.IsTenantLoaded(tenant)).Returns(true);
            A.CallTo(() => CkCache.EnsureModelIdRanges(tenant, A<IEnumerable<CkModelIdVersionRange>>._))
                .Returns(new List<CkModelIdVersionRange>());
            A.CallTo(() => CkCache.GetRtCkType(tenant, Rt(TypeId))).Returns(graph);
            A.CallTo(() => Repository.CreateTransientRtEntityByRtCkIdAsync(A<RtCkId<CkTypeId>>._))
                .ReturnsLazily(call => Task.FromResult(new RtEntity(call.GetArgument<RtCkId<CkTypeId>>(0)!,
                    OctoObjectId.GenerateNewId())));
            Command = new ImportRtModelCommand(Logger, CkCache, A.Fake<IRtYamlSerializer>(),
                A.Fake<IRtJsonSerializer>(), A.Fake<IRtImportAuditTrail>(), new RtImportOptions(),
                A.Fake<IDataPermissionResolver>(), A.Fake<IAuditEventSink>());
        }

        public IRuntimeRepository Repository { get; } = A.Fake<IRuntimeRepository>();
        public ICkCacheService CkCache { get; } = A.Fake<ICkCacheService>();
        public IOctoSession Session { get; } = A.Fake<IOctoSession>();
        public CapturingLogger Logger { get; } = new();
        public ImportRtModelCommand Command { get; }

        public Task ImportAsync(params RtEntityTcDto[] entities) =>
            Command.ImportModelAsync(Repository, new RtModelRootTcDto { Entities = entities.ToList() },
                ImportStrategy.Upsert);

        public IReadOnlyList<LogLine> Since(int index) => Logger.Lines.Skip(index).ToList();
    }

    [Fact]
    public async Task SecondImportOnSameInstance_WithSamePayload_LogsNoAlreadyImportedError()
    {
        var h = new Harness();
        var payload = Enumerable.Range(0, 20).Select(_ => Dto(OctoObjectId.GenerateNewId(), true)).ToArray();

        await h.ImportAsync(payload);
        var afterFirst = h.Logger.Lines.Count;
        await h.ImportAsync(payload);

        var second = h.Since(afterFirst);
        Assert.DoesNotContain(second, l => l.Level >= LogLevel.Warning);
        Assert.DoesNotContain(h.Logger.Lines, l => l.Message.Contains("already imported"));
        Assert.Contains(second, l => l.Message.StartsWith("20 entities, 0 associations imported"));
    }

    [Fact]
    public async Task MandatoryViolationOfFirstImport_DoesNotAppearInSecondImport()
    {
        var h = new Harness();
        var violating = OctoObjectId.GenerateNewId();

        await h.ImportAsync(Dto(violating, withRequired: false));
        Assert.Contains(h.Logger.Lines,
            l => l.Level == LogLevel.Warning && l.Message.Contains("missing mandatory") &&
                 l.Message.Contains(violating.ToString()));

        var afterFirst = h.Logger.Lines.Count;
        await h.ImportAsync(Dto(OctoObjectId.GenerateNewId(), withRequired: true));

        Assert.DoesNotContain(h.Since(afterFirst), l => l.Message.Contains("missing mandatory"));
    }

    [Fact]
    public async Task DuplicateRtIdWithinOnePayload_LogsWarningNotError()
    {
        var h = new Harness();
        var id = OctoObjectId.GenerateNewId();

        await h.ImportAsync(Dto(id, true), Dto(id, true));

        Assert.Contains(h.Logger.Lines,
            l => l.Level == LogLevel.Warning && l.Message.StartsWith("Duplicate RtId") &&
                 l.Message.Contains(id.ToString()));
        Assert.DoesNotContain(h.Logger.Lines, l => l.Level >= LogLevel.Error);
    }
}
