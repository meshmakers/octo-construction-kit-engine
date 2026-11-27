using System.Collections;
using System.Globalization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     The release configuration of a <c>System/AutoIncrement</c> number range (attribute <c>Release</c>, record
///     <c>System/NumberRangeRelease</c>, System 2.9.0): which attribute of the document type holds its state and which
///     state values mean released. A document whose state value is one of <see cref="ReleasedValues" /> is released
///     (design note <c>docs/concept-number-ranges-and-released-documents.md</c>, D8): it gets its number when it reaches
///     that state, and it is locked. One rule for the numbering and the lock.
/// </summary>
public sealed class NumberRangeRelease
{
    /// <summary>Name of the <c>Release</c> attribute of <c>System/AutoIncrement</c>.</summary>
    public const string ReleaseAttribute = "Release";

    /// <summary>Name of the <c>StateAttribute</c> attribute of <c>System/NumberRangeRelease</c>.</summary>
    public const string StateAttributeAttribute = "StateAttribute";

    /// <summary>Name of the <c>ReleasedValues</c> attribute of <c>System/NumberRangeRelease</c>.</summary>
    public const string ReleasedValuesAttribute = "ReleasedValues";

    /// <summary>Name of the <c>ChangeableAttributes</c> attribute of <c>System/NumberRangeRelease</c>.</summary>
    public const string ChangeableAttributesAttribute = "ChangeableAttributes";

    /// <summary>Name of the <c>StornoCkTypeId</c> attribute of <c>System/NumberRangeRelease</c>.</summary>
    public const string StornoCkTypeIdAttribute = "StornoCkTypeId";

    private readonly HashSet<string> _releasedValues;

    private NumberRangeRelease(string stateAttribute, HashSet<string> releasedValues,
        IReadOnlyCollection<string> changeableAttributes, string? stornoCkTypeId)
    {
        StornoCkTypeId = stornoCkTypeId;
        StateAttribute = stateAttribute;
        _releasedValues = releasedValues;
        ChangeableAttributes = changeableAttributes;
    }

    /// <summary>Name of the attribute of the document type that holds the document state.</summary>
    public string StateAttribute { get; }

    /// <summary>The state values that mean released (enum value names or string values, case-sensitive).</summary>
    public IReadOnlyCollection<string> ReleasedValues => _releasedValues;

    /// <summary>Attributes that may still change on a released document; empty when none.</summary>
    public IReadOnlyCollection<string> ChangeableAttributes { get; }

    /// <summary>CK type id of the storno document of this document type; null when no storno is configured.</summary>
    public string? StornoCkTypeId { get; }

    /// <summary>
    ///     Reads the release configuration of the counter entity <paramref name="counter" />. Returns true with
    ///     <paramref name="release" /> null when the range has none (numbering on insert, no lock, as before System
    ///     2.9.0), true with the configuration when it is valid, and false with <paramref name="error" /> when it is
    ///     present but not usable: no <c>StateAttribute</c>, or no <c>ReleasedValues</c> (a range whose documents could
    ///     never be released would never number them).
    /// </summary>
    public static bool TryCreate(RtEntity counter, out NumberRangeRelease? release, out string? error)
    {
        release = null;
        error = null;

        var raw = counter.GetAttributeValueOrDefault(ReleaseAttribute);
        if (raw == null)
        {
            return true;
        }

        if (raw is not RtRecord record)
        {
            error = $"{ReleaseAttribute} is not a {nameof(RtRecord)} but '{raw.GetType().Name}'.";
            return false;
        }

        if (record.GetAttributeValueOrDefault(StateAttributeAttribute) is not string stateAttribute ||
            string.IsNullOrWhiteSpace(stateAttribute))
        {
            error = $"{ReleaseAttribute}.{StateAttributeAttribute} is required: the name of the attribute that holds " +
                    "the document state.";
            return false;
        }

        if (!TryGetStrings(record, ReleasedValuesAttribute, out var releasedValues) ||
            releasedValues.Count == 0 || releasedValues.Any(string.IsNullOrWhiteSpace))
        {
            error = $"{ReleaseAttribute}.{ReleasedValuesAttribute} needs at least one state value, and no empty one.";
            return false;
        }

        if (!TryGetStrings(record, ChangeableAttributesAttribute, out var changeableAttributes))
        {
            error = $"{ReleaseAttribute}.{ChangeableAttributesAttribute} is not a list of attribute names.";
            return false;
        }

        release = new NumberRangeRelease(stateAttribute, new HashSet<string>(releasedValues, StringComparer.Ordinal),
            changeableAttributes,
            record.GetAttributeValueOrDefault(StornoCkTypeIdAttribute) is string storno && !string.IsNullOrWhiteSpace(storno)
                ? storno
                : null);
        return true;
    }

    /// <summary>
    ///     True when <paramref name="stateValue" />, a value of the state attribute <paramref name="stateAttribute" />,
    ///     is one of <see cref="ReleasedValues" />. An enum value is compared by the name of its CK enum value
    ///     (<paramref name="stateEnum" />), a string value as it is; both case-sensitively. A missing state (null) is
    ///     never released.
    /// </summary>
    /// <param name="stateValue">The state value of the document (the value the write leaves it in).</param>
    /// <param name="stateAttribute">The state attribute of the document type.</param>
    /// <param name="stateEnum">The CK enum of an enum state attribute; null for a string attribute.</param>
    public bool IsReleased(object? stateValue, CkTypeAttributeGraph stateAttribute, CkEnumGraph? stateEnum)
    {
        var name = StateName(stateValue, stateAttribute, stateEnum);
        return name != null && _releasedValues.Contains(name);
    }

    private static string? StateName(object? stateValue, CkTypeAttributeGraph stateAttribute, CkEnumGraph? stateEnum)
    {
        switch (stateValue)
        {
            case null:
                return null;
            case string text:
                // A string attribute, or an enum value given by its name.
                return text;
        }

        if (stateAttribute.ValueType != AttributeValueTypesDto.Enum || stateEnum == null)
        {
            return Convert.ToString(stateValue, CultureInfo.InvariantCulture);
        }

        int key;
        try
        {
            key = Convert.ToInt32(stateValue, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }

        return stateEnum.Values.FirstOrDefault(v => v.Key == key)?.Name;
    }

    private static bool TryGetStrings(RtRecord record, string attributeName, out IReadOnlyCollection<string> values)
    {
        values = [];
        var raw = record.GetAttributeValueOrDefault(attributeName);
        switch (raw)
        {
            case null:
                return true;
            case string:
                return false;
            case IEnumerable entries:
                var list = new List<string>();
                foreach (var entry in entries)
                {
                    if (entry is not string text)
                    {
                        return false;
                    }

                    list.Add(text);
                }

                values = list;
                return true;
            default:
                return false;
        }
    }
}
