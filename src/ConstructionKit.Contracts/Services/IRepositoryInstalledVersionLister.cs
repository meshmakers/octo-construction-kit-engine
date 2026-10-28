namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

/// <summary>
///     A version of a model that a repository holds, with the repository's own state name
///     (a tenant repository: <c>Available</c>, <c>ResolveFailed</c>, <c>Importing</c>).
/// </summary>
/// <param name="ModelId">The versioned model id</param>
/// <param name="State">The state the repository keeps the model in</param>
public sealed record RepositoryModelVersion(CkModelId ModelId, string State);

/// <summary>
///     Optional capability of a model repository (AB#7381): lists <b>every</b> version of a model the repository
///     holds together with its state, not only the versions the dependency resolver considers
///     (<see cref="IRepositoryVersionLister" />). The repository dependency resolver uses it to explain a rejected
///     dependency in terms of the repository: "<c>Industry.Basic-[2.5,3.0)</c>: the tenant has no Available version of
///     Industry.Basic (installed: 2.5.0 ResolveFailed)". A repository that does not implement it is reported through
///     <see cref="IRepositoryVersionLister" /> only.
/// </summary>
public interface IRepositoryInstalledVersionLister
{
    /// <summary>
    ///     Returns all versions of the model <paramref name="ckModelName" /> in the repository, ascending by version.
    ///     Best effort: an unreadable repository must not mask the original error. A wrapper whose underlying
    ///     repository cannot list throws <see cref="NotSupportedException" /> (an empty list means "none installed").
    /// </summary>
    /// <param name="ckModelName">The construction kit model name</param>
    /// <param name="sourceIdentifier">An object that describes the source the repository should search</param>
    Task<IReadOnlyList<RepositoryModelVersion>> ListInstalledVersionsAsync(string ckModelName,
        object? sourceIdentifier = null);
}
