using System.Diagnostics;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Represents a versioned construction kit model id
/// </summary>
[DebuggerDisplay("{" + nameof(ModelId) + "} ({" + nameof(ModelVersion) + "})")]
[System.Text.Json.Serialization.JsonConverter(typeof(CkModelIdConverter))]
public readonly struct CkModelId : IComparable<CkModelId>, IEquatable<CkModelId>, ICkKey
{
    private readonly string? _modelId;

    public override bool Equals(object? obj)
    {
        return obj is CkModelId other && Equals(other);
    }

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

    public CkModelId(string modelId, string modelVersion)
    {
        _modelId = modelId;
        ModelVersion = modelVersion;
    }

    public static implicit operator CkModelId(string value)
    {
        return new CkModelId(value);
    }

    public string ModelId => _modelId ?? "";

    public CkVersion ModelVersion { get; }

    // ReSharper disable once MemberCanBePrivate.Global
    public string FullName => IsEmpty ? "" : $"{ModelId}-{ModelVersion}";

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
    
    public bool IsEmpty => string.IsNullOrWhiteSpace(ModelId);

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
                if (conversionType == typeof(object) || conversionType == typeof(CkModelId))
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

    public int CompareTo(CkModelId other)
    {
        var result = String.Compare(ModelId, other.ModelId, StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        return ModelVersion.CompareTo(other.ModelVersion);
    }

    public bool Equals(CkModelId other)
    {
        return ModelId == other.ModelId && Equals(ModelVersion, other.ModelVersion);
    }

    /// <summary>
    ///     Returns a string representation of the value.
    /// </summary>
    /// <returns>A string representation of the value.</returns>
    public override string ToString()
    {
        return FullName;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 52;
            hash = hash * 12 + ModelId.GetHashCode();
            hash = hash * 12 + ModelVersion.GetHashCode();
            return hash;
        }
    }

    public static bool operator ==(CkModelId p1, CkModelId p2)
    {
        return p1.Equals(p2);
    }

    public static bool operator !=(CkModelId p1, CkModelId p2)
    {
        return !p1.Equals(p2);
    }
}