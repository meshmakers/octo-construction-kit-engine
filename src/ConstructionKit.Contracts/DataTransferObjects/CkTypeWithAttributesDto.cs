using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// Represents a construction kit type with attributes in the dependency graph
/// </summary>
public abstract class CkTypeWithAttributesDto
{
    /// <summary>
    ///     Gets or sets a list of attributes
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkTypeAttributeDto>? Attributes { get; set; }
}