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
    /// Defines the name of the attribute, e. g. "Designation"
    /// </summary>
    public string AttributeId { get; }

    public CkVersion Version { get; }

    public string FullName => IsEmpty ? "" : $"{AttributeId}-{Version}";

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
    
    public bool IsEmpty => string.IsNullOrWhiteSpace(AttributeId);
    
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

    public CkAttributeId(string attributeId, string typeVersion = "1.0.0")
    {
        AttributeId = attributeId;
        Version = typeVersion;
    }

    public static implicit operator CkAttributeId(string value)
    {
        return new CkAttributeId(value);
    }

    public int CompareTo(CkAttributeId other)
    {
        var result = String.Compare(AttributeId, other.AttributeId, StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        return Version.CompareTo(other.Version);
    }

    public TypeCode GetTypeCode()
    {
        return TypeCode.Object;
    }

    public bool ToBoolean(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    public byte ToByte(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    public char ToChar(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    public DateTime ToDateTime(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    public decimal ToDecimal(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    public double ToDouble(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    public short ToInt16(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    public int ToInt32(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    public long ToInt64(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    public sbyte ToSByte(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    public float ToSingle(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    public string ToString(IFormatProvider? provider)
    {
        return FullName;
    }

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

    public ushort ToUInt16(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    public uint ToUInt32(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

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

    public override bool Equals(object? obj)
    {
        if (obj == null)
        {
            return false;
        }

        var other = (CkAttributeId)obj;

        return AttributeId == other.AttributeId && Version == other.Version;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(AttributeId, Version);
    }

    public bool Equals(CkAttributeId other)
    {
        return AttributeId == other.AttributeId && Version.Equals(other.Version);
    }

    public static bool operator ==(CkAttributeId p1, CkAttributeId p2)
    {
        return p1.Equals(p2);
    }

    public static bool operator !=(CkAttributeId p1, CkAttributeId p2)
    {
        return !p1.Equals(p2);
    }
}