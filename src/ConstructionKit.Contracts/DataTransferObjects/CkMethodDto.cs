using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     Kind of a CK method (CK v2, AB#5669).
/// </summary>
public enum CkMethodKindDto
{
    /// <summary>
    ///     The method is invoked on a runtime entity (default).
    /// </summary>
    Instance = 0,

    /// <summary>
    ///     The method is invoked on the type, without a target entity.
    /// </summary>
    Static = 1
}

/// <summary>
///     A method declared on a CK type or interface (CK v2, AB#5669) — a definition only, there is no invocation
///     runtime yet. Methods are inherited by derived and implementing types; a derived type must not re-declare an
///     inherited method id, and a type must not redeclare an interface method with another signature.
/// </summary>
[DebuggerDisplay("{" + nameof(MethodId) + "}")]
public class CkMethodDto
{
    /// <summary>
    ///     The element-versioned method id, e.g. <c>ChangePassword-1</c>.
    /// </summary>
    [YamlMember(Alias = "methodId")]
    [JsonRequired]
    public string MethodId { get; set; } = null!;

    /// <summary>
    ///     The method kind; omitted means <see cref="CkMethodKindDto.Instance" />.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkMethodKindDto Kind { get; set; }

    /// <summary>
    ///     An optional description of the method.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }

    /// <summary>
    ///     The parameters of the method.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkMethodParameterDto>? Parameters { get; set; }

    /// <summary>
    ///     The result of the method; <c>null</c> means the method has no result.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkMethodResultDto? Result { get; set; }

    /// <summary>
    ///     The domain error codes the method may return as data.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<CkMethodErrorDto>? Errors { get; set; }

    /// <summary>
    ///     Who may invoke the method.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkMethodAuthorizationDto? Authorization { get; set; }

    /// <summary>
    ///     Execution options (timeout, idempotency).
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkMethodExecutionDto? Execution { get; set; }

    /// <summary>
    ///     CK v2 (F1.1-S4): <c>Internal</c> elements may only be referenced inside the declaring model. <c>null</c>
    ///     (omitted) means <see cref="CkVisibilityDto.Public" />. Requires <c>ckLanguage: 2</c>.
    /// </summary>
    [YamlMember(Alias = "visibility", DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CkVisibilityDto? Visibility { get; set; }
}

/// <summary>
///     A parameter of a <see cref="CkMethodDto" /> (CK v2, AB#5669).
/// </summary>
[DebuggerDisplay("{" + nameof(Name) + "}: {" + nameof(ValueType) + "}")]
public class CkMethodParameterDto
{
    /// <summary>
    ///     The parameter name in camelCase, e.g. <c>newPassword</c>.
    /// </summary>
    [JsonRequired]
    public string Name { get; set; } = null!;

    /// <summary>
    ///     The value type of the parameter.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AttributeValueTypesDto ValueType { get; set; }

    /// <summary>
    ///     The record of a <see cref="AttributeValueTypesDto.Record" /> parameter.
    /// </summary>
    [JsonConverter(typeof(CkIdRecordIdConverter))]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkId<CkRecordId>? ValueCkRecordId { get; set; }

    /// <summary>
    ///     The enum of an <see cref="AttributeValueTypesDto.Enum" /> parameter.
    /// </summary>
    [JsonConverter(typeof(CkIdEnumIdConverter))]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkId<CkEnumId>? ValueCkEnumId { get; set; }

    /// <summary>
    ///     True when the caller may omit the parameter.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool IsOptional { get; set; }

    /// <summary>
    ///     True when the value must never be logged, audited or traced (e.g. passwords).
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool Sensitive { get; set; }

    /// <summary>
    ///     An optional description of the parameter.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }
}

/// <summary>
///     The result of a <see cref="CkMethodDto" /> (CK v2, AB#5669).
/// </summary>
[DebuggerDisplay("{" + nameof(ValueType) + "}")]
public class CkMethodResultDto
{
    /// <summary>
    ///     The value type of the result.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AttributeValueTypesDto ValueType { get; set; }

    /// <summary>
    ///     The record of a <see cref="AttributeValueTypesDto.Record" /> result.
    /// </summary>
    [JsonConverter(typeof(CkIdRecordIdConverter))]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkId<CkRecordId>? ValueCkRecordId { get; set; }

    /// <summary>
    ///     The enum of an <see cref="AttributeValueTypesDto.Enum" /> result.
    /// </summary>
    [JsonConverter(typeof(CkIdEnumIdConverter))]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkId<CkEnumId>? ValueCkEnumId { get; set; }
}

/// <summary>
///     A domain error code a <see cref="CkMethodDto" /> may return (CK v2, AB#5669). Codes are UPPER_SNAKE_CASE and
///     must not start with <c>METHOD_</c> (reserved for platform errors).
/// </summary>
[DebuggerDisplay("{" + nameof(Code) + "}")]
public class CkMethodErrorDto
{
    /// <summary>
    ///     The error code, e.g. <c>PASSWORD_POLICY_VIOLATION</c>.
    /// </summary>
    [JsonRequired]
    public string Code { get; set; } = null!;

    /// <summary>
    ///     An optional description of the error.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }
}

/// <summary>
///     Authorization of a <see cref="CkMethodDto" /> (CK v2, AB#5669). The scope <c>octo_api</c> is always required.
/// </summary>
public class CkMethodAuthorizationDto
{
    /// <summary>
    ///     Any-of role names (role claim).
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<string>? Roles { get; set; }

    /// <summary>
    ///     True when the caller may invoke an instance method on its own entity (caller <c>sub</c> == target rtId).
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool AllowSelf { get; set; }

    /// <summary>
    ///     Additional required scopes.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<string>? Scopes { get; set; }
}

/// <summary>
///     Execution options of a <see cref="CkMethodDto" /> (CK v2, AB#5669).
/// </summary>
public class CkMethodExecutionDto
{
    /// <summary>
    ///     The timeout used when <see cref="TimeoutSeconds" /> is omitted.
    /// </summary>
    public const int DefaultTimeoutSeconds = 15;

    /// <summary>
    ///     Timeout in seconds (1-300); omitted means <see cref="DefaultTimeoutSeconds" />.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    ///     True when the method may safely be retried with the same arguments. Omitted means false.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool Idempotent { get; set; }
}
