using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.CkModelMigrations;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Serialization;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
/// AB#9651 - an install whose seed points at an entity the tenant does not hold yet (the Mesh Adapter before
/// Communication is enabled) reports the kept edges as a warning (73) instead of losing them silently; an install
/// whose edges all resolve reports nothing. The keeping and writing itself is covered by
/// <see cref="DeferredSeedAssociationsTests" />.
/// </summary>
public class BlueprintServiceDeferredSeedEdgesTests
{
    private const string TenantId = "tenant-a";
    private const string UnknownVariableText = "Unknown blueprint variable";
    private static readonly BlueprintId V1 = new("Notifications-1.0.0");
    private static readonly BlueprintId V2 = new("Notifications-1.1.0");
    private static readonly RtCkId<CkTypeId> TemplateType = new("Test-1.0.0/NotificationTemplate");
    private static readonly RtCkId<CkAttributeId> BodyAttribute = new("Test-1.0.0/Body");
    private static readonly OctoObjectId Template = new("6a0e000000000000000d0001");

    private readonly IBlueprintCatalogManager _catalog = A.Fake<IBlueprintCatalogManager>();
    private readonly ITenantBlueprintHistory _history = A.Fake<ITenantBlueprintHistory>();
    private readonly IRuntimeRepositoryProvider _repositoryProvider = A.Fake<IRuntimeRepositoryProvider>();
    private readonly IRuntimeRepository _repository = A.Fake<IRuntimeRepository>();
    private readonly IImportRtModelCommand _import = A.Fake<IImportRtModelCommand>();
    private readonly IRtYamlSerializer _serializer = A.Fake<IRtYamlSerializer>();
    private readonly IBlueprintVariableProvider _variables = A.Fake<IBlueprintVariableProvider>();
    private readonly IBlueprintDependencyResolver _resolver = A.Fake<IBlueprintDependencyResolver>();
    private readonly ITenantBlueprintInstallations _installations = A.Fake<ITenantBlueprintInstallations>();
    private readonly ICkModelUpgradeService _upgrade = A.Fake<ICkModelUpgradeService>();
    private readonly List<RtModelRootTcDto> _imported = [];
    private readonly Dictionary<string, string> _seedBodies = [];
    private Dictionary<string, string> _tenantVariables = new(StringComparer.Ordinal);

