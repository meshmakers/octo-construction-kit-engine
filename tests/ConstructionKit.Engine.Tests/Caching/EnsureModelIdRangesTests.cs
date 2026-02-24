using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Services;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Caching;

public class EnsureModelIdRangesTests
{
    private const string TenantId = "test-tenant";

    private static CkCacheService CreateCacheWithModels(params CkModelId[] modelIds)
    {
        var logger = LoggerFactory.Create(b => { }).CreateLogger<CkCacheService>();
        var cacheService = new CkCacheService(logger);
        cacheService.CreateTenant(TenantId);

        var modelGraph = new CkModelGraph();
        foreach (var modelId in modelIds)
        {
            modelGraph.AppendModel(new CkCompiledModelRoot
            {
                ModelId = modelId,
                Dependencies = []
            });
        }

        cacheService.LoadCkModelGraph(TenantId, modelGraph);
        return cacheService;
    }

    #region Satisfied Ranges

    [Fact]
    public void EnsureModelIdRanges_ExactVersionSatisfied_ReturnsEmpty()
    {
        var cacheService = CreateCacheWithModels(
            new CkModelId("Basic-2.0.0"),
            new CkModelId("System-2.0.0"));

        var result = cacheService.EnsureModelIdRanges(TenantId,
        [
            new CkModelIdVersionRange("Basic-[2.0.0]"),
            new CkModelIdVersionRange("System-[2.0.0]")
        ]);

        Assert.Empty(result);
    }

    [Fact]
    public void EnsureModelIdRanges_RangeSatisfiedByLoadedModel_ReturnsEmpty()
    {
        var cacheService = CreateCacheWithModels(
            new CkModelId("Basic-2.0.1"),
            new CkModelId("System-2.0.0"));

        var result = cacheService.EnsureModelIdRanges(TenantId,
        [
            new CkModelIdVersionRange("Basic-[2.0,3.0)"),
            new CkModelIdVersionRange("System-[2.0,3.0)")
        ]);

        Assert.Empty(result);
    }

    [Fact]
    public void EnsureModelIdRanges_SimpleVersionSatisfied_ReturnsEmpty()
    {
        // "Basic-2.0.0" as range means >= 2.0.0
        var cacheService = CreateCacheWithModels(
            new CkModelId("Basic-2.0.1"));

        var result = cacheService.EnsureModelIdRanges(TenantId,
        [
            new CkModelIdVersionRange("Basic-2.0.0")
        ]);

        Assert.Empty(result);
    }

    [Fact]
    public void EnsureModelIdRanges_PatchVersionBump_StillSatisfied()
    {
        // The core scenario: CK model version bumped from 2.0.0 to 2.0.1
        var cacheService = CreateCacheWithModels(
            new CkModelId("Basic-2.0.1"));

        var result = cacheService.EnsureModelIdRanges(TenantId,
        [
            new CkModelIdVersionRange("Basic-[2.0,3.0)")
        ]);

        Assert.Empty(result);
    }

    [Fact]
    public void EnsureModelIdRanges_MinorVersionBump_StillSatisfied()
    {
        var cacheService = CreateCacheWithModels(
            new CkModelId("Basic-2.1.0"));

        var result = cacheService.EnsureModelIdRanges(TenantId,
        [
            new CkModelIdVersionRange("Basic-[2.0,3.0)")
        ]);

        Assert.Empty(result);
    }

    [Fact]
    public void EnsureModelIdRanges_EmptyDependencies_ReturnsEmpty()
    {
        var cacheService = CreateCacheWithModels(
            new CkModelId("Basic-2.0.0"));

        var result = cacheService.EnsureModelIdRanges(TenantId,
            Array.Empty<CkModelIdVersionRange>());

        Assert.Empty(result);
    }

    #endregion

    #region Unsatisfied Ranges

