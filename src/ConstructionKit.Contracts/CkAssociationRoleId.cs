using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents a versioned construction kit association id
/// </summary>
[DebuggerDisplay("{" + nameof(RoleId) + "} ({" + nameof(Version) + "})")]
[JsonConverter(typeof(CkAssociationRoleIdConverter))]
public sealed record CkAssociationRoleId : IComparable<CkAssociationRoleId>, ICkKey
{
    /// <summary>
    ///     Creates a new <see cref="CkAssociationRoleId" /> from the given <paramref name="roleId" />.
    /// </summary>
    /// <param name="roleId"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
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

    /// <summary>
    ///     Creates a new <see cref="CkAssociationRoleId" /> from the given <paramref name="roleId" /> and
    ///     <paramref name="associationRoleVersion" />.
    /// </summary>
    /// <param name="roleId"></param>
    /// <param name="associationRoleVersion"></param>
    public CkAssociationRoleId(string roleId, string associationRoleVersion = "1.0.0")
    {
        RoleId = roleId;
        Version = associationRoleVersion;
    }

    /// <summary>
    ///     Creates a new <see cref="CkAssociationRoleId" /> from the given <paramref name="value" />.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static implicit operator CkAssociationRoleId(string value)
    {
        return new CkAssociationRoleId(value);
    }

    /// <summary>
    ///     Defines the name of the association, e. g. "ParentChild"
    /// </summary>
    public string RoleId { get; }

    /// <summary>
    ///     Returns the version of the association role, e. g. "1.0.0"
    /// </summary>
    public CkVersion Version { get; }

    /// <summary>
    ///     Returns the full name of the association role, e. g. "ParentChild-1.0.0"
    /// </summary>
    public string FullName => IsEmpty ? "" : $"{RoleId}-{Version}";

    /// <inheritdoc />
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

    /// <inheritdoc />
    public bool IsEmpty => string.IsNullOrWhiteSpace(RoleId);

    /// <inheritdoc />
    public int CompareTo(CkAssociationRoleId? other)
    {
        if (other == null)
        {
            return 1;
        }
        var result = string.Compare(RoleId, other.RoleId, StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        return Version.CompareTo(other.Version);
    }

    /// <inheritdoc />
    public bool Equals(CkAssociationRoleId? other)
    {
        return other is not null && RoleId == other.RoleId && Equals(Version, other.Version);
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
                if (conversionType == typeof(object) || conversionType == typeof(CkAssociationRoleId))
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
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 15;
            hash = hash * 22 + RoleId.GetHashCode();
            hash = hash * 22 + Version.GetHashCode();
            return hash;
        }
    }
}