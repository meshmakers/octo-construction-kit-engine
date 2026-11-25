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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
/// AB#8995 - a seed value with <c>$${name}</c> reaches the import as the literal <c>${name}</c> on install and on
/// update, without the "Unknown blueprint variable" warning (message 27); on install an unescaped unknown placeholder
/// still warns. The interpolation rules themselves are covered by <see cref="BlueprintVariableInterpolatorTests" />.
/// </summary>
public class BlueprintServiceSeedEscapeTests
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

    public BlueprintServiceSeedEscapeTests()
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

    private BlueprintService CreateService() => new(
        A.Fake<ICkCacheService>(), _catalog, _history, A.Fake<IBlueprintMigrationExecutor>(),
        A.Fake<IBlueprintMigrationParser>(), _upgrade, _repositoryProvider, () => _import, _serializer,
        A.Fake<IBlueprintNotifications>(), _resolver, _installations, _variables,
        NullLogger<BlueprintService>.Instance, null, null);

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

    private string ImportedBody()
    {
        var entity = Assert.Single(_imported.SelectMany(r => r.Entities), e => e.RtId.Equals(Template));
        return Assert.IsType<string>(Assert.Single(entity.Attributes, a => a.Id.Equals(BodyAttribute)).Value);
    }

    [Fact]
    public async Task Install_EscapedPlaceholders_AreImportedAsLiterals_WithoutWarning()
    {
        _tenantVariables = new Dictionary<string, string>(StringComparer.Ordinal) { ["octo.tenantId"] = TenantId };
        _seedBodies[V1.FullName] = "$${count} failed processes in ${octo.tenantId}: $${name}";
        GivenResolution(V1);

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("${count} failed processes in tenant-a: ${name}", ImportedBody());
        Assert.DoesNotContain(result.OperationResult.Messages, m => m.MessageNumber == 27);
    }

    [Fact]
    public async Task Install_EscapedPlaceholder_IsUnescaped_WhenTheTenantHasNoVariables()
    {
        _seedBodies[V1.FullName] = "$${count}";
        GivenResolution(V1);

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("${count}", ImportedBody());
        Assert.DoesNotContain(result.OperationResult.Messages, m => m.MessageNumber == 27);
    }

    [Fact]
    public async Task Install_UnescapedUnknownPlaceholder_StillWarns_AndIsKeptVerbatim()
    {
        _seedBodies[V1.FullName] = "${count}";
        GivenResolution(V1);

        var result = await CreateService().ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("${count}", ImportedBody());
        var warning = Assert.Single(result.OperationResult.Messages, m => m.MessageNumber == 27);
        Assert.Equal(MessageLevel.Warning, warning.MessageLevel);
    }

    [Fact]
    public async Task Update_EscapedPlaceholders_AreImportedAsLiterals_WithoutWarning()
    {
        // The update path imports the seed through the same interpolation; the stored value is the literal.
        _tenantVariables = new Dictionary<string, string>(StringComparer.Ordinal) { ["octo.tenantId"] = TenantId };
        GivenInstalled(V1);
        _seedBodies[V1.FullName] = "old";
        _seedBodies[V2.FullName] = "$${count} of $${name} in ${octo.tenantId}";

        var result = await CreateService().ApplyUpdateAsync(TenantId, V2, BlueprintUpdateMode.Merge,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("${count} of ${name} in tenant-a", ImportedBody());
        Assert.DoesNotContain(result.Warnings, w => w.Contains(UnknownVariableText));
    }
}
