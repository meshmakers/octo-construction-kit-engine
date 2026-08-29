using System.Collections;
using System.Collections.Concurrent;

using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Security;

/// <summary>
///     Loads the per-tenant data-policy table from the System.Identity entities in the tenant
///     database (AB#4972). The table is cached per tenant with a TTL safety net; callers can
///     invalidate a tenant explicitly (e.g. from an identity-entity change stream).
/// </summary>
internal class DataPermissionResolver(
    ILogger<DataPermissionResolver> logger,
    ICkCacheService ckCacheService) : IDataPermissionResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    /// <inheritdoc />
    public async Task<RtDataPolicyTable> GetPolicyTableAsync(IRuntimeRepository runtimeRepository)
    {
        var tenantId = runtimeRepository.TenantId;
        if (_cache.TryGetValue(tenantId, out var entry) && entry.ExpiresAtUtc > DateTime.UtcNow)
        {
            return entry.Table;
        }

        var table = await LoadPolicyTableAsync(runtimeRepository).ConfigureAwait(false);
        _cache[tenantId] = new CacheEntry(table, DateTime.UtcNow.Add(CacheTtl));
        return table;
    }

    /// <inheritdoc />
    public void Invalidate(string tenantId)
    {
        _cache.TryRemove(tenantId, out _);
    }

    private async Task<RtDataPolicyTable> LoadPolicyTableAsync(IRuntimeRepository runtimeRepository)
    {
        var tenantId = runtimeRepository.TenantId;
        var dataPolicyCkTypeId = new RtCkId<CkTypeId>(RtDataPermissionTypes.DataPolicyCkTypeId);
        if (!ckCacheService.TryGetRtCkType(tenantId, dataPolicyCkTypeId, out _))
        {
            // Data-permission model (System.Identity 2.12+) not installed — nothing is protected.
            return RtDataPolicyTable.Empty;
        }

        using var session = await runtimeRepository.GetSessionAsync().ConfigureAwait(false);

        var policies = (await runtimeRepository.GetRtEntitiesByTypeAsync(session, dataPolicyCkTypeId,
            RtEntityQueryOptions.Create()).ConfigureAwait(false)).Items.ToList();
        if (policies.Count == 0)
        {
            return RtDataPolicyTable.Empty;
        }

        var permissionCkTypeId = new RtCkId<CkTypeId>(RtDataPermissionTypes.DataPermissionCkTypeId);
        var roleCkTypeId = new RtCkId<CkTypeId>(RtDataPermissionTypes.RoleCkTypeId);

        var permissions = (await runtimeRepository.GetRtEntitiesByTypeAsync(session, permissionCkTypeId,
            RtEntityQueryOptions.Create()).ConfigureAwait(false)).Items.ToList();
        var roles = (await runtimeRepository.GetRtEntitiesByTypeAsync(session, roleCkTypeId,
            RtEntityQueryOptions.Create()).ConfigureAwait(false)).Items.ToList();

        // DataPolicy --PolicyPermission--> DataPermission
        var policyToPermission = await LoadAssociationMapAsync(runtimeRepository, session,
            policies.Select(p => new RtEntityId(dataPolicyCkTypeId, p.RtId)),
            RtDataPermissionTypes.PolicyPermissionRoleId).ConfigureAwait(false);

        // Role --GrantsPermission--> DataPermission
        var roleToPermissions = await LoadAssociationMapAsync(runtimeRepository, session,
            roles.Select(r => new RtEntityId(roleCkTypeId, r.RtId)),
            RtDataPermissionTypes.GrantsPermissionRoleId).ConfigureAwait(false);

        var permissionIdsByRtId = permissions.ToDictionary(p => p.RtId,
            p => p.GetAttributeValueOrDefault("PermissionId")?.ToString() ?? p.RtId.ToString());

        var roleNamesByPermissionRtId = new Dictionary<OctoObjectId, HashSet<string>>();
        foreach (var role in roles)
        {
            var roleName = role.GetAttributeValueOrDefault("Name")?.ToString();
            if (string.IsNullOrWhiteSpace(roleName) ||
                !roleToPermissions.TryGetValue(role.RtId, out var grantedPermissionRtIds))
            {
                continue;
            }

            foreach (var permissionRtId in grantedPermissionRtIds)
            {
                if (!roleNamesByPermissionRtId.TryGetValue(permissionRtId, out var names))
                {
                    names = new HashSet<string>(StringComparer.Ordinal);
                    roleNamesByPermissionRtId[permissionRtId] = names;
                }

                names.Add(roleName!);
            }
        }

        var rules = new List<RtDataPolicyRule>();
        foreach (var policy in policies)
        {
            var targets = GetStringSet(policy, "TargetCkTypeIds");
            if (targets.Count == 0)
            {
                continue;
            }

            var actions = new HashSet<RtDataAction>();
            foreach (var actionName in GetStringSet(policy, "Actions"))
            {
                if (Enum.TryParse<RtDataAction>(actionName, true, out var action))
                {
                    actions.Add(action);
                }
                else
                {
                    logger.LogWarning("[{TenantId}] DataPolicy '{PolicyRtId}' declares unknown action '{Action}'",
                        tenantId, policy.RtId, actionName);
                }
            }

            var ownedOnly = GetEnumKey(policy, "Scope") == 1;
            var auditOnly = GetEnumKey(policy, "EnforcementMode") == 1;

            var grantedRoleNames = new HashSet<string>(StringComparer.Ordinal);
            var permissionId = policy.RtId.ToString();
            if (policyToPermission.TryGetValue(policy.RtId, out var permissionRtIds) && permissionRtIds.Count > 0)
            {
                var permissionRtId = permissionRtIds.First();
                permissionIdsByRtId.TryGetValue(permissionRtId, out var resolvedPermissionId);
                permissionId = resolvedPermissionId ?? permissionId;
                if (roleNamesByPermissionRtId.TryGetValue(permissionRtId, out var names))
                {
                    grantedRoleNames = names;
                }
            }
            else
            {
                logger.LogWarning(
                    "[{TenantId}] DataPolicy '{PolicyRtId}' has no PolicyPermission association — it protects its targets but grants nobody access",
                    tenantId, policy.RtId);
            }

            rules.Add(new RtDataPolicyRule(permissionId, targets, actions, ownedOnly, auditOnly,
                grantedRoleNames));
        }

        logger.LogDebug("[{TenantId}] Resolved data-policy table: {RuleCount} rules", tenantId, rules.Count);
        return new RtDataPolicyTable(rules);
    }

    private static async Task<Dictionary<OctoObjectId, List<OctoObjectId>>> LoadAssociationMapAsync(
        IRuntimeRepository runtimeRepository, IOctoSession session, IEnumerable<RtEntityId> originIds,
        string associationRoleId)
    {
        var originIdList = originIds.ToList();
        var map = new Dictionary<OctoObjectId, List<OctoObjectId>>();
        if (originIdList.Count == 0)
        {
            return map;
        }

        var options = RtAssociationExtendedQueryOptions.Create(GraphDirections.Any,
            new RtCkId<CkAssociationRoleId>(associationRoleId));
        var resultSets = await runtimeRepository.GetRtAssociationsAsync(session, originIdList, options)
            .ConfigureAwait(false);

        foreach (var keyValuePair in resultSets)
        {
            var originRtId = keyValuePair.Key.RtId;
            foreach (var association in keyValuePair.Value.Items)
            {
                // Direction Any: pick the far end relative to the queried entity.
                var farEnd = association.OriginRtId == originRtId ? association.TargetRtId : association.OriginRtId;
                if (!map.TryGetValue(originRtId, out var list))
                {
                    list = [];
                    map[originRtId] = list;
                }

                list.Add(farEnd);
            }
        }

        return map;
    }

    private static HashSet<string> GetStringSet(RtEntity entity, string attributeName)
    {
        var raw = entity.GetAttributeValueOrDefault(attributeName);
        switch (raw)
        {
            case null:
                return [];
            case string single:
                return string.IsNullOrWhiteSpace(single) ? [] : new HashSet<string>(StringComparer.Ordinal) { single };
            case IEnumerable enumerable:
            {
                var result = new HashSet<string>(StringComparer.Ordinal);
                foreach (var value in enumerable)
                {
                    var s = value?.ToString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        result.Add(s!);
                    }
                }

                return result;
            }
            default:
                return [];
        }
    }

    private static int GetEnumKey(RtEntity entity, string attributeName)
    {
        var raw = entity.GetAttributeValueOrDefault(attributeName);
        return raw is IConvertible convertible ? Convert.ToInt32(convertible) : 0;
    }

    private sealed record CacheEntry(RtDataPolicyTable Table, DateTime ExpiresAtUtc);
}
