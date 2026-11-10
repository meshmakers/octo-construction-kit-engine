using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Security;

/// <summary>
///     One write of an entity (or of an association edge at one of its ends) that the type write restriction judges.
/// </summary>
/// <param name="CkTypeId">The runtime CK type of the entity</param>
/// <param name="RtId">The rtId of the entity, if known</param>
/// <param name="Operation">What is written: create, update, replace, delete, create or replace, association change</param>
internal readonly record struct TypeWriteTarget(RtCkId<CkTypeId> CkTypeId, OctoObjectId? RtId, string Operation);

/// <summary>
///     AB#8574: enforces the model-declared write restriction of a CK type (<c>writeAccess</c>, see
///     <see cref="CkTypeWriteAccessDto" />) for every non-system write. A write of an entity is allowed when every
///     <c>writeAccess</c> block along the type's inheritance chain (the type itself and each base type) admits the caller:
///     its principal kind is listed in <c>principals</c>, or its in-process write channel is listed in <c>channels</c>. A
///     caller of unknown principal kind is admitted only through a channel (fail closed). An association change counts
///     as a write of the entities at both ends. Types without a block (and their writes) are not affected, and system
///     sessions are never checked here (the callers skip them): pipelines, blueprint seeds and migrations keep writing.
///     A pure read of the CK cache, so callers run it before the first write; a refusal rejects the whole change set.
/// </summary>
internal static class TypeWriteAccessGuard
{
    internal const string OperationCreate = "create";
    internal const string OperationUpdate = "update";
    internal const string OperationReplace = "replace";
    internal const string OperationDelete = "delete";
    internal const string OperationCreateOrReplace = "create or replace";
    internal const string OperationAssociation = "association change";

    /// <summary>
    ///     True when at least one type of the tenant's CK cache declares a <c>writeAccess</c> block. Lets a caller skip
    ///     work (for example the extra pass over a large import file) on tenants without restricted types.
    /// </summary>
    internal static bool TenantHasRestrictedType(ICkCacheService ckCacheService, string tenantId) =>
        ckCacheService.GetCkTypes(tenantId).Any(t => t.WriteAccess != null);

    /// <summary>
    ///     Returns one refusal (message 144) per write the caller may not make, in the order of the input; empty when
    ///     all are allowed. Entities of a type unknown to the CK cache are skipped (the write path reports them).
    /// </summary>
    internal static IReadOnlyList<OperationMessage> Find(ICkCacheService ckCacheService, string tenantId,
        RtPrincipalKind principalKind, string? writeChannel, IEnumerable<TypeWriteTarget> targets)
    {
        var refusals = new List<OperationMessage>();
        var refusingBlockByType = new Dictionary<RtCkId<CkTypeId>, (CkTypeGraph Type, CkTypeWriteAccessDto Block)?>();
        foreach (var target in targets)
        {
            if (!refusingBlockByType.TryGetValue(target.CkTypeId, out var refusing))
            {
                refusing = FindRefusingBlock(ckCacheService, tenantId, target.CkTypeId, principalKind, writeChannel);
                refusingBlockByType[target.CkTypeId] = refusing;
            }

            if (refusing is { } r)
            {
                refusals.Add(ToMessage(target, r.Type, r.Block, principalKind, writeChannel));
            }
        }

        return refusals;
    }

    /// <summary>
    ///     Adds the refusals of the repository write funnel (entity changes and association changes) to
    ///     <paramref name="operationResult" />.
    /// </summary>
    internal static void Check(ICkCacheService ckCacheService, string tenantId, RtSecurityContext securityContext,
        IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList, OperationResult operationResult)
    {
        foreach (var refusal in Find(ckCacheService, tenantId, securityContext.PrincipalKind,
                     securityContext.WriteChannel, TargetsOf(entityUpdateInfoList, associationUpdateInfoList)))
        {
            operationResult.AddMessage(refusal);
        }
    }

    /// <summary>The targets of a change set of the repository write funnel.</summary>
    internal static IEnumerable<TypeWriteTarget> TargetsOf(IReadOnlyList<IEntityUpdateInfo<RtEntity>> entityUpdateInfoList,
        IReadOnlyList<AssociationUpdateInfo> associationUpdateInfoList)
    {
        foreach (var item in entityUpdateInfoList)
        {
            yield return new TypeWriteTarget(item.CkTypeId, item.RtId ?? item.RtEntity?.RtId, item.ModOption switch
            {
                EntityModOptions.Insert => OperationCreate,
                EntityModOptions.Delete => OperationDelete,
                EntityModOptions.Replace => OperationReplace,
                _ => OperationUpdate
            });
        }

        foreach (var association in associationUpdateInfoList)
        {
            yield return new TypeWriteTarget(association.Origin.CkTypeId, association.Origin.RtId, OperationAssociation);
            yield return new TypeWriteTarget(association.Target.CkTypeId, association.Target.RtId, OperationAssociation);
        }
    }

