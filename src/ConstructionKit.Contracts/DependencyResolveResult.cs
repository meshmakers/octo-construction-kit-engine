namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Resolve result of dependencies
/// </summary>
public class DependencyResolveResult
{
    /// <summary>
    /// Gets a list of root model ids that were requested to be resolved.
    /// </summary>
    public required IReadOnlyCollection<CkModelId> RootDependencyModelIds { get; init; }

    /// <summary>
    /// Gets a list of model ids that were successfully resolved including all dependencies.
    /// </summary>
    public required IReadOnlyCollection<CkModelId> ResolvedDependentModelIds { get; init; }

    /// <summary>
    /// Gets a list of model ids that were skipped during the resolution process, because of missing dependencies.
    /// </summary>
    public required IReadOnlyCollection<CkModelId> SkippedModelIds { get; init; }

    /// <summary>
    /// Gets a list of model ids that were not possible to resolve.
    /// </summary>
    public required IReadOnlyCollection<CkModelIdVersionRange> UnresolvedDependencyModelIds { get; init; }

}