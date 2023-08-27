using System.Diagnostics;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Represents a versioned construction kit attribute id
/// </summary>
[DebuggerDisplay("{" + nameof(AttributeId) + "} ({" + nameof(Version) + "})")]
[System.Text.Json.Serialization.JsonConverter(typeof(CkAttributeIdConverter))]
public readonly struct CkAttributeId : IComparable<CkAttributeId>, IEquatable<CkAttributeId>, ICkKey
{
    /// <summary>
    /// Creates a new <see cref="CkAttributeId"/> from the given <paramref name="attributeId"/>.
    /// </summary>
    /// <param name="attributeId"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public CkAttributeId(string attributeId)
    {
        var typeIndex = attributeId.IndexOf("-", StringComparison.Ordinal);
        if (typeIndex < 0)
        {
            AttributeId = attributeId;
            Version = "1.0.0";
        }
        else
        {
            AttributeId = attributeId.Substring(0, typeIndex);
            Version = attributeId.Substring(typeIndex + 1);
        }

        if (string.IsNullOrWhiteSpace(AttributeId))
        {
            throw new ArgumentOutOfRangeException(nameof(attributeId), attributeId, $"{nameof(attributeId)} must contain a type id");
        }
    }

    /// <summary>
    /// Creates a new <see cref="CkAttributeId"/> from the given <paramref name="attributeId"/> and <paramref name="attributeVersion"/>.
    /// </summary>
    /// <param name="attributeId"></param>
    /// <param name="attributeVersion"></param>
    public CkAttributeId(string attributeId, string attributeVersion = "1.0.0")
    {
        AttributeId = attributeId;
        Version = attributeVersion;
    }

    /// <summary>
    /// Creates a new <see cref="CkAttributeId"/> from the given <paramref name="value"/>.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static implicit operator CkAttributeId(string value)
    {
        return new CkAttributeId(value);
    }

    /// <summary>
    /// Defines the name of the attribute, e. g. "Designation"
    /// </summary>
    public string AttributeId { get; }

    /// <summary>
    /// Returns the version of the attribute, e. g. "1.0.0"
    /// </summary>
    public CkVersion Version { get; }

    /// <summary>
    /// Returns the full name of the attribute, e. g. "Designation-1.0.0"
    /// </summary>
    public string FullName => IsEmpty ? "" : $"{AttributeId}-{Version}";

    /// <inheritdoc />
    public string SemanticVersionedFullName
    {
        get
        {
            if (IsEmpty)
            {
                return "";
            }

            var s = AttributeId;
            if (Version.Major > 1)
            {
                s += $"-{Version.Major}";
            }

            return s;
        }
    }

    /// <inheritdoc />
    public bool IsEmpty => string.IsNullOrWhiteSpace(AttributeId);

    /// <inheritdoc />
    public int CompareTo(CkAttributeId other)
    {
        var result = String.Compare(AttributeId, other.AttributeId, StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        return Version.CompareTo(other.Version);
    }

    /// <inheritdoc />
    public TypeCode GetTypeCode()
    {
        return TypeCode.Object;
    }

    /// <inheritdoc />
    public bool ToBoolean(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public byte ToByte(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public char ToChar(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public DateTime ToDateTime(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public decimal ToDecimal(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public double ToDouble(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public short ToInt16(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public int ToInt32(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public long ToInt64(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public sbyte ToSByte(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public float ToSingle(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public string ToString(IFormatProvider? provider)
    {
        return FullName;
    }

    /// <inheritdoc />
    public object ToType(Type conversionType, IFormatProvider? provider)
    {
        switch (Type.GetTypeCode(conversionType))
        {
            case TypeCode.String:
                return ToString(provider);
            case TypeCode.Object:
                if (conversionType == typeof(object) || conversionType == typeof(CkAttributeId))
                {
                    return this;
                }

                break;
        }

        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public ushort ToUInt16(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public uint ToUInt32(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public ulong ToUInt64(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <summary>
    ///     Returns a string representation of the value.
    /// </summary>
    /// <returns>A string representation of the value.</returns>
    public override string ToString()
    {
        return FullName;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj == null)
        {
            return false;
        }

        var other = (CkAttributeId)obj;

        return AttributeId == other.AttributeId && Version == other.Version;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
#if NETSTANDARD2_0
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + AttributeId.GetHashCode();
            hash = hash * 23 + Version.GetHashCode();
            return hash;
        }
#else
            return HashCode.Combine(AttributeId, Version);
#endif
    }

    /// <inheritdoc />
    public bool Equals(CkAttributeId other)
    {
        return AttributeId == other.AttributeId && Version.Equals(other.Version);
    }

    /// <summary>
    /// Compares two <see cref="CkAttributeId"/> instances for equality.
    /// </summary>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <returns></returns>
    public static bool operator ==(CkAttributeId p1, CkAttributeId p2)
    {
        return p1.Equals(p2);
    }

    /// <summary>
    /// Compares two <see cref="CkAttributeId"/> instances for inequality.
    /// </summary>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <returns></returns>
    public static bool operator !=(CkAttributeId p1, CkAttributeId p2)
    {
        return !p1.Equals(p2);
    }
}