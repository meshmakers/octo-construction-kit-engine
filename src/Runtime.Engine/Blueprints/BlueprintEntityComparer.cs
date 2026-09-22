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
    /// <param name="resolveRecord">
    /// Resolves a record id to its CK graph so record MEMBERS run through the same conversions as
    /// top-level attributes (AB#5308). Without it an enum member the seed writes as "4" compares
    /// unequal to the stored key 4, and every dashboard query reports a phantom change.
    /// </param>
    /// <param name="resolveEnum">Enum lookup by id; null when the enum is unknown.</param>
    internal static List<BlueprintAttributeChange> Compare(
        RtEntityTcDto seed,
        RtEntity tenant,
        CkTypeWithAttributesGraph ckType,
        Func<object?, object?> toTransport,
        Func<CkId<CkEnumId>, CkEnumGraph?> resolveEnum,
        Func<RtCkId<CkRecordId>, CkRecordGraph?>? resolveRecord = null)
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

            // What the WRITE will put there, not what the seed literally says. Two ways those
            // differ, both of which made a zero-change target report changes (AB#5308):
            //  * an attribute the seed OMITS keeps the CK defaultValues entry the import created
            //    the entity with (RuntimeRepositoryBase.CreateTransientRtEntity, then
            //    AssignAttributes only touches what the seed declares) - the seed saying nothing
            //    about Adapter.LifecycleMode re-writes the stored 0, it does not clear it;
            //  * a RecordArray whose seed value is null lands as an EMPTY LIST, because
            //    AssignAttributes builds its List<RtRecord> and assigns it unconditionally -
            //    "Sorting: null" in the seed and a stored [] are the same state.
            var seedRaw = EffectiveSeedValue(attribute, seedAttribute);

            // ONE failure boundary around preparation and comparison alike. The conversions
            // can throw too - ToTransportValue resolves record definitions through the CK cache
            // and a stale or orphaned record blows up there - and a value the comparer cannot
            // prepare must surface as a change with the raw values, never as a failed preview
            // (3.4.124 took PreviewBlueprintUpdate AND UpdateBlueprint down on prod-1 with an
            // exception from inside this loop).
            object? seedValue = seedRaw;
            object? storedValue = storedRaw;
            bool equal;
            try
            {
                if (attribute.ValueType is AttributeValueTypesDto.Record or AttributeValueTypesDto.RecordArray)
                {
                    // Records: the seed carries transport DTOs, the repository RtRecords. Compare
                    // in transport shape. Member-level import conversions (trimming, enum names
                    // inside a record) run through the member's own declaration since AB#5308,
                    // resolved through its record graph - an enum member the seed writes as "4"
                    // is the stored key 4 once imported.
                    storedValue = toTransport(storedRaw);
                    equal = ValuesEqual(seedValue, storedValue,
                        NestedRecordGraph(attribute, resolveRecord), resolveEnum, resolveRecord);

                    // A RecordArray seed null writes an empty list (see EffectiveSeedValue).
                    if (!equal && attribute.ValueType == AttributeValueTypesDto.RecordArray
                              && IsEmptyOrNullSequence(seedValue) && IsEmptyOrNullSequence(storedValue))
                    {
                        equal = true;
                    }

                    if (equal)
                    {
                        continue;
                    }

                    changes.Add(new BlueprintAttributeChange
                    {
                        AttributeName = attribute.AttributeName,
                        OldValue = storedValue,
                        NewValue = seedValue
                    });
                    continue;
                }
                else
                {
                    // Scalars and primitive lists: run BOTH sides through the converter the
                    // import applies on write (AttributeValueConverter: trims strings, parses
                    // booleans and doubles, turns tick strings into TimeSpans, list wrappers
                    // into plain lists) so a seed "  x " against a stored "x", or a stored
                    // AttributeStringValueList against a seed string[], compare as what they
                    // would be once written. Enums resolve to their key on top of that.
                    seedValue = Normalise(ConvertLikeTheImport(attribute, seedRaw), attribute, resolveEnum);
                    storedValue = Normalise(ConvertLikeTheImport(attribute, storedRaw), attribute, resolveEnum);
                }

                equal = ValuesEqual(seedValue, storedValue);
            }
            catch (Exception)
            {
                equal = false;
            }

            if (equal)
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
    /// <summary>
    ///     The value the apply will actually write for <paramref name="attribute" />, mirroring the
    ///     import path rather than the rule engine: the blueprint apply goes through
    ///     <c>ImportRtModelCommand</c>, whose bulk path deliberately BYPASSES
    ///     <c>EntityRuleEngine</c> (AB#4772 - that is why it collects mandatory-attribute
    ///     violations itself), so <c>SetDefaultValuesOnInsert</c> never runs here.
    ///     <para>
    ///     What runs instead: <c>RuntimeRepositoryBase.CreateTransientRtEntity</c> pre-populates
    ///     every attribute that declares <c>defaultValues</c> - optional ones included - and then
    ///     <c>AssignAttributes</c> overwrites only the attributes the seed actually declares. So an
    ///     OMITTED attribute keeps its default, while an attribute the seed declares as null is
    ///     written as null and the default is gone. The pre-population switch is mirrored exactly,
    ///     <see cref="AttributeValueTypesDto.StringArray" /> / <see cref="AttributeValueTypesDto.IntArray" />
    ///     taking the whole collection and everything else - RecordArray included - the first entry.
    ///     </para>
    ///     The RecordArray "null writes an empty list" rule is the caller's, because it applies
    ///     whether or not the attribute declares a default.
    /// </summary>
    internal static object? EffectiveSeedValue(CkTypeAttributeGraph attribute, RtAttributeTcDto? seedAttribute)
    {
        if (seedAttribute != null)
        {
            return seedAttribute.Value;
        }

        if (attribute.DefaultValues == null || attribute.DefaultValues.Count == 0)
        {
            return null;
        }

        return attribute.ValueType switch
        {
            AttributeValueTypesDto.IntArray or AttributeValueTypesDto.StringArray => attribute.DefaultValues,
            _ => attribute.DefaultValues.FirstOrDefault()
        };
    }

    private static CkRecordGraph? NestedRecordGraph(CkTypeAttributeGraph attribute,
        Func<RtCkId<CkRecordId>, CkRecordGraph?>? resolveRecord)
    {
        return attribute.ValueCkRecordId == null || resolveRecord == null
            ? null
            : resolveRecord(attribute.ValueCkRecordId.ToRtCkId());
    }

    private static bool IsEmptyOrNullSequence(object? value)
    {
        return value switch
        {
            null => true,
            string => false,
            System.Collections.IEnumerable items => !items.Cast<object?>().Any(),
            _ => false
        };
    }

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
        catch (Exception)
        {
            // Whatever the converter refuses stays raw and compares as-is; a refused conversion
            // is at worst a reported change, never a failed preview.
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
    /// Structural equality without a serializer. The first version serialised both sides with
    /// the default System.Text.Json options and compared the text - and the engine's transport
    /// DTOs are not meant for those options: <c>RtRecordTcDto.CkRecordId</c> carries a converter
    /// attribute that only works inside <c>RtSystemTextJsonSerializer</c>'s registered set, so
    /// the first record-valued attribute on prod-1 threw and took PreviewBlueprintUpdate and
    /// UpdateBlueprint down with it (3.4.124). Walk the shapes directly instead: records by id
    /// and attribute set, sequences element by element, scalars by value with numeric
    /// cross-type tolerance. Anything unrecognised compares by <see cref="object.Equals(object)" />
    /// - which for two distinct instances says "different", the over-reporting direction.
    /// </summary>
    internal static bool ValuesEqual(object? a, object? b)
    {
        return ValuesEqual(a, b, null, null, null);
    }

    private static bool ValuesEqual(object? a, object? b, CkRecordGraph? recordGraph,
        Func<CkId<CkEnumId>, CkEnumGraph?>? resolveEnum,
        Func<RtCkId<CkRecordId>, CkRecordGraph?>? resolveRecord)
    {
        // JSON first: a JSON null is a null, and two containers from different documents are
        // equal by content, not by instance. Both would otherwise fall through to
        // JsonElement.Equals, which is reference-like and says "different".
        if (a is JsonElement ea)
        {
            if (ea.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                a = null;
            }
            else if (b is JsonElement eb0)
            {
                return JsonElement.DeepEquals(ea, eb0);
            }
            else
            {
                a = FromJsonElement(ea);
            }
        }

        if (b is JsonElement eb)
        {
            b = eb.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : FromJsonElement(eb);
        }

        if (a == null && b == null)
        {
            return true;
        }

        if (a == null || b == null)
        {
            return false;
        }

        if (a is RtRecordTcDto ra && b is RtRecordTcDto rb)
        {
            return RecordsEqual(ra, rb, recordGraph, resolveEnum, resolveRecord);
        }

        if (IsNumber(a) && IsNumber(b))
        {
            return NumbersEqual(a, b);
        }

        if (a is string sa && b is string sb)
        {
            return string.Equals(sa, sb, StringComparison.Ordinal);
        }

        if (a is not string && b is not string
            && a is System.Collections.IEnumerable seqA && b is System.Collections.IEnumerable seqB)
        {
            return SequencesEqual(seqA, seqB, recordGraph, resolveEnum, resolveRecord);
        }

        return a.Equals(b);
    }

    private static bool RecordsEqual(RtRecordTcDto a, RtRecordTcDto b, CkRecordGraph? recordGraph,
        Func<CkId<CkEnumId>, CkEnumGraph?>? resolveEnum,
        Func<RtCkId<CkRecordId>, CkRecordGraph?>? resolveRecord)
    {
        if (!string.Equals(a.CkRecordId?.ToString(), b.CkRecordId?.ToString(), StringComparison.Ordinal))
        {
            return false;
        }

        // Resolve the graph from the record INSTANCE, not from the attribute's declaration: a
        // record-valued attribute may store a DERIVED record, and a member declared only on the
        // derived record is absent from the base graph - it would fall through to the raw
        // comparison and report a phantom again (review of AB#5308). Both sides carry the same
        // CkRecordId at this point (checked above); the declared graph stays as the fallback.
        var graph = (resolveRecord != null && a.CkRecordId != null ? resolveRecord(a.CkRecordId) : null)
                    ?? recordGraph;

        // Attribute sets by id; an attribute present on one side only counts as a difference,
        // except when its value is null on the side that has it (absent == null in storage).
        var byIdA = a.Attributes.ToDictionary(x => x.Id.ToString(), x => x.Value, StringComparer.Ordinal);
        var byIdB = b.Attributes.ToDictionary(x => x.Id.ToString(), x => x.Value, StringComparer.Ordinal);
        foreach (var key in byIdA.Keys.Union(byIdB.Keys, StringComparer.Ordinal))
        {
            byIdA.TryGetValue(key, out var va);
            byIdB.TryGetValue(key, out var vb);

            // The member's own declaration decides how its value compares: an enum member reaches
            // the repository as its key while the seed writes the name or the key as text
            // (AB#5308). No graph, or a member the record does not declare: compare raw, which is
            // the pre-AB#5308 behaviour and over-reports at worst.
            // Match on the RUNTIME id: the DTO carries RtCkId (model name, no version) while the
            // graph holds CkId (versioned) and the two render differently, so comparing
            // CkAttributeId.ToString() against the DTO key never matched and every member fell
            // through to the raw comparison.
            var member = graph?.AllAttributes.Values.FirstOrDefault(m =>
                string.Equals(m.CkAttributeId.ToRtCkId().ToString(), key, StringComparison.Ordinal));

            if (member == null)
            {
                if (!ValuesEqual(va, vb, null, resolveEnum, resolveRecord))
                {
                    return false;
                }

                continue;
            }

            if (member.ValueType is AttributeValueTypesDto.Record or AttributeValueTypesDto.RecordArray)
            {
                if (ValuesEqual(va, vb, NestedRecordGraph(member, resolveRecord), resolveEnum, resolveRecord))
                {
                    continue;
                }

                // A nested RecordArray follows the same write rule as a top-level one: null lands
                // as an empty list, so the two are the same stored state (review of AB#5308).
                if (member.ValueType == AttributeValueTypesDto.RecordArray
                    && IsEmptyOrNullSequence(va) && IsEmptyOrNullSequence(vb))
                {
                    continue;
                }

                return false;
            }

            if (resolveEnum == null)
            {
                if (!ValuesEqual(va, vb, null, null, resolveRecord))
                {
                    return false;
                }

                continue;
            }

            if (!ValuesEqual(
                    Normalise(ConvertLikeTheImport(member, va), member, resolveEnum),
                    Normalise(ConvertLikeTheImport(member, vb), member, resolveEnum),
                    null, resolveEnum, resolveRecord))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SequencesEqual(System.Collections.IEnumerable a, System.Collections.IEnumerable b,
        CkRecordGraph? recordGraph,
        Func<CkId<CkEnumId>, CkEnumGraph?>? resolveEnum,
        Func<RtCkId<CkRecordId>, CkRecordGraph?>? resolveRecord)
    {
        var listA = a.Cast<object?>().ToList();
        var listB = b.Cast<object?>().ToList();
        if (listA.Count != listB.Count)
        {
            return false;
        }

        for (var i = 0; i < listA.Count; i++)
        {
            if (!ValuesEqual(listA[i], listB[i], recordGraph, resolveEnum, resolveRecord))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNumber(object o) =>
        o is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static bool IsFloating(object o) => o is float or double;

    /// <summary>
    /// Integral pairs compare exactly through decimal (int vs long from YAML vs Mongo);
    /// floating pairs compare as doubles, exactly - routing them through decimal rounded to 15
    /// significant digits and hid a real change between adjacent doubles. A mixed pair compares
    /// as doubles too: that is the shape the import would store for a Double attribute.
    /// </summary>
    private static bool NumbersEqual(object a, object b)
    {
        if (IsFloating(a) || IsFloating(b))
        {
            return Convert.ToDouble(a, CultureInfo.InvariantCulture)
                .Equals(Convert.ToDouble(b, CultureInfo.InvariantCulture));
        }

        try
        {
            return Convert.ToDecimal(a, CultureInfo.InvariantCulture) == Convert.ToDecimal(b, CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return Convert.ToDouble(a, CultureInfo.InvariantCulture)
                .Equals(Convert.ToDouble(b, CultureInfo.InvariantCulture));
        }
    }
}
