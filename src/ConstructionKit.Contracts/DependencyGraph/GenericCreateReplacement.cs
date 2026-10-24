#if !NETSTANDARD2_0
using System.Diagnostics.CodeAnalysis;
#endif
using Meshmakers.Octo.ConstructionKit.Contracts.Services;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
///     AB#7121: the lookup "which method replaces the generic create of type X". A static method with
///     <c>replacesGenericCreate: true</c> (AB#7024) makes the method the only way to create an entity of the type that
///     declares it and of every type derived from it; the methods of a type graph include the inherited ones, so the
///     lookup is the same for the declaring type and for a derived concrete type. Shared by every writer that must
///     refuse a generic create (asset repository, MCP, adapter pre-check, blueprint apply) so none keeps its own copy.
/// </summary>
public static class GenericCreateReplacement
{
    /// <summary>
    ///     The stable, language-neutral reason token that callers put into a refusal (GraphQL code, blueprint apply
    ///     message) when a generic create is refused because a method replaces it.
    /// </summary>
    public const string ReasonToken = "GENERIC_CREATE_REPLACED_BY_METHOD";

    /// <summary>
    ///     The method that replaces the generic create of the type, or null when the generic create is allowed. When a
    ///     (malformed) graph holds more than one flagged method, the one with the smallest qualified id is returned,
    ///     so the answer is deterministic.
    /// </summary>
    /// <param name="typeGraph">The type graph (own and inherited methods)</param>
    public static CkMethodGraph? FindReplacingMethod(CkTypeGraph typeGraph)
    {
        if (typeGraph == null)
        {
            throw new ArgumentNullException(nameof(typeGraph));
        }

        return typeGraph.AllMethods.Values
            .Where(m => m.ReplacesGenericCreate)
            .OrderBy(m => m.QualifiedMethodId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    ///     Looks up the type by its version-less runtime id in the tenant's CK cache and returns the method that
    ///     replaces its generic create. A type that is not in the cache yields false (the create fails on its own).
    /// </summary>
    /// <param name="ckCacheService">The CK cache</param>
    /// <param name="tenantId">The tenant</param>
    /// <param name="rtCkTypeId">The type to be created</param>
    /// <param name="method">The replacing method, when the result is true</param>
    /// <returns>True when a method replaces the generic create of the type</returns>
#if NETSTANDARD2_0
    public static bool TryGetReplacingMethod(ICkCacheService ckCacheService, string tenantId,
        RtCkId<CkTypeId> rtCkTypeId, out CkMethodGraph? method)
#else
    public static bool TryGetReplacingMethod(ICkCacheService ckCacheService, string tenantId,
        RtCkId<CkTypeId> rtCkTypeId, [NotNullWhen(true)] out CkMethodGraph? method)
#endif
    {
        if (ckCacheService == null)
        {
            throw new ArgumentNullException(nameof(ckCacheService));
        }

        method = null;
        if (!ckCacheService.TryGetRtCkType(tenantId, rtCkTypeId, out var typeGraph) || typeGraph == null)
        {
            return false;
        }

        method = FindReplacingMethod(typeGraph);
        return method != null;
    }
}
