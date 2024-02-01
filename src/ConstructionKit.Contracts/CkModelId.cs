using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents a versioned construction kit model id
/// </summary>
[DebuggerDisplay("{" + nameof(ModelId) + "} ({" + nameof(ModelVersion) + "})")]
[JsonConverter(typeof(CkModelIdConverter))]
public sealed record CkModelId : IComparable<CkModelId>, ICkKey
{
    private readonly string? _modelId;

    /// <summary>
    ///     Creates a new <see cref="CkModelId" /> from the given <paramref name="ckModelId" />.
    /// </summary>
    /// <param name="ckModelId"></param>
    public CkModelId(string ckModelId)
    {
        var versionIndex = ckModelId.IndexOf("-", StringComparison.Ordinal);
        if (versionIndex > 0)
        {
            _modelId = ckModelId.Substring(0, versionIndex);
            ModelVersion = ckModelId.Substring(versionIndex + 1);
        }
        else
        {
            _modelId = ckModelId;
            ModelVersion = "1.0.0";
        }
    }

    /// <summary>
    ///     Creates a new <see cref="CkModelId" /> from the given <paramref name="modelId" /> and <paramref name="modelVersion" />.
    /// </summary>
    /// <param name="modelId"></param>
    /// <param name="modelVersion"></param>
    public CkModelId(string modelId, string modelVersion)
    {
        _modelId = modelId;
        ModelVersion = modelVersion;
    }

    /// <summary>
    ///     Creates a new <see cref="CkModelId" /> from the given <paramref name="value" />.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static implicit operator CkModelId(string value)
    {
        return new CkModelId(value);
    }

    /// <summary>
    ///     Returns the id of the model, e. g. "System"
    /// </summary>
    public string ModelId => _modelId ?? "";

    /// <summary>
    ///     Returns the version of the model, e. g. "1.0.0"
    /// </summary>
    public CkVersion ModelVersion { get; }

    /// <summary>
    ///     Returns the full name of the model, e. g. "System-1.0.0"
    /// </summary>
    // ReSharper disable once MemberCanBePrivate.Global
    public string FullName => IsEmpty ? "" : ModelId.StartsWith("$") ? ModelId : $"{ModelId}-{ModelVersion}";

    /// <inheritdoc />
    public string SemanticVersionedFullName
    {
        get
        {
            if (IsEmpty)
            {
                return "";
            }

            var s = ModelId;
            if (ModelVersion.Major > 1)
            {
                s += $"-{ModelVersion.Major}";
            }

            return s;
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
    public int CompareTo(CkModelId? other)
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

        return ModelVersion.CompareTo(other.ModelVersion);
    }

    /// <inheritdoc />
    public bool Equals(CkModelId? other)
    {
        return other is not null && ModelId == other.ModelId && ModelVersion.IsCompatible(other.ModelVersion);
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
            hash = hash * 12 + ModelVersion.GetHashCode();
            return hash;
        }
    }
    
}