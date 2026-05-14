using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// Resolves the transitive dependency closure of a blueprint into a topo-sorted
/// install plan, and surfaces dependency-graph conflicts (circular references,
/// missing entries, version mismatches) before any tenant is touched.
/// </summary>
public interface IBlueprintDependencyResolver
{
    /// <summary>
    /// Walks <c>blueprintDependencies</c> from <paramref name="rootBlueprintId"/>
    /// outward. Version ranges are resolved to concrete versions via the catalog.
    /// </summary>
    /// <remarks>
    /// The returned <see cref="BlueprintResolutionResult.InstallOrder"/> is
    /// topo-sorted (base dependencies first, root last). When conflicts are
    /// detected, <see cref="BlueprintResolutionResult.Success"/> is <c>false</c>
    /// and <see cref="BlueprintResolutionResult.Conflicts"/> describes them;
    /// callers MUST NOT install from a failed result.
    /// </remarks>
    Task<BlueprintResolutionResult> ResolveAsync(
        BlueprintId rootBlueprintId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of <see cref="IBlueprintDependencyResolver.ResolveAsync"/>.
/// </summary>
public class BlueprintResolutionResult
{
    /// <summary>The blueprint the resolver was asked to plan.</summary>
    public required BlueprintId RootBlueprintId { get; init; }

    /// <summary>
    /// <c>true</c> when the install plan is safe to execute. <c>false</c>
    /// when <see cref="Conflicts"/> is non-empty.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Blueprints to install, base dependencies first and the root last. Empty
    /// when <see cref="Success"/> is <c>false</c>.
    /// </summary>
    public required List<BlueprintMetaRootDto> InstallOrder { get; init; }

    /// <summary>
    /// Dependency-graph conflicts that block the plan. Each entry describes
    /// one detected problem; the resolver does not stop on the first conflict
    /// so callers can present a complete picture.
    /// </summary>
    public required List<BlueprintResolutionConflict> Conflicts { get; init; }

    /// <summary>Non-blocking advisories surfaced during resolution.</summary>
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// A problem the resolver found while walking the dependency graph.
/// </summary>
public class BlueprintResolutionConflict
{
    /// <summary>What kind of conflict this is.</summary>
    public required BlueprintResolutionConflictType ConflictType { get; init; }

    /// <summary>Human-readable description; safe to surface in CLI / UI.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// The blueprint the conflict is about. <c>null</c> when the conflict has
    /// no single offender (e.g. graph-level circular references list all
    /// participants in <see cref="AdditionalContext"/>).
    /// </summary>
    public BlueprintId? OffendingBlueprintId { get; init; }

    /// <summary>
    /// Free-form supplementary information (cycle path, conflicting version
    /// ranges, etc.).
    /// </summary>
    public string? AdditionalContext { get; init; }
}

/// <summary>
/// Categories of dependency-graph conflicts detected during resolution.
/// </summary>
public enum BlueprintResolutionConflictType
{
    /// <summary>The graph contains a cycle.</summary>
    CircularDependency,

    /// <summary>A declared dependency is not present in any catalog.</summary>
    MissingDependency,

    /// <summary>
    /// Two paths through the graph require incompatible versions of the same
    /// blueprint (e.g. A→C-[1.0,2.0) and B→C-[2.0,3.0)).
    /// </summary>
    IncompatibleDependencyVersions
}
