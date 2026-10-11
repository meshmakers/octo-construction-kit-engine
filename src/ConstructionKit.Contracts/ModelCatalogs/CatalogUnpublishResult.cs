namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;

/// <summary>
///     Outcome of <see cref="ICatalog.UnpublishAsync" /> (AB#5433).
/// </summary>
public enum CatalogUnpublishResult
{
    /// <summary>The version was removed from the catalog and its number is burned (recorded as withdrawn).</summary>
    Removed,

    /// <summary>The version was already unpublished earlier; nothing changed.</summary>
    AlreadyWithdrawn
}
