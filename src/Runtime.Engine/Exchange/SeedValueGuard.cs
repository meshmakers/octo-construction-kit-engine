using System.Collections;
using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Exchange;

/// <summary>
/// What an Upsert import does with one attribute value (AB#6313).
/// </summary>
public enum SeedValueDecisionKind
{
    /// <summary>The seed value is written (blueprint-owned change, new value, or an allowed blanking).</summary>
    TakeSeed = 0,

    /// <summary>The tenant's existing value is carried over; the seed value does not land.</summary>
    KeepExisting = 1,
}

/// <summary>
/// The decision for one attribute: what to write and whether the seed blanks an existing value.
/// <see cref="Blanking"/> is independent of <see cref="Kind"/>: under
/// <see cref="RtImportBlankingPolicy.Keep"/> it is reported together with <c>KeepExisting</c>, under
/// <see cref="RtImportBlankingPolicy.Allow"/> together with <c>TakeSeed</c>. The preview of AB#6315
/// lists every verdict whose <see cref="Blanking"/> is not <c>None</c>.
/// </summary>
/// <param name="Kind">Keep the existing value or take the seed value.</param>
/// <param name="Blanking">Why the seed counts as blanking, or <c>None</c>.</param>
public readonly record struct SeedValueVerdict(SeedValueDecisionKind Kind, RtImportBlankingReason Blanking)
{
    /// <summary>True when the seed would blank a non-empty existing value.</summary>
    public bool IsBlanking => Blanking != RtImportBlankingReason.None;
}

/// <summary>
/// The single, pure rule that decides what happens to an attribute when a seed entity is imported
/// over an existing entity (AB#6313). No repository, cache or logging dependency, so the import
/// pass, the blueprint update preview and tests share exactly one decision.
/// </summary>
/// <remarks>
/// <para>Rule table (first matching row wins):</para>
/// <list type="table">
/// <listheader><term>Situation</term><description>Verdict</description></listheader>
/// <item><term>Existing entity has no value for the attribute</term><description>TakeSeed (new attribute, initial value)</description></item>
/// <item><term>Ownership is TenantOwned, RuntimeState or Secret</term><description>KeepExisting, not blanking (unchanged behaviour of the preserve pass)</description></item>
/// <item><term>SeedOwned, existing value is empty</term><description>TakeSeed (nothing to lose)</description></item>
/// <item><term>SeedOwned, existing non-empty, seed non-empty</term><description>TakeSeed (blueprint-owned change wins, also when the value differs)</description></item>
/// <item><term>SeedOwned, existing and seed are JSON texts and the seed carries an empty string where the existing has a non-empty one</term><description>Blanking (seed empty skeleton, see below)</description></item>
/// <item><term>SeedOwned, existing non-empty, seed empty or omitted</term><description>Blanking: KeepExisting under Keep, TakeSeed under Allow</description></item>
/// </list>
/// <para>
/// "Empty" (<see cref="IsEmpty"/>) is deliberately narrow: <c>null</c>, the empty string, an array
/// without elements, and a record whose members are all empty. Numbers, booleans, enums, dates,
/// time spans and geospatial points are never empty: an explicit <c>0</c>, <c>false</c> or the
/// enum's first member is a value a seed author chose, not blanking. They are only guarded when
/// the seed omits the attribute altogether. Blanking is judged per top-level attribute; a record is
/// replaced or kept as one unit.
/// </para>
/// <para>
/// JSON text attributes (AB#6310): a string attribute can carry a serialized configuration, and a
/// seed then delivers an "empty skeleton" - valid JSON whose string leaves are empty (the EDA adapter
/// configuration: host, user and password <c>""</c>). Such a seed is not an empty string yet it blanks
/// the tenant's credentials. When both the existing and the seed value parse as JSON, the seed counts
/// as blanking when it has an empty (or null) string at a path where the existing value has a non-empty
/// string. Properties the seed merely does not mention are NOT blanking here (a new seed version may
/// legitimately drop a property); numbers and booleans are never compared. The attribute is then
/// kept or replaced as a whole, never merged.
/// </para>
/// </remarks>
public static class SeedValueGuard
{
    /// <summary>
    /// Decides what to do with one attribute.
    /// </summary>
    /// <param name="valueType">The attribute's value type (kept for documentation of intent and future rules).</param>
    /// <param name="ownership">The effective ownership of the attribute on this type.</param>
    /// <param name="existingPresent">True when the existing entity carries a value slot for the attribute.</param>
    /// <param name="existingValue">The existing value (repository shape).</param>
    /// <param name="seedPresent">True when the seed declares the attribute.</param>
    /// <param name="seedValue">The seed value (transport shape).</param>
    /// <param name="policy">What to do when the seed blanks a non-empty value.</param>
    public static SeedValueVerdict Decide(
        AttributeValueTypesDto valueType,
        AttributeOwnershipDto ownership,
        bool existingPresent,
        object? existingValue,
        bool seedPresent,
        object? seedValue,
        RtImportBlankingPolicy policy)
    {
        _ = valueType;

        if (!existingPresent)
        {
            return new SeedValueVerdict(SeedValueDecisionKind.TakeSeed, RtImportBlankingReason.None);
        }

        if (ownership.IsPreservedOnUpsert())
        {
            return new SeedValueVerdict(SeedValueDecisionKind.KeepExisting, RtImportBlankingReason.None);
        }

        if (IsEmpty(existingValue))
        {
            return new SeedValueVerdict(SeedValueDecisionKind.TakeSeed, RtImportBlankingReason.None);
        }

        RtImportBlankingReason reason;
        if (!seedPresent)
        {
            reason = RtImportBlankingReason.SeedOmitted;
        }
        else if (IsEmpty(seedValue))
        {
            reason = RtImportBlankingReason.SeedEmpty;
        }
        else if (BlanksJsonLeaf(existingValue, seedValue))
        {
            reason = RtImportBlankingReason.SeedEmpty;
        }
        else
        {
            return new SeedValueVerdict(SeedValueDecisionKind.TakeSeed, RtImportBlankingReason.None);
        }

        return new SeedValueVerdict(
            policy == RtImportBlankingPolicy.Allow ? SeedValueDecisionKind.TakeSeed : SeedValueDecisionKind.KeepExisting,
            reason);
    }

