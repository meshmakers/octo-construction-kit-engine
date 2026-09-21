using System.Globalization;
using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Exchange;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// AB#5297: decides whether a blueprint update would actually change a tenant entity, and which
/// attributes. Before this, the update preview counted every blueprint-managed entity as "to
/// update" without looking at a single value - a target identical to the tenant reported 137
/// updates, and the three hand-enabled pipelines the seed would have flipped back to disabled
/// were indistinguishable from the 134 that would not move. The preview is the only instrument an
/// operator has before touching production; it has to name what it would change.
/// <para>
/// Mirrors the apply path (<see cref="ImportRtModelCommand" />, upsert = full replace): every
/// attribute of the CK type that the tenant does NOT own is compared - the seed's value against
/// the stored one. Attributes the tenant owns (RuntimeState, TenantOwned, Secret) are preserved
/// by the upsert and therefore never reported. A seed that omits an attribute the tenant holds
/// would CLEAR it; that is a change with a null new value. Values are normalised the way the
/// import reads them - enum names resolve to their key, date strings to UTC instants, numbers by
/// value - so "Matched" against 1 and "2026-01-01" against midnight UTC are equal, and then
/// compared structurally through their JSON shape. Anything this cannot decide is reported as a
/// change: for a safety preview, over-reporting is the acceptable failure direction.
/// </para>
/// Pure function so it is unit-testable without the repository or the CK cache: the two
/// conversions that need them are injected.
/// </summary>
internal static class BlueprintEntityComparer
{
    /// <summary>
    /// The blueprint's own bookkeeping on every managed entity. LoadAndTagSeedAsync stamps the
    /// target version and a fresh UtcNow onto each seed before the comparison runs, so these
    /// differ on every locked entity by construction - they are the apply's provenance, not a
    /// change to the entity, and the review of AB#5297 caught that they would have turned the
    /// promised "3 changed / 134 unchanged" back into 137.
    /// </summary>
    private static readonly HashSet<string> BlueprintBookkeeping = new(StringComparer.Ordinal)
    {
        "RtBlueprintSource", "RtBlueprintAppliedAt", "RtBlueprintLocked"
    };

    /// <param name="seed">The entity as the target blueprint's seed defines it.</param>
    /// <param name="tenant">The entity as stored on the tenant.</param>
    /// <param name="ckType">The entity's CK type, for attribute names, ownership and value types.</param>
    /// <param name="toTransport">Repository value to transport shape (production: <see cref="ImportRtModelCommand.ToTransportValue" />).</param>
    /// <param name="resolveEnum">Enum lookup by id; null when the enum is unknown.</param>
    internal static List<BlueprintAttributeChange> Compare(
        RtEntityTcDto seed,
        RtEntity tenant,
        CkTypeWithAttributesGraph ckType,
        Func<object?, object?> toTransport,
        Func<CkId<CkEnumId>, CkEnumGraph?> resolveEnum)
    {
        var changes = new List<BlueprintAttributeChange>();

        foreach (var attribute in ckType.AllAttributes.Values)
        {
            if (BlueprintBookkeeping.Contains(attribute.AttributeName))
            {
                continue;
            }

            if (attribute.Ownership.IsPreservedOnUpsert())
            {
                // The apply carries the stored value over (ImportRtModelCommand.PreserveAttributesForEntity);
                // whatever the seed says here never lands.
                continue;
            }

            var seedAttribute = seed.Attributes.FirstOrDefault(a => a.Id.Equals(attribute.CkAttributeId));
            tenant.Attributes.TryGetValue(attribute.AttributeName, out var storedRaw);

            object? seedValue;
            object? storedValue;
            if (attribute.ValueType is AttributeValueTypesDto.Record or AttributeValueTypesDto.RecordArray)
            {
                // Records: the seed carries transport DTOs, the repository RtRecords. Compare in
                // transport shape. Member-level import conversions (trimming, enum names inside a
                // record) are not replayed here, so a record that differs only in those reports
                // as a change - over-reporting, the acceptable direction.
                seedValue = seedAttribute?.Value;
                storedValue = toTransport(storedRaw);
            }
            else
            {
                // Scalars and primitive lists: run BOTH sides through the converter the import
                // applies on write (AttributeValueConverter: trims strings, parses booleans and
                // doubles, turns tick strings into TimeSpans, list wrappers into plain lists) so a
                // seed "  x " against a stored "x", or a stored AttributeStringValueList against a
                // seed string[], compare as what they would be once written - not as the shapes
                // they happen to arrive in. Enums resolve to their key on top of that.
                seedValue = Normalise(ConvertLikeTheImport(attribute, seedAttribute?.Value), attribute, resolveEnum);
                storedValue = Normalise(ConvertLikeTheImport(attribute, storedRaw), attribute, resolveEnum);
            }

            if (ValuesEqual(seedValue, storedValue))
            {
                continue;
            }

            changes.Add(new BlueprintAttributeChange
            {
                AttributeName = attribute.AttributeName,
                OldValue = storedValue,
                NewValue = seedValue
            });
        }

        return changes;
    }

