using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Exchange;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Exchange;

/// <summary>
/// Unit tests for <see cref="ImportRtModelCommand.PartitionDanglingAssociations"/> — the pure,
/// repository-free seam of the AB#5005 dangling-edge filter. An import writes associations straight
/// into the association collection with no endpoint check, so an edge whose origin or target
/// resolves to no entity (neither carried by the archive nor present in the target tenant) would be
/// stored as a dead row. The filter drops such edges and surfaces a warning; a complete archive
/// (both ends present) is left untouched, which is what keeps restore/blueprint imports lossless.
/// </summary>
public class ImportRtModelCommandPartitionDanglingAssociationsTests
{
    private static readonly RtCkId<CkTypeId> RoleType = new("Test/Role");
    private static readonly RtCkId<CkTypeId> PermissionType = new("Test/Permission");
    private static readonly RtCkId<CkAssociationRoleId> GrantsRole = new("Test/GrantsPermission");

    private static RtAssociation Edge(OctoObjectId origin, RtCkId<CkTypeId> originType,
        OctoObjectId target, RtCkId<CkTypeId> targetType)
    {
        return new RtAssociation(GrantsRole, OctoObjectId.GenerateNewId(),
            new Dictionary<string, object?>())
        {
            OriginRtId = origin,
            OriginCkTypeId = originType,
            TargetRtId = target,
            TargetCkTypeId = targetType
        };
    }

    [Fact]
    public void AllEndpointsValid_KeepsEveryEdge_SkipsNothing()
    {
        var role = OctoObjectId.GenerateNewId();
        var permission = OctoObjectId.GenerateNewId();
        var valid = new HashSet<OctoObjectId> { role, permission };
        var edges = new[] { Edge(role, RoleType, permission, PermissionType) };

        var (kept, skippedCount, samples) =
            ImportRtModelCommand.PartitionDanglingAssociations(valid, edges);

        Assert.Single(kept);
        Assert.Equal(0, skippedCount);
        Assert.Empty(samples);
    }

    [Fact]
    public void MissingTarget_DropsEdge_AndSamplesTheMissingEnd()
    {
        var role = OctoObjectId.GenerateNewId();
        var permission = OctoObjectId.GenerateNewId();
        // Only the origin (role) is valid — the granted permission is not in the archive/tenant.
        var valid = new HashSet<OctoObjectId> { role };
        var edges = new[] { Edge(role, RoleType, permission, PermissionType) };

        var (kept, skippedCount, samples) =
            ImportRtModelCommand.PartitionDanglingAssociations(valid, edges);

        Assert.Empty(kept);
        Assert.Equal(1, skippedCount);
        var sample = Assert.Single(samples);
        Assert.Contains("missing target", sample);
        Assert.Contains(permission.ToString(), sample);
    }

    [Fact]
    public void MissingOrigin_DropsEdge_AndNamesTheOrigin()
    {
        var role = OctoObjectId.GenerateNewId();
        var permission = OctoObjectId.GenerateNewId();
        var valid = new HashSet<OctoObjectId> { permission };
        var edges = new[] { Edge(role, RoleType, permission, PermissionType) };

        var (kept, skippedCount, samples) =
            ImportRtModelCommand.PartitionDanglingAssociations(valid, edges);

        Assert.Empty(kept);
        Assert.Equal(1, skippedCount);
        Assert.Contains("missing origin", Assert.Single(samples));
    }

    [Fact]
    public void BothEndpointsMissing_ReportsBothEndpoints()
    {
        var edges = new[]
        {
            Edge(OctoObjectId.GenerateNewId(), RoleType, OctoObjectId.GenerateNewId(), PermissionType)
        };

        var (kept, skippedCount, samples) =
            ImportRtModelCommand.PartitionDanglingAssociations(new HashSet<OctoObjectId>(), edges);

        Assert.Empty(kept);
        Assert.Equal(1, skippedCount);
        Assert.Contains("both endpoints", Assert.Single(samples));
    }

    [Fact]
    public void Mixed_KeepsValidEdges_DropsOnlyDangling()
    {
        var role = OctoObjectId.GenerateNewId();
        var permissionPresent = OctoObjectId.GenerateNewId();
        var permissionMissing = OctoObjectId.GenerateNewId();
        var valid = new HashSet<OctoObjectId> { role, permissionPresent };
        var edges = new[]
        {
            Edge(role, RoleType, permissionPresent, PermissionType),
            Edge(role, RoleType, permissionMissing, PermissionType)
        };

        var (kept, skippedCount, _) =
            ImportRtModelCommand.PartitionDanglingAssociations(valid, edges);

        Assert.Single(kept);
        Assert.Equal(permissionPresent, kept[0].TargetRtId);
        Assert.Equal(1, skippedCount);
    }

    [Fact]
    public void SampleIsCapped_ButTotalCountIsExact()
    {
        // 25 dangling edges, sample capped at 3 — the count stays exact so the operator sees the
        // true total even though the message lists only a bounded sample.
        var edges = Enumerable.Range(0, 25)
            .Select(_ => Edge(OctoObjectId.GenerateNewId(), RoleType,
                OctoObjectId.GenerateNewId(), PermissionType))
            .ToList();

        var (kept, skippedCount, samples) =
            ImportRtModelCommand.PartitionDanglingAssociations(
                new HashSet<OctoObjectId>(), edges, sampleCap: 3);

        Assert.Empty(kept);
        Assert.Equal(25, skippedCount);
        Assert.Equal(3, samples.Count);
    }
}
