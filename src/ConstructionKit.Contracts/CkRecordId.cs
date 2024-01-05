using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents a versioned construction kit type id
/// </summary>
[DebuggerDisplay("{" + nameof(RecordId) + "} ({" + nameof(Version) + "})")]
[JsonConverter(typeof(CkRecordIdConverter))]
public readonly struct CkRecordId : IComparable<CkRecordId>, IEquatable<CkRecordId>, ICkKey
{
    /// <summary>
    ///     Creates a new <see cref="CkRecordId" /> from the given <paramref name="recordId" />.
    /// </summary>
    /// <param name="recordId"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public CkRecordId(string recordId)
    {
        var typeIndex = recordId.IndexOf("-", StringComparison.Ordinal);
        if (typeIndex < 0)
        {
            RecordId = recordId;
            Version = "1.0.0";
        }
        else
        {
            RecordId = recordId.Substring(0, typeIndex);
            Version = recordId.Substring(typeIndex + 1);
        }

        if (string.IsNullOrWhiteSpace(RecordId))
        {
            throw new ArgumentOutOfRangeException(nameof(recordId), recordId, $"{nameof(recordId)} must contain a record id");
        }
    }

    /// <summary>
    ///     Creates a new <see cref="CkRecordId" /> from the given <paramref name="recordId" /> and <paramref name="version" />.
    /// </summary>
    /// <param name="recordId"></param>
    /// <param name="version"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public CkRecordId(string recordId, string version = "1.0.0")
    {
        RecordId = recordId;
        Version = version;
        if (string.IsNullOrWhiteSpace(RecordId))
        {
            throw new ArgumentOutOfRangeException(nameof(recordId), recordId, $"{nameof(recordId)} must contain a record id");
        }
    }

    /// <summary>
    ///     Creates a new <see cref="CkRecordId" /> from the given <paramref name="value" />.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static implicit operator CkRecordId(string value)
    {
        return new CkRecordId(value);
    }

    /// <summary>
    ///     Defines the name of the type, e. g. "Person"
    /// </summary>
    public string RecordId { get; }

    /// <summary>
    ///     Returns the version of the type, e. g. "1.0.0"
    /// </summary>
    public CkVersion Version { get; }

    /// <inheritdoc />
    public string FullName => IsEmpty ? "" : $"{RecordId}-{Version}";

    /// <inheritdoc />
    public string SemanticVersionedFullName
    {
        get
        {
            if (IsEmpty)
            {
                return "";
            }

            var s = RecordId;
            if (Version.Major > 1)
            {
                s += $"-{Version.Major}";
            }

            return s;
        }
    }

    /// <inheritdoc />
    public bool IsEmpty => string.IsNullOrWhiteSpace(RecordId);


    /// <inheritdoc />
    public int CompareTo(CkRecordId other)
    {
        var result = string.Compare(RecordId, other.RecordId, StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        return Version.CompareTo(other.Version);
    }

    /// <inheritdoc />
    public bool Equals(CkRecordId other)
    {
        return RecordId == other.RecordId && Equals(Version, other.Version);
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
                if (conversionType == typeof(object) || conversionType == typeof(CkRecordId))
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

        var other = (CkRecordId)obj;

        return RecordId == other.RecordId && Version == other.Version;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 23 + RecordId.GetHashCode();
            hash = hash * 23 + Version.GetHashCode();
            return hash;
        }
    }

    /// <summary>
    ///     Compares two <see cref="CkRecordId" /> instances for equality.
    /// </summary>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <returns></returns>
    public static bool operator ==(CkRecordId p1, CkRecordId p2)
    {
        return p1.Equals(p2);
    }

    /// <summary>
    ///     Compares two <see cref="CkRecordId" />s for inequality.
    /// </summary>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <returns></returns>
    public static bool operator !=(CkRecordId p1, CkRecordId p2)
    {
        return !p1.Equals(p2);
    }
}