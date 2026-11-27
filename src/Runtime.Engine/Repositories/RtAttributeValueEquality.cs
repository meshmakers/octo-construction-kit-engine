using System.Collections;
using System.Globalization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     Decides whether a written attribute value equals the stored one, for the released-document lock: a write that
///     leaves every locked attribute at its stored value changes nothing. The comparison is strict - only values that
///     are certainly the same count as equal, so an uncertain case is a change and is refused, never let through:
///     strings ordinal (no trimming, no case folding), integral numbers by value (<c>int</c> 5 equals <c>long</c> 5),
///     other numbers by their <c>double</c> value, times by their UTC ticks, records by record type and members, lists
///     element by element in order, an enum value given by name against its stored key through the CK enum. A linked
///     binary that is written is always a change (its bytes are uploaded again). <c>null</c> and a missing attribute are
///     the same.
/// </summary>
internal static class RtAttributeValueEquality
{
    /// <summary>
    ///     True when <paramref name="written" /> equals <paramref name="stored" /> for the attribute
    ///     <paramref name="attribute" /> (null when the type does not know it).
    /// </summary>
    public static bool AreEqual(object? written, object? stored, CkTypeAttributeGraph? attribute,
        CkEnumGraph? attributeEnum)
    {
        if (attribute?.ValueType == AttributeValueTypesDto.Enum && attributeEnum != null)
        {
            written = EnumKeyOrSelf(written, attributeEnum);
            stored = EnumKeyOrSelf(stored, attributeEnum);
        }

        return AreEqual(written, stored);
    }

    /// <summary>True when the two values are certainly the same (see the class remarks).</summary>
    public static bool AreEqual(object? a, object? b)
    {
        if (a == null || b == null)
        {
            return a == null && b == null;
        }

        if (a is EntityBinaryInfo || b is EntityBinaryInfo)
        {
            return false;
        }

        if (a is string textA)
        {
            return b is string textB && string.Equals(textA, textB, StringComparison.Ordinal);
        }

        if (b is string)
        {
            return false;
        }

        if (IsIntegral(a) && IsIntegral(b))
        {
            return IntegralEquals(a, b);
        }

        if (IsNumber(a) && IsNumber(b))
        {
            return Convert.ToDouble(a, CultureInfo.InvariantCulture)
                .Equals(Convert.ToDouble(b, CultureInfo.InvariantCulture));
        }

        if (a is bool boolA)
        {
            return b is bool boolB && boolA == boolB;
        }

        if (TryGetUtcTicks(a, out var ticksA))
        {
            return TryGetUtcTicks(b, out var ticksB) && ticksA == ticksB;
        }

        if (a is RtTypeWithAttributes withAttributesA)
        {
            return b is RtTypeWithAttributes withAttributesB && RecordsEqual(withAttributesA, withAttributesB);
        }

        if (a is byte[] bytesA)
        {
            return b is byte[] bytesB && bytesA.AsSpan().SequenceEqual(bytesB);
        }

        if (a is IDictionary dictionaryA)
        {
            return b is IDictionary dictionaryB && DictionariesEqual(dictionaryA, dictionaryB);
        }

        if (a is IEnumerable listA)
        {
            return b is IEnumerable listB and not IDictionary && SequencesEqual(listA, listB);
        }

        return a.Equals(b);
    }

    private static object? EnumKeyOrSelf(object? value, CkEnumGraph ckEnum)
    {
        if (value is not string name)
        {
            return value;
        }

        var match = ckEnum.Values.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal));
        return match != null ? match.Key : value;
    }

    private static bool RecordsEqual(RtTypeWithAttributes a, RtTypeWithAttributes b)
    {
        if (a is RtRecord recordA && b is RtRecord recordB &&
            !string.Equals(recordA.CkRecordId?.FullName, recordB.CkRecordId?.FullName, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var name in a.Attributes.Keys.Union(b.Attributes.Keys, StringComparer.Ordinal))
        {
            if (!AreEqual(a.GetAttributeValueOrDefault(name), b.GetAttributeValueOrDefault(name)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DictionariesEqual(IDictionary a, IDictionary b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (DictionaryEntry entry in a)
        {
            if (!b.Contains(entry.Key) || !AreEqual(entry.Value, b[entry.Key]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SequencesEqual(IEnumerable a, IEnumerable b)
    {
        var enumeratorB = b.GetEnumerator();
        try
        {
            foreach (var itemA in a)
            {
                if (!enumeratorB.MoveNext() || !AreEqual(itemA, enumeratorB.Current))
                {
                    return false;
                }
            }

            return !enumeratorB.MoveNext();
        }
        finally
        {
            (enumeratorB as IDisposable)?.Dispose();
        }
    }

    private static bool IsIntegral(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or Enum;

    private static bool IsNumber(object value) => IsIntegral(value) || value is float or double or decimal;

    private static bool IntegralEquals(object a, object b)
    {
        // ulong above long.MaxValue is compared as ulong; every other integral fits into a long.
        if (a is ulong ulongA && ulongA > long.MaxValue || b is ulong ulongB && ulongB > long.MaxValue)
        {
            return a is ulong first && b is ulong second && first == second;
        }

        return ToLong(a) == ToLong(b);
    }

    private static long ToLong(object value) => Convert.ToInt64(value, CultureInfo.InvariantCulture);

    private static bool TryGetUtcTicks(object value, out long ticks)
    {
        switch (value)
        {
            case DateTime dateTime:
                ticks = (dateTime.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                    : dateTime.ToUniversalTime()).Ticks;
                return true;
            case DateTimeOffset dateTimeOffset:
                ticks = dateTimeOffset.UtcTicks;
                return true;
            default:
                ticks = 0;
                return false;
        }
    }
}
