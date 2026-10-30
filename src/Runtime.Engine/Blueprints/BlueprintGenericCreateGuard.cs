using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Engine.Exchange;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
///     AB#7121: a blueprint (seed data on install, the diff of a blueprint update, the Add step of a blueprint
///     migration) never runs a CK method, so it must not create a NEW entity of a type whose static method declares
///     <c>replacesGenericCreate</c> (AB#7024) — the seed would skip what the method guarantees (certificate,
///     provisioning). The preflight reads the stored state like <c>ImportBlueprintLockEnforcer.PreflightAsync</c>
///     and runs before the import, so a refusal writes nothing. An entity whose rtId already exists in the tenant is
///     allowed (it is upserted as before); only new entities are refused. Types without such a method cost no read.
///     AB#7767 extends the same preflight: the entities must not carry a value for an attribute that only a CK method
///     may write (<c>access: Hidden</c> or <c>MethodOnly</c>, message 139), which reads no stored state.
/// </summary>
internal static class BlueprintGenericCreateGuard
{
    /// <summary>The message number of the refusal (<c>BlueprintSeedCreateReplacedByMethod</c>).</summary>
    internal const int MessageNumber = 136;

    /// <summary>
    ///     Returns one error message per value for a Hidden or MethodOnly attribute (AB#7767), then one per entity that
    ///     would be created new although a method replaces the generic create of its type (own or inherited). Empty
    ///     when the blueprint does neither.
    /// </summary>
    internal static async Task<IReadOnlyList<OperationMessage>> FindRefusalsAsync(
        ICkCacheService ckCacheService, IRuntimeRepository repository, IEnumerable<RtEntityTcDto> entities)
    {
        var entityList = entities as IReadOnlyCollection<RtEntityTcDto> ?? entities.ToList();

        var restricted = RtAttributeAccessCheck.TenantHasRestrictedAttribute(ckCacheService, repository.TenantId)
            ? RtAttributeAccessCheck.Find(ckCacheService, repository.TenantId, entityList)
            : [];
        if (restricted.Count > 0)
        {
            return restricted.Select(RtAttributeAccessCheck.ToMessage).ToList();
        }

        var offenders = await GenericCreateNewEntityCheck.FindAsync(ckCacheService, repository, null,
            entityList.Where(e => e.CkTypeId != null).Select(e => (e.CkTypeId, e.RtId))).ConfigureAwait(false);

        return offenders.Select(o => MessageCodes.BlueprintSeedCreateReplacedByMethod(
                $"{o.CkTypeId.SemanticVersionedFullName}@{o.RtId}", o.RtId, o.CkTypeId.SemanticVersionedFullName,
                o.Method.QualifiedMethodId, GenericCreateReplacement.ReasonToken))
            .ToList();
    }

    /// <summary>The refusal as a single error string for the string-based result channels.</summary>
    internal static string ToErrorString(OperationMessage message) =>
        $"OCTO-CK{message.MessageNumber}: {message.MessageText}";
}
