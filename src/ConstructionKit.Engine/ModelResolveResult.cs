using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine;

/// <summary>
/// Represents the result of a model resolution process.
/// </summary>
public class ModelResolveResult
{
    /// <summary>
    /// Gets the resolved CK model graph.
    /// </summary>
    public required CkModelGraph CkModelGraph { get; init; }

    /// <summary>
    /// Gets a list of model ids that were skipped during the resolution process, because of missing dependencies.
    /// </summary>
    public required IReadOnlyCollection<CkModelId> SkippedModelIds { get; init; }

    /// <summary>
    /// Gets a list of model ids that were not possible to resolve.
    /// </summary>
    public required IReadOnlyCollection<CkModelIdVersionRange> UnresolvedDependencyModelIds { get; init; }

    /// <summary>
    /// Gets a list of model ids that failed during inheritance resolution (e.g., broken base type references
    /// after a dependency model was upgraded to a new major version).
    /// </summary>
    public required IReadOnlyCollection<CkModelId> FailedModelIds { get; init; }
}
