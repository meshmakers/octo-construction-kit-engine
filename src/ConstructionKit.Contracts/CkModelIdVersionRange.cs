using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents a versioned construction kit model id
/// </summary>
[DebuggerDisplay("{" + nameof(Name) + "}-{" + nameof(ModelVersionRange) + "}")]
[JsonConverter(typeof(CkModelIdVersionRangeConverter))]
public sealed record CkModelIdVersionRange : IComparable<CkModelIdVersionRange>, ICkElementId
{
    private readonly string? _name;

    /// <summary>
    ///     Creates a new <see cref="CkModelId" /> from the given <paramref name="ckName" />.
    /// </summary>
    /// <param name="ckName"></param>
    public CkModelIdVersionRange(string ckName)
    {
        var versionIndex = ckName.IndexOf("-", StringComparison.Ordinal);
        if (versionIndex > 0)
        {
            _name = ckName.Substring(0, versionIndex);
            ModelVersionRange = ckName.Substring(versionIndex + 1);
        }
        else
        {
            _name = ckName;
            ModelVersionRange = "1.0.0";
        }
    }

    /// <summary>
    ///     Creates a new <see cref="CkModelId" /> from the given <paramref name="name" /> and <paramref name="modelVersionRange" />.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="modelVersionRange"></param>
    public CkModelIdVersionRange(string name, string modelVersionRange)
    {
        _name = name;
        ModelVersionRange = modelVersionRange;
    }

    /// <summary>
    ///     Creates a new <see cref="CkModelId" /> from the given <paramref name="value" />.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static implicit operator CkModelIdVersionRange(string value)
    {
        return new CkModelIdVersionRange(value);
    }

    /// <summary>
    ///     Returns the id of the model, e. g. "System"
    /// </summary>
    public string Name => _name ?? "";

    /// <summary>
    ///     Returns the version range of the model (e.g. "1.0.0" or "[1.0,)")
    /// </summary>
    public CkVersionRange ModelVersionRange { get; }

    /// <summary>
    ///     Returns the full name of the model, e. g. "System-1.0.0"
    /// </summary>
    // ReSharper disable once MemberCanBePrivate.Global
    public string FullName => IsEmpty ? "" : Name.StartsWith("$") ? Name : $"{Name}-{ModelVersionRange}";

    /// <inheritdoc />
    public string SemanticVersionedFullName
    {
        get
        {
            if (IsEmpty)
            {
                return "";
            }

            return $"{Name}-{ModelVersionRange}";
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
                if (conversionType == typeof(object) || conversionType == typeof(CkModelId))
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
    public int CompareTo(CkModelIdVersionRange? other)
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

        return ModelVersionRange.CompareTo(other.ModelVersionRange);
    }

    /// <inheritdoc />
    public bool Equals(CkModelIdVersionRange? other)
    {
        return other is not null && Name == other.Name && ModelVersionRange.Overlaps(other.ModelVersionRange);
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
            var hash = 52;
            hash = hash * 12 + Name.GetHashCode();
            hash = hash * 12 + ModelVersionRange.GetHashCode();
            return hash;
        }
    }

    /// <summary>
    ///     Checks if a version satisfies this version range
    /// </summary>
    /// <param name="version">The version to check</param>
    /// <returns>True if the version is within the range, false otherwise</returns>
    public bool IsSatisfiedBy(CkModelId version)
    {
        var result = string.Compare(Name, version.Name, StringComparison.Ordinal);
        if (result != 0)
        {
            return false;
        }

        return ModelVersionRange.IsSatisfiedBy(version.Version);
    }
    
}