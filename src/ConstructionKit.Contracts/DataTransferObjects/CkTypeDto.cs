using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// Defines a CK type.
/// </summary>
[DebuggerDisplay("{" + nameof(TypeId) + "}")]
public class CkTypeDto
{
    /// <summary>
    ///     Gets or sets the construction kit id
    /// </summary>
    [JsonRequired]
    public CkTypeId TypeId { get; set; }

    /// <summary>
    /// Defines the base type of this type. Only one type may not have a base type: System/Entity
    /// </summary>
    [JsonConverter(typeof(CkIdTypeIdConverter))]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkId<CkTypeId>? DerivedFromCkTypeId { get; set; }

    /// <summary>
    ///     If true, the type cannot be inherited again
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool IsFinal { get; set; }

    /// <summary>
    ///     If true, the type cannot be instantiated by a runtime entity
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool IsAbstract { get; set; }

    /// <summary>
    ///     Gets or sets a list of attributes
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkTypeAttributeDto>? Attributes { get; set; }

    /// <summary>
    /// Gets or sets a list of indexes
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkTypeIndexDto>? Indexes { get; set; }

    /// <summary>
    /// Get or sets a list of associations
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkTypeAssociationDto>? Associations { get; set; }

    /// <summary>
    /// Gets or sets if the change stream should include pre and post images
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool EnableChangeStreamPreAndPostImages { get; set; }
}