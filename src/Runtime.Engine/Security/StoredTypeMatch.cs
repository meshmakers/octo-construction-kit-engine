using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Security;

/// <summary>
///     Whether a stored entity is stored as the type a change addresses, or one of its derived types. Several types
///     can share one collection, so a raw read by id may return an entity of a sibling type; for the write funnel such
///     an entity does not exist under the addressed type. Compared like the read filter, by
///     <c>SemanticVersionedFullName</c>; an entity without a stored type, or an addressed type the CK cache does not
///     know, cannot be judged and counts as matching.
/// </summary>
internal static class StoredTypeMatch
{
    internal static HashSet<string> AcceptedStoredTypeNames(CkTypeGraph addressedTypeGraph) =>
        new(addressedTypeGraph.GetAllDerivedTypes(true).Select(typeId => typeId.ToRtCkId().SemanticVersionedFullName),
            StringComparer.Ordinal);

    internal static bool IsStoredAsAcceptedType(RtEntity storedEntity, HashSet<string> acceptedTypeNames) =>
        storedEntity.CkTypeId == null || acceptedTypeNames.Contains(storedEntity.CkTypeId.SemanticVersionedFullName);

    internal static bool IsStoredAs(RtEntity storedEntity, ICkCacheService ckCacheService, string tenantId,
        RtCkId<CkTypeId> addressedTypeId)
    {
        if (!ckCacheService.TryGetRtCkType(tenantId, addressedTypeId, out var addressedTypeGraph))
        {
            return true;
        }

        return IsStoredAsAcceptedType(storedEntity, AcceptedStoredTypeNames(addressedTypeGraph));
    }
}
