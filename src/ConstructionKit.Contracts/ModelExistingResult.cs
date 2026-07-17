namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Result of checking if a model exists in a model repository
/// </summary>
public record ModelExistingResult
{
    /// <summary>
    /// Indicates if the model exists
    /// </summary>
    public required bool Exists { get; init; }


    /// <summary>
    /// When <see cref="Exists"/> is <c>true</c>, the actual <see cref="CkModelId"/> of the existing model.
    /// </summary>
    public CkModelId? ModelId { get; init; }

    /// <summary>
    /// Name of the catalog that produced this result. When results of multiple catalogs were
    /// aggregated, the catalog that provided <see cref="ModelId"/>.
    /// </summary>
    public string? CatalogName { get; init; }

    /// <summary>
    /// Timestamp of the last update of the catalog cache the result was answered from,
    /// when the catalog is cache-backed; otherwise null.
    /// </summary>
    public DateTime? CacheUpdatedAt { get; init; }

    /// <summary>
    /// True when at least one queried catalog could not reach its source during the last cache
    /// refresh. A negative <see cref="Exists"/> result with this flag set must not be interpreted
    /// as "model does not exist" — the source of truth was unavailable.
    /// </summary>
    public bool SourceUnreachable { get; init; }
}