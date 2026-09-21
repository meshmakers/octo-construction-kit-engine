using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Exchange;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
/// Unit tests for <see cref="ImportRtModelCommand.PartitionToOneAssociations"/> — the pure,
/// repository-free seam of the to-one replacement step (AB#5296). The bulk association write
/// upserts an edge on (role, origin, target) and never checked the role's cardinality, so an
/// Upsert import that points a to-one role (One / ZeroOrOne) at a different target than the tenant
/// holds appended a second edge next to the existing one. The seam decides which imported edges
/// to write and which existing edges to delete so the role ends up with exactly the imported target.
/// </summary>
public class ImportRtModelCommandPartitionToOneAssociationsTests
{
    private static readonly RtCkId<CkTypeId> AdapterType = new("Test/Adapter");
    private static readonly RtCkId<CkTypeId> RepoType = new("Test/HelmRepositoryConfiguration");
    private static readonly RtCkId<CkTypeId> PipelineType = new("Test/Pipeline");
    private static readonly RtCkId<CkAssociationRoleId> HelmRepositoryRole = new("Test/HelmRepository");
    private static readonly RtCkId<CkAssociationRoleId> ExecutesRole = new("Test/Executes");

    private static RtAssociation Edge(RtCkId<CkAssociationRoleId> role, OctoObjectId origin,
        RtCkId<CkTypeId> originType, OctoObjectId target, RtCkId<CkTypeId> targetType)
    {
        return new RtAssociation(role, OctoObjectId.GenerateNewId(), new Dictionary<string, object?>())
        {
            OriginRtId = origin,
            OriginCkTypeId = originType,
            TargetRtId = target,
            TargetCkTypeId = targetType
        };
    }

    private static bool IsHelmRepositoryRole(RtAssociation a) => a.AssociationRoleId == HelmRepositoryRole;

    [Fact]
    public void ExistingEdgeWithDifferentTarget_IsReplaced_ImportedEdgeKept()
    {
        // The prod-1 shape: the tenant holds adapter -> meshmakers-public, the seed says
        // adapter -> meshmakers-dev. The seeded edge must replace the existing one, not join it.
        var adapter = OctoObjectId.GenerateNewId();
        var publicRepo = OctoObjectId.GenerateNewId();
        var devRepo = OctoObjectId.GenerateNewId();
        var imported = Edge(HelmRepositoryRole, adapter, AdapterType, devRepo, RepoType);
        var existing = Edge(HelmRepositoryRole, adapter, AdapterType, publicRepo, RepoType);

        var (kept, stale, samples) = ImportRtModelCommand.PartitionToOneAssociations(
            [imported], IsHelmRepositoryRole, [existing]);

        Assert.Same(imported, Assert.Single(kept));
        Assert.Same(existing, Assert.Single(stale));
        var sample = Assert.Single(samples);
        Assert.Contains(publicRepo.ToString(), sample);
        Assert.Contains(devRepo.ToString(), sample);
    }

    [Fact]
    public void ExistingEdgeWithSameTarget_IsLeftToTheUpsert()
    {
        var adapter = OctoObjectId.GenerateNewId();
        var repo = OctoObjectId.GenerateNewId();
        var imported = Edge(HelmRepositoryRole, adapter, AdapterType, repo, RepoType);
        var existing = Edge(HelmRepositoryRole, adapter, AdapterType, repo, RepoType);

        var (kept, stale, samples) = ImportRtModelCommand.PartitionToOneAssociations(
            [imported], IsHelmRepositoryRole, [existing]);

        Assert.Single(kept);
        Assert.Empty(stale);
        Assert.Empty(samples);
    }

    [Fact]
    public void TenantAlreadyCarriesTwoEdges_BothStaleOnesAreDeleted()
    {
        // A tenant that already suffered the duplicate (dev + public) and now gets re-seeded with
        // a third target: every edge that is not the imported target goes.
        var adapter = OctoObjectId.GenerateNewId();
        var imported = Edge(HelmRepositoryRole, adapter, AdapterType, OctoObjectId.GenerateNewId(), RepoType);
        var existingA = Edge(HelmRepositoryRole, adapter, AdapterType, OctoObjectId.GenerateNewId(), RepoType);
        var existingB = Edge(HelmRepositoryRole, adapter, AdapterType, OctoObjectId.GenerateNewId(), RepoType);

        var (kept, stale, _) = ImportRtModelCommand.PartitionToOneAssociations(
            [imported], IsHelmRepositoryRole, [existingA, existingB]);

        Assert.Single(kept);
        Assert.Equal(2, stale.Count);
        Assert.Contains(existingA, stale);
        Assert.Contains(existingB, stale);
    }

