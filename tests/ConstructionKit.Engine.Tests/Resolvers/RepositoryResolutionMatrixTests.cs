using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Repository;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

/// <summary>
///     AB#4468: the repository (import) side of the SemVer resolution matrix; the catalog side is
///     <c>DependencyResolutionMatrixTests</c> (compiler tests), the tenant re-validation is pinned in engine-mongodb.
///     A range-retaining model that declares <c>System-[2.5,3.0)</c> (floor 2.5.0) is imported into a repository
///     holding a set of System versions:
///     a higher minor or patch resolves, a higher major only or a version below the floor only is rejected with a
///     message naming the range and the installed versions, several versions in range resolve to the highest.
///     No special treatment for System and Meshmakers-shipped models; classic exact pins keep their exact match.
/// </summary>
public class RepositoryResolutionMatrixTests
{
    public static TheoryData<string[], string?> Cases() => new()
    {
        { ["2.5.0"], "System-2.5.0" },
        { ["2.6.1"], "System-2.6.1" },
        { ["2.4.9"], null },
        { ["3.0.0"], null },
        { ["2.5.0", "2.6.1", "3.0.0"], "System-2.6.1" },
        { ["2.5.0", "2.5.7", "2.9.0", "3.0.0"], "System-2.9.0" }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RangeRetainingRoot_AgainstInstalledVersions_ResolvesOrIsRejectedWithRangeAndVersions(
        string[] installed, string? expectedResolved)
    {
        var root = RangeRetainingRoot("System-[2.5,3.0)", "2.5.0");

        await AssertAsync(root, installed, expectedResolved);
    }

    // A floor above the oldest version in range: the installed 2.5.0 is inside the range but below the floor.
    [Fact]
    public async Task RangeRetainingRoot_InstalledVersionBelowTheFloor_IsRejected()
    {
        var root = RangeRetainingRoot("System-[2.5,3.0)", "2.6.0");

        // The resolver judges the effective range (lower bound raised to the floor) and names it.
        await AssertAsync(root, ["2.5.0"], null, root.GetResolutionRanges().Single().ToString());
    }

    [Theory]
    [InlineData(new[] { "2.5.0" }, "System-2.5.0")]
    [InlineData(new[] { "2.6.1" }, null)]
    [InlineData(new[] { "2.4.9" }, null)]
    public async Task ClassicExactPin_KeepsExactMatch(string[] installed, string? expectedResolved)
    {
        var root = new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Dep-1.0.0"), Dependencies = [new CkModelId("System-2.5.0")]
        };

        await AssertAsync(root, installed, expectedResolved, "System-[2.5.0]");
    }

    private static async Task AssertAsync(CkCompiledModelRoot root, string[] installed, string? expectedResolved,
        string expectedRange = "System-[2.5,3.0)")
    {
        var versions = installed.Select(v => new CkModelId($"System-{v}")).ToList();
        var repository = A.Fake<IRepositoryManagementService>(o => o.Implements<IRepositoryVersionLister>());
        A.CallTo(() => repository.IsExistingAsync(A<CkModelIdVersionRange>._, A<object?>._))
            .ReturnsLazily((CkModelIdVersionRange range, object? _) =>
            {
                var match = versions.Where(range.IsSatisfiedBy).OrderBy(m => m.Version).LastOrDefault();
                return Task.FromResult(new ModelExistingResult { Exists = match != null, ModelId = match });
            });
        A.CallTo(() => repository.TryLookupCkModelAsync(A<CkModelId>._, A<OperationResult>._, A<object?>._,
                A<CancellationToken?>._))
            .ReturnsLazily((CkModelId id, OperationResult _, object? _, CancellationToken? _) =>
                Task.FromResult<CkCompiledModelRoot?>(new CkCompiledModelRoot { ModelId = id }));
        A.CallTo(() => ((IRepositoryVersionLister)repository).ListVersionsAsync(A<string>._, A<object?>._))
            .Returns(Task.FromResult<IReadOnlyList<CkModelId>>(versions.OrderBy(v => v.Version).ToList()));
        var resolver = new RepositoryDependencyResolver(NullLogger<RepositoryDependencyResolver>.Instance,
            new Lazy<IRepositoryManagementService>(() => repository));
        var ranges = RangeRetainingDependencyRanges.For(root.GetResolutionRanges(), root.IsRangeRetaining);

        if (expectedResolved != null)
        {
            var resolved = await resolver.HardResolveDependenciesAsync(ranges, new CkModelGraph(),
                A.Fake<IVariableResolver>(), A.Fake<IOriginFileResolver>(), new OperationResult());
            Assert.Equal([expectedResolved], resolved.Select(m => m.FullName));
        }
        else
        {
            var exception = await Assert.ThrowsAsync<ModelValidationException>(() =>
                resolver.HardResolveDependenciesAsync(ranges, new CkModelGraph(), A.Fake<IVariableResolver>(),
                    A.Fake<IOriginFileResolver>(), new OperationResult()));
            Assert.True(exception.Message.Contains(expectedRange), exception.Message);
            foreach (var version in installed)
            {
                Assert.Contains(version, exception.Message);
            }
        }
    }

    private static CkCompiledModelRoot RangeRetainingRoot(string range, string floor) => new()
    {
        ModelId = new CkModelId("Dep-1.0.0"),
        Dependencies = [new CkModelId("System-2.5.0")],
        DependencyRanges =
        [
            new CkModelDependencyDto { Range = new CkModelIdVersionRange(range), Floor = floor }
        ]
    };
}
