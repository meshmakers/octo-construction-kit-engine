namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

/// <summary>
///     Optional capability of a model repository (AB#4468): lists the versions of a model that the dependency
///     resolver can see, so that a rejected dependency is reported with the range <b>and</b> the versions that are
///     installed ("<c>System-[2.5,3.0)</c> does not match any visible version of System (2.4.9)"). Implemented next to
///     <see cref="IRepositoryManagementService" /> / <c>IModelRepository</c>; a repository that does not implement it
///     is reported without versions.
/// </summary>
public interface IRepositoryVersionLister
{
    /// <summary>
    ///     Returns the versions of the model <paramref name="ckModelName" /> that <c>IsExistingAsync</c> considers,
    ///     in ascending order. Best effort: an unreadable repository must not mask the original error.
    /// </summary>
    /// <param name="ckModelName">The construction kit model name</param>
    /// <param name="sourceIdentifier">An object that describes the source the repository should search</param>
    Task<IReadOnlyList<CkModelId>> ListVersionsAsync(string ckModelName, object? sourceIdentifier = null);
}