    [Fact]
    public void ExistingEdgeOfAnotherOrigin_IsNotTouched()
    {
        var importedAdapter = OctoObjectId.GenerateNewId();
        var otherAdapter = OctoObjectId.GenerateNewId();
        var imported = Edge(HelmRepositoryRole, importedAdapter, AdapterType, OctoObjectId.GenerateNewId(), RepoType);
        var foreign = Edge(HelmRepositoryRole, otherAdapter, AdapterType, OctoObjectId.GenerateNewId(), RepoType);

        var (kept, stale, _) = ImportRtModelCommand.PartitionToOneAssociations(
            [imported], IsHelmRepositoryRole, [foreign]);

        Assert.Single(kept);
        Assert.Empty(stale);
    }

    [Fact]
    public void ToManyRole_IsPassedThroughUntouched_EvenWithOtherTargetsPresent()
    {
        // Executes is N: a pipeline executed by one adapter today may be pointed at a second one
        // by the import; nothing must be deleted and nothing dropped.
        var pipeline = OctoObjectId.GenerateNewId();
        var imported = Edge(ExecutesRole, pipeline, PipelineType, OctoObjectId.GenerateNewId(), AdapterType);
        var existing = Edge(ExecutesRole, pipeline, PipelineType, OctoObjectId.GenerateNewId(), AdapterType);

        var (kept, stale, samples) = ImportRtModelCommand.PartitionToOneAssociations(
            [imported], IsHelmRepositoryRole, [existing]);

        Assert.Same(imported, Assert.Single(kept));
        Assert.Empty(stale);
        Assert.Empty(samples);
    }

    [Fact]
    public void SecondImportedTargetForSameOriginAndRole_IsDropped_FirstDeclarationWins()
    {
        var adapter = OctoObjectId.GenerateNewId();
        var first = Edge(HelmRepositoryRole, adapter, AdapterType, OctoObjectId.GenerateNewId(), RepoType);
        var second = Edge(HelmRepositoryRole, adapter, AdapterType, OctoObjectId.GenerateNewId(), RepoType);
        // The existing edge matches the SECOND declaration — it must still be replaced, because
        // the first declaration is the one that is written.
        var existing = Edge(HelmRepositoryRole, adapter, AdapterType, second.TargetRtId, RepoType);

        var (kept, stale, _) = ImportRtModelCommand.PartitionToOneAssociations(
            [first, second], IsHelmRepositoryRole, [existing]);

        Assert.Same(first, Assert.Single(kept));
        Assert.Same(existing, Assert.Single(stale));
    }

    [Fact]
    public void SampleIsCapped_ButEveryStaleEdgeIsStillReturned()
    {
        var adapter = OctoObjectId.GenerateNewId();
        var imported = Edge(HelmRepositoryRole, adapter, AdapterType, OctoObjectId.GenerateNewId(), RepoType);
        var existing = Enumerable.Range(0, 5)
            .Select(_ => Edge(HelmRepositoryRole, adapter, AdapterType, OctoObjectId.GenerateNewId(), RepoType))
            .ToList();

        var (_, stale, samples) = ImportRtModelCommand.PartitionToOneAssociations(
            [imported], IsHelmRepositoryRole, existing, sampleCap: 2);

        Assert.Equal(5, stale.Count);
        Assert.Equal(2, samples.Count);
    }

    [Fact]
    public void MixedImport_OnlyToOneEdgesParticipate()
    {
        var adapter = OctoObjectId.GenerateNewId();
        var pipeline = OctoObjectId.GenerateNewId();
        var repoEdge = Edge(HelmRepositoryRole, adapter, AdapterType, OctoObjectId.GenerateNewId(), RepoType);
        var executesEdge = Edge(ExecutesRole, pipeline, PipelineType, adapter, AdapterType);
        var staleRepoEdge = Edge(HelmRepositoryRole, adapter, AdapterType, OctoObjectId.GenerateNewId(), RepoType);

        var (kept, stale, _) = ImportRtModelCommand.PartitionToOneAssociations(
            [repoEdge, executesEdge], IsHelmRepositoryRole, [staleRepoEdge]);

        Assert.Equal(2, kept.Count);
        Assert.Same(staleRepoEdge, Assert.Single(stale));
    }
}