    /// <summary>
    /// The import's own write-side conversion (<see cref="AttributeValueConverter" />), applied to
    /// a value regardless of which side it came from. A conversion the converter refuses leaves
    /// the raw value in place, which at worst reports a change that the apply would not make.
    /// </summary>
    private static object? ConvertLikeTheImport(CkTypeAttributeGraph attribute, object? value)
    {
        if (value == null || attribute.ValueType == AttributeValueTypesDto.Enum)
        {
            return value;
        }

        try
        {
            return AttributeValueConverter.ConvertAttributeValue(attribute.ValueType, value) ?? value;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            return value;
        }
    }

    /// <summary>
    /// Bring both sides into the shape the import would store, as far as that can be done
    /// without the repository: enum names to keys, date strings to UTC instants, integral
    /// numbers to <see cref="long" />, floating numbers to <see cref="double" />.
    /// </summary>
    internal static object? Normalise(object? value, CkTypeAttributeGraph attribute,
        Func<CkId<CkEnumId>, CkEnumGraph?> resolveEnum)
    {
        if (value == null)
        {
            return null;
        }

        if (value is JsonElement element)
        {
            value = FromJsonElement(element);
            if (value == null)
            {
                return null;
            }
        }

        switch (attribute.ValueType)
        {
            case AttributeValueTypesDto.Enum:
                return NormaliseEnum(value, attribute, resolveEnum);
            case AttributeValueTypesDto.DateTime:
            case AttributeValueTypesDto.DateTimeOffset:
                return NormaliseDate(value);
            case AttributeValueTypesDto.Integer:
            case AttributeValueTypesDto.Integer64:
                return NormaliseIntegral(value);
            case AttributeValueTypesDto.Double:
                return NormaliseFloating(value);
            case AttributeValueTypesDto.Boolean:
                return value is string boolText && bool.TryParse(boolText, out var parsed) ? parsed : value;
            case AttributeValueTypesDto.TimeSpan:
                return value is string spanText && TimeSpan.TryParse(spanText, CultureInfo.InvariantCulture, out var span) ? span : value;
            default:
                return value;
        }
    }

    private static object NormaliseEnum(object value, CkTypeAttributeGraph attribute,
        Func<CkId<CkEnumId>, CkEnumGraph?> resolveEnum)
    {
        var enumGraph = attribute.ValueCkEnumId == null ? null : resolveEnum(attribute.ValueCkEnumId);
        if (enumGraph == null)
        {
            return value;
        }

        // Same three matches as ImportRtModelCommand: key, key as text, name (case-insensitive).
        var text = value.ToString();
        var match = enumGraph.Values.FirstOrDefault(v =>
            v.Key.Equals(value)
            || string.Equals(v.Key.ToString(CultureInfo.InvariantCulture), text, StringComparison.Ordinal)
            || string.Equals(v.Name, text, StringComparison.OrdinalIgnoreCase));
        return match == null ? value : (object)(long)match.Key;
    }

    private static object NormaliseDate(object value)
    {
        switch (value)
        {
            case DateTime dt:
                return dt.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                    : dt.ToUniversalTime();
            case DateTimeOffset dto:
                return dto.UtcDateTime;
            case string text when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed):
                return parsed.UtcDateTime;
            default:
                return value;
        }
    }

    private static object NormaliseIntegral(object value)
    {
        return value switch
        {
            int i => (long)i,
            long l => l,
            short sh => (long)sh,
            byte b => (long)b,
            double d when Math.Abs(d - Math.Round(d)) < double.Epsilon => (long)d,
            decimal m when m == Math.Round(m) => (long)m,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => value
        };
    }

    private static object NormaliseFloating(object value)
    {
        return value switch
        {
            int i => (double)i,
            long l => (double)l,
            float f => (double)f,
            decimal m => (double)m,
            double d => d,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => value
        };
    }

    private static object? FromJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            _ => element
        };
    }

    /// <summary>
    /// Structural equality through the JSON shape: records, lists and scalars alike, after both
    /// sides went through <see cref="Normalise" />. Two values that serialise identically are the
    /// same stored document.
    /// </summary>
    internal static bool ValuesEqual(object? a, object? b)
    {
        if (a == null && b == null)
        {
            return true;
        }

        if (a == null || b == null)
        {
            return false;
        }

        if (a.Equals(b))
        {
            return true;
        }

        try
        {
            return string.Equals(
                JsonSerializer.Serialize(a, a.GetType()),
                JsonSerializer.Serialize(b, b.GetType()),
                StringComparison.Ordinal);
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
