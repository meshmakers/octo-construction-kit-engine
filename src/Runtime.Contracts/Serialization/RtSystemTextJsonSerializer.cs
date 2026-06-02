using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
/// System.Text.Json counterpart of <see cref="RtNewtonsoftSerializer"/> — the canonical STJ options
/// for the Rt model. Lives next to its Newtonsoft twin and aggregates the same converter family
/// (the CK/Rt id converters plus <see cref="RtAttributesConverter"/>).
/// </summary>
/// <remarks>
/// <para>
/// Like <see cref="RtNewtonsoftSerializer"/> (which uses <c>DefaultValueHandling.Ignore</c>), this
/// default <b>drops null properties</b> on write (<see cref="JsonIgnoreCondition.WhenWritingNull"/>).
/// It does <i>not</i> drop default value-types (<c>0</c>/<c>false</c>) — the STJ migration deliberately
/// stopped doing that.
/// </para>
/// <para>
/// The ETL pipeline needs the opposite null policy (preserve explicit nulls so it can distinguish
/// <c>DataKind.Null</c> from <c>DataKind.Undefined</c>); it overrides
/// <see cref="JsonSerializerOptions.DefaultIgnoreCondition"/> at the SDK level — see
/// <c>SystemTextJsonOptions</c> in <c>Sdk.Common</c>.
/// </para>
/// </remarks>
public static class RtSystemTextJsonSerializer
{
    /// <summary>
    /// Shared default options instance. Drops null properties, matching <see cref="RtNewtonsoftSerializer"/>.
    /// </summary>
    public static readonly JsonSerializerOptions Default = CreateDefault();

    /// <summary>
    /// Creates a fresh <see cref="JsonSerializerOptions"/> with the Rt-model converter family applied.
    /// Callers that need a different null policy (e.g. the pipeline's null preservation) start from
    /// this and override <see cref="JsonSerializerOptions.DefaultIgnoreCondition"/>.
    /// </summary>
    public static JsonSerializerOptions CreateDefault() => new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
                         | JsonNumberHandling.AllowNamedFloatingPointLiterals,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Match RtNewtonsoftSerializer's character escaping: STJ's default JavaScriptEncoder
        // escapes all non-ASCII (ü→ü) and HTML-sensitive chars (&→&, <→<), which would
        // silently change the wire bytes for any payload with umlauts (routine here) versus the
        // legacy Newtonsoft output. UnsafeRelaxedJsonEscaping still escapes control chars / " / \
        // but emits non-ASCII and HTML chars literally — the closest STJ match to Newtonsoft.
        // Flows into SystemTextJsonOptions.Default (octo-sdk) and every wire node.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            // Whole-number doubles/floats/decimals render with a trailing .0, matching
            // Newtonsoft's JsonConvert.ToString rules. Without these, STJ writes 0 for
            // double 0.0 which round-trips as long via JsonScalar.ToClr — the
            // (quantity=0, BsonInt64) regression in MongoDB. Verified by
            // Sdk.Common.PipelineParityTests.AttributeRoundTripClrTypeParityTests.
            new NewtonsoftParityDoubleConverter(),
            new NewtonsoftParitySingleConverter(),
            new NewtonsoftParityDecimalConverter(),
            // Newtonsoft parity: plain CLR enums serialize as their underlying integer (read-tolerant
            // of name strings), NOT as the member name a JsonStringEnumConverter would emit.
            new NewtonsoftParityEnumConverter(),
            new OctoObjectIdConverter(),
            new CkTypeIdConverter(),
            new CkEnumIdConverter(),
            new CkRecordIdConverter(),
            new CkAttributeIdConverter(),
            new CkAssociationRoleIdConverter(),
            new RtCkIdTypeIdConverter(),
            new RtCkIdEnumIdConverter(),
            new RtCkIdRecordIdConverter(),
            new RtCkIdAttributeIdConverter(),
            new RtCkIdAssociationRoleIdConverter(),
            new RtAttributesConverter()
        }
    };
}
