using FakeItEasy;

using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.Repositories.Query;
using Meshmakers.Octo.Runtime.Engine.Security;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     AB#9824: the data-policy table reads whole System.Identity types on purpose. Inside a pipeline node those reads
///     run in a suppressed <see cref="RuntimeReadScope" />, so the runtime read guard neither reports them as the node's
///     unbounded read nor lets them hide the node's own read; the node's caller applies again afterwards.
/// </summary>
public class DataPermissionResolverRuntimeReadScopeTests
{
    private const string Tenant = "tenant-9824";

    [Fact]
    public async Task PolicyTableLoad_RunsSuppressed_AndRestoresTheNodeCaller()
    {
        var ckCache = A.Fake<ICkCacheService>();
        CkTypeGraph? ignored;
        A.CallTo(() => ckCache.TryGetRtCkType(Tenant, A<RtCkId<CkTypeId>>._, out ignored))
            .Returns(true)
            .AssignsOutAndRefParameters((CkTypeGraph?)null);

        var repository = A.Fake<IRuntimeRepository>();
        A.CallTo(() => repository.TenantId).Returns(Tenant);
        A.CallTo(() => repository.GetSessionAsync()).Returns(A.Fake<IOctoSession>());

        RuntimeReadCaller? callerDuringPolicyRead = null;
        A.CallTo(() => repository.GetRtEntitiesByTypeAsync(A<IOctoSession>._,
                A<RtCkId<CkTypeId>>.That.Matches(id => id.ToString().Contains("DataPolicy")),
                A<RtEntityQueryOptions>._, A<int?>._, A<int?>._))
            .ReturnsLazily(() =>
            {
                callerDuringPolicyRead = RuntimeReadScope.Current;
                return Task.FromResult<IResultSet<RtEntity>>(
                    new ResultSet<RtEntity>(Array.Empty<RtEntity>(), 0, null, null));
            });

        var resolver = new DataPermissionResolver(NullLogger<DataPermissionResolver>.Instance, ckCache);

        using var execution = RuntimeReadScope.BeginExecution(
            new RuntimeReadExecution(Tenant, "pipeline-1", null, "flow-1", "exec-1"));
        using var node = RuntimeReadScope.BeginNode("GetRtEntitiesByType@1", "PipelineExecution/GetRtEntitiesByType@1");

        var table = await resolver.GetPolicyTableAsync(repository);

        Assert.Same(RtDataPolicyTable.Empty, table);
        Assert.NotNull(callerDuringPolicyRead);
        Assert.True(callerDuringPolicyRead!.Suppressed);
        Assert.Equal("GetRtEntitiesByType@1", callerDuringPolicyRead.NodeType);

        var after = RuntimeReadScope.Current;
        Assert.NotNull(after);
        Assert.False(after!.Suppressed);
        Assert.Equal("GetRtEntitiesByType@1", after.NodeType);
        // The policy load used up nothing of the node: its own unbounded read is still the first one reported.
        Assert.True(after.Execution!.TryMarkReported(after.NodeKey));
    }
}
