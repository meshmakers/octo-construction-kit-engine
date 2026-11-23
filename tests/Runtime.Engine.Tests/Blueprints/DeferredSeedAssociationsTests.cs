using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Blueprints;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
/// AB#9651 - seed edges to entities the tenant does not hold yet are kept in the tenant and written once both ends
/// exist (the pipelines of a blueprint and the Mesh Adapter that System.Communication seeds after the install).
/// </summary>
public class DeferredSeedAssociationsTests
{
    private const string TenantId = "tenant-a";
    private static readonly BlueprintId Samples = new("Samples.PipelineBasics-2.0.0");
    private static readonly BlueprintId Communication = new("System.Communication-4.6.0");
    private static readonly RtCkId<CkAssociationRoleId> Executes = new("System.Communication-4.6.0/Executes");
    private static readonly RtCkId<CkTypeId> PipelineType = new("System.Communication-4.6.0/Pipeline");
    private static readonly RtCkId<CkTypeId> AdapterType = new("System.Communication-4.6.0/Adapter");
    private static readonly OctoObjectId Pipeline1 = new("aa0000000000000000000002");
    private static readonly OctoObjectId Pipeline2 = new("aa0000000000000000000003");
    private static readonly OctoObjectId MeshAdapter = new("670000000000000000000002");
    private static readonly OctoObjectId OtherAdapter = new("670000000000000000000009");
    private static readonly DateTime Now = new(2026, 11, 23, 8, 0, 0, DateTimeKind.Utc);

    private readonly IRuntimeRepository _repository = A.Fake<IRuntimeRepository>();
    private readonly HashSet<(RtCkId<CkTypeId>, OctoObjectId)> _existing = [];
    private readonly List<RtAssociation> _written = [];
    private RtEntity? _stored;
    private DateTime _now = Now;

    public DeferredSeedAssociationsTests()
    {
        A.CallTo(() => _repository.TenantId).Returns(TenantId);
        A.CallTo(() => _repository.GetRtEntitiesByTypeAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .ReturnsLazily(() => Task.FromResult(ResultSet(_stored == null ? [] : [_stored])));
        A.CallTo(() => _repository.GetRtEntitiesByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<IReadOnlyList<OctoObjectId>>._, A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .ReturnsLazily((IOctoSession _, RtCkId<CkTypeId> type, IReadOnlyList<OctoObjectId> ids,
                    RtEntityQueryOptions _, int? _, int? _) =>
                Task.FromResult(ResultSet(ids.Where(id => _existing.Contains((type, id)))
                    .Select(id => new RtEntity(type, id)).ToList())));
        A.CallTo(() => _repository.CreateTransientRtEntityByRtCkIdAsync(A<RtCkId<CkTypeId>>._))
            .ReturnsLazily((RtCkId<CkTypeId> type) => Task.FromResult(new RtEntity(type, OctoObjectId.GenerateNewId())));
        A.CallTo(() => _repository.InsertOneRtEntityAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._, A<RtEntity>._))
            .Invokes((IOctoSession _, RtCkId<CkTypeId> _, RtEntity entity) => _stored = entity);
        A.CallTo(() => _repository.ReplaceOneRtEntityByIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<OctoObjectId>._, A<RtEntity>._))
            .Invokes((IOctoSession _, RtCkId<CkTypeId> _, OctoObjectId _, RtEntity entity) => _stored = entity);
        A.CallTo(() => _repository.DeleteOneRtEntityByRtIdAsync(A<IOctoSession>._, A<RtCkId<CkTypeId>>._,
                A<OctoObjectId>._, A<DeleteOptions>._))
            .Invokes(() => _stored = null);
        A.CallTo(() => _repository.BulkRtAssociationsAsync(A<IOctoSession>._, A<IEnumerable<RtAssociation>>._,
                A<BulkOperationOptions>._))
            .Invokes((IOctoSession _, IEnumerable<RtAssociation> edges, BulkOperationOptions _) =>
                _written.AddRange(edges));
    }

    private static IResultSet<RtEntity> ResultSet(List<RtEntity> items)
    {
        var set = A.Fake<IResultSet<RtEntity>>();
        A.CallTo(() => set.Items).Returns(items);
        return set;
    }

    private DeferredSeedAssociations Store() =>
        new(_repository, A.Fake<ICkCacheService>(), NullLogger.Instance, () => _now);

    private static RtImportSkippedAssociation Edge(OctoObjectId origin, OctoObjectId target,
        RtCkId<CkAssociationRoleId>? role = null, bool hasAttributes = false) =>
        new(role ?? Executes, PipelineType, origin, AdapterType, target, hasAttributes);

    private static Func<string, Task<bool>> Installed(params string[] names) =>
        name => Task.FromResult(names.Contains(name));

