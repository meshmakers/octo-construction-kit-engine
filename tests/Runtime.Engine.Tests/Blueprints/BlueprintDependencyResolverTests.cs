using FakeItEasy;

using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Engine.Blueprints;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
/// Unit tests for the dependency resolver. The catalog manager is faked so
/// the tests can describe arbitrary graphs without touching disk or Mongo.
/// </summary>
public class BlueprintDependencyResolverTests
{
    private readonly FakeBlueprintCatalog _catalog = new();
    private readonly BlueprintDependencyResolver _resolver;

    public BlueprintDependencyResolverTests()
    {
        var catalogManager = A.Fake<IBlueprintCatalogManager>();

        A.CallTo(() => catalogManager.GetAsync(
                A<BlueprintId>._, A<OperationResult>._, A<object?>._, A<CancellationToken?>._))
            .ReturnsLazily((BlueprintId id, OperationResult op, object? _, CancellationToken? _) =>
                Task.FromResult(_catalog.Get(id, op)));

        A.CallTo(() => catalogManager.IsExistingAsync(A<BlueprintIdVersionRange>._, A<object?>._))
            .ReturnsLazily((BlueprintIdVersionRange range, object? _) => Task.FromResult(_catalog.Resolve(range)));

        _resolver = new BlueprintDependencyResolver(catalogManager, NullLogger<BlueprintDependencyResolver>.Instance);
    }

