namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Represents a versioned construction kit element id
/// </summary>
/// <typeparam name="TKey"></typeparam>
public readonly struct CkId<TKey> : IComparable<CkId<TKey>>, IEquatable<CkId<TKey>> where TKey : struct, IComparable<TKey>, ICkKey
{
    /// <summary>
    /// Creates a new <see cref="CkId{TKey}"/> from the given <paramref name="modelId"/> and <paramref name="key"/>.
    /// </summary>
    /// <param name="modelId"></param>
    /// <param name="key"></param>
    public CkId(CkModelId modelId, TKey key)
    {
        ModelId = modelId;
        Key = key;
    }

    /// <summary>
    /// Creates a new <see cref="CkId{TKey}"/> from the given <paramref name="ckId"/>.
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

        ModelId = ckId.Substring(0, modelIndex);

        var typeId = ckId.Substring(modelIndex + 1);
        if (string.IsNullOrWhiteSpace(typeId))
        {
            throw new ArgumentOutOfRangeException(nameof(ckId), ckId, $"'{nameof(ckId)}' must contain a key");
        }

        var value = Activator.CreateInstance(typeof(TKey), new object?[] { typeId });
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
    /// Creates a new <see cref="CkId{TKey}"/> from the given <paramref name="value"/>.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static implicit operator CkId<TKey>(string value)
    {
        return new CkId<TKey>(value);
    }

    /// <inheritdoc />
    public bool Equals(CkId<TKey> other)
    {
        return ModelId.Equals(other.ModelId) && Key.Equals(other.Key);
    }

    /// <inheritdoc />
    public int CompareTo(CkId<TKey> other)
    {
        var result = ModelId.CompareTo(other.ModelId);
        if (result != 0)
        {
            return result;
        }

        return Key.CompareTo(other.Key);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is CkId<TKey> other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(ModelId, Key);
    }


    /// <summary>
    /// Returns the versioned model id, e. g. "System-1.0.0"
    /// </summary>
    public CkModelId ModelId { get; }

    /// <summary>
    /// Returns the element key
    /// </summary>
    public TKey Key { get; }

    /// <summary>
    /// Returns the full name of the element, e. g. "System-1.0.0/Person-1.0.0"
    /// </summary>
    public string FullName => IsEmpty ? "" : $"{ModelId.FullName}/{Key}";

    /// <summary>
    /// Returns the semantic versioned name of the element, e. g. "System/Person-2"
    /// </summary>
    public string SemanticVersionedFullName => IsEmpty ? "" : $"{ModelId.SemanticVersionedFullName}/{Key.SemanticVersionedFullName}";

    /// <summary>
    /// Returns true if the model id and key is empty
    /// </summary>
    public bool IsEmpty => ModelId.IsEmpty && Key.IsEmpty;

    /// <summary>
    /// Compares two <see cref="CkId{TKey}"/> for equality
    /// </summary>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <returns></returns>
    public static bool operator ==(CkId<TKey> p1, CkId<TKey> p2)
    {
        return p1.Equals(p2);
    }

    /// <summary>
    /// Compares two <see cref="CkId{TKey}"/> for inequality
    /// </summary>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <returns></returns>
    public static bool operator !=(CkId<TKey> p1, CkId<TKey> p2)
    {
        return !p1.Equals(p2);
    }

    /// <summary>
    /// Returns a string representation of the value.
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
        return SemanticVersionedFullName;
    }
}