    /// <summary>
    /// True when a value counts as empty: <c>null</c>, the empty string, an enumerable without
    /// elements, or a record (repository or transport shape) whose members are all empty or that has
    /// none. Numbers, booleans, enums, dates, time spans and every other scalar are never empty.
    /// </summary>
    public static bool IsEmpty(object? value)
    {
        switch (value)
        {
            case null:
                return true;
            case string s:
                return s.Length == 0;
            case RtRecordTcDto recordTc:
                return recordTc.Attributes.All(a => IsEmpty(a.Value));
            case RtRecord record:
                return record.Attributes.Values.All(IsEmpty);
            case byte[]:
                // Binary payloads are scalar values; only a null reference is empty.
                return false;
            case IEnumerable enumerable:
                return !enumerable.Cast<object?>().Any();
            default:
                return false;
        }
    }

    /// <summary>
    /// True when both values are JSON texts and the seed has an empty or null string at a path where
    /// the existing value has a non-empty string. Non-JSON or non-string values never match.
    /// </summary>
    internal static bool BlanksJsonLeaf(object? existingValue, object? seedValue)
    {
        if (existingValue is not string existingText || seedValue is not string seedText ||
            !LooksLikeJsonContainer(existingText) || !LooksLikeJsonContainer(seedText))
        {
            return false;
        }

        try
        {
            using var existingDoc = JsonDocument.Parse(existingText);
            using var seedDoc = JsonDocument.Parse(seedText);
            return BlanksJsonLeaf(existingDoc.RootElement, seedDoc.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LooksLikeJsonContainer(string text)
    {
        var trimmed = text.AsSpan().Trim();
        return trimmed.Length > 1 && (trimmed[0] == '{' || trimmed[0] == '[');
    }

    private static bool BlanksJsonLeaf(JsonElement existing, JsonElement seed)
    {
        switch (seed.ValueKind)
        {
            case JsonValueKind.Object when existing.ValueKind == JsonValueKind.Object:
                foreach (var seedProperty in seed.EnumerateObject())
                {
                    if (existing.TryGetProperty(seedProperty.Name, out var existingProperty) &&
                        BlanksJsonLeaf(existingProperty, seedProperty.Value))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array when existing.ValueKind == JsonValueKind.Array:
                var existingItems = existing.EnumerateArray().ToList();
                var index = 0;
                foreach (var seedItem in seed.EnumerateArray())
                {
                    if (index < existingItems.Count && BlanksJsonLeaf(existingItems[index], seedItem))
                    {
                        return true;
                    }

                    index++;
                }

                return false;
            case JsonValueKind.String when existing.ValueKind == JsonValueKind.String:
                return seed.GetString()!.Length == 0 && existing.GetString()!.Length > 0;
            case JsonValueKind.Null when existing.ValueKind == JsonValueKind.String:
                return existing.GetString()!.Length > 0;
            default:
                return false;
        }
    }
}
