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
///     The handler kinds of a CK method (CK v2 Phase 3, AB#6877), the values of
///     <see cref="CkMethodExecutionDto.Handler" />. The values are lower-case, exactly as authored; any other value is
///     rejected by the compiler (message 131).
/// </summary>
public static class CkMethodHandlers
{
    /// <summary>
    ///     A service registers a handler for the method (default; the key is usually omitted).
    /// </summary>
    public const string Service = "service";

    /// <summary>
    ///     The method is implemented by a pipeline: the deployed pipeline whose well-known name equals the qualified
    ///     method id (e.g. <c>Acme.Assets/Device.UpdateFirmware-1</c>) handles the call and no service handler is
    ///     expected. Requires an explicit <see cref="CkMethodExecutionDto.TimeoutSeconds" /> and is not allowed on
    ///     interface methods (message 131).
    /// </summary>
    public const string Pipeline = "pipeline";

    /// <summary>
    ///     True for a supported handler kind (ordinal, lower-case).
    /// </summary>
    public static bool IsKnown(string? handler) => handler is Service or Pipeline;

    /// <summary>
    ///     The effective handler kind: <paramref name="handler" />, or <see cref="Service" /> when omitted.
    /// </summary>
    public static string Effective(string? handler) => handler ?? Service;
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

    /// <summary>
    ///     CK v2 Phase 3 (AB#7024): <c>true</c> declares that this static method replaces the generic create of its
    ///     type — entities of the type (and of every derived type) are created only through the method. Allowed only
    ///     on a <see cref="CkMethodKindDto.Static" /> method of a non-abstract type (never on an interface method), at
    ///     most one per concrete type including inherited ones, and only in a <c>ckLanguage: 2</c> model (message 133;
    ///     message 90 for ckLanguage 1). The compiler only records the flag; the refusal of the generic create is
    ///     enforced by the asset repository. <c>false</c> (default, key omitted) is not serialized, so models that do
    ///     not use the key serialize byte-identically. A model that uses it carries a raised <c>minEngineVersion</c>
    ///     (<c>CkEngineVersion.ReplacesGenericCreateMinEngineVersion</c>), so older engines refuse it with message 126
    ///     instead of silently dropping the flag.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ReplacesGenericCreate { get; set; }
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

    /// <summary>
    ///     CK v2 Phase 3 (AB#7006): <c>true</c> declares the whole result value (scalar, enum or record) as sensitive —
    ///     it must never be logged, audited or traced (the redaction itself is enforced at runtime, not by the
    ///     compiler). The counterpart of <see cref="CkMethodParameterDto.Sensitive" /> for the result. A method with
    ///     <c>execution.handler: pipeline</c> must not have a sensitive result, because a pipeline execution stores its
    ///     output (message 134). <c>false</c> (default, key omitted) is not serialized, so models that do not use the
    ///     key serialize byte-identically. A model that uses it carries a raised <c>minEngineVersion</c>
    ///     (<c>CkEngineVersion.SensitiveResultMinEngineVersion</c>), so older engines refuse it with message 126
    ///     instead of silently dropping the marker. A <c>Secret</c> value type is not sensitive implicitly.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Sensitive { get; set; }
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
    ///     Any-of role names (role claim): one listed role suffices. DEFAULT-DENY (AB#6338, platform-owner decision
    ///     2026-10-10): an omitted or empty list — and an omitted authorization block — admits administrators only, not
    ///     every caller with <c>octo_api</c>. Adding a role is looser, removing one (or emptying the list) stricter.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<string>? Roles { get; set; }

    /// <summary>
    ///     True when the caller may invoke an instance method on its own entity (caller <c>sub</c> == target rtId).
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool AllowSelf { get; set; }

    /// <summary>
    ///     Additional required scopes, all-of: every listed scope is required in addition to <c>octo_api</c>. Adding a
    ///     scope is stricter, removing one looser (AB#6338).
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

    /// <summary>
    ///     CK v2 Phase 3 (AB#6877): who implements the method, see <see cref="CkMethodHandlers" />. <c>null</c>
    ///     (omitted) means <see cref="CkMethodHandlers.Service" />, so models that do not use the key serialize
    ///     byte-identically. An unknown value is reported by the compiler (message 131), not by the schema. The runtime
    ///     that routes <see cref="CkMethodHandlers.Pipeline" /> calls is AB#6879.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Handler { get; set; }
}
