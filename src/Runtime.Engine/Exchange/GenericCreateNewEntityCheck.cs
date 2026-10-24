using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

namespace Meshmakers.Octo.Runtime.Engine.Exchange;

/// <summary>
///     A new entity (its rtId is not stored in the tenant yet) of a type whose static method declares
///     <c>replacesGenericCreate</c> (own or inherited), and the method that replaces the generic create.
/// </summary>
/// <param name="CkTypeId">The runtime CK type id of the entity as written in the file</param>
/// <param name="RtId">The rtId of the entity</param>
/// <param name="Method">The replacing method</param>
internal readonly record struct GenericCreateOffender(RtCkId<CkTypeId> CkTypeId, OctoObjectId RtId, CkMethodGraph Method);

/// <summary>
///     The one implementation of "which of these entities would be created new although a method replaces the
///     generic create of its type". Used by the blueprint writers (seed data, update diff, migration Add step) and by
///     the caller path of the RT import, so the two paths cannot drift. It reads the stored state, never writes, and
///     costs no read for types without such a method. An entity whose rtId exists in the tenant is not an offender:
///     updating an existing entity is allowed (as in the GraphQL update).
/// </summary>
internal static class GenericCreateNewEntityCheck
{
    /// <summary>
    ///     True when at least one type of the tenant's CK cache has a method that replaces the generic create. Lets a
    ///     caller skip work (for example the extra pass over a large import file) on tenants without such a type.
    /// </summary>
    internal static bool TenantHasReplacingMethod(ICkCacheService ckCacheService, string tenantId)
    {
        return ckCacheService.GetCkTypes(tenantId).Any(t => GenericCreateReplacement.FindReplacingMethod(t) != null);
    }

    /// <summary>
    ///     Returns one offender per entity that would be created new although a method replaces the generic create of
    ///     its type. Empty when there is none. Offenders keep the order of the input.
    /// </summary>
    /// <param name="ckCacheService">The CK cache</param>
    /// <param name="repository">The tenant repository</param>
    /// <param name="session">A session to read with; null opens one on first need</param>
    /// <param name="entities">Type and rtId of the entities to be written</param>
    internal static async Task<IReadOnlyList<GenericCreateOffender>> FindAsync(ICkCacheService ckCacheService,
        IRuntimeRepository repository, IOctoSession? session,
        IEnumerable<(RtCkId<CkTypeId> CkTypeId, OctoObjectId RtId)> entities)
    {
        var offenders = new List<GenericCreateOffender>();

        foreach (var typeGroup in entities.Where(e => e.CkTypeId != null).GroupBy(e => e.CkTypeId))
        {
            if (!GenericCreateReplacement.TryGetReplacingMethod(ckCacheService, repository.TenantId, typeGroup.Key,
                    out var method) || method == null)
            {
                continue;
            }

            var candidates = typeGroup.ToList();
            var rtIds = candidates.Select(e => e.RtId).Where(id => !id.Equals(OctoObjectId.Empty)).Distinct()
                .ToList();
            var stored = new HashSet<OctoObjectId>();
            if (rtIds.Count > 0)
            {
                session ??= await repository.GetSessionAsync().ConfigureAwait(false);
                var result = await repository.GetRtEntitiesByIdAsync(session, typeGroup.Key, rtIds,
                    RtEntityQueryOptions.Create(), take: rtIds.Count).ConfigureAwait(false);
                stored.UnionWith(result.Items.Select(e => e.RtId));
            }

            offenders.AddRange(candidates.Where(e => !stored.Contains(e.RtId))
                .Select(e => new GenericCreateOffender(typeGroup.Key, e.RtId, method)));
        }

        return offenders;
    }
}
