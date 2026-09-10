using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     Defines an assignment of a CK type to a CK attribute.
/// </summary>
[DebuggerDisplay("{" + nameof(CkAttributeId) + "} -> {" + nameof(AttributeName) + "}")]
public class CkTypeAttributeDto
{
    /// <summary>
    ///     Gets or sets the CK attribute id.
    /// </summary>
    [YamlMember(Alias = "id")]
    [JsonPropertyName("id")]
    [JsonRequired]
    [JsonConverter(typeof(CkIdAttributeIdConverter))]
    public CkId<CkAttributeId> CkAttributeId { get; set; } = null!;

    /// <summary>
    ///     Gets or sets the name of the attribute.
    /// </summary>
    [YamlMember(Alias = "name")]
    [JsonPropertyName("name")]
    [JsonRequired]
    public string AttributeName { get; set; } = null!;

    /// <summary>
    ///     Gets or sets a list of values that are used for auto-completion.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<object>? AutoCompleteValues { get; set; }

    /// <summary>
    ///     If auto-completion is enabled, this property defines the attribute that is used as a reference for the auto-completion values.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? AutoIncrementReference { get; set; }

    /// <summary>
    ///     If true, the attribute is optional, that means it can be null
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool IsOptional { get; set; }

    /// <summary>
    ///     Optional per-assignment override of the attribute definition's
    ///     <see cref="CkAttributeDto.Ownership" />. <c>null</c> (omitted) means "inherit from the
    ///     definition" — which is what every assignment authored before AB#5187 declares, so
    ///     existing models are untouched.
    /// </summary>
    /// <remarks>
    ///     The override exists because ownership sits on the attribute DEFINITION and one
    ///     definition is shared by many types: a single <c>ClientId</c> is assigned by
    ///     <c>FinApiConfiguration</c>, <c>MicrosoftGraphConfiguration</c> and
    ///     <c>ServiceAccountConfiguration</c>. Marking the definition
    ///     <see cref="AttributeOwnershipDto.Secret" /> would also freeze the service account's
    ///     client id against a blueprint rename. With the override the definition carries the
    ///     common case and the outlier states its own answer, on the assignment, next to the type
    ///     it belongs to (AB#5187, product decision 3). Applies to type attributes, record
    ///     attributes and association-role attributes alike — they all use this DTO.
    /// </remarks>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public AttributeOwnershipDto? Ownership { get; set; }
}