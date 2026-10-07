using System.Collections;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Secrets;

/// <summary>
///     Compares two in-memory attribute values the way the conditional rewrite
///     (<see cref="Repositories.IRuntimeRepository.RewriteAttributeValueIfUnchangedForMigrationAsync" />,
///     AB#5532) needs it: "is the stored value still the one that was read?". The comparison is about the
///     stored content, not about the CLR shape a read happens to produce, so a repository can compare the
///     value it reads back now with the value a caller read earlier.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>
///             Secret values compare by their stored form: a protected value by its envelope, a legacy
///             value (<see cref="RtSecretValueState.LegacyPlaintext" /> or a plain <see cref="string" />) by
///             its text. <c>LegacyPlaintext("x")</c> equals <c>"x"</c>; a protected value never equals a
///             string, even when the string holds the same envelope (sub-document vs. string slot).
///         </item>
///         <item>Records compare by record id and member values (a missing member equals <c>null</c>).</item>
///         <item>Arrays compare element by element, whatever the collection type.</item>
///         <item>Numbers compare by value across CLR types (a BSON int32 read as int vs. an int64).</item>
///     </list>
///     Never logs or exposes a value.
/// </remarks>
public static class StoredAttributeValueComparer
{
    /// <summary>
    ///     True when <paramref name="stored" /> and <paramref name="expected" /> describe the same stored
    ///     attribute value.
    /// </summary>
    /// <param name="stored">The value as currently stored (read back by the repository)</param>
    /// <param name="expected">The value the caller read earlier</param>
    public static bool AreEqual(object? stored, object? expected)
    {
        if (ReferenceEquals(stored, expected))
        {
            return true;
        }

        if (stored == null || expected == null)
        {
            return false;
        }

        if (stored is RtSecretValue || expected is RtSecretValue)
        {
            return TryGetSecretForm(stored, out var storedProtected, out var storedRaw)
                   && TryGetSecretForm(expected, out var expectedProtected, out var expectedRaw)
                   && storedProtected == expectedProtected
                   && string.Equals(storedRaw, expectedRaw, StringComparison.Ordinal);
        }

        if (stored is string storedText)
        {
            return expected is string expectedText && string.Equals(storedText, expectedText, StringComparison.Ordinal);
        }

        if (stored is RtRecord storedRecord)
        {
            return expected is RtRecord expectedRecord && RecordsEqual(storedRecord, expectedRecord);
        }

        if (stored is byte[] storedBytes)
        {
            return expected is byte[] expectedBytes && storedBytes.AsSpan().SequenceEqual(expectedBytes);
        }

        if (IsSequence(stored))
        {
            return IsSequence(expected) && SequencesEqual((IEnumerable)stored, (IEnumerable)expected);
        }

        if (IsNumber(stored) && IsNumber(expected))
        {
            return NumbersEqual(stored, expected);
        }

        return stored.Equals(expected);
    }

    private static bool TryGetSecretForm(object value, out bool isProtected, out string? raw)
    {
        switch (value)
        {
            case RtSecretValue secret:
                isProtected = secret.IsProtected;
                raw = secret.RawValue;
                return true;
            case string text:
                isProtected = false;
                raw = text;
                return true;
            default:
                isProtected = false;
                raw = null;
                return false;
        }
    }

    private static bool RecordsEqual(RtRecord stored, RtRecord expected)
    {
        if (!string.Equals(stored.CkRecordId?.FullName, expected.CkRecordId?.FullName, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var key in stored.Attributes.Keys.Union(expected.Attributes.Keys, StringComparer.Ordinal))
        {
            stored.Attributes.TryGetValue(key, out var storedValue);
            expected.Attributes.TryGetValue(key, out var expectedValue);
            if (!AreEqual(storedValue, expectedValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSequence(object value)
    {
        return value is IEnumerable and not string and not byte[] and not IDictionary;
    }

    private static bool SequencesEqual(IEnumerable stored, IEnumerable expected)
    {
        var storedEnumerator = stored.GetEnumerator();
        var expectedEnumerator = expected.GetEnumerator();
        try
        {
            while (true)
            {
                var storedHasNext = storedEnumerator.MoveNext();
                var expectedHasNext = expectedEnumerator.MoveNext();
                if (storedHasNext != expectedHasNext)
                {
                    return false;
                }

                if (!storedHasNext)
                {
                    return true;
                }

                if (!AreEqual(storedEnumerator.Current, expectedEnumerator.Current))
                {
                    return false;
                }
            }
        }
        finally
        {
            (storedEnumerator as IDisposable)?.Dispose();
            (expectedEnumerator as IDisposable)?.Dispose();
        }
    }

    private static bool IsNumber(object value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
    }

    private static bool NumbersEqual(object stored, object expected)
    {
        if (stored is float or double || expected is float or double)
        {
            return Convert.ToDouble(stored, System.Globalization.CultureInfo.InvariantCulture)
                .Equals(Convert.ToDouble(expected, System.Globalization.CultureInfo.InvariantCulture));
        }

        return Convert.ToDecimal(stored, System.Globalization.CultureInfo.InvariantCulture) ==
               Convert.ToDecimal(expected, System.Globalization.CultureInfo.InvariantCulture);
    }
}
