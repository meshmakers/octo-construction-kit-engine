using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     Describes a construction kit record that is used as structured type of an attribute
/// </summary>
[DebuggerDisplay("{" + nameof(RecordId) + "}")]
public class CkRecordDto : CkTypeWithAttributesDto
{
    /// <summary>
    ///     Gets or sets the construction kit id
    /// </summary>
    [JsonRequired]
    public CkRecordId RecordId { get; set; } = null!;

    /// <summary>
    ///     Defines the base record of this record.
    /// </summary>
    [JsonConverter(typeof(CkIdRecordIdConverter))]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkId<CkRecordId>? DerivedFromCkRecordId { get; set; }

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
    ///     Name of the sub-attribute that identifies an element of this record inside a record
    ///     array (AB#5528, concept §4.6). Required when the record contains a
    ///     <see cref="AttributeValueTypesDto.Secret" /> sub-attribute: when a record array is
    ///     replaced, a secret sub-value the client left empty is carried over from the stored
    ///     element with the same key. Inherited by derived records unless they declare their own.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? RecordKey { get; set; }
    
    /// <summary>
    ///     An optional description of the record
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }
}