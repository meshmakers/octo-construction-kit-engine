using System.Diagnostics;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Represents a versioned construction kit type id
/// </summary>
[DebuggerDisplay("{" + nameof(TypeId) + "} ({" + nameof(Version) + "})")]
[System.Text.Json.Serialization.JsonConverter(typeof(CkTypeIdConverter))]
public readonly struct CkTypeId : IComparable<CkTypeId>, IEquatable<CkTypeId>, ICkKey
{

    /// <summary>
    /// Defines the name of the type, e. g. "Person"
    /// </summary>
    public string TypeId { get; }
    
    public CkVersion Version { get; }

    public string FullName => IsEmpty ? "" : $"{TypeId}-{Version}";
    
    public string SemanticVersionedFullName
    {
        get
        {
            if (IsEmpty)
            {
                return "";
            }
            
            var s = TypeId;
            if (Version.Major > 1)
            {
                s += $"-{Version.Major}";
            }

            return s;
        }
    }
    
    public bool IsEmpty => string.IsNullOrWhiteSpace(TypeId);

    public CkTypeId(string typeId)
    {
        var typeIndex = typeId.IndexOf("-", StringComparison.Ordinal);
        if (typeIndex < 0)
        {
            TypeId = typeId;
            Version = "1.0.0";
        }
        else
        {
            TypeId = typeId.Substring(0, typeIndex);
            Version = typeId.Substring(typeIndex + 1);
        }
        if (string.IsNullOrWhiteSpace(TypeId))
        {
            throw new ArgumentOutOfRangeException(nameof(typeId), typeId, $"{nameof(typeId)} must contain a type id");
        }
    }

    public CkTypeId(string typeId, string version = "1.0.0") 
    {
        TypeId = typeId;
        Version = version;
        if (string.IsNullOrWhiteSpace(TypeId))
        {
            throw new ArgumentOutOfRangeException(nameof(typeId), typeId, $"{nameof(typeId)} must contain a type id");
        }
    }
    
    public static implicit operator CkTypeId(string value)
    {
        return new CkTypeId(value);
    }

    public int CompareTo(CkTypeId other)
    {
        var result = String.Compare(TypeId, other.TypeId, StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        return Version.CompareTo(other.Version);
    }

    public bool Equals(CkTypeId other)
    {
        return TypeId == other.TypeId && Equals(Version, other.Version);
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
                if (conversionType == typeof(object) || conversionType == typeof(CkTypeId))
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
        
        var other = (CkTypeId)obj;
        
        return TypeId == other.TypeId && Version == other.Version;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + TypeId.GetHashCode();
            hash = hash * 23 + Version.GetHashCode();
            return hash;
        }
    }
    
    public static bool operator ==(CkTypeId p1, CkTypeId p2)
    {
        return p1.Equals(p2);
    }

    public static bool operator !=(CkTypeId p1, CkTypeId p2)
    {
        return !p1.Equals(p2);
    }
}