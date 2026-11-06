using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     Lookups on <see cref="IRuntimeRepository" /> for callers that load an entity by its runtime id in order to
///     <b>use</b> it (execute a persisted query, run a configuration) and must not act on a deleted entity.
/// </summary>
/// <remarks>
///     AB#8355: the default delete (<see cref="DeleteStrategies" />) archives an entity - the document stays in its
///     collection with <see cref="RtState.Archived" />. Queries over a collection filter archived entities out, but
///     <see cref="IRuntimeRepository.GetRtEntityByRtIdAsync{TEntity}" /> returns the document whatever its state, and
///     some callers rely on that (restore, history, import). A caller that only checked for <c>null</c> kept executing a
///     deleted persisted query for everyone who knew its rtId. The lookups here treat an archived entity as not found;
///     the semantics of <see cref="IRuntimeRepository.GetRtEntityByRtIdAsync{TEntity}" /> stay unchanged.
/// </remarks>
public static class RuntimeRepositoryExtensions
{
    /// <summary>
    ///     Gets an entity by its runtime id, or <c>null</c> when no entity with the id exists or it has been deleted
    ///     (<see cref="RtState.Archived" />).
    /// </summary>
    /// <param name="repository">The runtime repository</param>
    /// <param name="session">The session object</param>
    /// <param name="rtId">The object id</param>
    /// <typeparam name="TEntity">The type of entity derived from <see cref="RtEntity" /></typeparam>
    /// <returns>The entity, or <c>null</c> when it does not exist or is archived</returns>
    public static async Task<TEntity?> GetActiveRtEntityByRtIdAsync<TEntity>(this IRuntimeRepository repository,
        IOctoSession session, OctoObjectId rtId)
        where TEntity : RtEntity, new()
    {
        ArgumentNullException.ThrowIfNull(repository);

        var entity = await repository.GetRtEntityByRtIdAsync<TEntity>(session, rtId).ConfigureAwait(false);
        return IsActive(entity) ? entity : null;
    }

    /// <summary>
    ///     Gets an entity by its runtime entity id, or <c>null</c> when no entity with the id exists or it has been
    ///     deleted (<see cref="RtState.Archived" />).
    /// </summary>
    /// <param name="repository">The runtime repository</param>
    /// <param name="session">The session object</param>
    /// <param name="rtEntityId">The runtime entity id</param>
    /// <returns>The entity, or <c>null</c> when it does not exist or is archived</returns>
    public static async Task<RtEntity?> GetActiveRtEntityByRtIdAsync(this IRuntimeRepository repository,
        IOctoSession session, RtEntityId rtEntityId)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var entity = await repository.GetRtEntityByRtIdAsync(session, rtEntityId).ConfigureAwait(false);
        return IsActive(entity) ? entity : null;
    }

    private static bool IsActive(RtEntity? entity) => entity is not null && entity.RtState != RtState.Archived;
}
