using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
///     AB#7121: a blueprint (seed data on install, the diff of a blueprint update, the Add step of a blueprint
///     migration) never runs a CK method, so it must not create a NEW entity of a type whose static method declares
///     <c>replacesGenericCreate</c> (AB#7024) — the seed would skip what the method guarantees (certificate,
///     provisioning). The preflight reads the stored state like <c>ImportBlueprintLockEnforcer.PreflightAsync</c>
///     and runs before the import, so a refusal writes nothing. An entity whose rtId already exists in the tenant is
///     allowed (it is upserted as before); only new entities are refused. Types without such a method cost no read.
/// </summary>
internal static class BlueprintGenericCreateGuard
{
    /// <summary>The message number of the refusal (<c>BlueprintSeedCreateReplacedByMethod</c>).</summary>
    internal const int MessageNumber = 136;

    /// <summary>
    ///     Returns one error message per entity that would be created new although a method replaces the generic
    ///     create of its type (own or inherited). Empty when the blueprint creates no such entity.
    /// </summary>
    internal static async Task<IReadOnlyList<OperationMessage>> FindRefusalsAsync(
        ICkCacheService ckCacheService, IRuntimeRepository repository, IEnumerable<RtEntityTcDto> entities)
    {
        var refusals = new List<OperationMessage>();
        IOctoSession? session = null;

        foreach (var typeGroup in entities.Where(e => e.CkTypeId != null).GroupBy(e => e.CkTypeId))
        {
            if (!GenericCreateReplacement.TryGetReplacingMethod(ckCacheService, repository.TenantId, typeGroup.Key,
                    out var method))
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

            foreach (var entity in candidates.Where(e => !stored.Contains(e.RtId)))
            {
                refusals.Add(MessageCodes.BlueprintSeedCreateReplacedByMethod(
                    $"{typeGroup.Key.SemanticVersionedFullName}@{entity.RtId}", entity.RtId,
                    typeGroup.Key.SemanticVersionedFullName, method.QualifiedMethodId,
                    GenericCreateReplacement.ReasonToken));
            }
        }

        return refusals;
    }

    /// <summary>The refusal as a single error string for the string-based result channels.</summary>
    internal static string ToErrorString(OperationMessage message) =>
        $"OCTO-CK{message.MessageNumber}: {message.MessageText}";
}
