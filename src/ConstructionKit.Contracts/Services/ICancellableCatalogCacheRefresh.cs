namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

/// <summary>
///     Refreshes the caches of all catalogs and can be canceled (AB#9388), e.g. when the time limit of the CK MSBuild
///     tasks is reached. Implemented by the engine's catalog service and catalog manager.
/// </summary>
/// <remarks>
///     A separate interface, so <see cref="ICatalogService" /> keeps its members unchanged for its implementations and
///     fakes in other repositories (the contracts also target netstandard2.0, which has no default interface methods).
/// </remarks>
public interface ICancellableCatalogCacheRefresh
{
    /// <summary>
    ///     Refreshes the catalog cache for all catalogs and stops when <paramref name="cancellationToken" /> is canceled.
    /// </summary>
    /// <param name="sourceIdentifier">An object that describes the source which the catalog should search, set it to
    /// null to use default</param>
    /// <param name="forceRefresh">When true, the refresh bypasses the cache freshness window and always contacts the
    /// source</param>
    /// <param name="cancellationToken">Cancels the refresh</param>
    Task RefreshAllCatalogCachesAsync(object? sourceIdentifier, bool forceRefresh, CancellationToken cancellationToken);
}
