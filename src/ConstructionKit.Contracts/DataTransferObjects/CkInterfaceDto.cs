using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     A CK interface (CK v2, AB#5667): a versioned contract that types declare with <c>implements</c>.
///     Phase 0 interfaces have attribute members only. Used both in source YAML (<c>interfaces/*.yaml</c>) and in
///     compiled models.
/// </summary>
[DebuggerDisplay("{" + nameof(InterfaceId) + "}")]
public class CkInterfaceDto
{
    /// <summary>
    ///     The interface id with its mandatory element version (= contract version), e.g. <c>Named-1</c>.
    /// </summary>
    [YamlMember(Alias = "interfaceId")]
    [JsonRequired]
    public CkInterfaceId InterfaceId { get; set; } = null!;

    /// <summary>
    ///     An optional description of the interface.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }

    /// <summary>
    ///     The attribute members of the interface.
    /// </summary>
    public List<CkInterfaceAttributeDto> Attributes { get; set; } = [];

    /// <summary>
    ///     CK v2 (F1.1-S4): <c>Internal</c> elements may only be referenced inside the declaring model. <c>null</c>
    ///     (omitted) means <see cref="CkVisibilityDto.Public" />. Requires <c>ckLanguage: 2</c>.
    /// </summary>
    [YamlMember(Alias = "visibility", DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CkVisibilityDto? Visibility { get; set; }
}

/// <summary>
///     An attribute member of a <see cref="CkInterfaceDto" /> (CK v2, AB#5667). Implementing types must assign the
///     referenced attribute definition under the same <see cref="AttributeName" />.
/// </summary>
[DebuggerDisplay("{" + nameof(CkAttributeId) + "} -> {" + nameof(AttributeName) + "}")]
public class CkInterfaceAttributeDto
{
    /// <summary>
    ///     The attribute definition the member refers to, e.g. <c>${System}/Name</c>.
    /// </summary>
    [YamlMember(Alias = "id")]
    [JsonPropertyName("id")]
    [JsonRequired]
    [JsonConverter(typeof(CkIdAttributeIdConverter))]
    public CkId<CkAttributeId> CkAttributeId { get; set; } = null!;

    /// <summary>
    ///     The member name; implementing types must use the same assignment name.
    /// </summary>
    [YamlMember(Alias = "name")]
    [JsonPropertyName("name")]
    [JsonRequired]
    public string AttributeName { get; set; } = null!;

    /// <summary>
    ///     Optional members need not be assigned by implementing types.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool IsOptional { get; set; }
}