    [Fact]
    public void EnsureModelIdRanges_MajorVersionMismatch_ReturnsUnsatisfied()
    {
        // Model is 3.0.0, but range requires [2.0,3.0)
        var cacheService = CreateCacheWithModels(
            new CkModelId("Basic-3.0.0"));

        var result = cacheService.EnsureModelIdRanges(TenantId,
        [
            new CkModelIdVersionRange("Basic-[2.0,3.0)")
        ]);

        Assert.Single(result);
        Assert.Equal("Basic", result.First().Name);
    }

    [Fact]
    public void EnsureModelIdRanges_ModelNotLoaded_ReturnsUnsatisfied()
    {
        var cacheService = CreateCacheWithModels(
            new CkModelId("System-2.0.0"));

        var result = cacheService.EnsureModelIdRanges(TenantId,
        [
            new CkModelIdVersionRange("Basic-[2.0,3.0)")
        ]);

        Assert.Single(result);
        Assert.Equal("Basic", result.First().Name);
    }

    [Fact]
    public void EnsureModelIdRanges_VersionTooLow_ReturnsUnsatisfied()
    {
        var cacheService = CreateCacheWithModels(
            new CkModelId("Basic-1.9.0"));

        var result = cacheService.EnsureModelIdRanges(TenantId,
        [
            new CkModelIdVersionRange("Basic-[2.0,3.0)")
        ]);

        Assert.Single(result);
    }

    [Fact]
    public void EnsureModelIdRanges_PartiallyUnsatisfied_ReturnsOnlyMissing()
    {
        var cacheService = CreateCacheWithModels(
            new CkModelId("Basic-2.0.0"));

        var result = cacheService.EnsureModelIdRanges(TenantId,
        [
            new CkModelIdVersionRange("Basic-[2.0,3.0)"),
            new CkModelIdVersionRange("Industry.Energy-[2.0,3.0)")
        ]);

        Assert.Single(result);
        Assert.Equal("Industry.Energy", result.First().Name);
    }

    #endregion

    #region Backward Compatibility

    [Fact]
    public void EnsureModelIdRanges_OldStyleExactVersion_WorksWithLoadedModel()
    {
        // Old RT exports used exact versions like "Basic-2.0.0"
        // After parsing as CkModelIdVersionRange, this becomes >= 2.0.0
        // So it should match any model with version >= 2.0.0
        var cacheService = CreateCacheWithModels(
            new CkModelId("Basic-2.0.1"));

        var oldStyleDependency = new CkModelIdVersionRange("Basic-2.0.0");

        var result = cacheService.EnsureModelIdRanges(TenantId,
            [oldStyleDependency]);

        Assert.Empty(result);
    }

    [Fact]
    public void EnsureModelIdRanges_NameOnlyDependency_MatchesAnyVersion()
    {
        // "System" without version defaults to >= 1.0.0
        var cacheService = CreateCacheWithModels(
            new CkModelId("System-2.0.0"));

        var result = cacheService.EnsureModelIdRanges(TenantId,
        [
            new CkModelIdVersionRange("System")
        ]);

        Assert.Empty(result);
    }

    #endregion

    #region EnsureModelIds (existing method) still works

    [Fact]
    public void EnsureModelIds_ExactMatch_ReturnsEmpty()
    {
        var cacheService = CreateCacheWithModels(
            new CkModelId("Basic-2.0.0"));

        var result = cacheService.EnsureModelIds(TenantId,
            [new CkModelId("Basic-2.0.0")]);

        Assert.Empty(result);
    }

    [Fact]
    public void EnsureModelIds_VersionMismatch_ReturnsMissing()
    {
        var cacheService = CreateCacheWithModels(
            new CkModelId("Basic-2.0.1"));

        var result = cacheService.EnsureModelIds(TenantId,
            [new CkModelId("Basic-2.0.0")]);

        // Exact match required - 2.0.0 != 2.0.1
        Assert.Single(result);
    }

    #endregion
}