    public BlueprintServiceDeferredSeedEdgesTests()
    {
        A.CallTo(() => _catalog.IsExistingAsync(A<BlueprintId>._, A<object?>._)).Returns(true);
        A.CallTo(() => _catalog.GetAsync(A<BlueprintId>._, A<OperationResult>._, A<object?>._, A<CancellationToken?>._))
            .ReturnsLazily((BlueprintId id, OperationResult _, object? _, CancellationToken? _) => Task.FromResult(Meta(id)));
        A.CallTo(() => _catalog.TryOpenBlueprintFileAsync(A<BlueprintId>._, A<string>._, A<object?>._, A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult<Stream?>(new MemoryStream()));
        // A fresh model per call: the interpolator works in place.
        A.CallTo(() => _serializer.DeserializeAsync(A<Stream>._, A<string>._, A<OperationResult>._))
            .ReturnsLazily((Stream _, string location, OperationResult _) =>
                Task.FromResult(BuildSeed(_seedBodies[location[..location.IndexOf('/')]])));
        A.CallTo(() => _variables.GetVariablesAsync(A<string>._, A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult<IReadOnlyDictionary<string, string>>(_tenantVariables));
        A.CallTo(() => _repositoryProvider.GetRepositoryAsync(TenantId, A<CancellationToken>._))
            .Returns(Task.FromResult<IRuntimeRepository?>(_repository));
        A.CallTo(() => _repository.GetRtEntitiesByTypeAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .ReturnsLazily(() => Task.FromResult(EmptyResultSet()));
        A.CallTo(() => _repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .ReturnsLazily(() => Task.FromResult(EmptyResultSet()));
        A.CallTo(() => _import.ImportModelAsync(A<IRuntimeRepository>._, A<RtModelRootTcDto>._,
                A<ImportStrategy>._, A<CancellationToken?>._, A<RtImportBlankingPolicy>._,
                A<IReadOnlyCollection<RtImportBlankingConfirmation>?>._))
            .ReturnsLazily((IRuntimeRepository _, RtModelRootTcDto root, ImportStrategy _, CancellationToken? _,
                RtImportBlankingPolicy _, IReadOnlyCollection<RtImportBlankingConfirmation>? _) =>
            {
                _imported.Add(root);
                return Task.CompletedTask;
            });
        A.CallTo(() => _upgrade.UpgradeModelsAsync(A<string>._, A<IEnumerable<CkModelIdVersionRange>>._,
                A<CkMigrationOptions?>._, A<IReadOnlyDictionary<string, string>?>._, A<CancellationToken>._))
            .Returns(Task.FromResult(new CkModelUpgradeResult { Success = true }));
        A.CallTo(() => _installations.GetByBlueprintNameAsync(TenantId, A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<BlueprintInstallation?>(null));
    }

    private static IResultSet<RtEntity> EmptyResultSet()
    {
        var set = A.Fake<IResultSet<RtEntity>>();
        A.CallTo(() => set.Items).Returns(new List<RtEntity>());
        return set;
    }

    private BlueprintService CreateService(Func<IImportRtModelCommand>? importCommandFactory = null,
        ILogger<BlueprintService>? logger = null) => new(
        A.Fake<ICkCacheService>(), _catalog, _history, A.Fake<IBlueprintMigrationExecutor>(),
        A.Fake<IBlueprintMigrationParser>(), _upgrade, _repositoryProvider, importCommandFactory ?? (() => _import),
        _serializer,
        A.Fake<IBlueprintNotifications>(), _resolver, _installations, _variables,
        logger ?? NullLogger<BlueprintService>.Instance, null, null);

    private static BlueprintMetaRootDto Meta(BlueprintId id) => new()
    {
        BlueprintId = id,
        SeedDataPath = "seed.yaml"
    };

    private static RtModelRootTcDto BuildSeed(string body) => new()
    {
        Entities =
        {
            new RtEntityTcDto
            {
                RtId = Template,
                CkTypeId = TemplateType,
                RtWellKnownName = "eda-process-failed",
                Attributes =
                {
                    new RtAttributeTcDto { Id = BodyAttribute, Value = body }
                }
            }
        }
    };

    private void GivenResolution(BlueprintId root) =>
        A.CallTo(() => _resolver.ResolveAsync(root, A<CancellationToken>._))
            .Returns(new BlueprintResolutionResult
            {
                RootBlueprintId = root,
                Success = true,
                InstallOrder = [Meta(root)],
                Conflicts = [],
                DependencyRanges = new Dictionary<string, List<BlueprintIdVersionRange>>()
            });

    private void GivenInstalled(BlueprintId id)
    {
        A.CallTo(() => _history.GetCurrentByBlueprintNameAsync(TenantId, id.Name, A<CancellationToken>._))
            .Returns(Task.FromResult<TenantBlueprintInfo?>(new TenantBlueprintInfo
            {
                BlueprintId = id,
                AppliedAt = DateTime.UtcNow
            }));
        A.CallTo(() => _installations.GetByBlueprintNameAsync(TenantId, id.Name, A<CancellationToken>._))
            .Returns(Task.FromResult<BlueprintInstallation?>(new BlueprintInstallation
            {
                BlueprintId = id,
                InstalledAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow,
                IsDependency = false
            }));
    }

    private static readonly RtCkId<CkAssociationRoleId> Executes = new("Test-1.0.0/Executes");
    private static readonly RtCkId<CkTypeId> AdapterType = new("Test-1.0.0/Adapter");
    private static readonly OctoObjectId Adapter = new("670000000000000000000002");

    private void GivenSkipped(params RtImportSkippedAssociation[] skipped) =>
        A.CallTo(() => _import.SkippedAssociations).Returns(skipped);

    [Fact]
    public async Task Install_SeedEdgeToAMissingEntity_IsReportedAsKept_Warning73()
    {
        _seedBodies[V1.FullName] = "body";
        GivenResolution(V1);
        GivenSkipped(new RtImportSkippedAssociation(Executes, TemplateType, Template, AdapterType, Adapter, false));

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var warning = Assert.Single(result.OperationResult.Messages, m => m.MessageNumber == 73);
        Assert.Equal(MessageLevel.Warning, warning.MessageLevel);
        Assert.Contains("670000000000000000000002", warning.MessageText);
        A.CallTo(() => _repository.InsertOneRtEntityAsync(A<IOctoSession>._,
                A<RtCkId<CkTypeId>>.That.Matches(t => t.ToString().Contains("TenantConfiguration")), A<RtEntity>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task Install_WithoutDroppedEdges_ReportsNothing_AndWritesNoStore()
    {
        _seedBodies[V1.FullName] = "body";
        GivenResolution(V1);
        GivenSkipped();

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(result.OperationResult.Messages, m => m.MessageNumber is 73 or 74 or 75);
        A.CallTo(() => _repository.InsertOneRtEntityAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._, A<RtEntity>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Install_StoreFailure_IsAWarning_NeverAFailedInstall()
    {
        _seedBodies[V1.FullName] = "body";
        GivenResolution(V1);
        GivenSkipped(new RtImportSkippedAssociation(Executes, TemplateType, Template, AdapterType, Adapter, false));
        A.CallTo(() => _repository.CreateTransientRtEntityByRtCkIdAsync(A<RtCkId<CkTypeId>>._))
            .Throws(new InvalidOperationException("store down"));

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var warning = Assert.Single(result.OperationResult.Messages, m => m.MessageNumber == 73);
        Assert.Contains("store down", warning.MessageText);
    }

    [Fact]
    public async Task Update_SeedEdgeToAMissingEntity_IsReportedAsAWarning()
    {
        GivenInstalled(V1);
        _seedBodies[V1.FullName] = "old";
        _seedBodies[V2.FullName] = "new";
        GivenSkipped(new RtImportSkippedAssociation(Executes, TemplateType, Template, AdapterType, Adapter, false));

        var result = await CreateService().ApplyUpdateAsync(TenantId, V2, BlueprintUpdateMode.Merge,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains(result.Warnings, w => w.Contains("670000000000000000000002") && w.Contains("kept"));
    }

    // ---- one import command per apply ---------------------------------------------------------------------------
    // One BlueprintService applies blueprints to many tenants at the same time (parallel tenant setup on startup). The
    // import command holds the state of the import running on it, so every apply takes its own command and reads the
    // results of its import (here: the skipped seed edges) from that command only.

    [Fact]
    public async Task Install_TakesANewImportCommand_AndReadsTheResultsOfItsOwnImportOnly()
    {
        _seedBodies[V1.FullName] = "body";
        GivenResolution(V1);
        var withSkippedEdge = ImportCommand(new RtImportSkippedAssociation(Executes, TemplateType, Template, AdapterType,
            Adapter, false));
        var clean = ImportCommand();
        var commands = new Queue<IImportRtModelCommand>([withSkippedEdge, clean]);
        var service = CreateService(() => commands.Dequeue());

        var first = await service.ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);
        var second = await service.ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.Empty(commands);
        Assert.Contains(first.OperationResult.Messages, m => m.MessageNumber == 73);
        Assert.DoesNotContain(second.OperationResult.Messages, m => m.MessageNumber == 73);
        A.CallTo(() => withSkippedEdge.ImportModelAsync(A<IRuntimeRepository>._, A<RtModelRootTcDto>._,
                A<ImportStrategy>._, A<CancellationToken?>._, A<RtImportBlankingPolicy>._,
                A<IReadOnlyCollection<RtImportBlankingConfirmation>?>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => clean.ImportModelAsync(A<IRuntimeRepository>._, A<RtModelRootTcDto>._,
                A<ImportStrategy>._, A<CancellationToken?>._, A<RtImportBlankingPolicy>._,
                A<IReadOnlyCollection<RtImportBlankingConfirmation>?>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ConcurrentInstalls_OnOneService_ImportOnSeparateCommands()
    {
        _seedBodies[V1.FullName] = "body";
        GivenResolution(V1);
        var bothImporting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var importing = 0;
        var used = new System.Collections.Concurrent.ConcurrentBag<IImportRtModelCommand>();
        var service = CreateService(() =>
        {
            var command = ImportCommand();
            A.CallTo(() => command.ImportModelAsync(A<IRuntimeRepository>._, A<RtModelRootTcDto>._,
                    A<ImportStrategy>._, A<CancellationToken?>._, A<RtImportBlankingPolicy>._,
                    A<IReadOnlyCollection<RtImportBlankingConfirmation>?>._))
                .ReturnsLazily(async () =>
                {
                    used.Add(command);
                    // Both imports are in flight at the same time before either finishes.
                    if (Interlocked.Increment(ref importing) == 2)
                    {
                        bothImporting.SetResult();
                    }

                    await bothImporting.Task.WaitAsync(TimeSpan.FromSeconds(10));
                });
            return command;
        });

        var results = await Task.WhenAll(
            service.ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken),
            service.ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken));

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal(2, used.Distinct().Count());
    }

    [Fact]
    public async Task Update_TakesANewImportCommand()
    {
        GivenInstalled(V1);
        _seedBodies[V1.FullName] = "old";
        _seedBodies[V2.FullName] = "new";
        var created = 0;
        var service = CreateService(() =>
        {
            created++;
            return ImportCommand();
        });

        await service.ApplyUpdateAsync(TenantId, V2, BlueprintUpdateMode.Merge,
            cancellationToken: TestContext.Current.CancellationToken);
        await service.ApplyUpdateAsync(TenantId, V2, BlueprintUpdateMode.Merge,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, created);
    }

    [Fact]
    public async Task Install_LogLinesOfTheImport_CarryTenantAndBlueprintId()
    {
        _seedBodies[V1.FullName] = "body";
        GivenResolution(V1);
        GivenSkipped();
        var sink = new ScopeCapturingProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(sink));
        var importLogger = loggerFactory.CreateLogger("import");
        A.CallTo(() => _import.ImportModelAsync(A<IRuntimeRepository>._, A<RtModelRootTcDto>._,
                A<ImportStrategy>._, A<CancellationToken?>._, A<RtImportBlankingPolicy>._,
                A<IReadOnlyCollection<RtImportBlankingConfirmation>?>._))
            .ReturnsLazily(() =>
            {
                importLogger.LogWarning("line written by the import command");
                return Task.CompletedTask;
            });

        await CreateService(logger: loggerFactory.CreateLogger<BlueprintService>())
            .ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        var scope = Assert.Single(sink.Lines, l => l.Message == "line written by the import command").Scope;
        Assert.Equal(TenantId, scope["TenantId"]);
        Assert.Equal(V1.ToString(), scope["BlueprintId"]);
    }

    [Fact]
    public async Task Update_EveryLogLineOfTheUpdate_CarriesTenantAndBlueprintId()
    {
        GivenInstalled(V1);
        _seedBodies[V1.FullName] = "old";
        _seedBodies[V2.FullName] = "new";
        GivenSkipped();
        var sink = new ScopeCapturingProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(sink));

        await CreateService(logger: loggerFactory.CreateLogger<BlueprintService>())
            .ApplyUpdateAsync(TenantId, V2, BlueprintUpdateMode.Merge,
                cancellationToken: TestContext.Current.CancellationToken);

        // Also the lines outside the diff import (start of the update, migration steps) carry the scope.
        Assert.NotEmpty(sink.Lines);
        Assert.All(sink.Lines, l =>
        {
            Assert.Equal(TenantId, l.Scope["TenantId"]);
            Assert.Equal(V2.ToString(), l.Scope["BlueprintId"]);
        });
    }

    private sealed class ScopeCapturingProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

        public System.Collections.Concurrent.ConcurrentQueue<(string Message, Dictionary<string, object?> Scope)> Lines
        {
            get;
        } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public void Dispose()
        {
        }

        private sealed class Logger(ScopeCapturingProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var scope = new Dictionary<string, object?>();
                owner._scopes.ForEachScope((value, target) =>
                {
                    if (value is IEnumerable<KeyValuePair<string, object>> pairs)
                    {
                        foreach (var pair in pairs)
                        {
                            target[pair.Key] = pair.Value;
                        }
                    }
                }, scope);
                owner.Lines.Enqueue((formatter(state, exception), scope));
            }
        }
    }

    private static IImportRtModelCommand ImportCommand(params RtImportSkippedAssociation[] skipped)
    {
        var command = A.Fake<IImportRtModelCommand>();
        A.CallTo(() => command.SkippedAssociations).Returns(skipped);
        return command;
    }
}
