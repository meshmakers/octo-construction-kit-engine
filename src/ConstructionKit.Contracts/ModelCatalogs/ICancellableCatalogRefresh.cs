namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;

/// <summary>
///     A catalog whose refresh can be canceled (AB#9388), e.g. when the time limit of the CK MSBuild tasks is reached.
/// </summary>
/// <remarks>
///     A separate interface, so <see cref="ICatalog" /> and its implementations outside this repository keep their
///     members unchanged (the contracts also target netstandard2.0, which has no default interface methods).
/// </remarks>
public interface ICancellableCatalogRefresh
{
    /// <summary>
    ///     Refreshes the catalog like <see cref="ICatalog.RefreshCatalogAsync" /> and stops when
    ///     <paramref name="cancellationToken" /> is canceled.
    /// </summary>
    /// <param name="sourceIdentifier">An object, which describes the source which the catalog should search,
    /// set it to null to use default</param>
    /// <param name="forceRefresh">When true, the refresh bypasses the cache freshness window and always
    /// contacts the source</param>
    /// <param name="cancellationToken">Cancels the refresh</param>
    Task RefreshCatalogAsync(object? sourceIdentifier, bool forceRefresh, CancellationToken cancellationToken);
}