    [Fact]
    public async Task SeedEdgeToAMissingAdapter_IsKept_AndReportedAsWaiting()
    {
        _existing.Add((PipelineType, Pipeline1));

        var outcome = await Store().ProcessAsync(Samples, [Edge(Pipeline1, MeshAdapter)],
            Installed(), CancellationToken.None);

        Assert.Equal(1, outcome.DeferredForCurrent);
        Assert.Contains(MeshAdapter.ToString(), outcome.DeferredSamples.Single());
        Assert.Equal(0, outcome.Applied);
        Assert.Empty(_written);
        Assert.NotNull(_stored);
        Assert.Equal(DeferredSeedAssociations.ConfigurationKey, _stored!.RtWellKnownName);
    }

    [Fact]
    public async Task KeptEdge_IsWritten_ByTheNextSeedImport_OnceBothEndsExist_AndTheStoreIsRemoved()
    {
        _existing.Add((PipelineType, Pipeline1));
        _existing.Add((PipelineType, Pipeline2));
        await Store().ProcessAsync(Samples, [Edge(Pipeline1, MeshAdapter), Edge(Pipeline2, MeshAdapter)],
            Installed(), CancellationToken.None);

        // The service-managed System.Communication seed brings the Mesh Adapter (Communication enabled).
        _existing.Add((AdapterType, MeshAdapter));
        var outcome = await Store().ProcessAsync(Communication, [], Installed(Samples.Name), CancellationToken.None);

        Assert.Equal(2, outcome.Applied);
        Assert.Equal(2, outcome.AppliedByBlueprint[Samples.FullName]);
        Assert.Equal(0, outcome.DeferredForCurrent);
        Assert.Equal([Pipeline1, Pipeline2], _written.Select(e => e.OriginRtId).Order().ToList());
        Assert.All(_written, e =>
        {
            Assert.Equal(MeshAdapter, e.TargetRtId);
            Assert.Equal(Executes, e.AssociationRoleId);
            Assert.Equal(PipelineType, e.OriginCkTypeId);
            Assert.Equal(AdapterType, e.TargetCkTypeId);
        });
        Assert.Null(_stored);
    }

    [Fact]
    public async Task EdgeWithAnEndStillMissing_StaysKept()
    {
        _existing.Add((PipelineType, Pipeline1));
        await Store().ProcessAsync(Samples, [Edge(Pipeline1, MeshAdapter)], Installed(), CancellationToken.None);

        var outcome = await Store().ProcessAsync(Communication, [], Installed(Samples.Name), CancellationToken.None);

        Assert.Equal(0, outcome.Applied);
        Assert.Empty(_written);
        Assert.NotNull(_stored);
    }

    [Fact]
    public async Task EdgeOfAnUninstalledBlueprint_IsDropped_AfterTheGracePeriod_NotBefore()
    {
        _existing.Add((PipelineType, Pipeline1));
        await Store().ProcessAsync(Samples, [Edge(Pipeline1, MeshAdapter)], Installed(), CancellationToken.None);

        _now = Now + DeferredSeedAssociations.UninstalledGracePeriod - TimeSpan.FromMinutes(1);
        var early = await Store().ProcessAsync(Communication, [], Installed(), CancellationToken.None);
        Assert.Equal(0, early.Dropped);
        Assert.NotNull(_stored);

        _now = Now + DeferredSeedAssociations.UninstalledGracePeriod + TimeSpan.FromMinutes(1);
        _existing.Add((AdapterType, MeshAdapter));
        var late = await Store().ProcessAsync(Communication, [], Installed(), CancellationToken.None);

        Assert.Equal(1, late.Dropped);
        Assert.Contains("is not installed", late.DroppedSamples.Single());
        Assert.Empty(_written);
        Assert.Null(_stored);
    }

    [Fact]
    public async Task RemoveBlueprint_ForgetsOnlyItsEdges()
    {
        _existing.Add((PipelineType, Pipeline1));
        await Store().ProcessAsync(Samples, [Edge(Pipeline1, MeshAdapter)], Installed(), CancellationToken.None);
        await Store().ProcessAsync(new BlueprintId("Other-1.0.0"), [Edge(Pipeline2, MeshAdapter)], Installed(),
            CancellationToken.None);

        Assert.Equal(1, await Store().RemoveBlueprintAsync(Samples.Name));

        _existing.Add((PipelineType, Pipeline2));
        _existing.Add((AdapterType, MeshAdapter));
        var outcome = await Store().ProcessAsync(Communication, [], Installed("Other"), CancellationToken.None);
        Assert.Equal(Pipeline2, _written.Single().OriginRtId);
        Assert.Equal(1, outcome.Applied);
    }

    [Fact]
    public async Task UnreadableStore_IsDiscarded_WithoutFailing()
    {
        _stored = new RtEntity(new RtCkId<CkTypeId>("System", "TenantConfiguration"), OctoObjectId.GenerateNewId())
        {
            RtWellKnownName = DeferredSeedAssociations.ConfigurationKey
        };
        _stored.SetAttributeValue("ConfigurationValue",
            ConstructionKit.Contracts.DataTransferObjects.AttributeValueTypesDto.String, "{not json");

        var outcome = await Store().ProcessAsync(Samples, [], Installed(), CancellationToken.None);

        Assert.Equal(0, outcome.Applied);
        Assert.Null(_stored);
    }

