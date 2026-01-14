using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
///     Represents a versioned blueprint id with version range
/// </summary>
[DebuggerDisplay("{" + nameof(Name) + "}-{" + nameof(BlueprintVersionRange) + "}")]
[JsonConverter(typeof(BlueprintIdVersionRangeConverter))]
public sealed record BlueprintIdVersionRange : IComparable<BlueprintIdVersionRange>, ICkElementId
{
    private readonly string? _name;

    /// <summary>
    ///     Creates a new <see cref="BlueprintIdVersionRange" /> from the given <paramref name="blueprintName" />.
    /// </summary>
    /// <param name="blueprintName"></param>
    public BlueprintIdVersionRange(string blueprintName)
    {
        var versionIndex = blueprintName.IndexOf("-", StringComparison.Ordinal);
        if (versionIndex > 0)
        {
            _name = blueprintName.Substring(0, versionIndex);
            BlueprintVersionRange = blueprintName.Substring(versionIndex + 1);
        }
        else
        {
            _name = blueprintName;
            BlueprintVersionRange = "1.0.0";
        }
    }

    /// <summary>
    ///     Creates a new <see cref="BlueprintIdVersionRange" /> from the given <paramref name="name" /> and <paramref name="blueprintVersionRange" />.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="blueprintVersionRange"></param>
    public BlueprintIdVersionRange(string name, string blueprintVersionRange)
    {
        _name = name;
        BlueprintVersionRange = blueprintVersionRange;
    }

    /// <summary>
    ///     Creates a new <see cref="BlueprintIdVersionRange" /> from the given <paramref name="value" />.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static implicit operator BlueprintIdVersionRange(string value)
    {
        return new BlueprintIdVersionRange(value);
    }

    /// <summary>
    ///     Returns the name of the blueprint, e.g. "InfrastructureStarter"
    /// </summary>
    public string Name => _name ?? "";

    /// <summary>
    ///     Returns the version range of the blueprint (e.g. "1.0.0" or "[1.0,)")
    /// </summary>
    public CkVersionRange BlueprintVersionRange { get; }

    /// <summary>
    ///     Returns the full name of the blueprint, e.g. "InfrastructureStarter-1.0.0"
    /// </summary>
    public string FullName => IsEmpty ? "" : $"{Name}-{BlueprintVersionRange}";

    /// <inheritdoc />
    public string SemanticVersionedFullName
    {
        get
        {
            if (IsEmpty)
            {
                return "";
            }

            return $"{Name}-{BlueprintVersionRange}";
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
                if (conversionType == typeof(object) || conversionType == typeof(BlueprintIdVersionRange))
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
    public int CompareTo(BlueprintIdVersionRange? other)
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

        return BlueprintVersionRange.CompareTo(other.BlueprintVersionRange);
    }

    /// <inheritdoc />
    public bool Equals(BlueprintIdVersionRange? other)
    {
        return other is not null && Name == other.Name && BlueprintVersionRange.Overlaps(other.BlueprintVersionRange);
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
            hash = hash * 13 + BlueprintVersionRange.GetHashCode();
            return hash;
        }
    }

    /// <summary>
    ///     Checks if a version satisfies this version range
    /// </summary>
    /// <param name="version">The version to check</param>
    /// <returns>True if the version is within the range, false otherwise</returns>
    public bool IsSatisfiedBy(BlueprintId version)
    {
        var result = string.Compare(Name, version.Name, StringComparison.Ordinal);
        if (result != 0)
        {
            return false;
        }

        return BlueprintVersionRange.IsSatisfiedBy(version.Version);
    }
}
