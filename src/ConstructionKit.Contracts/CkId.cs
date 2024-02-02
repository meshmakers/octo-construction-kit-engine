namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents a versioned construction kit element id
/// </summary>
/// <typeparam name="TKey">The key type that is managed with a model id</typeparam>
public sealed record CkId<TKey> : IComparable<CkId<TKey>> where TKey : IComparable<TKey>, ICkKey
{
    /// <summary>
    ///     Creates a new <see cref="CkId{TKey}" /> from the given <paramref name="modelId" /> and <paramref name="key" />.
    /// </summary>
    /// <param name="modelId"></param>
    /// <param name="key"></param>
    public CkId(CkModelId modelId, TKey key)
    {
        ModelId = modelId;
        Key = key;
    }

    /// <summary>
    ///     Creates a new <see cref="CkId{TKey}" /> from the given <paramref name="ckId" />.
    /// </summary>
    /// <param name="ckId"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public CkId(string ckId)
    {
        var modelIndex = ckId.IndexOf("/", StringComparison.Ordinal);
        if (modelIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ckId), ckId, $"'{nameof(ckId)}' must contain a model id");
        }

        ModelId = new CkModelId(ckId.Substring(0, modelIndex));

        var typeId = ckId.Substring(modelIndex + 1);
        if (string.IsNullOrWhiteSpace(typeId))
        {
            throw new ArgumentOutOfRangeException(nameof(ckId), ckId, $"'{nameof(ckId)}' must contain a key");
        }

        var value = Activator.CreateInstance(typeof(TKey), typeId);
        if (value != null)
        {
            Key = (TKey)value;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(ckId), ckId, $"Cannot create key of type '{typeof(TKey)}'");
        }
    }

    /// <summary>
    ///     Creates a new <see cref="CkId{TKey}" /> from the given <paramref name="value" />.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static implicit operator CkId<TKey>(string value)
    {
        return new CkId<TKey>(value);
    }
    
    /// <inheritdoc />
    public bool Equals(CkId<TKey>? other)
    {
        return other is not null && ModelId.Equals(other.ModelId) && Key.Equals(other.Key);
    }

    /// <inheritdoc />
    public int CompareTo(CkId<TKey>? other)
    {
        if (other == null)
        {
            return 1;
        }
        var result = ModelId.CompareTo(other.ModelId);
        if (result != 0)
        {
            return result;
        }

        return Key.CompareTo(other.Key);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
#if NETSTANDARD2_0
        unchecked
        {
            int hash = 17;
            hash = hash * 24 + ModelId.GetHashCode();
            hash = hash * 24 + Key.GetHashCode();
            return hash;
        }
#else
        return HashCode.Combine(ModelId, Key);
#endif
    }


    /// <summary>
    ///     Returns the versioned model id, e. g. "System-1.0.0"
    /// </summary>
    public CkModelId ModelId { get; }

    /// <summary>
    ///     Returns the element key
    /// </summary>
    public TKey Key { get; }

    /// <summary>
    ///     Returns the full name of the element, e. g. "System-1.0.0/Person-1.0.0"
    /// </summary>
    public string FullName => IsEmpty ? "" : $"{ModelId.FullName}/{Key}";

    /// <summary>
    ///     Returns the semantic versioned name of the element, e. g. "System/Person-2"
    /// </summary>
    public string SemanticVersionedFullName => IsEmpty ? "" : $"{ModelId.SemanticVersionedFullName}/{Key.SemanticVersionedFullName}";

    /// <summary>
    ///     Returns true if the model id and key is empty
    /// </summary>
    public bool IsEmpty => ModelId.IsEmpty && Key.IsEmpty;

    /// <summary>
    ///     Returns a string representation of the value.
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
        return SemanticVersionedFullName;
    }
}