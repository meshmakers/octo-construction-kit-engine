using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Extensions for <see cref="RtCkId{TKey}"/> covering storage-identifier conversions.
/// </summary>
public static class RtCkIdExtensions
{
    private static readonly Regex NonAlphanumeric = new("[^A-Za-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    /// Returns the <see cref="RtCkId{TKey}.SemanticVersionedFullName"/> with all
    /// non-alphanumeric characters stripped. Suitable as a database identifier
    /// (collection name, table name, schema name).
    /// </summary>
    public static string ToStorageIdentifier<TKey>(this RtCkId<TKey> ckKey)
        where TKey : IComparable<TKey>, ICkElementId
    {
        return NonAlphanumeric.Replace(ckKey.SemanticVersionedFullName, string.Empty);
    }

    /// <summary>
    /// Same as <see cref="ToStorageIdentifier{TKey}(RtCkId{TKey})"/> but caps the result at
    /// <paramref name="maxLength"/> characters. When the cleaned name exceeds the limit it is
    /// truncated and a deterministic 16-character SHA-256 prefix of the original cleaned name is
    /// appended (form: <c>{truncated}_{hash16}</c>) so different sources do not collide after
    /// truncation. CrateDB identifiers are limited to 255 bytes; pass 63 for schema names and 200
    /// for table names per the StreamData archive concept §4.
    /// </summary>
    public static string ToStorageIdentifier<TKey>(this RtCkId<TKey> ckKey, int maxLength)
        where TKey : IComparable<TKey>, ICkElementId
    {
        if (maxLength <= 17)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength),
                "maxLength must be greater than 17 to leave room for the hash suffix.");
        }

        var cleaned = ckKey.ToStorageIdentifier();
        if (cleaned.Length <= maxLength)
        {
            return cleaned;
        }

        var hash = ShortHash(cleaned);
        var keep = maxLength - 1 - hash.Length;
        return $"{cleaned.Substring(0, keep)}_{hash}";
    }

    private static string ShortHash(string value)
    {
#if NETSTANDARD2_0
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
#else
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
#endif
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++)
        {
            sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
