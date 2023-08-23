namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Represents a unique identifier of a runtime model entity and its construction kit type.
/// </summary>
public readonly struct RtEntityId : IComparable<RtEntityId>, IEquatable<RtEntityId>
{
    /// <summary>
    /// Creates a new instance of <see cref="RtEntityId"/>.
    /// </summary>
    /// <param name="ckTypeId"></param>
    /// <param name="rtId"></param>
    public RtEntityId(CkId<CkTypeId> ckTypeId, OctoObjectId rtId)
    {
        CkTypeId = ckTypeId;
        RtId = rtId;
    }
    
    /// <summary>
    /// Creates a new instance of <see cref="RtEntityId"/>.
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
    /// The construction kit type id.
    /// </summary>
    public CkId<CkTypeId> CkTypeId { get; init; }
    
    /// <summary>
    /// The runtime id.
    /// </summary>
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
        return $"CkTypeId: '{CkTypeId}', RtId: '{RtId}'";
    }
}
