using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
///     Represents a versioned blueprint id
/// </summary>
/// <remarks>
/// We use a dash ("-") to separate the blueprint id and the version.
/// The version number of a blueprint allows to manage versioning of blueprints.
/// </remarks>
[DebuggerDisplay("{" + nameof(Name) + "}-{" + nameof(Version) + "}")]
[JsonConverter(typeof(BlueprintIdConverter))]
public sealed record BlueprintId : IComparable<BlueprintId>, ICkElementId
{
    /// <summary>
    ///     Creates a new <see cref="BlueprintId" /> from the given <paramref name="blueprintId" />.
    /// </summary>
    /// <param name="blueprintId"></param>
    public BlueprintId(string blueprintId)
    {
        var versionIndex = blueprintId.IndexOf("-", StringComparison.Ordinal);
        if (versionIndex > 0)
        {
            Name = blueprintId.Substring(0, versionIndex);
            Version = blueprintId.Substring(versionIndex + 1);
        }
        else
        {
            Name = blueprintId;
            Version = "1.0.0";
        }
    }

    /// <summary>
    ///     Creates a new <see cref="BlueprintId" /> from the given <paramref name="blueprintId" /> and <paramref name="version" />.
    /// </summary>
    /// <param name="blueprintId"></param>
    /// <param name="version"></param>
    public BlueprintId(string blueprintId, string version)
    {
        Name = blueprintId;
        Version = version;
    }

    /// <summary>
    ///     Creates a new <see cref="BlueprintId" /> from the given <paramref name="blueprintId" /> and <paramref name="version" />.
    /// </summary>
    /// <param name="blueprintId"></param>
    /// <param name="version"></param>
    public BlueprintId(string blueprintId, CkVersion version)
    {
        Name = blueprintId;
        Version = version;
    }

    /// <summary>
    ///     Creates a new <see cref="BlueprintId" /> from the given <paramref name="value" />.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static implicit operator BlueprintId(string value)
    {
        return new BlueprintId(value);
    }

    /// <summary>
    ///     Returns the name of the blueprint, e.g. "InfrastructureStarter"
    /// </summary>
    public string Name => field ?? "";

    /// <summary>
    ///     Returns the version of the blueprint, e.g. "1.0.0"
    /// </summary>
    public CkVersion Version { get; }

    /// <summary>
    ///     Returns the full name of the blueprint, e.g. "InfrastructureStarter-1.0.0"
    /// </summary>
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
                if (conversionType == typeof(object) || conversionType == typeof(BlueprintId))
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

    /// <inheritdoc />
    public int CompareTo(BlueprintId? other)
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
    public bool Equals(BlueprintId? other)
    {
        return other is not null && Name == other.Name && Version == other.Version;
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
            var hash = 53;
            hash = hash * 13 + Name.GetHashCode();
            return hash;
        }
    }

    /// <summary>
    /// Converts to a version range
    /// </summary>
    /// <returns></returns>
    public BlueprintIdVersionRange ToVersionRange()
    {
        return new BlueprintIdVersionRange(Name, $"[{Version.ToString()}]");
    }
}
