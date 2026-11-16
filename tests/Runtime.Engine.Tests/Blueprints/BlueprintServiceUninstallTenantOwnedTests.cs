using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
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

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
/// AB#8966 - uninstalling a blueprint erases its tenant-owned seed entities (<c>rtBlueprintLocked: false</c> in the seed)
/// the tenant never edited - same comparison as the AB#8778 update rule: the content against the seed of the installed
/// version, no stored hash. Edited ones are kept (reason <c>Edited</c>), unedited ones an entity outside the uninstall
/// references are kept (reason <c>Referenced</c>), locked entities are erased as before, and the dry run reports the same
/// without changing anything.
/// </summary>
public class BlueprintServiceUninstallTenantOwnedTests
{
    private const string TenantId = "tenant-a";
    private static readonly BlueprintId V1 = new("Shop-1.0.0");
    private static readonly RtCkId<CkTypeId> RuleType = new("Test-1.0.0/Rule");
    private static readonly RtCkId<CkTypeId> NoteType = new("Test-1.0.0/Note");
    private static readonly RtCkId<CkAttributeId> LockedAttr = new("System/RtBlueprintLocked");
    private static readonly RtCkId<CkAttributeId> ThresholdAttr = new CkId<CkAttributeId>("Test-1.0.0/Threshold").ToRtCkId();
    private static readonly RtCkId<CkAssociationRoleId> RelatedRole = new("Test-1.0.0/Related");

    /// <summary>A to-one role out of Rule (a version belongs to one page); Related is to-many (links between pages).</summary>
    private static readonly RtCkId<CkAssociationRoleId> VersionOfRole =
        new CkId<CkAssociationRoleId>("Test-1.0.0/VersionOf").ToRtCkId();

    private static readonly RtCkId<CkAttributeId> SummaryAttr = new CkId<CkAttributeId>("Test-1.0.0/Summary").ToRtCkId();

    /// <summary>
    /// One seed entity: key, the seed's lock flag (null = absent, i.e. locked), a payload value and the keys (or raw
    /// rtIds) its seed-declared associations point to.
    /// </summary>
    private sealed record SeedSpec(string Key, bool? Locked, int Threshold = 1, params string[] Links)
    {
        /// <summary>The CK type of the entity (default Rule).</summary>
        public RtCkId<CkTypeId> Type { get; init; } = RuleType;

        /// <summary>The rtId the seed gives it; default derived from the key.</summary>
        public string? RtIdKey { get; init; }

        /// <summary>False: the seed gives the entity no well-known name (its key is then its rtId).</summary>
        public bool Named { get; init; } = true;

        /// <summary>The key of the entity this one is a seeded child of (to-one VersionOf association).</summary>
        public string? VersionOf { get; init; }

        /// <summary>A TenantOwned content attribute (a page's summary).</summary>
        public string? Summary { get; init; }
    }

