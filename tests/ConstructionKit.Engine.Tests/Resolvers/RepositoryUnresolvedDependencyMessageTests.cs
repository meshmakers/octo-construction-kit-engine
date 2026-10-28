using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Repository;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

/// <summary>
///     AB#7381: a repository (tenant) that cannot satisfy a dependency range explains it in terms of the repository -
///     the range, and the versions it holds with their state - and never says "no catalog knows any version"
///     (the repository resolves against its own <c>Available</c> models; observed on test-2 meshtest 2026-10-23, where
///     Industry.Basic 2.5.0 was installed but <c>ResolveFailed</c>).
/// </summary>
public class RepositoryUnresolvedDependencyMessageTests
{
    [Fact]
    public async Task OnlyResolveFailedVersionInstalled_NamesRangeAndState_NotACatalog()
    {
        var message = await ResolveAndGetMessageAsync([("2.5.0", "ResolveFailed")]);

        Assert.Contains("Industry.Basic-[2.5,3.0)", message);
        Assert.Contains("the tenant has no Available version of Industry.Basic (installed: 2.5.0 ResolveFailed)", message);
        Assert.DoesNotContain("catalog", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AvailableVersionsOutsideTheRange_AreListed()
    {
        var message = await ResolveAndGetMessageAsync([("2.4.0", "Available"), ("3.0.0", "Available")]);

        Assert.Contains("the tenant has Available versions of Industry.Basic (2.4.0, 3.0.0), none satisfies the range", message);
    }

    [Fact]
    public async Task NoVersionInstalled_SaysSo()
    {
        var message = await ResolveAndGetMessageAsync([]);

        Assert.Contains("the tenant has no version of Industry.Basic installed", message);
        Assert.DoesNotContain("catalog", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepositoryWithoutStateListing_KeepsTheVisibleVersionsText()
    {
        var repository = A.Fake<IRepositoryManagementService>(o => o.Implements<IRepositoryVersionLister>());
        A.CallTo(() => repository.IsExistingAsync(A<CkModelIdVersionRange>._, A<object?>._))
            .Returns(Task.FromResult(new ModelExistingResult { Exists = false }));
        A.CallTo(() => ((IRepositoryVersionLister)repository).ListVersionsAsync(A<string>._, A<object?>._))
            .Returns(Task.FromResult<IReadOnlyList<CkModelId>>([new CkModelId("Industry.Basic-2.4.0")]));

        var message = await ResolveAndGetMessageAsync(repository);

        Assert.Contains("does not match any visible version of Industry.Basic (2.4.0)", message);
    }

    [Fact]
    public async Task ManagementServiceOverARepositoryWithoutListing_FallsBackToTheVisibleVersionsText()
    {
        var modelRepository = A.Fake<IModelRepository>(o => o.Implements<IRepositoryVersionLister>());
        A.CallTo(() => modelRepository.IsExistingAsync(A<CkModelIdVersionRange>._, A<object?>._))
            .Returns(Task.FromResult(new ModelExistingResult { Exists = false }));
        A.CallTo(() => ((IRepositoryVersionLister)modelRepository).ListVersionsAsync(A<string>._, A<object?>._))
            .Returns(Task.FromResult<IReadOnlyList<CkModelId>>([new CkModelId("Industry.Basic-2.4.0")]));

        var message = await ResolveAndGetMessageAsync(new RepositoryManagementService(modelRepository));

        Assert.Contains("does not match any visible version of Industry.Basic (2.4.0)", message);
        Assert.DoesNotContain("no version of Industry.Basic installed", message);
    }

    private static Task<string> ResolveAndGetMessageAsync((string Version, string State)[] held)
    {
        var repository = A.Fake<IRepositoryManagementService>(o => o.Implements<IRepositoryInstalledVersionLister>());
        A.CallTo(() => repository.IsExistingAsync(A<CkModelIdVersionRange>._, A<object?>._))
            .Returns(Task.FromResult(new ModelExistingResult { Exists = false }));
        A.CallTo(() => ((IRepositoryInstalledVersionLister)repository).ListInstalledVersionsAsync(A<string>._, A<object?>._))
            .Returns(Task.FromResult<IReadOnlyList<RepositoryModelVersion>>(held
                .Select(h => new RepositoryModelVersion(new CkModelId($"Industry.Basic-{h.Version}"), h.State))
                .ToList()));
        return ResolveAndGetMessageAsync(repository);
    }

    private static async Task<string> ResolveAndGetMessageAsync(IRepositoryManagementService repository)
    {
        var resolver = new RepositoryDependencyResolver(NullLogger<RepositoryDependencyResolver>.Instance,
            new Lazy<IRepositoryManagementService>(() => repository));
        var ranges = new List<CkModelIdVersionRange> { new("Industry.Basic", "[2.5,3.0)") };

        var exception = await Assert.ThrowsAsync<ModelValidationException>(() =>
            resolver.HardResolveDependenciesAsync(ranges, new CkModelGraph(), A.Fake<IVariableResolver>(),
                A.Fake<IOriginFileResolver>(), new OperationResult()));
        return exception.Message;
    }
}