    /// <summary>The targets of a bulk import of entities (insert or upsert).</summary>
    internal static IEnumerable<TypeWriteTarget> TargetsOf(IEnumerable<RtEntity> entities) =>
        entities.Where(e => e.CkTypeId != null)
            .Select(e => new TypeWriteTarget(e.CkTypeId!, e.RtId, OperationCreateOrReplace));

    /// <summary>The targets of a bulk import of association edges (both ends).</summary>
    internal static IEnumerable<TypeWriteTarget> TargetsOf(IEnumerable<RtAssociation> associations) =>
        associations.SelectMany(a => new[]
        {
            new TypeWriteTarget(a.OriginCkTypeId, a.OriginRtId, OperationAssociation),
            new TypeWriteTarget(a.TargetCkTypeId, a.TargetRtId, OperationAssociation)
        });

    /// <summary>
    ///     The targets of the entities of an RT import file: each entity (insert or upsert) and both ends of each of its
    ///     association edges.
    /// </summary>
    internal static IEnumerable<TypeWriteTarget> TargetsOf(IEnumerable<RtEntityTcDto> entities)
    {
        foreach (var entity in entities)
        {
            if (entity.CkTypeId == null)
            {
                continue;
            }

            yield return new TypeWriteTarget(entity.CkTypeId, entity.RtId, OperationCreateOrReplace);
            foreach (var association in entity.Associations ?? [])
            {
                yield return new TypeWriteTarget(entity.CkTypeId, entity.RtId, OperationAssociation);
                if (association.TargetCkTypeId != null)
                {
                    yield return new TypeWriteTarget(association.TargetCkTypeId, association.TargetRtId,
                        OperationAssociation);
                }
            }
        }
    }

    /// <summary>
    ///     True when <paramref name="block" /> admits a caller of <paramref name="principalKind" /> on
    ///     <paramref name="writeChannel" />: a listed channel, or a listed principal kind other than unknown.
    /// </summary>
    internal static bool Admits(CkTypeWriteAccessDto block, RtPrincipalKind principalKind, string? writeChannel)
    {
        if (writeChannel != null && (block.Channels ?? []).Contains(writeChannel, StringComparer.Ordinal))
        {
            return true;
        }

        var principal = principalKind switch
        {
            RtPrincipalKind.Interactive => CkWritePrincipals.Interactive,
            RtPrincipalKind.Client => CkWritePrincipals.Client,
            _ => null
        };
        return principal != null && (block.Principals ?? []).Contains(principal, StringComparer.Ordinal);
    }

    /// <summary>
    ///     The first block along the inheritance chain (own type first, then the base types from the nearest) that does
    ///     not admit the caller; <c>null</c> when every block admits it or the chain declares none.
    /// </summary>
    private static (CkTypeGraph Type, CkTypeWriteAccessDto Block)? FindRefusingBlock(ICkCacheService ckCacheService,
        string tenantId, RtCkId<CkTypeId> ckTypeId, RtPrincipalKind principalKind, string? writeChannel)
    {
        if (!ckCacheService.TryGetRtCkType(tenantId, ckTypeId, out var typeGraph) || typeGraph == null)
        {
            return null;
        }

        if (typeGraph.WriteAccess is { } own && !Admits(own, principalKind, writeChannel))
        {
            return (typeGraph, own);
        }

        foreach (var inheritance in typeGraph.BaseTypes.OrderBy(b => b.BaseTypeDepthIndex))
        {
            if (ckCacheService.TryGetCkType(tenantId, inheritance.BaseCkTypeId, out var baseGraph) &&
                baseGraph?.WriteAccess is { } inherited && !Admits(inherited, principalKind, writeChannel))
            {
                return (baseGraph, inherited);
            }
        }

        return null;
    }

    private static OperationMessage ToMessage(TypeWriteTarget target, CkTypeGraph restrictingType,
        CkTypeWriteAccessDto block, RtPrincipalKind principalKind, string? writeChannel)
    {
        var ckTypeId = target.CkTypeId.SemanticVersionedFullName;
        var principals = block.Principals is { Count: > 0 } ? string.Join(", ", block.Principals) : "none";
        var channels = block.Channels is { Count: > 0 } ? string.Join(", ", block.Channels) : "none";
        return MessageCodes.RtTypeWriteAccessDenied($"{ckTypeId}@{target.RtId}", target.Operation, target.RtId?.ToString() ?? "new",
            ckTypeId, restrictingType.CkTypeId.ToRtCkId().SemanticVersionedFullName, principalKind, writeChannel ?? "none",
            principals, channels);
    }
}
