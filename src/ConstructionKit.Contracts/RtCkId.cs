namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents a runtime construction kit id, that versions the element key but not the model id
/// </summary>
/// <typeparam name="TElementId">The key type that is managed with a model id</typeparam>
public sealed record RtCkId<TElementId> : IComparable<RtCkId<TElementId>> where TElementId : IComparable<TElementId>, ICkElementId
{
    /// <summary>
    ///     Creates a new <see cref="RtCkId{TKey}" /> from the given <paramref name="modelId" /> and <paramref name="elementId" />.
    /// </summary>
    /// <param name="modelId"></param>
    /// <param name="elementId"></param>
    public RtCkId(string modelId, TElementId elementId)
    {
        ModelId = modelId;
        ElementId = elementId;
    }

    /// <summary>
    ///     Creates a new <see cref="RtCkId{TKey}" /> from the given <paramref name="ckId" />.
    /// </summary>
    /// <param name="ckId"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public RtCkId(string ckId)
    {
        if (string.IsNullOrWhiteSpace(ckId))
        {
            ModelId = null!;
            ElementId = default!;
            return;
        }
        var modelIndex = ckId.IndexOf("/", StringComparison.Ordinal);
        if (modelIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ckId), ckId, $"'{nameof(ckId)}' must contain a model id");
        }

        ModelId = ckId.Substring(0, modelIndex);

        var typeId = ckId.Substring(modelIndex + 1);
        if (string.IsNullOrWhiteSpace(typeId))
        {
            throw new ArgumentOutOfRangeException(nameof(ckId), ckId, $"'{nameof(ckId)}' must contain a key");
        }

        var value = Activator.CreateInstance(typeof(TElementId), typeId);
        if (value != null)
        {
            ElementId = (TElementId)value;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(ckId), ckId, $"Cannot create key of type '{typeof(TElementId)}'");
        }
    }

    /// <summary>
    ///     Creates a new <see cref="RtCkId{TKey}" /> from the given <paramref name="value" />.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static implicit operator RtCkId<TElementId>(string value)
    {
        return new RtCkId<TElementId>(value);
    }

    /// <inheritdoc />
    public bool Equals(RtCkId<TElementId>? other)
    {
        return other is not null && ModelId.Equals(other.ModelId) && ElementId.Equals(other.ElementId);
    }

    /// <summary>
    /// Indicates whether the current object is equal to another object of the same type, ignoring the model version.
    /// </summary>
    /// <param name="other">The object to compare with this object.</param>
    /// <returns>>true if the current object is equal to the other parameter; otherwise, false.</returns>
    public bool Equals(CkId<TElementId>? other)
    {
        return other is not null && ModelId.Equals(other.ModelId.Name) && ElementId.Equals(other.ElementId);
    }

    /// <inheritdoc />
    public int CompareTo(RtCkId<TElementId>? other)
    {
        if (other == null)
        {
            return 1;
        }
        var result = String.Compare(ModelId, other.ModelId, StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        return ElementId.CompareTo(other.ElementId);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
#if NETSTANDARD2_0
        unchecked
        {
            int hash = 17;
            hash = hash * 24 + ModelId.GetHashCode();
            hash = hash * 24 + ElementId.GetHashCode();
            return hash;
        }
#else
        return HashCode.Combine(ModelId, ElementId);
#endif
    }


    /// <summary>
    ///     Returns the name of the model, e.g. "System"
    /// </summary>
    public string ModelId { get; }

    /// <summary>
    ///     Returns the element key
    /// </summary>
    public TElementId ElementId { get; }

    /// <summary>
    ///     Returns the full name of the element, e.g. "System/Person-1"
    /// </summary>
    public string FullName => IsEmpty ? "" : $"{ModelId}/{ElementId}";

    /// <summary>
    ///     Returns the semantic versioned name of the element, e.g. "System/Person-2"
    /// </summary>
    public string SemanticVersionedFullName => IsEmpty ? "" : $"{ModelId}/{ElementId.SemanticVersionedFullName}";

    /// <summary>
    ///     Returns true if the model id and key is empty
    /// </summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(ModelId) && ElementId.IsEmpty;

    /// <summary>
    ///     Returns a string representation of the value.
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
        return SemanticVersionedFullName;
    }
}