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
///     D2 (CK v2 Phase 0 E2E): a tenant holding exact-pinned AND range-retaining models validates its
///     dependencies after an additive System minor. <c>CkModelIdVersionRange.Equals</c> means "overlaps", so
///     <c>System-[2.5,3.0)</c> matched both exact pins <c>System-[2.5.0]</c> and <c>System-[2.6.0]</c> and the
///     dependency list lookup threw "Sequence contains more than one matching element".
/// </summary>
public class RangeRetentionRepositoryResolverTests
{
    private readonly Dictionary<CkModelId, CkCompiledModelRoot> _installed = new();
    private readonly RepositoryDependencyResolver _resolver;

    public RangeRetentionRepositoryResolverTests()
    {
        var repository = A.Fake<IRepositoryManagementService>();
        A.CallTo(() => repository.IsExistingAsync(A<CkModelIdVersionRange>._, A<object?>._))
            .ReturnsLazily((CkModelIdVersionRange range, object? _) =>
            {
                var match = _installed.Keys.Where(range.IsSatisfiedBy).OrderBy(m => m.Version).LastOrDefault();
                return Task.FromResult(new ModelExistingResult { Exists = match != null, ModelId = match });
            });
        A.CallTo(() => repository.TryLookupCkModelAsync(A<CkModelId>._, A<OperationResult>._, A<object?>._,
                A<CancellationToken?>._))
            .ReturnsLazily((CkModelId id, OperationResult _, object? _, CancellationToken? _) =>
                Task.FromResult(_installed.TryGetValue(id, out var model) ? Copy(model) : null));
        _resolver = new RepositoryDependencyResolver(NullLogger<RepositoryDependencyResolver>.Instance,
            new Lazy<IRepositoryManagementService>(() => repository));
    }

    [Fact]
    public async Task MixedExactAndRangeModels_AfterSystemMinor_RangeModelResolves_ExactModelsSkipped()
    {
        // Order matters for the old bug: the exact pin System-[2.5.0] is queued before the range-retaining
        // models expand System-[2.5,3.0), which then overlapped both System-[2.5.0] and System-[2.6.0].
        Install(new CkCompiledModelRoot
        {
            ModelId = new CkModelId("System.Bot-3.4.0"), Dependencies = [new CkModelId("System-2.5.0")]
        });
        Install(new CkCompiledModelRoot
        {
            ModelId = new CkModelId("System.Communication-3.40.0"),
            Dependencies = [new CkModelId("System-2.5.0"), new CkModelId("System.Bot-3.4.0")]
        });
        Install(new CkCompiledModelRoot { ModelId = new CkModelId("System-2.6.0") });
        Install(RangeRetaining("Basic-2.4.0"));
        Install(RangeRetaining("Basic.Energy-1.9.0"));
        Install(new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Basic.Accounting-1.9.0"),
            Dependencies = [new CkModelId("Basic-2.4.0"), new CkModelId("System-2.5.0")]
        });

        // ValidateDependencies soft-resolves every Available model as an exact root.
        var result = await _resolver.SoftResolveDependenciesAsync(_installed.Keys.ToList(), new CkModelGraph(),
            A.Fake<IVariableResolver>(), A.Fake<IOriginFileResolver>(), new OperationResult());

        Assert.DoesNotContain(new CkModelId("Basic-2.4.0"), result.SkippedModelIds);
        Assert.DoesNotContain(new CkModelId("Basic.Energy-1.9.0"), result.SkippedModelIds);
        Assert.DoesNotContain(new CkModelId("System-2.6.0"), result.SkippedModelIds);
        Assert.Contains(new CkModelId("System.Bot-3.4.0"), result.SkippedModelIds);
        Assert.Contains(new CkModelId("System.Communication-3.40.0"), result.SkippedModelIds);
        Assert.Contains(new CkModelId("Basic.Accounting-1.9.0"), result.SkippedModelIds);
        Assert.Contains(result.UnresolvedDependencyModelIds, r => r.FullName == "System-[2.5.0]");
    }

    private static CkCompiledModelRoot RangeRetaining(string modelId) => new()
    {
        ModelId = new CkModelId(modelId),
        Dependencies = [new CkModelId("System-2.5.0")],
        DependencyRanges =
        [
            new CkModelDependencyDto { Range = new CkModelIdVersionRange("System-[2.5,3.0)"), Floor = "2.5.0" }
        ]
    };

    private void Install(CkCompiledModelRoot model) => _installed[model.ModelId] = model;

    private static CkCompiledModelRoot Copy(CkCompiledModelRoot model) => new()
    {
        ModelId = model.ModelId,
        Dependencies = model.Dependencies?.ToList(),
        DependencyRanges = model.DependencyRanges?.ToList()
    };
}
