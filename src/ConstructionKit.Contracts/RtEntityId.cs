using System.ComponentModel;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents a unique identifier of a runtime model entity and its construction kit type.
/// </summary>
[Newtonsoft.Json.JsonConverter(typeof(NewtonRtEntityIdConverter))]
[TypeConverter(typeof(RtEntityIdConverter))]
public readonly struct RtEntityId : IComparable<RtEntityId>, IEquatable<RtEntityId>, IConvertible
{
    /// <summary>
    ///     Creates a new <see cref="RtEntityId" /> from the given <paramref name="rtEntityId" />.
    /// </summary>
    /// <param name="rtEntityId"></param>
    public RtEntityId(string rtEntityId)
    {
        var rtIdIndex = rtEntityId.IndexOf("@", StringComparison.Ordinal);
        if (rtIdIndex > 0)
        {
            CkTypeId = rtEntityId.Substring(0, rtIdIndex);
            RtId = new OctoObjectId(rtEntityId.Substring(rtIdIndex + 1));
        }
        else
        {
            CkTypeId = rtEntityId;
            RtId = OctoObjectId.Empty;
        }
    }
    
    /// <summary>
    ///     Creates a new instance of <see cref="RtEntityId" />.
    /// </summary>
    /// <param name="ckTypeId"></param>
    /// <param name="rtId"></param>
    [Newtonsoft.Json.JsonConstructor]
    [JsonConstructor]
    public RtEntityId(CkId<CkTypeId> ckTypeId, OctoObjectId rtId)
    {
        CkTypeId = ckTypeId;
        RtId = rtId;
    }

    /// <summary>
    ///     Creates a new instance of <see cref="RtEntityId" />.
    /// </summary>
    /// <param name="ckModelId"></param>
    /// <param name="ckTypeId"></param>
    /// <param name="rtId"></param>
    public RtEntityId(CkModelId ckModelId, CkTypeId ckTypeId, OctoObjectId rtId)
    {
        CkTypeId = new CkId<CkTypeId>(ckModelId, ckTypeId);
        RtId = rtId;
    }
    
    /// <summary>
    ///     Creates a new <see cref="RtEntityId" /> from the given <paramref name="value" />.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static implicit operator RtEntityId(string value)
    {
        return new RtEntityId(value);
    }

    /// <summary>
    ///     The construction kit type id.
    /// </summary>
    [JsonConverter(typeof(CkIdTypeIdConverter))]
    public CkId<CkTypeId> CkTypeId { get; }

    /// <summary>
    ///     The runtime id.
    /// </summary>
    [JsonConverter(typeof(OctoObjectIdConverter))]
    public OctoObjectId RtId { get; }

    /// <inheritdoc />
    public int CompareTo(RtEntityId other)
    {
        var num = CkTypeId.CompareTo(other.CkTypeId);
        if (num != 0)
        {
            return num;
        }

        return RtId.CompareTo(other.RtId);
    }

    /// <summary>Compares this ObjectId to another object.</summary>
    /// <param name="obj">The other object.</param>
    /// <returns>True if the other object is an ObjectId and equal to this one.</returns>
    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is RtEntityId rhs && Equals(rhs);
    }

    /// <inheritdoc />
    public bool Equals(RtEntityId other)
    {
        return Equals(CkTypeId, other.CkTypeId) &&
               Equals(RtId, other.RtId);
    }

    /// <summary>Gets the hash code.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return CkTypeId.GetHashCode() ^ RtId.GetHashCode();
    }


    /// <summary>Compares two RtEntityId.</summary>
    /// <param name="lhs">The first RtEntityId.</param>
    /// <param name="rhs">The other RtEntityId.</param>
    /// <returns>True if the two RtEntityIds are equal.</returns>
    public static bool operator ==(RtEntityId lhs, RtEntityId rhs)
    {
        return lhs.Equals(rhs);
    }

    /// <summary>Compares two RtEntityIds.</summary>
    /// <param name="lhs">The first RtEntityId.</param>
    /// <param name="rhs">The other RtEntityId.</param>
    /// <returns>True if the two RtEntityIds are not equal.</returns>
    public static bool operator !=(RtEntityId lhs, RtEntityId rhs)
    {
        return !(lhs == rhs);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"{CkTypeId}@{RtId}";
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
        return ToString();
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
}