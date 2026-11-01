#if !NETSTANDARD2_0
using System.Diagnostics.CodeAnalysis;
#endif
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
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
    ///     AB#7777: the static method <c>User.CreateUser-1</c> of System.Identity. It replaces the generic create of
    ///     <c>System.Identity/User</c> at runtime (see <see cref="FindReplacingMethod" />), because the model flag is a
    ///     Major of System.Identity (row M16) and waits for the next major (AB#8053).
    /// </summary>
    public const string IdentityUserCreateMethodId = "CreateUser-1";

    /// <summary>
    ///     The method that replaces the generic create of the type, or null when the generic create is allowed. When a
    ///     (malformed) graph holds more than one flagged method, the one with the smallest qualified id is returned,
    ///     so the answer is deterministic.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Runtime replacement of <c>System.Identity/User</c> (AB#7777, interim).</b> A user is created only through
    ///         the identity service (its REST API or the CK method <c>User.CreateUser-1</c>), which runs the user manager:
    ///         normalised name and e-mail, password policy, security stamp. The model cannot say so in System.Identity 2.x:
    ///         <c>replacesGenericCreate</c> on the existing type is a Major (row M16 of the SemVer rules). So when the type is
    ///         <c>System.Identity/User</c>, no method of it carries the flag and the tenant's model declares the static
    ///         <c>CreateUser-1</c>, that method is returned as if it carried the flag. Every writer that asks this helper
    ///         (asset repository generic and typed create, MCP, the mesh-adapter insert pre-check, the caller path of
    ///         <c>ImportRt</c>, blueprint seed data, update and migration Add step) then refuses a NEW user with
    ///         <see cref="ReasonToken" /> and the method id; an existing user (its rtId is stored) is updated as before.
    ///         The identity service writes users through its own repository and is not affected. A tenant whose
    ///         System.Identity predates the method (below 2.29) keeps the generic create. The returned method's
    ///         <see cref="CkMethodGraph.ReplacesGenericCreate" /> stays <c>false</c>: the CK meta reports the model as it is.
    ///         Remove this rule when the next System.Identity major sets the flag in the model (AB#8053).
    ///     </para>
    /// </remarks>
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
            .FirstOrDefault() ?? FindRuntimeReplacement(typeGraph);
    }

    /// <summary>The runtime replacement of <c>System.Identity/User</c> (AB#7777), see <see cref="FindReplacingMethod" />.</summary>
    private static CkMethodGraph? FindRuntimeReplacement(CkTypeGraph typeGraph)
    {
        if (!IdentityOwnedWrites.IsUserType(typeGraph.CkTypeId.ToRtCkId()) ||
            !typeGraph.AllMethods.TryGetValue(IdentityUserCreateMethodId, out var method) ||
            method.Definition.Kind != CkMethodKindDto.Static)
        {
            return null;
        }

        return method;
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
