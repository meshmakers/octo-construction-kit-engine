// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     The root object for a CK model that is a candidate for compilation.
/// </summary>
public class CkModelCompileCandidate : CkModelRootBase
{
    /// <summary>
    /// Gets or sets the dependency ranges of the model.
    /// </summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public List<CkModelIdVersionRange>? DependencyRanges { get; set;}
}