    private static OctoObjectId IdOf(string key) =>
        new(Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..24]
            .ToLowerInvariant());

    private readonly IBlueprintCatalogManager _catalog = A.Fake<IBlueprintCatalogManager>();
    private readonly ITenantBlueprintHistory _history = A.Fake<ITenantBlueprintHistory>();
    private readonly IRuntimeRepositoryProvider _repositoryProvider = A.Fake<IRuntimeRepositoryProvider>();
    private readonly IRuntimeRepository _repository = A.Fake<IRuntimeRepository>();
    private readonly IImportRtModelCommand _import = A.Fake<IImportRtModelCommand>();
    private readonly IRtYamlSerializer _serializer = A.Fake<IRtYamlSerializer>();
    private readonly IBlueprintVariableProvider _variables = A.Fake<IBlueprintVariableProvider>();
    private readonly IBlueprintDependencyResolver _resolver = A.Fake<IBlueprintDependencyResolver>();
    private readonly ITenantBlueprintInstallations _installations = A.Fake<ITenantBlueprintInstallations>();
    private readonly IBlueprintNotifications _notifications = A.Fake<IBlueprintNotifications>();
    private readonly ICkCacheService _cache = A.Fake<ICkCacheService>();

    private readonly Dictionary<string, SeedSpec[]> _seeds = [];
    private readonly List<RtEntity> _tenant = [];
    private readonly List<RtAssociation> _associations = [];
    private readonly List<OctoObjectId> _erased = [];
    private readonly HashSet<OctoObjectId> _failingErase = [];
    private readonly List<RtEntityTcDto> _imported = [];
    private BlueprintInstallation? _installation;

    public BlueprintServiceUninstallTenantOwnedTests()
    {
        A.CallTo(() => _catalog.IsExistingAsync(A<BlueprintId>._, A<object?>._)).Returns(true);
        A.CallTo(() => _catalog.GetAsync(A<BlueprintId>._, A<OperationResult>._, A<object?>._, A<CancellationToken?>._))
            .ReturnsLazily((BlueprintId id, OperationResult _, object? _, CancellationToken? _) =>
                Task.FromResult(new BlueprintMetaRootDto { BlueprintId = id, SeedDataPath = "seed.yaml" }));
        A.CallTo(() => _catalog.TryOpenBlueprintFileAsync(A<BlueprintId>._, A<string>._, A<object?>._, A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult<Stream?>(new MemoryStream()));
        // The description handed to the serializer is "<blueprint full name>/<path>".
        A.CallTo(() => _serializer.DeserializeAsync(A<Stream>._, A<string>._, A<OperationResult>._))
            .ReturnsLazily((Stream _, string location, OperationResult _) =>
                Task.FromResult(BuildSeed(_seeds[location[..location.IndexOf('/')]])));
        A.CallTo(() => _variables.GetVariablesAsync(A<string>._, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()));

        A.CallTo(() => _repositoryProvider.GetRepositoryAsync(TenantId, A<CancellationToken>._))
            .Returns(Task.FromResult<IRuntimeRepository?>(_repository));
        A.CallTo(() => _repository.GetRtEntitiesByTypeAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .ReturnsLazily((IOctoSession _, RtCkId<CkTypeId> type, RtEntityQueryOptions _, int? _, int? _) =>
            {
                var set = A.Fake<IResultSet<RtEntity>>();
                A.CallTo(() => set.Items).Returns(_tenant.Where(t => Equals(t.CkTypeId, type)).ToList());
                return Task.FromResult(set);
            });
        // The graph rule engine erases an entity together with its associations.
        A.CallTo(() => _repository.DeleteOneRtEntityByRtIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<OctoObjectId>._, A<DeleteOptions>._))
            .Invokes((IOctoSession _, RtCkId<CkTypeId> type, OctoObjectId rtId, DeleteOptions _) =>
            {
                if (_failingErase.Contains(rtId))
                {
                    throw new InvalidOperationException("erase refused");
                }

                _erased.Add(rtId);
                _tenant.RemoveAll(t => t.RtId.Equals(rtId) && Equals(t.CkTypeId, type));
                _associations.RemoveAll(a => (a.OriginRtId.Equals(rtId) && Equals(a.OriginCkTypeId, type))
                                             || (a.TargetRtId.Equals(rtId) && Equals(a.TargetCkTypeId, type)));
            })
            .Returns(Task.CompletedTask);
        A.CallTo(() => _repository.GetRtAssociationsAsync(A<IOctoSession>._, A<IEnumerable<RtEntityId>>._,
                A<RtAssociationExtendedQueryOptions>._))
            .ReturnsLazily((IOctoSession _, IEnumerable<RtEntityId> ids, RtAssociationExtendedQueryOptions _) =>
            {
                var result = new OriginResultSet();
                foreach (var id in ids)
                {
                    var set = A.Fake<IResultSet<RtAssociation>>();
                    // By rtId only: the harsher case - the stored type of an end need not be spelled like the request.
                    var items = _associations
                        .Where(a => a.OriginRtId.Equals(id.RtId) || a.TargetRtId.Equals(id.RtId)).ToList();
                    A.CallTo(() => set.Items).Returns(items);
                    result[id] = set;
                }

                return Task.FromResult<IMultipleOriginResultSet<RtAssociation>>(result);
            });
        A.CallTo(() => _import.ImportModelAsync(A<IRuntimeRepository>._, A<RtModelRootTcDto>._,
                A<ImportStrategy>._, A<CancellationToken?>._, A<RtImportBlankingPolicy>._,
                A<IReadOnlyCollection<RtImportBlankingConfirmation>?>._))
            .Invokes((IRuntimeRepository _, RtModelRootTcDto root, ImportStrategy _, CancellationToken? _,
                RtImportBlankingPolicy _, IReadOnlyCollection<RtImportBlankingConfirmation>? _) =>
                _imported.AddRange(root.Entities))
            .Returns(Task.CompletedTask);

        A.CallTo(() => _installations.GetByBlueprintNameAsync(TenantId, A<string>._, A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult(_installation));
        A.CallTo(() => _installations.GetInstalledAsync(TenantId, A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult<IReadOnlyList<BlueprintInstallation>>(
                _installation == null ? [] : [_installation]));
        A.CallTo(() => _installations.RemoveAsync(TenantId, A<string>._, A<CancellationToken>._))
            .Invokes(() => _installation = null)
            .Returns(Task.FromResult(true));

        _installation = new BlueprintInstallation
        {
            BlueprintId = V1,
            InstalledAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            IsDependency = false
        };
        GivenRuleTypeKnown();
    }

    private sealed class OriginResultSet : Dictionary<RtEntityId, IResultSet<RtAssociation>>,
        IMultipleOriginResultSet<RtAssociation>;

    private BlueprintService CreateService() => new(
        _cache,
        _catalog,
        _history,
        A.Fake<IBlueprintMigrationExecutor>(),
        A.Fake<IBlueprintMigrationParser>(),
        A.Fake<ICkModelUpgradeService>(),
        _repositoryProvider,
        _import,
        _serializer,
        _notifications,
        _resolver,
        _installations,
        _variables,
        NullLogger<BlueprintService>.Instance);

    // ---- scenario building ------------------------------------------------------------------

    private static RtModelRootTcDto BuildSeed(SeedSpec[] specs)
    {
        var root = new RtModelRootTcDto();
        foreach (var spec in specs)
        {
            var entity = new RtEntityTcDto
            {
                RtId = IdOf(spec.RtIdKey ?? spec.Key), CkTypeId = spec.Type, RtWellKnownName = spec.Named ? spec.Key : null
            };
            entity.Attributes.Add(new RtAttributeTcDto { Id = ThresholdAttr, Value = spec.Threshold });
            if (spec.Locked is { } locked)
            {
                entity.Attributes.Add(new RtAttributeTcDto { Id = LockedAttr, Value = locked });
            }

            if (spec.Summary != null)
            {
                entity.Attributes.Add(new RtAttributeTcDto { Id = SummaryAttr, Value = spec.Summary });
            }

            var associations = spec.Links
                .Select(target => new RtAssociationTcDto
                {
                    RoleId = RelatedRole,
                    TargetRtId = TargetId(target),
                    TargetCkTypeId = RuleType
                })
                .ToList();
            if (spec.VersionOf != null)
            {
                associations.Add(new RtAssociationTcDto
                {
                    RoleId = VersionOfRole, TargetRtId = TargetId(spec.VersionOf), TargetCkTypeId = RuleType
                });
            }

            if (associations.Count > 0)
            {
                entity.Associations = associations;
            }

            root.Entities.Add(entity);
        }

        return root;
    }

    /// <summary>A raw 24-hex rtId stays as it is, anything else is a key.</summary>
    private static OctoObjectId TargetId(string keyOrRtId) =>
        keyOrRtId.Length == 24 && keyOrRtId.All(Uri.IsHexDigit) ? new OctoObjectId(keyOrRtId) : IdOf(keyOrRtId);

    private void GivenSeed(params SeedSpec[] specs) => _seeds[V1.FullName] = specs;

    /// <summary>A tenant entity of the blueprint as the install wrote it (seed value 1), or edited (another value).</summary>
    private RtEntity GivenTenantEntity(string key, bool storedLocked, int threshold = 1, BlueprintId? source = null,
        RtCkId<CkTypeId>? type = null, string? rtIdKey = null, string? summary = null)
    {
        var attributes = new Dictionary<string, object?>
        {
            ["RtBlueprintSource"] = (source ?? V1).FullName,
            ["RtBlueprintLocked"] = storedLocked,
            ["Threshold"] = threshold
        };
        if (summary != null)
        {
            attributes["Summary"] = summary;
        }

        var entity = new RtEntity(type ?? RuleType, IdOf(rtIdKey ?? key), attributes)
        {
            RtWellKnownName = key
        };
        _tenant.Add(entity);
        return entity;
    }

    /// <summary>An entity the tenant created itself (no blueprint source).</summary>
    private RtEntity GivenUserEntity(string rtId)
    {
        var entity = new RtEntity(RuleType, new OctoObjectId(rtId), new Dictionary<string, object?> { ["Threshold"] = 5 });
        _tenant.Add(entity);
        return entity;
    }

    private int _associationIndex;

    private void GivenAssociation(OctoObjectId origin, OctoObjectId target, RtCkId<CkAssociationRoleId>? role = null,
        RtCkId<CkTypeId>? originType = null, RtCkId<CkTypeId>? targetType = null) =>
        _associations.Add(new RtAssociation(role ?? RelatedRole, new OctoObjectId($"aa{++_associationIndex:x22}"),
            new Dictionary<string, object?>())
        {
            OriginRtId = origin,
            OriginCkTypeId = originType ?? RuleType,
            TargetRtId = target,
            TargetCkTypeId = targetType ?? RuleType
        });

    private void GivenRuleTypeKnown()
    {
        var attrId = new CkId<CkAttributeId>("Test-1.0.0/Threshold");
        var threshold = new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = "Threshold" },
            new CkAttributeGraph(attrId, new CkAttributeDto
            {
                AttributeId = "Threshold", ValueType = AttributeValueTypesDto.Integer
            }));
        var summaryId = new CkId<CkAttributeId>("Test-1.0.0/Summary");
        var summary = new CkTypeAttributeGraph(summaryId,
            new CkTypeAttributeDto
            {
                CkAttributeId = summaryId, AttributeName = "Summary", IsOptional = true,
                Ownership = AttributeOwnershipDto.TenantOwned
            },
            new CkAttributeGraph(summaryId, new CkAttributeDto
            {
                AttributeId = "Summary", ValueType = AttributeValueTypesDto.String
            }));
        var ruleId = new CkId<CkTypeId>("Test-1.0.0/Rule");
        var outbound = new CkGraphAssociationInheritance(
        [
            new CkTypeAssociationGraph("versionOf", MultiplicitiesDto.ZeroOrOne,
                new CkId<CkAssociationRoleId>("Test-1.0.0/VersionOf"), ruleId, ruleId, null),
            new CkTypeAssociationGraph("related", MultiplicitiesDto.N,
                new CkId<CkAssociationRoleId>("Test-1.0.0/Related"), ruleId, ruleId, null)
        ], []);
        var graph = new CkTypeGraph(ruleId, isAbstract: false, isFinal: false,
            isCollectionRoot: false, baseTypes: [], derivedFromCkTypeId: null, definingCollectionRootCkTypeId: null,
            derivedTypes: [], definedAttributes: [],
            allAttributes: new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph>
            {
                [attrId] = threshold, [summaryId] = summary
            },
            indexes: [],
            associations: new CkGraphDirectedAssociations([], new CkGraphAssociationInheritance(), outbound),
            description: "test",
            enableChangeStreamPreAndPostImages: false);
        CkTypeGraph? ignored;
        A.CallTo(() => _cache.TryGetRtCkType(A<string>._, RuleType, out ignored))
            .Returns(true)
            .AssignsOutAndRefParameters(graph);
        A.CallTo(() => _cache.TryGetRtCkType(A<string>._, NoteType, out ignored))
            .Returns(true)
            .AssignsOutAndRefParameters(graph);
    }

    private void GivenRuleTypeUnknown()
    {
        CkTypeGraph? ignored;
        A.CallTo(() => _cache.TryGetRtCkType(A<string>._, RuleType, out ignored)).Returns(false);
    }

    private Task<BlueprintUninstallResult> Uninstall() =>
        CreateService().UninstallAsync(TenantId, V1.Name, cancellationToken: TestContext.Current.CancellationToken);

    private Task<BlueprintUninstallResult> PreviewUninstall() =>
        CreateService().PreviewUninstallAsync(TenantId, V1.Name, cancellationToken: TestContext.Current.CancellationToken);

    private static string[] Keys(IEnumerable<BlueprintTenantOwnedEntity> entities) =>
        entities.Select(e => e.Key).Order(StringComparer.Ordinal).ToArray();

    // ---- unedited / edited / not comparable --------------------------------------------------

    [Fact]
    public async Task Uninstall_UneditedTenantOwnedEntity_IsErasedAndCounted()
    {
        GivenSeed(new SeedSpec("page", false));
        var page = GivenTenantEntity("page", storedLocked: false);

        var result = await Uninstall();

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(result.DryRun);
        Assert.Equal([page.RtId], _erased);
        Assert.Equal(1, result.EntitiesDeleted);
        var deleted = Assert.Single(result.DeletedEntities);
        Assert.Equal("page", deleted.Key);
        Assert.True(deleted.TenantOwned);
        Assert.Empty(result.TenantOwnedKept);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("keeping as user data"));
    }

    [Fact]
    public async Task Uninstall_EditedTenantOwnedEntity_IsKeptWithReasonEdited()
    {
        GivenSeed(new SeedSpec("page", false, Threshold: 1));
        var page = GivenTenantEntity("page", storedLocked: false, threshold: 7);

        var result = await Uninstall();

        Assert.True(result.Success);
        Assert.Empty(_erased);
        Assert.Equal(0, result.EntitiesDeleted);
        var kept = Assert.Single(result.TenantOwnedKept);
        Assert.Equal("page", kept.Key);
        Assert.Equal(RuleType.ToString(), kept.CkTypeId);
        Assert.Equal(page.RtId.ToString(), kept.EntityId);
        Assert.Equal(BlueprintTenantOwnedKeptReason.Edited, kept.Reason);
        Assert.Contains(result.Warnings, w => w.Contains("TenantOwnedKept") && w.Contains("page (Edited)"));
    }

    [Fact]
    public async Task Uninstall_TenantOwnedEntityTheTenantRenamed_IsKeptAsEdited()
    {
        GivenSeed(new SeedSpec("page", false));
        var page = GivenTenantEntity("page", storedLocked: false);
        page.RtWellKnownName = "renamed-by-tenant";

        var result = await Uninstall();

        Assert.Empty(_erased);
        Assert.Equal(BlueprintTenantOwnedKeptReason.Edited, Assert.Single(result.TenantOwnedKept).Reason);
    }

    [Fact]
    public async Task Uninstall_CkTypeNotCached_TenantOwnedEntityIsKeptAsNotComparable()
    {
        GivenRuleTypeUnknown();
        GivenSeed(new SeedSpec("page", false));
        GivenTenantEntity("page", storedLocked: false);

        var result = await Uninstall();

        Assert.Empty(_erased);
        Assert.Equal(BlueprintTenantOwnedKeptReason.NotComparable, Assert.Single(result.TenantOwnedKept).Reason);
    }

    [Fact]
    public async Task Uninstall_TenantOwnedEntityOfAnotherBlueprint_IsLeftAlone()
    {
        GivenSeed(new SeedSpec("page", false));
        GivenTenantEntity("page", storedLocked: false, source: new BlueprintId("Other-1.0.0"));

        var result = await Uninstall();

        Assert.Empty(_erased);
        Assert.Empty(result.TenantOwnedKept);
        Assert.Empty(result.DeletedEntities);
    }

    // ---- locked and product-owned entities keep their behaviour ------------------------------

    [Fact]
    public async Task Uninstall_LockedEntity_IsErasedAsBefore()
    {
        GivenSeed(new SeedSpec("product", null, Threshold: 1));
        // Even with a value the tenant changed: a locked entity belongs to the blueprint.
        var product = GivenTenantEntity("product", storedLocked: true, threshold: 9);

        var result = await Uninstall();

        Assert.Equal([product.RtId], _erased);
        var deleted = Assert.Single(result.DeletedEntities);
        Assert.False(deleted.TenantOwned);
        Assert.Equal(1, result.EntitiesDeleted);
    }

    [Fact]
    public async Task Uninstall_UnlockedCopyOfProductOwnedSeedEntity_IsKeptAsUserDataAsBefore()
    {
        GivenSeed(new SeedSpec("product", null));
        GivenTenantEntity("product", storedLocked: false);

        var result = await Uninstall();

        Assert.Empty(_erased);
        Assert.Empty(result.TenantOwnedKept);
        Assert.Contains(result.Warnings, w => w.Contains("keeping as user data"));
    }

    // ---- referenced ----------------------------------------------------------------------------

    [Fact]
    public async Task Uninstall_UneditedEntityReferencedByAUserEntity_IsKeptWithReasonReferenced()
    {
        const string userRtId = "cc0000000000000000000001";
        GivenSeed(new SeedSpec("page", false), new SeedSpec("other", false));
        var page = GivenTenantEntity("page", storedLocked: false);
        var other = GivenTenantEntity("other", storedLocked: false);
        var user = GivenUserEntity(userRtId);
        GivenAssociation(user.RtId, page.RtId);

        var result = await Uninstall();

        Assert.Equal([other.RtId], _erased);
        var kept = Assert.Single(result.TenantOwnedKept);
        Assert.Equal("page", kept.Key);
        Assert.Equal(BlueprintTenantOwnedKeptReason.Referenced, kept.Reason);
        Assert.Equal([userRtId], kept.ReferencedBy);
        // No association left pointing to an erased entity.
        Assert.DoesNotContain(_associations, a => _erased.Contains(a.OriginRtId) || _erased.Contains(a.TargetRtId));
        Assert.Single(_associations);
    }

    [Fact]
    public async Task Uninstall_UneditedEntityPointingToAUserEntityWithoutSeedDeclaration_IsKeptAsReferenced()
    {
        // The tenant linked the seeded page to its own entity: erasing the page would drop the tenant's link.
        GivenSeed(new SeedSpec("page", false));
        var page = GivenTenantEntity("page", storedLocked: false);
        var user = GivenUserEntity("cc0000000000000000000002");
        GivenAssociation(page.RtId, user.RtId);

        var result = await Uninstall();

        Assert.Empty(_erased);
        Assert.Equal(BlueprintTenantOwnedKeptReason.Referenced, Assert.Single(result.TenantOwnedKept).Reason);
    }

    [Fact]
    public async Task Uninstall_UneditedPageAndVersionLinkedBySeed_AreBothErased()
    {
        GivenSeed(new SeedSpec("page", false, 1, "page@1"), new SeedSpec("page@1", false));
        var page = GivenTenantEntity("page", storedLocked: false);
        var version = GivenTenantEntity("page@1", storedLocked: false);
        GivenAssociation(page.RtId, version.RtId);

        var result = await Uninstall();

        Assert.Equal(2, result.EntitiesDeleted);
        Assert.Equal(new[] { page.RtId, version.RtId }.Order(), _erased.Order());
        Assert.Empty(result.TenantOwnedKept);
        Assert.Empty(_associations);
    }

    [Fact]
    public async Task Uninstall_EditedPage_KeepsItsSeededVersion()
    {
        // The version is a seeded child of the page (to-one VersionOf, declared on the version): it stays with the page.
        GivenSeed(new SeedSpec("page", false), new SeedSpec("page@1", false) { VersionOf = "page" },
            new SeedSpec("other", false));
        var page = GivenTenantEntity("page", storedLocked: false, threshold: 7);
        var version = GivenTenantEntity("page@1", storedLocked: false);
        var other = GivenTenantEntity("other", storedLocked: false);
        GivenAssociation(version.RtId, page.RtId, VersionOfRole);

        var result = await Uninstall();

        Assert.Equal([other.RtId], _erased);
        Assert.Equal(["page", "page@1"], Keys(result.TenantOwnedKept));
        Assert.Equal(BlueprintTenantOwnedKeptReason.Edited, result.TenantOwnedKept.Single(k => k.Key == "page").Reason);
        var keptVersion = result.TenantOwnedKept.Single(k => k.Key == "page@1");
        Assert.Equal(BlueprintTenantOwnedKeptReason.Referenced, keptVersion.Reason);
        Assert.Equal([page.RtId.ToString()], keptVersion.ReferencedBy);
        Assert.Single(_associations);
    }

    [Fact]
    public async Task Uninstall_SeedLinksBetweenPages_DoNotSpreadAKeep()
    {
        // The edited page links (seed-declared, to-many) to an unedited page, which links on: no propagation. The linked
        // pages are erased; their links to the kept page go with them.
        GivenSeed(new SeedSpec("edited", false, 1, "b"), new SeedSpec("b", false, 1, "c", "edited"),
            new SeedSpec("c", false, 1, "b"));
        var edited = GivenTenantEntity("edited", storedLocked: false, threshold: 7);
        var b = GivenTenantEntity("b", storedLocked: false);
        var c = GivenTenantEntity("c", storedLocked: false);
        GivenAssociation(edited.RtId, b.RtId);
        GivenAssociation(b.RtId, c.RtId);
        GivenAssociation(b.RtId, edited.RtId);
        GivenAssociation(c.RtId, b.RtId);

        var result = await Uninstall();

        Assert.Equal(new[] { b.RtId, c.RtId }.Order(), _erased.Order());
        Assert.Equal(["edited"], Keys(result.TenantOwnedKept));
        Assert.Empty(_associations);
    }

    [Fact]
    public async Task Uninstall_KbTest2Shape_KeepsOnlyTheReferencedAndTheEditedPageWithTheirVersions()
    {
        // System.Ai.Builder on kb-test2 (AB#8966 comment 6667842): 23 pages, every page links to every other page (seed
        // LinksTo), each page has its version (VersionOf). A tenant answer page links to one page; the tenant added a new
        // version to another page. Expected: those two pages and their seeded versions stay, 21 pages + versions go.
        const int pages = 23;
        var keys = Enumerable.Range(1, pages).Select(i => $"p{i:00}").ToList();
        var seed = new List<SeedSpec>();
        foreach (var key in keys)
        {
            seed.Add(new SeedSpec(key, false, 1, keys.Where(k => k != key).ToArray()));
            seed.Add(new SeedSpec($"{key}@1", false) { VersionOf = key });
        }
        GivenSeed(seed.ToArray());
        foreach (var key in keys)
        {
            var page = GivenTenantEntity(key, storedLocked: false);
            var version = GivenTenantEntity($"{key}@1", storedLocked: false);
            GivenAssociation(version.RtId, page.RtId, VersionOfRole);
            foreach (var target in keys.Where(k => k != key))
            {
                GivenAssociation(page.RtId, IdOf(target));
            }
        }

        var answer = GivenUserEntity("cc00000000000000000000a1");
        GivenAssociation(answer.RtId, IdOf("p01"));
        var tenantVersion = GivenUserEntity("cc00000000000000000000a2");
        GivenAssociation(tenantVersion.RtId, IdOf("p02"), VersionOfRole);

        var preview = await PreviewUninstall();
        var result = await Uninstall();

        Assert.Equal(["p01", "p01@1", "p02", "p02@1"], Keys(result.TenantOwnedKept));
        Assert.Equal(BlueprintTenantOwnedKeptReason.Referenced, result.TenantOwnedKept.Single(k => k.Key == "p01").Reason);
        Assert.Equal([answer.RtId.ToString()], result.TenantOwnedKept.Single(k => k.Key == "p01").ReferencedBy);
        Assert.Equal(BlueprintTenantOwnedKeptReason.Edited, result.TenantOwnedKept.Single(k => k.Key == "p02").Reason);
        Assert.Equal([tenantVersion.RtId.ToString()], result.TenantOwnedKept.Single(k => k.Key == "p02").ReferencedBy);
        Assert.Equal(BlueprintTenantOwnedKeptReason.Referenced, result.TenantOwnedKept.Single(k => k.Key == "p01@1").Reason);
        Assert.Equal(42, result.EntitiesDeleted);
        Assert.Equal(Keys(preview.DeletedEntities), Keys(result.DeletedEntities));
        Assert.Equal(Keys(preview.TenantOwnedKept), Keys(result.TenantOwnedKept));
        // No association points to an erased entity.
        Assert.DoesNotContain(_associations, a => _erased.Contains(a.OriginRtId) || _erased.Contains(a.TargetRtId));
    }

    [Fact]
    public async Task Uninstall_TenantOwnedContentChanged_IsEdited()
    {
        // TenantOwned attributes are the content a tenant edits (a page's summary); a change there is an edit.
        GivenSeed(new SeedSpec("page", false) { Summary = "seeded" }, new SeedSpec("same", false) { Summary = "seeded" });
        GivenTenantEntity("page", storedLocked: false, summary: "rewritten by the tenant");
        var same = GivenTenantEntity("same", storedLocked: false, summary: "seeded");

        var result = await Uninstall();

        Assert.Equal([same.RtId], _erased);
        Assert.Equal(BlueprintTenantOwnedKeptReason.Edited, Assert.Single(result.TenantOwnedKept).Reason);
    }

    [Fact]
    public async Task Uninstall_ReferenceChain_IsFollowedToTheFixpoint()
    {
        // user -> a, a -> b (tenant link), b -> c (tenant link): all three stay once a stays.
        GivenSeed(new SeedSpec("a", false), new SeedSpec("b", false), new SeedSpec("c", false), new SeedSpec("d", false));
        var a = GivenTenantEntity("a", storedLocked: false);
        var b = GivenTenantEntity("b", storedLocked: false);
        var c = GivenTenantEntity("c", storedLocked: false);
        var d = GivenTenantEntity("d", storedLocked: false);
        var user = GivenUserEntity("cc0000000000000000000003");
        GivenAssociation(user.RtId, a.RtId);
        GivenAssociation(a.RtId, b.RtId);
        GivenAssociation(b.RtId, c.RtId);

        var result = await Uninstall();

        Assert.Equal([d.RtId], _erased);
        Assert.Equal(["a", "b", "c"], Keys(result.TenantOwnedKept));
        Assert.All(result.TenantOwnedKept, k => Assert.Equal(BlueprintTenantOwnedKeptReason.Referenced, k.Reason));
    }

    [Fact]
    public async Task Uninstall_SeedDeclaredLinkToAnEntityOutsideTheSeed_DoesNotKeepTheEntity()
    {
        // A blueprint-seeded association to, e.g., an entity of another blueprint: erased with the page.
        const string foreignRtId = "dd0000000000000000000001";
        GivenSeed(new SeedSpec("page", false, 1, foreignRtId));
        var page = GivenTenantEntity("page", storedLocked: false);
        var foreign = GivenUserEntity(foreignRtId);
        GivenAssociation(page.RtId, foreign.RtId);

        var result = await Uninstall();

        Assert.Equal([page.RtId], _erased);
        Assert.Empty(result.TenantOwnedKept);
        Assert.Empty(_associations);
    }

    [Fact]
    public async Task Uninstall_SeedDeclaredLinkStoredUnderAnotherRoleSpelling_IsStillTheEntitysOwnLink()
    {
        // The stored role id is the one the CK cache resolved; it need not be spelled like the seed's.
        const string foreignRtId = "dd0000000000000000000002";
        GivenSeed(new SeedSpec("page", false, 1, foreignRtId));
        var page = GivenTenantEntity("page", storedLocked: false);
        var foreign = GivenUserEntity(foreignRtId);
        GivenAssociation(page.RtId, foreign.RtId, new RtCkId<CkAssociationRoleId>("Test-2.0.0/Related"));

        var result = await Uninstall();

        Assert.Equal([page.RtId], _erased);
        Assert.Empty(result.TenantOwnedKept);
    }

    [Fact]
    public async Task Uninstall_UneditedEntityLinkedToALockedEntityOfTheBlueprint_IsErasedWithIt()
    {
        GivenSeed(new SeedSpec("page", false), new SeedSpec("space", null));
        var page = GivenTenantEntity("page", storedLocked: false);
        var space = GivenTenantEntity("space", storedLocked: true);
        GivenAssociation(page.RtId, space.RtId);

        var result = await Uninstall();

        Assert.Equal(2, result.EntitiesDeleted);
        Assert.Empty(result.TenantOwnedKept);
        Assert.Empty(_associations);
    }

    // ---- CodeRabbit round 1 -----------------------------------------------------------------------

    [Fact]
    public async Task Uninstall_TenantAddedAWellKnownNameTheSeedDoesNotHave_IsKeptAsEdited()
    {
        GivenSeed(new SeedSpec("page", false) { Named = false });
        var page = GivenTenantEntity("page", storedLocked: false);   // the tenant gave it the name "page"

        var result = await Uninstall();

        Assert.Empty(_erased);
        var kept = Assert.Single(result.TenantOwnedKept);
        Assert.Equal(page.RtId.ToString(), kept.Key);
        Assert.Equal(BlueprintTenantOwnedKeptReason.Edited, kept.Reason);
    }

    [Fact]
    public async Task Uninstall_SameRtIdUnderTwoCkTypes_AreTwoEntities()
    {
        // Seeds reuse an rtId across CK types. The locked Note "x" is erased; the edited Rule with the same rtId stays, so
        // the unedited Rule linked to it must stay too.
        GivenSeed(new SeedSpec("x-note", null) { Type = NoteType, RtIdKey = "x" }, new SeedSpec("x-rule", false) { RtIdKey = "x" },
            new SeedSpec("child", false));
        var note = GivenTenantEntity("x-note", storedLocked: true, type: NoteType, rtIdKey: "x");
        var rule = GivenTenantEntity("x-rule", storedLocked: false, threshold: 7, rtIdKey: "x");
        var child = GivenTenantEntity("child", storedLocked: false);
        GivenAssociation(rule.RtId, child.RtId);

        var result = await Uninstall();

        Assert.Equal([note.RtId], _erased);
        Assert.Equal(["child", "x-rule"], Keys(result.TenantOwnedKept));
        Assert.Equal(BlueprintTenantOwnedKeptReason.Referenced, result.TenantOwnedKept.Single(k => k.Key == "child").Reason);
        Assert.Contains(_tenant, t => ReferenceEquals(t, rule));
    }

    [Fact]
    public async Task Uninstall_SeedLinkToARenamedUnlockedProductOwnedParent_KeepsTheChild()
    {
        // The parent stays (unlocked by the tenant, renamed, so the key match misses it); the child is its seeded child
        // (to-one link into this blueprint's seed), so the child is kept.
        GivenSeed(new SeedSpec("parent", null), new SeedSpec("child", false) { VersionOf = "parent" });
        var parent = GivenTenantEntity("parent", storedLocked: false);
        parent.RtWellKnownName = "renamed-by-tenant";
        var child = GivenTenantEntity("child", storedLocked: false);
        GivenAssociation(child.RtId, parent.RtId, VersionOfRole);

        var result = await Uninstall();

        Assert.Empty(_erased);
        Assert.Equal(BlueprintTenantOwnedKeptReason.Referenced, Assert.Single(result.TenantOwnedKept).Reason);
    }

    [Fact]
    public async Task Uninstall_EraseOfALockedNeighbourFails_KeepsTheLinkedCandidate()
    {
        GivenSeed(new SeedSpec("space", null), new SeedSpec("page", false), new SeedSpec("other", false));
        var space = GivenTenantEntity("space", storedLocked: true);
        var page = GivenTenantEntity("page", storedLocked: false);
        var other = GivenTenantEntity("other", storedLocked: false);
        GivenAssociation(page.RtId, space.RtId);
        _failingErase.Add(space.RtId);

        var result = await Uninstall();

        Assert.Equal([other.RtId], _erased);
        var kept = Assert.Single(result.TenantOwnedKept);
        Assert.Equal("page", kept.Key);
        Assert.Equal([space.RtId.ToString()], kept.ReferencedBy);
        Assert.Contains(result.Warnings, w => w.Contains("Failed to delete entity 'space'"));
        Assert.Single(_associations);
        Assert.Equal(1, result.EntitiesDeleted);
    }

    [Fact]
    public async Task Uninstall_EraseOfACandidateFails_KeepsTheCandidatesLinkedToIt()
    {
        GivenSeed(new SeedSpec("a", false), new SeedSpec("b", false));
        var a = GivenTenantEntity("a", storedLocked: false);
        var b = GivenTenantEntity("b", storedLocked: false);
        GivenAssociation(a.RtId, b.RtId);
        _failingErase.Add(a.RtId);

        var result = await Uninstall();

        Assert.Empty(_erased);
        Assert.Equal(["a", "b"], Keys(result.TenantOwnedKept));
        Assert.Equal(BlueprintTenantOwnedKeptReason.EraseFailed, result.TenantOwnedKept.Single(k => k.Key == "a").Reason);
        Assert.Equal(BlueprintTenantOwnedKeptReason.Referenced, result.TenantOwnedKept.Single(k => k.Key == "b").Reason);
        Assert.Empty(result.DeletedEntities);
        Assert.Single(_associations);
    }

    [Fact]
    public async Task Uninstall_ExternalEntityWithTheRtIdOfAnErasedEntityOfAnotherType_KeepsTheCandidate()
    {
        // Locked Note "x" is erased; a user entity Rule "x" (same rtId, other type, outside the uninstall) stays and is
        // linked to the candidate, so the candidate stays.
        GivenSeed(new SeedSpec("x-note", null) { Type = NoteType, RtIdKey = "x" }, new SeedSpec("c", false));
        var note = GivenTenantEntity("x-note", storedLocked: true, type: NoteType, rtIdKey: "x");
        var c = GivenTenantEntity("c", storedLocked: false);
        var external = GivenUserEntity(IdOf("x").ToString()!);
        GivenAssociation(external.RtId, c.RtId);

        var result = await Uninstall();

        Assert.Equal([note.RtId], _erased);
        Assert.Equal(BlueprintTenantOwnedKeptReason.Referenced, Assert.Single(result.TenantOwnedKept).Reason);
    }

    [Fact]
    public async Task Uninstall_LinkToAnErasedNeighbourStoredUnderAResolvedModelVersion_StillErasesBoth()
    {
        // The stored end type carries another model spelling of the same type name: the neighbour is erased all the same.
        GivenSeed(new SeedSpec("page", false), new SeedSpec("space", null));
        var page = GivenTenantEntity("page", storedLocked: false);
        var space = GivenTenantEntity("space", storedLocked: true);
        GivenAssociation(page.RtId, space.RtId, targetType: new RtCkId<CkTypeId>("Test-2.0.0/Rule"));

        var result = await Uninstall();

        Assert.Equal(2, result.EntitiesDeleted);
        Assert.Empty(result.TenantOwnedKept);
    }

    [Fact]
    public async Task Uninstall_EraseFailsAfterTheLinkedCandidateWasErased_ListsEachEntityOnce()
    {
        // b is erased first; a's erase fails afterwards. b must not show up as kept as well.
        GivenSeed(new SeedSpec("b", false), new SeedSpec("a", false));
        var a = GivenTenantEntity("a", storedLocked: false);
        var b = GivenTenantEntity("b", storedLocked: false);
        GivenAssociation(a.RtId, b.RtId);
        _failingErase.Add(a.RtId);

        var result = await Uninstall();

        Assert.Equal([b.RtId], _erased);
        Assert.Equal(["b"], Keys(result.DeletedEntities));
        var kept = Assert.Single(result.TenantOwnedKept);
        Assert.Equal("a", kept.Key);
        Assert.Equal(BlueprintTenantOwnedKeptReason.EraseFailed, kept.Reason);
    }

    [Fact]
    public async Task Uninstall_AssociationEndStoredUnderAnotherTypeSpelling_StillKeepsTheReferencedEntity()
    {
        // The stored type of an association end can differ from the seed's spelling (resolved, concrete type).
        GivenSeed(new SeedSpec("page", false));
        var page = GivenTenantEntity("page", storedLocked: false);
        var user = GivenUserEntity("cc0000000000000000000009");
        GivenAssociation(user.RtId, page.RtId, targetType: new RtCkId<CkTypeId>("Test-1.1.0/RuleConcrete"));

        var result = await Uninstall();

        Assert.Empty(_erased);
        Assert.Equal(BlueprintTenantOwnedKeptReason.Referenced, Assert.Single(result.TenantOwnedKept).Reason);
    }

    [Fact]
    public async Task PreviewUninstall_Cascade_DependentAlreadyRemovedByAnEarlierDependent_IsSkipped()
    {
        // A and B depend on the base; B also depends on A. The cascade of A removes B first; the base must not fail on B.
        var a = Installation("A-1.0.0", isDependency: false, V1);
        var b = Installation("B-1.0.0", isDependency: false, V1, a.BlueprintId);
        GivenSeed(new SeedSpec("base-page", false));
        _seeds[a.BlueprintId.FullName] = [new SeedSpec("a-page", false)];
        _seeds[b.BlueprintId.FullName] = [new SeedSpec("b-page", false)];
        GivenTenantEntity("base-page", storedLocked: false);
        GivenTenantEntity("a-page", storedLocked: false, source: a.BlueprintId);
        GivenTenantEntity("b-page", storedLocked: false, source: b.BlueprintId);
        GivenInstallations(_installation!, a, b);

        var preview = await CreateService().PreviewUninstallAsync(TenantId, V1.Name, cascade: true,
            TestContext.Current.CancellationToken);

        Assert.True(preview.Success, string.Join("; ", preview.Errors));
        Assert.Equal(["A", "B"], preview.CascadedDependencies.Select(d => d.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["a-page", "b-page", "base-page"], Keys(preview.DeletedEntities));
    }

    [Fact]
    public async Task PreviewUninstall_Cascade_ActiveAncestorIsNotOrphanCleanedTwice()
    {
        // The base came in as a dependency of A. Previewing the base with cascade previews A first; A's orphan cleanup
        // must not preview the base again while the outer call handles it.
        _installation!.IsDependency = true;
        var a = Installation("A-1.0.0", isDependency: false, V1);
        GivenSeed(new SeedSpec("base-page", false));
        _seeds[a.BlueprintId.FullName] = [new SeedSpec("a-page", false)];
        GivenTenantEntity("base-page", storedLocked: false);
        GivenTenantEntity("a-page", storedLocked: false, source: a.BlueprintId);
        a.ResolvedDependencies = [V1];
        GivenInstallations(_installation!, a);

        var preview = await CreateService().PreviewUninstallAsync(TenantId, V1.Name, cascade: true,
            TestContext.Current.CancellationToken);

        Assert.True(preview.Success, string.Join("; ", preview.Errors));
        Assert.Equal(["A"], preview.CascadedDependencies.Select(d => d.Name));
        Assert.Equal(["a-page", "base-page"], Keys(preview.DeletedEntities));
        Assert.Equal(2, preview.EntitiesDeleted);
    }

    private static BlueprintInstallation Installation(string id, bool isDependency, params BlueprintId[] dependencies) => new()
    {
        BlueprintId = new BlueprintId(id),
        InstalledAt = DateTime.UtcNow,
        LastUpdatedAt = DateTime.UtcNow,
        IsDependency = isDependency,
        ResolvedDependencies = dependencies.ToList()
    };

    private void GivenInstallations(params BlueprintInstallation[] installations)
    {
        foreach (var installation in installations)
        {
            A.CallTo(() => _installations.GetByBlueprintNameAsync(TenantId, installation.BlueprintId.Name, A<CancellationToken>._))
                .Returns(Task.FromResult<BlueprintInstallation?>(installation));
        }

        A.CallTo(() => _installations.GetInstalledAsync(TenantId, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<BlueprintInstallation>>(installations));
    }

    // ---- dry run -------------------------------------------------------------------------------

    [Fact]
    public async Task PreviewUninstall_ReportsDeletedAndKeptButChangesNothing()
    {
        GivenSeed(new SeedSpec("unedited", false), new SeedSpec("edited", false), new SeedSpec("referenced", false),
            new SeedSpec("product", null));
        GivenTenantEntity("unedited", storedLocked: false);
        GivenTenantEntity("edited", storedLocked: false, threshold: 9);
        var referenced = GivenTenantEntity("referenced", storedLocked: false);
        GivenTenantEntity("product", storedLocked: true);
        GivenAssociation(GivenUserEntity("cc0000000000000000000004").RtId, referenced.RtId);

        var preview = await PreviewUninstall();

        Assert.True(preview.Success, string.Join("; ", preview.Errors));
        Assert.True(preview.DryRun);
        Assert.Equal(2, preview.EntitiesDeleted);
        Assert.Equal(["product", "unedited"], Keys(preview.DeletedEntities));
        Assert.Equal(["edited", "referenced"], Keys(preview.TenantOwnedKept));
        Assert.Empty(_erased);
        Assert.Equal(5, _tenant.Count);
        Assert.NotNull(_installation);
        A.CallTo(() => _installations.RemoveAsync(A<string>._, A<string>._, A<CancellationToken>._)).MustNotHaveHappened();
        A.CallTo(() => _notifications.NotifyUninstalledAsync(A<BlueprintUninstalledNotification>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        // The real run does what the dry run announced.
        var result = await Uninstall();

        Assert.Equal(Keys(preview.DeletedEntities), Keys(result.DeletedEntities));
        Assert.Equal(Keys(preview.TenantOwnedKept), Keys(result.TenantOwnedKept));
        Assert.Null(_installation);
    }

    [Fact]
    public async Task PreviewUninstall_NotInstalled_FailsWithoutNotification()
    {
        _installation = null;

        var preview = await PreviewUninstall();

        Assert.False(preview.Success);
        Assert.True(preview.DryRun);
        A.CallTo(() => _notifications.NotifyOperationFailedAsync(A<BlueprintOperationFailedNotification>._,
            A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Fact]
    public async Task PreviewUninstall_Cascade_CountsEntitiesOfTheDependentAsErasedFirst()
    {
        // The dependent's page links to the base's page. The real cascade erases the dependent first, so the base page
        // is no longer referenced; the dry run must come to the same answer without erasing anything.
        var dependentId = new BlueprintId("Addon-1.0.0");
        GivenSeed(new SeedSpec("base-page", false));
        _seeds[dependentId.FullName] = [new SeedSpec("addon-page", false, 1, "base-page")];
        var basePage = GivenTenantEntity("base-page", storedLocked: false);
        var addonPage = GivenTenantEntity("addon-page", storedLocked: false, source: dependentId);
        GivenAssociation(addonPage.RtId, basePage.RtId);
        var dependent = new BlueprintInstallation
        {
            BlueprintId = dependentId,
            InstalledAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            IsDependency = false,
            ResolvedDependencies = [V1]
        };
        var baseInstallation = _installation!;
        A.CallTo(() => _installations.GetByBlueprintNameAsync(TenantId, dependentId.Name, A<CancellationToken>._))
            .Returns(Task.FromResult<BlueprintInstallation?>(dependent));
        A.CallTo(() => _installations.GetInstalledAsync(TenantId, A<CancellationToken>._))
            .Returns(Task.FromResult<IReadOnlyList<BlueprintInstallation>>([baseInstallation, dependent]));

        var preview = await CreateService().PreviewUninstallAsync(TenantId, V1.Name, cascade: true,
            TestContext.Current.CancellationToken);

        Assert.True(preview.Success, string.Join("; ", preview.Errors));
        Assert.Equal([dependentId], preview.CascadedDependencies);
        Assert.Equal(["addon-page", "base-page"], Keys(preview.DeletedEntities));
        Assert.Empty(preview.TenantOwnedKept);
        Assert.Empty(_erased);
    }

    // ---- reinstall -------------------------------------------------------------------------------

    [Fact]
    public async Task Reinstall_AfterUninstall_AdoptsKeptEntitiesAndRecreatesErasedOnesOnce()
    {
        GivenSeed(new SeedSpec("unedited", false), new SeedSpec("edited", false), new SeedSpec("product", null));
        GivenTenantEntity("unedited", storedLocked: false);
        var edited = GivenTenantEntity("edited", storedLocked: false, threshold: 9);
        GivenTenantEntity("product", storedLocked: true);

        var uninstall = await Uninstall();
        Assert.Equal(["product", "unedited"], Keys(uninstall.DeletedEntities));
        Assert.Equal([edited], _tenant);

        A.CallTo(() => _resolver.ResolveAsync(V1, A<CancellationToken>._))
            .Returns(new BlueprintResolutionResult
            {
                RootBlueprintId = V1,
                Success = true,
                InstallOrder = [new BlueprintMetaRootDto { BlueprintId = V1, SeedDataPath = "seed.yaml" }],
                Conflicts = [],
                DependencyRanges = new Dictionary<string, List<BlueprintIdVersionRange>>()
            });

        var install = await CreateService().ApplyBlueprintAsync(TenantId, V1, false, TestContext.Current.CancellationToken);

        Assert.True(install.IsSuccess);
        // The kept, edited entity is not written again (no duplicate, its content stays); the erased ones come back once.
        Assert.Equal(["product", "unedited"], _imported.Select(e => e.RtWellKnownName!).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(9, edited.GetAttributeValueOrDefault<int>("Threshold"));
    }
}