    [Fact]
    public async Task ResolveAsync_RootWithoutDependencies_ReturnsOnlyRoot()
    {
        _catalog.AddBlueprint("AppBp", "1.0.0");

        var result = await _resolver.ResolveAsync(new BlueprintId("AppBp", "1.0.0"), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Empty(result.Conflicts);
        Assert.Single(result.InstallOrder);
        Assert.Equal("AppBp-1.0.0", result.InstallOrder[0].BlueprintId.FullName);
    }

    [Fact]
    public async Task ResolveAsync_RootWithOneDirectDependency_OrdersDependencyFirst()
    {
        _catalog.AddBlueprint("BaseBp", "1.0.0");
        _catalog.AddBlueprint("AppBp", "1.0.0", depends: [("BaseBp", "[1.0,2.0)")]);

        var result = await _resolver.ResolveAsync(new BlueprintId("AppBp", "1.0.0"), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(2, result.InstallOrder.Count);
        Assert.Equal("BaseBp-1.0.0", result.InstallOrder[0].BlueprintId.FullName);
        Assert.Equal("AppBp-1.0.0", result.InstallOrder[1].BlueprintId.FullName);
    }

    [Fact]
    public async Task ResolveAsync_TransitiveDependencies_TopoSorted()
    {
        // C is a leaf; B depends on C; A depends on B.
        _catalog.AddBlueprint("C", "1.0.0");
        _catalog.AddBlueprint("B", "1.0.0", depends: [("C", "[1.0,)")]);
        _catalog.AddBlueprint("A", "1.0.0", depends: [("B", "[1.0,)")]);

        var result = await _resolver.ResolveAsync(new BlueprintId("A", "1.0.0"), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(3, result.InstallOrder.Count);
        Assert.Equal("C-1.0.0", result.InstallOrder[0].BlueprintId.FullName);
        Assert.Equal("B-1.0.0", result.InstallOrder[1].BlueprintId.FullName);
        Assert.Equal("A-1.0.0", result.InstallOrder[2].BlueprintId.FullName);
    }

    [Fact]
    public async Task ResolveAsync_DiamondDependency_SharedDepAppearsOnce()
    {
        // Shared base depended on by two intermediate blueprints.
        //   D
        //  / \
        // B   C
        //  \ /
        //   A
        _catalog.AddBlueprint("D", "1.0.0");
        _catalog.AddBlueprint("B", "1.0.0", depends: [("D", "[1.0,)")]);
        _catalog.AddBlueprint("C", "1.0.0", depends: [("D", "[1.0,)")]);
        _catalog.AddBlueprint("A", "1.0.0", depends: [("B", "[1.0,)"), ("C", "[1.0,)")]);

        var result = await _resolver.ResolveAsync(new BlueprintId("A", "1.0.0"), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(4, result.InstallOrder.Count);

        // D installs before B and C; A installs last.
        var positions = result.InstallOrder
            .Select((bp, i) => (bp.BlueprintId.Name, i))
            .ToDictionary(t => t.Name, t => t.i);
        Assert.True(positions["D"] < positions["B"]);
        Assert.True(positions["D"] < positions["C"]);
        Assert.True(positions["B"] < positions["A"]);
        Assert.True(positions["C"] < positions["A"]);
    }

    [Fact]
    public async Task ResolveAsync_CircularDependency_RaisesConflict()
    {
        // A -> B -> A
        _catalog.AddBlueprint("A", "1.0.0", depends: [("B", "[1.0,)")]);
        _catalog.AddBlueprint("B", "1.0.0", depends: [("A", "[1.0,)")]);

        var result = await _resolver.ResolveAsync(new BlueprintId("A", "1.0.0"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Empty(result.InstallOrder);
        Assert.Contains(result.Conflicts,
            c => c.ConflictType == BlueprintResolutionConflictType.CircularDependency);
    }

    [Fact]
    public async Task ResolveAsync_MissingDependency_RaisesConflict()
    {
        // AppBp references a base that the catalog does not know.
        _catalog.AddBlueprint("AppBp", "1.0.0", depends: [("Missing", "[1.0,)")]);

        var result = await _resolver.ResolveAsync(new BlueprintId("AppBp", "1.0.0"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Empty(result.InstallOrder);
        var missing = Assert.Single(result.Conflicts);
        Assert.Equal(BlueprintResolutionConflictType.MissingDependency, missing.ConflictType);
    }

    [Fact]
    public async Task ResolveAsync_MissingRoot_RaisesConflict()
    {
        var result = await _resolver.ResolveAsync(new BlueprintId("NeverPublished", "1.0.0"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal(BlueprintResolutionConflictType.MissingDependency, conflict.ConflictType);
    }

    [Fact]
    public async Task ResolveAsync_TwoPathsRequireIncompatibleVersions_RaisesConflict()
    {
        // A -> B requires C-[1.0,2.0)  -> resolves to C 1.5
        // A -> D requires C-[2.0,3.0)  -> resolves to C 2.5
        _catalog.AddBlueprint("C", "1.5.0");
        _catalog.AddBlueprint("C", "2.5.0");
        _catalog.AddBlueprint("B", "1.0.0", depends: [("C", "[1.0,2.0)")]);
        _catalog.AddBlueprint("D", "1.0.0", depends: [("C", "[2.0,3.0)")]);
        _catalog.AddBlueprint("A", "1.0.0", depends: [("B", "[1.0,)"), ("D", "[1.0,)")]);

        var result = await _resolver.ResolveAsync(new BlueprintId("A", "1.0.0"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(result.Conflicts,
            c => c.ConflictType == BlueprintResolutionConflictType.IncompatibleDependencyVersions);
    }

    /// <summary>
    /// Tiny in-memory blueprint catalog for resolver tests. Stores blueprints
    /// by (name, version), resolves version ranges to the highest matching
    /// concrete version (matches what the real catalog does).
    /// </summary>
    private sealed class FakeBlueprintCatalog
    {
        private readonly Dictionary<string, BlueprintMetaRootDto> _byFullName = new(StringComparer.Ordinal);

        public void AddBlueprint(
            string name,
            string version,
            IEnumerable<(string Name, string Range)>? depends = null)
        {
            var bp = new BlueprintMetaRootDto
            {
                BlueprintId = new BlueprintId(name, version),
                Description = $"{name} {version}",
                CkModelDependencies = []
            };
            if (depends != null)
            {
                bp.BlueprintDependencies = depends
                    .Select(d => new BlueprintIdVersionRange(d.Name, d.Range))
                    .ToList();
            }
            _byFullName[bp.BlueprintId.FullName] = bp;
        }

        public BlueprintMetaRootDto Get(BlueprintId id, OperationResult operationResult)
        {
            if (_byFullName.TryGetValue(id.FullName, out var bp))
            {
                return bp;
            }

            operationResult.AddMessage(new OperationMessage(
                MessageLevel.Error, null, 0, $"Blueprint '{id.FullName}' not found in fake catalog"));
            return new BlueprintMetaRootDto { BlueprintId = id };
        }

        public BlueprintExistingResult Resolve(BlueprintIdVersionRange range)
        {
            var candidates = _byFullName.Values
                .Where(bp => bp.BlueprintId.Name == range.Name
                    && range.BlueprintVersionRange.IsSatisfiedBy(bp.BlueprintId.Version))
                .OrderByDescending(bp => bp.BlueprintId.Version)
                .ToList();
            return candidates.Count > 0
                ? new BlueprintExistingResult { Exists = true, BlueprintId = candidates[0].BlueprintId }
                : new BlueprintExistingResult { Exists = false, BlueprintId = null };
        }
    }
}
