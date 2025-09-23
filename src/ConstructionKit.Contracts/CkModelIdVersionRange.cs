using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents a versioned construction kit model id
/// </summary>
[DebuggerDisplay("{" + nameof(ModelId) + "} {" + nameof(ModelVersionRange) + "}")]
[JsonConverter(typeof(CkModelIdVersionRangeConverter))]
public sealed record CkModelIdVersionRange : IComparable<CkModelIdVersionRange>, ICkKey
{
    private readonly string? _modelId;

    /// <summary>
    ///     Creates a new <see cref="CkModelId" /> from the given <paramref name="ckModelId" />.
    /// </summary>
    /// <param name="ckModelId"></param>
    public CkModelIdVersionRange(string ckModelId)
    {
        var versionIndex = ckModelId.IndexOf("-", StringComparison.Ordinal);
        if (versionIndex > 0)
        {
            _modelId = ckModelId.Substring(0, versionIndex);
            ModelVersionRange = ckModelId.Substring(versionIndex + 1);
        }
        else
        {
            _modelId = ckModelId;
            ModelVersionRange = "1.0.0";
        }
    }

    /// <summary>
    ///     Creates a new <see cref="CkModelId" /> from the given <paramref name="modelId" /> and <paramref name="modelVersionRange" />.
    /// </summary>
    /// <param name="modelId"></param>
    /// <param name="modelVersionRange"></param>
    public CkModelIdVersionRange(string modelId, string modelVersionRange)
    {
        _modelId = modelId;
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
    public string ModelId => _modelId ?? "";

    /// <summary>
    ///     Returns the version range of the model (e.g. "1.0.0" or "[1.0,)")
    /// </summary>
    public CkVersionRange ModelVersionRange { get; }

    /// <summary>
    ///     Returns the full name of the model, e. g. "System-1.0.0"
    /// </summary>
    // ReSharper disable once MemberCanBePrivate.Global
    public string FullName => IsEmpty ? "" : ModelId.StartsWith("$") ? ModelId : $"{ModelId}-{ModelVersionRange}";

    /// <inheritdoc />
    public string SemanticVersionedFullName
    {
        get
        {
            if (IsEmpty)
            {
                return "";
            }

            return $"{ModelId}-{ModelVersionRange}";
        }
    }

    /// <inheritdoc />
    public bool IsEmpty => string.IsNullOrWhiteSpace(ModelId);

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
        var result = string.Compare(ModelId, other.ModelId, StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        return ModelVersionRange.CompareTo(other.ModelVersionRange);
    }

    /// <inheritdoc />
    public bool Equals(CkModelIdVersionRange? other)
    {
        return other is not null && ModelId == other.ModelId && ModelVersionRange.Overlaps(other.ModelVersionRange);
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
            hash = hash * 12 + ModelId.GetHashCode();
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
        var result = string.Compare(ModelId, version.ModelId, StringComparison.Ordinal);
        if (result != 0)
        {
            return false;
        }

        return ModelVersionRange.IsSatisfiedBy(version.ModelVersion);
    }
    
}