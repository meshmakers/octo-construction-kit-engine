namespace Meshmakers.Octo.ConstructionKit.Contracts;

public readonly struct CkId<TKey> : IComparable<CkId<TKey>>, IEquatable<CkId<TKey>> where TKey : struct, IComparable<TKey>, ICkKey
{
    public bool Equals(CkId<TKey> other)
    {
        return ModelId.Equals(other.ModelId) && Key.Equals(other.Key);
    }

    public int CompareTo(CkId<TKey> other)
    {
        var result = ModelId.CompareTo(other.ModelId);
        if (result != 0)
        {
            return result;
        }

        return Key.CompareTo(other.Key);
    }

    public override bool Equals(object? obj)
    {
        return obj is CkId<TKey> other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(ModelId, Key);
    }

    public CkId(CkModelId modelId, TKey key)
    {
        ModelId = modelId;
        Key = key;
    }

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

    public CkModelId ModelId { get; }

    public TKey Key { get; }

    public string FullName => IsEmpty ? "" : $"{ModelId.FullName}/{Key}";

    public string SemanticVersionedFullName => IsEmpty ? "" : $"{ModelId.SemanticVersionedFullName}/{Key.SemanticVersionedFullName}";

    public bool IsEmpty => ModelId.IsEmpty && Key.IsEmpty;

    public static bool operator ==(CkId<TKey> p1, CkId<TKey> p2)
    {
        return p1.Equals(p2);
    }

    public static bool operator !=(CkId<TKey> p1, CkId<TKey> p2)
    {
        return !p1.Equals(p2);
    }

    public static implicit operator CkId<TKey>(string value)
    {
        return new CkId<TKey>(value);
    }

    public override string ToString()
    {
        return SemanticVersionedFullName;
    }
}