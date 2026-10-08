using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     A CK interface (CK v2, AB#5667): a versioned contract that types declare with <c>implements</c>.
///     Members are attributes, associations (F1.1-S5) and methods (F1.1-S5, definitions only); an interface may
///     extend other interfaces and inherits their members. Used both in source YAML (<c>interfaces/*.yaml</c>) and
///     in compiled models.
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
    ///     The attribute members of the interface (may be empty when the interface has other members or extends
    ///     another interface, F1.1-S5).
    /// </summary>
    public List<CkInterfaceAttributeDto> Attributes { get; set; } = [];

    /// <summary>
    ///     CK v2 (F1.1-S5): the interfaces this interface extends, e.g. <c>${this}/Named-1</c>. Their members are
    ///     inherited (transitively); implementing this interface implements the extended ones, too.
    /// </summary>
    [YamlMember(Alias = "extends", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    [JsonConverter(typeof(CkIdInterfaceIdListConverter))]
    public List<CkId<CkInterfaceId>>? Extends { get; set; }

    /// <summary>
    ///     CK v2 (F1.1-S5): the association members — an implementing type has an outbound association with the role
    ///     to a target type or interface.
    /// </summary>
    [YamlMember(Alias = "associations", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkInterfaceAssociationDto>? Associations { get; set; }

    /// <summary>
    ///     CK v2 (F1.1-S5): the method members (definitions only, same schema as type methods).
    /// </summary>
    [YamlMember(Alias = "methods", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkMethodDto>? Methods { get; set; }

    /// <summary>
    ///     CK v2 (F1.1-S5): a deprecated interface stays usable; dependents get a compile warning. It is removed in
    ///     the next model major only. <c>null</c> (omitted) means <c>false</c>.
    /// </summary>
    [YamlMember(Alias = "deprecated", DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public bool? Deprecated { get; set; }

    /// <summary>
    ///     CK v2 (F1.1-S4): <c>Internal</c> elements may only be referenced inside the declaring model. <c>null</c>
    ///     (omitted) means <see cref="CkVisibilityDto.Public" />. Requires <c>ckLanguage: 2</c>.
    /// </summary>
    [YamlMember(Alias = "visibility", DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CkVisibilityDto? Visibility { get; set; }
}

/// <summary>
///     An association member of a <see cref="CkInterfaceDto" /> (CK v2, F1.1-S5): implementing types must have an
///     outbound association with the role <see cref="CkRoleId" />, whose target is (or derives from)
///     <see cref="TargetCkTypeId" /> or implements <see cref="TargetCkInterfaceId" />. Exactly one target is set.
/// </summary>
[DebuggerDisplay("{" + nameof(CkRoleId) + "}")]
public class CkInterfaceAssociationDto
{
    /// <summary>
    ///     The association role, e.g. <c>${System}/ParentChild</c>.
    /// </summary>
    [YamlMember(Alias = "id")]
    [JsonPropertyName("id")]
    [JsonRequired]
    [JsonConverter(typeof(CkIdAssociationRoleIdConverter))]
    public CkId<CkAssociationRoleId> CkRoleId { get; set; } = null!;

    /// <summary>
    ///     The target type (exclusive with <see cref="TargetCkInterfaceId" />).
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    [JsonConverter(typeof(CkIdTypeIdConverter))]
    public CkId<CkTypeId>? TargetCkTypeId { get; set; }

    /// <summary>
    ///     The target interface (exclusive with <see cref="TargetCkTypeId" />).
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    [JsonConverter(typeof(CkIdInterfaceIdConverter))]
    public CkId<CkInterfaceId>? TargetCkInterfaceId { get; set; }

    /// <summary>
    ///     The required outbound multiplicity of the implementing association; <c>null</c> = any.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MultiplicitiesDto? Multiplicity { get; set; }

    /// <summary>
    ///     Optional members need not be provided by implementing types.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool IsOptional { get; set; }
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
