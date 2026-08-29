using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Contracts.DataPermissions;

/// <summary>
///     A caller-specific, tenant-global read filter derived from the policy table (AB#4973). All type
///     ids are runtime full names ("Model/Type"), expanded to concrete derived types, so the filter
///     applies uniformly to any entity row — root queries, association stages and navigation lookups:
///     a row passes when its type is unprotected, allowed, or owned-only and created by the caller.
/// </summary>
/// <param name="ProtectedCkTypeIds">All concrete type ids any enforcing policy protects</param>
/// <param name="AllowedCkTypeIds">Protected types the caller may fully read</param>
/// <param name="OwnedOnlyCkTypeIds">Protected types the caller may read own entities of</param>
/// <param name="SubjectId">Subject id for the owned-only predicate</param>
/// <param name="AuditDeniedCkTypeIds">
///     Types where only AuditOnly policies would restrict the caller — access stays open, but reads
///     should be logged as would-be violations
/// </param>
public sealed record RtDataSecurityQueryFilter(
    IReadOnlyCollection<string> ProtectedCkTypeIds,
    IReadOnlyCollection<string> AllowedCkTypeIds,
    IReadOnlyCollection<string> OwnedOnlyCkTypeIds,
    string? SubjectId,
    IReadOnlyCollection<string> AuditDeniedCkTypeIds)
{
    /// <summary>
    ///     True when the filter restricts anything (at least one enforcing policy exists).
    /// </summary>
    public bool HasEnforcement => ProtectedCkTypeIds.Count > 0;

    /// <summary>
    ///     Stable string for the query-result-cache key. Callers with the same allowed/owned type sets
    ///     share cache entries; the subject only participates when an owned-only predicate is present.
    /// </summary>
    public string ComputeCacheSegment()
    {
        if (!HasEnforcement)
        {
            return string.Empty;
        }

        var allowed = string.Join(",", AllowedCkTypeIds.OrderBy(x => x, StringComparer.Ordinal));
        var owned = string.Join(",", OwnedOnlyCkTypeIds.OrderBy(x => x, StringComparer.Ordinal));
        var prot = string.Join(",", ProtectedCkTypeIds.OrderBy(x => x, StringComparer.Ordinal));
        var subject = OwnedOnlyCkTypeIds.Count > 0 ? SubjectId ?? string.Empty : string.Empty;
        return $"p:{prot}|a:{allowed}|o:{owned}|s:{subject}";
    }
}

/// <summary>
///     Builds the caller-specific read filter for a tenant. Returns null for system contexts and for
///     tenants without policies — the dormant guarantee: query shapes and cache keys stay byte-identical
///     to the pre-permission behavior in that case.
/// </summary>
public interface IDataSecurityFilterFactory
{
    /// <summary>
    ///     Creates the read filter for the caller, or null when nothing must be filtered or audited.
    /// </summary>
    /// <param name="runtimeRepository">Repository of the tenant</param>
    /// <param name="securityContext">The caller</param>
    Task<RtDataSecurityQueryFilter?> CreateAsync(IRuntimeRepository runtimeRepository,
        RtSecurityContext securityContext);
}

/// <summary>
///     Shared helpers for resolving CK type hierarchies against the data-permission model.
/// </summary>
public static class RtDataPermissionCkTypeHelper
{
    /// <summary>
    ///     Returns the runtime full names of the type and all its base types (policies targeting a base
    ///     or collection-root type protect derived types). Falls back to the id itself when the type is
    ///     unknown in the tenant's CK cache.
    /// </summary>
    /// <param name="ckCacheService">The CK cache</param>
    /// <param name="tenantId">The tenant id</param>
    /// <param name="rtCkTypeId">The runtime type id</param>
    public static IReadOnlyList<string> GetSelfAndBaseFullNames(ICkCacheService ckCacheService, string tenantId,
        RtCkId<CkTypeId> rtCkTypeId)
    {
        var names = new List<string> { rtCkTypeId.FullName };
        if (!ckCacheService.TryGetRtCkType(tenantId, rtCkTypeId, out var graph) || graph == null)
        {
            return names;
        }

        var baseCkTypeId = graph.DerivedFromCkTypeId;
        while (baseCkTypeId != null)
        {
            names.Add(baseCkTypeId.ToRtCkId().FullName);
            if (!ckCacheService.TryGetCkType(tenantId, baseCkTypeId, out var baseGraph) || baseGraph == null)
            {
                break;
            }

            baseCkTypeId = baseGraph.DerivedFromCkTypeId;
        }

        return names;
    }
}
