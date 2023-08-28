using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// The root object of the compiled version of a CK model.
/// </summary>
public class CkCacheRoot
{
    /// <summary>
    /// Returns types of the model
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    public List<CkTypeGraph> Types { get; set; } = new();

    /// <summary>
    /// Returns associations of the model
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    public List<CkAssociationRoleGraph> AssociationRoles { get; set; } = new();

    /// <summary>
    /// Returns attributes of the model
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    public List<CkAttributeGraph> Attributes { get; set; } = new();
}