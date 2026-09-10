using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     Represents an attribute
/// </summary>
[DebuggerDisplay("{" + nameof(AttributeId) + "}")]
public class CkAttributeDto
{
    private bool _isRuntimeState;

    /// <summary>
    ///     The id of the attribute
    /// </summary>
    [JsonPropertyName("id")]
    [YamlMember(Alias = "id")]
    [JsonRequired]
    public CkAttributeId AttributeId { get; set; } = null!;

    /// <summary>
    ///     Value type of the attribute
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AttributeValueTypesDto ValueType { get; set; }

    /// <summary>
    ///     Defines the record of the attribute if the value type is a record.
    /// </summary>
    [JsonConverter(typeof(CkIdRecordIdConverter))]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkId<CkRecordId>? ValueCkRecordId { get; set; }

    /// <summary>
    ///     Defines the enum of the attribute if the value type is a enum.
    /// </summary>
    [JsonConverter(typeof(CkIdEnumIdConverter))]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public CkId<CkEnumId>? ValueCkEnumId { get; set; }

    /// <summary>
    ///     Default value of the attribute
    /// </summary>
    [JsonConverter(typeof(DefaultValuesConverter))]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public ICollection<object>? DefaultValues { get; set; }

    /// <summary>
    ///     Declares who owns this attribute's value and whether the value is portable — see
    ///     <see cref="AttributeOwnershipDto" /> for the four values and the author decision matrix.
    ///     This is the DEFAULT for every assignment of this attribute; a type-attribute or
    ///     record-attribute assignment may override it via <see cref="CkTypeAttributeDto.Ownership" />.
    ///     <c>null</c> (omitted) means "not declared": the deprecated <see cref="IsRuntimeState" />
    ///     alias is used instead, which is what every model authored before AB#5187 does.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public AttributeOwnershipDto? Ownership { get; set; }

    /// <summary>
    ///     DEPRECATED alias for <see cref="Ownership" />, kept so every model authored before
    ///     AB#5187 compiles and behaves exactly as before. Declaring <c>isRuntimeState: true</c>
    ///     resolves to <see cref="AttributeOwnershipDto.RuntimeState" />, declaring <c>false</c> or
    ///     omitting it resolves to <see cref="AttributeOwnershipDto.SeedOwned" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Once <see cref="Ownership" /> is declared this property stops being an input and
    ///         becomes a computed MIRROR of it, meaning "preserved on Upsert"
    ///         (<see cref="AttributeOwnership.IsPreservedOnUpsert" />) — true for
    ///         <see cref="AttributeOwnershipDto.TenantOwned" />,
    ///         <see cref="AttributeOwnershipDto.RuntimeState" /> and
    ///         <see cref="AttributeOwnershipDto.Secret" />. The mirror is what makes version skew
    ///         safe: it is serialised into the compiled model and persisted by the CK-model
    ///         repository, so an engine that does not yet know <c>ownership</c> reads a
    ///         tenant-owned or secret attribute as <c>isRuntimeState: true</c> and degrades to
    ///         today's behaviour (preserve + exclude from export) instead of regressing to
    ///         "seed wins", which would reset credentials.
    ///     </para>
    ///     <para>
    ///         Declaring both <c>ownership</c> and <c>isRuntimeState</c> on the same attribute is
    ///         an authoring error the <c>CkLintRuntimeStateMarkers</c> MSBuild task rejects
    ///         (OCTO-CK003). The engine resolves it deterministically anyway: <c>ownership</c> wins.
    ///     </para>
    /// </remarks>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool IsRuntimeState
    {
        get => Ownership.HasValue ? Ownership.Value.IsPreservedOnUpsert() : _isRuntimeState;
        set => _isRuntimeState = value;
    }

    /// <summary>
    ///     An optional description of the attribute
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public string? Description { get; set; }

    /// <summary>
    ///     Optional meta data of the attribute
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public ICollection<CkAttributeMetaDataDto>? MetaData { get; set; }
}