    [Fact]
    public void Merge_DoesNotKeep_IdentityRoles_OrEdgesWithAttributes()
    {
        var entries = new List<DeferredSeedAssociation>();
        var outcome = new DeferredSeedAssociationOutcome();

        var changed = DeferredSeedAssociations.Merge(entries, Samples,
        [
            Edge(Pipeline1, MeshAdapter, new RtCkId<CkAssociationRoleId>("System.Identity/AssignedRole")),
            Edge(Pipeline1, MeshAdapter, hasAttributes: true)
        ], Now, outcome);

        Assert.False(changed);
        Assert.Empty(entries);
        Assert.Equal(2, outcome.NotDeferrable);
    }

    [Fact]
    public void Merge_KeepsOneEntryPerEdge_TheLastBlueprintWins()
    {
        var entries = new List<DeferredSeedAssociation>();
        var outcome = new DeferredSeedAssociationOutcome();

        DeferredSeedAssociations.Merge(entries, Samples, [Edge(Pipeline1, MeshAdapter)], Now, outcome);
        DeferredSeedAssociations.Merge(entries, new BlueprintId("Samples.PipelineBasics-2.1.0"),
            [Edge(Pipeline1, MeshAdapter), Edge(Pipeline1, MeshAdapter)], Now, outcome);

        Assert.Equal("Samples.PipelineBasics-2.1.0", entries.Single().Blueprint);
    }

    [Fact]
    public void Merge_StopsAtTheCap_AndReportsTheRest()
    {
        var entries = Enumerable.Range(0, DeferredSeedAssociations.MaxEntries)
            .Select(i => new DeferredSeedAssociation(Samples.FullName, Executes.SemanticVersionedFullName,
                PipelineType.SemanticVersionedFullName, OctoObjectId.GenerateNewId().ToString(),
                AdapterType.SemanticVersionedFullName, MeshAdapter.ToString(), Now))
            .ToList();
        var outcome = new DeferredSeedAssociationOutcome();

        var changed = DeferredSeedAssociations.Merge(entries, Samples, [Edge(Pipeline1, MeshAdapter)], Now, outcome);

        Assert.False(changed);
        Assert.Equal(DeferredSeedAssociations.MaxEntries, entries.Count);
        Assert.Equal(1, outcome.Dropped);
    }

    [Fact]
    public void TryParse_RoundTripsTheRecordedEdge_AndRefusesIdentityRoles()
    {
        var entries = new List<DeferredSeedAssociation>();
        DeferredSeedAssociations.Merge(entries, Samples, [Edge(Pipeline1, MeshAdapter)], Now,
            new DeferredSeedAssociationOutcome());

        Assert.True(DeferredSeedAssociations.TryParse(entries.Single(), out var edge));
        Assert.Equal(Executes, edge.AssociationRoleId);
        Assert.Equal(PipelineType, edge.OriginCkTypeId);
        Assert.Equal(Pipeline1, edge.OriginRtId);
        Assert.Equal(AdapterType, edge.TargetCkTypeId);
        Assert.Equal(MeshAdapter, edge.TargetRtId);
        Assert.Equal(RtState.Active, edge.RtState);

        var forged = entries.Single() with { Role = "System.Identity/AssignedRole" };
        Assert.False(DeferredSeedAssociations.TryParse(forged, out _));
        Assert.False(DeferredSeedAssociations.TryParse(entries.Single() with { TargetRtId = "nope" }, out _));
    }

    [Fact]
    public void DecideWrites_NeverReplacesAnotherTargetOnAToOneRole()
    {
        var entries = new List<DeferredSeedAssociation>();
        DeferredSeedAssociations.Merge(entries, Samples,
            [Edge(Pipeline1, MeshAdapter), Edge(Pipeline2, MeshAdapter)], Now, new DeferredSeedAssociationOutcome());
        var ready = entries.Select(e =>
        {
            DeferredSeedAssociations.TryParse(e, out var edge);
            return (e, edge);
        }).ToList();
        var taken = new Dictionary<(OctoObjectId, RtCkId<CkAssociationRoleId>), OctoObjectId>
        {
            [(Pipeline1, Executes)] = OtherAdapter, // the tenant linked it elsewhere meanwhile
        };

        var (toWrite, blocked) = DeferredSeedAssociations.DecideWrites(ready, taken);

        Assert.Equal(Pipeline2, toWrite.Single().Edge.OriginRtId);
        Assert.Equal(OtherAdapter, blocked.Single().OtherTarget);

        taken[(Pipeline1, Executes)] = MeshAdapter; // already there: neither written nor blocked
        (toWrite, blocked) = DeferredSeedAssociations.DecideWrites(ready, taken);
        Assert.Equal(Pipeline2, toWrite.Single().Edge.OriginRtId);
        Assert.Empty(blocked);
    }
}
