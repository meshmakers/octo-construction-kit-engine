using System.Diagnostics;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Represents a versioned construction kit association id
/// </summary>
[DebuggerDisplay("{" + nameof(RoleId) + "} ({" + nameof(Version) + "})")]
[System.Text.Json.Serialization.JsonConverter(typeof(CkAssociationIdConverter))]
public readonly struct CkAssociationRoleId : IComparable<CkAssociationRoleId>, IEquatable<CkAssociationRoleId>, ICkKey
{
    /// <summary>
    /// Defines the name of the association, e. g. "ParentChild"
    /// </summary>
    public string RoleId { get; }
    
    public CkVersion Version { get; }

    public string FullName => IsEmpty ? "" : $"{RoleId}-{Version}";

    public string SemanticVersionedFullName
    {
        get
        {
            if (IsEmpty)
            {
                return "";
            }
            
            var s = RoleId;
            if (Version.Major > 1)
            {
                s += $"-{Version.Major}";
            }

            return s;
        }
    }

    public bool IsEmpty => string.IsNullOrWhiteSpace(RoleId);

    public CkAssociationRoleId(string roleId)
    {
        var typeIndex = roleId.IndexOf("-", StringComparison.Ordinal);
        if (typeIndex < 0)
        {
            RoleId = roleId;
            Version = "1.0.0";
        }
        else
        {
            RoleId = roleId.Substring(0, typeIndex);
            Version = roleId.Substring(typeIndex + 1);
        }
        if (string.IsNullOrWhiteSpace(RoleId))
        {
            throw new ArgumentOutOfRangeException(nameof(roleId), roleId, $"{nameof(roleId)} must contain a type id");
        }
    }

    public CkAssociationRoleId(string roleId, string typeVersion = "1.0.0") 
    {
        RoleId = roleId;
        Version = typeVersion;
    }
    
    public static implicit operator CkAssociationRoleId(string value)
    {
        return new CkAssociationRoleId(value);
    }

    public int CompareTo(CkAssociationRoleId other)
    {
        var result = String.Compare(RoleId, other.RoleId, StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        return Version.CompareTo(other.Version);
    }

    public bool Equals(CkAssociationRoleId other)
    {
        return RoleId == other.RoleId && Equals(Version, other.Version);
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
                if (conversionType == typeof(object) || conversionType == typeof(CkAssociationRoleId))
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
        
        var other = (CkAssociationRoleId)obj;
        
        return RoleId == other.RoleId && Version == other.Version;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 15;
            hash = hash * 22 + RoleId.GetHashCode();
            hash = hash * 22 + Version.GetHashCode();
            return hash;
        }
    }
    
    public static bool operator ==(CkAssociationRoleId p1, CkAssociationRoleId p2)
    {
        return p1.Equals(p2);
    }

    public static bool operator !=(CkAssociationRoleId p1, CkAssociationRoleId p2)
    {
        return !p1.Equals(p2);
    }
}