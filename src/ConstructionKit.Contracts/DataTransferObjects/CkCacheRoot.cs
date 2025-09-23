using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     The root object of the compiled version of a CK model.
/// </summary>
public class CkCacheRoot
{
    /// <summary>
    ///     Returns types of the model
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    public List<CkTypeGraph> Types { get; set; } = [];

    /// <summary>
    ///     Returns associations of the model
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    public List<CkAssociationRoleGraph> AssociationRoles { get; set; } = [];

    /// <summary>
    ///     Returns attributes of the model
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    public List<CkAttributeGraph> Attributes { get; set; } = [];

    /// <summary>
    ///     Returns records of the model
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    public List<CkRecordGraph> Records { get; set; } = [];

    /// <summary>
    ///     Returns enums of the model
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    public List<CkEnumGraph> Enums { get; set; } = [];

    /// <summary>
    ///     Returns a list of model dependencies of the graph
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    public Dictionary<CkModelId, ICollection<CkModelIdVersionRange>> Dependencies { get; set; } = new();
    
    /// <summary>
    ///     Returns a list of models of the graph
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    public List<CkModelPropertiesDto> Models { get; set; } = [];
}