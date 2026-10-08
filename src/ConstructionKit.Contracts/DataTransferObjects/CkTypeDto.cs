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

    /// <summary>
    ///     Optional attribute path whose value identifies the owner (subject id) of a runtime entity
    ///     for owned-only data permissions (AB#4978), e.g. "AssigneeId" or "Owner.UserId". When absent,
    ///     ownership is the server-stamped rtCreatedBy. Inherited along the derivedFromCkTypeId chain;
    ///     a derived type may override it (nearest declared path wins). Dot-separated segments traverse
    ///     single-valued Record attributes (RecordArray segments are rejected — ownership would be
    ///     multi-valued); the terminal segment must be of value type String. Associations are not
    ///     traversable.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? OwnerAttributePath { get; set; }

    /// <summary>
    ///     CK v2 (AB#5667): the interfaces this type implements, e.g. <c>${System.Identity}/Named-1</c>. Inherited by
    ///     derived types. Requires <c>ckLanguage: 2</c>.
    /// </summary>
    [YamlMember(Alias = "implements", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    [JsonConverter(typeof(CkIdInterfaceIdListConverter))]
    public List<CkId<CkInterfaceId>>? Implements { get; set; }

    /// <summary>
    ///     CK v2 (AB#5669): the methods this type declares. Inherited by derived types. Requires <c>ckLanguage: 2</c>.
    /// </summary>
    [YamlMember(Alias = "methods", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkMethodDto>? Methods { get; set; }

    /// <summary>
    ///     CK v2 (F1.1-S4): <c>Internal</c> elements may only be referenced inside the declaring model. <c>null</c>
    ///     (omitted) means <see cref="CkVisibilityDto.Public" />. Requires <c>ckLanguage: 2</c>.
    /// </summary>
    [YamlMember(Alias = "visibility", DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CkVisibilityDto? Visibility { get; set; }

    /// <summary>
    ///     CK v2 (F1.1-S4): <c>Model</c> = only the declaring model may derive from this element. <c>null</c> (omitted)
    ///     means <c>Model</c> in a <c>ckLanguage: 2</c> model and <c>Any</c> in a v1 model
    ///     (<see cref="CkModifiers.ResolveDerivable" />). Requires <c>ckLanguage: 2</c>.
    /// </summary>
    [YamlMember(Alias = "derivable", DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CkDerivableDto? Derivable { get; set; }
}