using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// Defines the common properties of a CK model root
/// </summary>
public abstract class CkModelRootBase : CkModelPropertiesDto
{
    /// <summary>
    ///     Returns types of the model
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkCompiledTypeDto>? Types { get; set; }

    /// <summary>
    ///     Returns associations of the model
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkAssociationRoleDto>? AssociationRoles { get; set; }

    /// <summary>
    ///     Returns attributes of the model
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkAttributeDto>? Attributes { get; set; }

    /// <summary>
    ///     Returns records of the model that are used for complex attributes
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkRecordDto>? Records { get; set; }

    /// <summary>
    ///     Returns enums of the model used for enum attributes
    /// </summary>
    // ReSharper disable once CollectionNeverUpdated.Global
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkEnumDto>? Enums { get; set; }
}