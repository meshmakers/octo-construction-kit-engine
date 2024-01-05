using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents a versioned construction kit type id
/// </summary>
[DebuggerDisplay("{" + nameof(EnumId) + "} ({" + nameof(Version) + "})")]
[JsonConverter(typeof(CkEnumIdConverter))]
public readonly struct CkEnumId : IComparable<CkEnumId>, IEquatable<CkEnumId>, ICkKey
{
    /// <summary>
    ///     Creates a new <see cref="CkEnumId" /> from the given <paramref name="enumId" />.
    /// </summary>
    /// <param name="enumId"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public CkEnumId(string enumId)
    {
        var typeIndex = enumId.IndexOf("-", StringComparison.Ordinal);
        if (typeIndex < 0)
        {
            EnumId = enumId;
            Version = "1.0.0";
        }
        else
        {
            EnumId = enumId.Substring(0, typeIndex);
            Version = enumId.Substring(typeIndex + 1);
        }

        if (string.IsNullOrWhiteSpace(EnumId))
        {
            throw new ArgumentOutOfRangeException(nameof(enumId), enumId, $"{nameof(enumId)} must contain a enum id");
        }
    }

    /// <summary>
    ///     Creates a new <see cref="CkEnumId" /> from the given <paramref name="enumId" /> and <paramref name="version" />.
    /// </summary>
    /// <param name="enumId"></param>
    /// <param name="version"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public CkEnumId(string enumId, string version = "1.0.0")
    {
        EnumId = enumId;
        Version = version;
        if (string.IsNullOrWhiteSpace(EnumId))
        {
            throw new ArgumentOutOfRangeException(nameof(enumId), enumId, $"{nameof(enumId)} must contain a enum id");
        }
    }

    /// <summary>
    ///     Creates a new <see cref="CkEnumId" /> from the given <paramref name="value" />.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static implicit operator CkEnumId(string value)
    {
        return new CkEnumId(value);
    }

    /// <summary>
    ///     Defines the name of the type, e. g. "Person"
    /// </summary>
    public string EnumId { get; }

    /// <summary>
    ///     Returns the version of the type, e. g. "1.0.0"
    /// </summary>
    public CkVersion Version { get; }

    /// <inheritdoc />
    public string FullName => IsEmpty ? "" : $"{EnumId}-{Version}";

    /// <inheritdoc />
    public string SemanticVersionedFullName
    {
        get
        {
            if (IsEmpty)
            {
                return "";
            }

            var s = EnumId;
            if (Version.Major > 1)
            {
                s += $"-{Version.Major}";
            }

            return s;
        }
    }

    /// <inheritdoc />
    public bool IsEmpty => string.IsNullOrWhiteSpace(EnumId);


    /// <inheritdoc />
    public int CompareTo(CkEnumId other)
    {
        var result = string.Compare(EnumId, other.EnumId, StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        return Version.CompareTo(other.Version);
    }

    /// <inheritdoc />
    public bool Equals(CkEnumId other)
    {
        return EnumId == other.EnumId && Equals(Version, other.Version);
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
                if (conversionType == typeof(object) || conversionType == typeof(CkEnumId))
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

        var other = (CkEnumId)obj;

        return EnumId == other.EnumId && Version == other.Version;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 23 + EnumId.GetHashCode();
            hash = hash * 23 + Version.GetHashCode();
            return hash;
        }
    }

    /// <summary>
    ///     Compares two <see cref="CkEnumId" /> instances for equality.
    /// </summary>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <returns></returns>
    public static bool operator ==(CkEnumId p1, CkEnumId p2)
    {
        return p1.Equals(p2);
    }

    /// <summary>
    ///     Compares two <see cref="CkEnumId" />s for inequality.
    /// </summary>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <returns></returns>
    public static bool operator !=(CkEnumId p1, CkEnumId p2)
    {
        return !p1.Equals(p2);
    }
}