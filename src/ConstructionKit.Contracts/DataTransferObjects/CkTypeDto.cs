using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     Defines a CK type.
/// </summary>
[DebuggerDisplay("{" + nameof(TypeId) + "}")]
public class CkTypeDto : CkTypeWithAttributesDto
{
    /// <summary>
    ///     Gets or sets the construction kit id
    /// </summary>
    [JsonRequired]
    public CkTypeId TypeId { get; set; } = null!;

    /// <summary>
    ///     Defines the base type of this type. Only one type may not have a base type: System/Entity
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
    ///     Gets or sets a list of indexes
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkTypeIndexDto>? Indexes { get; set; }

    /// <summary>
    ///     Get or sets a list of associations
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkTypeAssociationDto>? Associations { get; set; }

    /// <summary>
    ///     Gets or sets if the change stream should include pre and post images
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool EnableChangeStreamPreAndPostImages { get; set; }

    /// <summary>
    ///     An optional description of the type
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }

    /// <summary>
    ///     Optional rule defining how the display name (rtDisplayName) of a runtime entity is computed
    ///     from its attribute values on save, e.g. "${roomNumber} - ${name ?? globalId}".
    ///     Supports ${attributePath} interpolation (own attributes including record paths, no associations)
    ///     and the ?? coalesce operator. Inherited along the derivedFromCkTypeId chain; a derived type
    ///     may override it with its own rule (nearest non-empty rule wins).
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? DisplayNameRule { get; set; }

    /// <summary>
    ///     Optional rule defining how the display description (rtDisplayDescription) of a runtime entity
    ///     is computed from its attribute values on save. Same syntax and inheritance semantics as
    ///     <see cref="DisplayNameRule" />.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? DisplayDescriptionRule { get; set; }
}