using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents a versioned construction kit type id
/// </summary>
[DebuggerDisplay("{" + nameof(Name) + "} ({" + nameof(Version) + "})")]
[JsonConverter(typeof(CkTypeIdConverter))]
public sealed record CkTypeId : IComparable<CkTypeId>, ICkKey
{
    /// <summary>
    ///     Creates a new <see cref="CkTypeId" /> from the given <paramref name="name" />.
    /// </summary>
    /// <param name="name"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public CkTypeId(string name)
    {
        var typeIndex = name.IndexOf("-", StringComparison.Ordinal);
        if (typeIndex < 0)
        {
            Name = name;
            Version = "1.0.0";
        }
        else
        {
            Name = name.Substring(0, typeIndex);
            Version = new CkVersion(name.Substring(typeIndex + 1));
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentOutOfRangeException(nameof(name), name, $"{nameof(name)} must contain a type id");
        }
    }
    
    /// <summary>
    ///     Creates a new <see cref="CkTypeId" /> from the given <paramref name="value" />.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static implicit operator CkTypeId(string value)
    {
        return new CkTypeId(value);
    }

    /// <summary>
    ///     Defines the name of the type, e. g. "Person"
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Returns the version of the type, e. g. "1.0.0"
    /// </summary>
    public CkVersion Version { get; }

    /// <inheritdoc />
    public string FullName => IsEmpty ? "" : $"{Name}-{Version}";

    /// <inheritdoc />
    public string SemanticVersionedFullName
    {
        get
        {
            if (IsEmpty)
            {
                return "";
            }

            var s = Name;
            if (Version.Major > 1)
            {
                s += $"-{Version.Major}";
            }

            return s;
        }
    }

    /// <inheritdoc />
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);


    /// <inheritdoc />
    public int CompareTo(CkTypeId? other)
    {
        if (other == null)
        {
            return 1;
        }
        
        var result = string.Compare(Name, other.Name, StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        return Version.CompareTo(other.Version);
    }

    /// <inheritdoc />
    public bool Equals(CkTypeId? other)
    {
        return other is not null && Name == other.Name && Version.IsCompatible(other.Version);
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
                if (conversionType == typeof(object) || conversionType == typeof(CkTypeId))
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
            var hash = 17;
            hash = hash * 23 + Name.GetHashCode();
            hash = hash * 23 + Version.GetHashCode();
            return hash;
        }
    }
}