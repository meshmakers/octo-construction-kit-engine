using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Security;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Exchange;

/// <summary>
///     Blueprint-lock protection of a user-initiated RT import (AB#6392, extends AB#6384 to the ImportRt route).
///     Reuses the classification (<see cref="RtDataAccessEvaluator.ClassifyBlueprintLockProtection" />, the
///     <c>ProtectBlueprintLocked</c> opt-in with Enforce / AuditOnly), the lock predicate and the message of the
///     write guard (<see cref="DataPermissionWriteGuard" />) — there is no second comparison. The import itself
///     keeps its system session; this class only adds
///     <list type="bullet">
///         <item>
///             a <b>preflight</b> over the whole file before any write: one batched read of the stored entities
///             per opted-in type; an entity that exists with <c>RtBlueprintLocked = true</c> fails the import
///             atomically (every offender listed), an AuditOnly policy writes and publishes one audit event;
///         </item>
///         <item>
///             <b>stripping</b> of <c>RtBlueprintLocked</c>, <c>RtBlueprintSource</c> and
///             <c>RtBlueprintAppliedAt</c> from incoming entities of opted-in types (an export of another tenant
///             carries them): new entities become tenant-owned, existing ones keep their stored values.
///         </item>
///     </list>
///     The stored entities read by the preflight are shared with the preserve pass of the import (no second
///     read). The instance only exists when the tenant has at least one opted-in policy, so tenants without one
///     pay nothing.
/// </summary>
internal sealed class ImportBlueprintLockEnforcer
{
    private readonly ICkCacheService _ckCacheService;
    private readonly IAuditEventSink? _auditEventSink;
    private readonly ILogger _logger;
    private readonly string _tenantId;
    private readonly RtDataPolicyTable _policyTable;
    private readonly RtSecurityContext _securityContext;
    private readonly string? _subjectId;

    private readonly Dictionary<RtCkId<CkTypeId>, RtBlueprintLockProtection> _modes = new();

    private readonly Dictionary<RtCkId<CkTypeId>, IReadOnlyList<(RtCkId<CkAttributeId> Id, string Name)>>
        _protectedAttributes = new();

    private readonly Dictionary<RtCkId<CkTypeId>, IReadOnlyDictionary<OctoObjectId, RtEntity>> _existing = new();

    private int _strippedAttributes;
    private int _strippedEntities;
    private int _auditedViolations;

    private ImportBlueprintLockEnforcer(ILogger logger, ICkCacheService ckCacheService,
        IAuditEventSink? auditEventSink, string tenantId, RtDataPolicyTable policyTable, string? subjectId)
    {
        _logger = logger;
        _ckCacheService = ckCacheService;
        _auditEventSink = auditEventSink;
        _tenantId = tenantId;
        _policyTable = policyTable;
        _subjectId = subjectId;
        // A non-system context for the classification: the restriction never depends on grants, only on
        // the caller not being the system.
        _securityContext = RtSecurityContext.ForUser(subjectId, null);
    }

    /// <summary>What the protection did so far.</summary>
    internal RtImportBlueprintLockSummary Summary =>
        new(_strippedAttributes, _strippedEntities, _auditedViolations);

    /// <summary>
    ///     Returns the enforcer for this import, or null when nothing is to be enforced: the import is a system
    ///     flow (no caller context / flag off) or the tenant has no policy with <c>ProtectBlueprintLocked</c>.
    ///     In both cases no repository read happens beyond the (cached) policy table.
    /// </summary>
    internal static async Task<ImportBlueprintLockEnforcer?> TryCreateAsync(ILogger logger,
        ICkCacheService ckCacheService, IDataPermissionResolver dataPermissionResolver,
        IAuditEventSink? auditEventSink, IRuntimeRepository runtimeRepository, RtImportCallerContext? caller)
    {
        if (caller is not { EnforceBlueprintLock: true })
        {
            return null;
        }

        var policyTable = await dataPermissionResolver.GetPolicyTableAsync(runtimeRepository).ConfigureAwait(false);
        if (!policyTable.HasBlueprintLockProtection)
        {
            return null;
        }

        return new ImportBlueprintLockEnforcer(logger, ckCacheService, auditEventSink,
            runtimeRepository.TenantId, policyTable, caller.SubjectId);
    }

    /// <summary>How the caller is restricted for the CK type (derived types inherit from their base types).</summary>
    internal RtBlueprintLockProtection Classify(RtCkId<CkTypeId> ckTypeId)
    {
        if (_modes.TryGetValue(ckTypeId, out var mode))
        {
            return mode;
        }

        var selfAndBase = RtDataPermissionCkTypeHelper.GetSelfAndBaseFullNames(_ckCacheService, _tenantId, ckTypeId);
        mode = RtDataAccessEvaluator.ClassifyBlueprintLockProtection(_policyTable, selfAndBase, _securityContext);
        _modes[ckTypeId] = mode;
        return mode;
    }

    /// <summary>
    ///     The stored entities of an opted-in type read by <see cref="PreflightAsync" />, keyed by rtId (only the
    ///     ids that exist). Lets the preserve pass of the import reuse the read instead of repeating it.
    /// </summary>
    internal bool TryGetStoredEntities(RtCkId<CkTypeId> ckTypeId,
        out IReadOnlyDictionary<OctoObjectId, RtEntity> storedEntities)
    {
        return _existing.TryGetValue(ckTypeId, out storedEntities!);
    }

    /// <summary>
    ///     Preflight over all entities of the file, before any write. One batched read per opted-in type that
    ///     occurs in the file. Throws (nothing written) when an Enforce policy covers a stored entity that is
    ///     locked by a blueprint, listing every such entity; an AuditOnly policy publishes one audit event per
    ///     type and lets the import proceed.
    /// </summary>
    /// <param name="session">The import session (read on it, so the preserve pass sees the same state)</param>
    /// <param name="entityIds">Type and id of every entity in the file</param>
    /// <param name="runtimeRepository">The repository of the target tenant</param>
    internal async Task PreflightAsync(IOctoSession session,
        IEnumerable<(RtCkId<CkTypeId> CkTypeId, OctoObjectId RtId)> entityIds, IRuntimeRepository runtimeRepository)
    {
        var enforceOffenders = new List<string>();
        var auditOffenders = new List<(RtCkId<CkTypeId> CkTypeId, int Count)>();

        foreach (var typeGroup in entityIds
                     .Where(e => !e.RtId.Equals(OctoObjectId.Empty))
                     .GroupBy(e => e.CkTypeId))
        {
            var mode = Classify(typeGroup.Key);
            if (mode == RtBlueprintLockProtection.None)
            {
                continue;
            }

            var rtIds = typeGroup.Select(e => e.RtId).Distinct().ToList();
            var result = await runtimeRepository.GetRtEntitiesByIdAsync(session, typeGroup.Key, rtIds,
                RtEntityQueryOptions.Create(), take: rtIds.Count).ConfigureAwait(false);
            var stored = result.Items.ToDictionary(e => e.RtId);
            _existing[typeGroup.Key] = stored;

            var lockedIds = stored.Values.Where(DataPermissionWriteGuard.IsBlueprintLocked).Select(e => e.RtId)
                .ToList();
            if (lockedIds.Count == 0)
            {
                continue;
            }

            if (mode == RtBlueprintLockProtection.Enforce)
            {
                enforceOffenders.AddRange(lockedIds.Select(id => DataPermissionWriteGuard
                    .LockedMessage(typeGroup.Key, id, EntityModOptions.Replace).Location ?? $"{typeGroup.Key}@{id}"));
            }
            else
            {
                auditOffenders.Add((typeGroup.Key, lockedIds.Count));
            }
        }

        if (enforceOffenders.Count > 0)
        {
            _logger.LogWarning(
                "Import into '{TenantId}' rejected: {Count} entity(ies) are locked by blueprint (policy ProtectBlueprintLocked, subject '{SubjectId}')",
                _tenantId, enforceOffenders.Count, _subjectId);
            throw ExchangeException.BlueprintLockedEntities(
                RtBlueprintLockProtectionNames.ForbiddenMessageNumber, enforceOffenders);
        }

        foreach (var (ckTypeId, count) in auditOffenders)
        {
            _auditedViolations += count;
            if (_auditEventSink != null)
            {
                await _auditEventSink.PublishAsync(new AuditEvent(_tenantId, AuditEventLevel.Warning,
                    "DataPermissions.BlueprintLockViolation",
                    $"Subject '{_subjectId}' imported over {count} blueprint-locked entity(ies) of " +
                    $"'{ckTypeId.SemanticVersionedFullName}' (AuditOnly policy — not blocked).")).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Strips the blueprint bookkeeping attributes from incoming entities of opted-in types and, for entities
    ///     that already exist, puts the stored values back so the full replace of an Upsert does not clear them.
    ///     Synchronous on purpose: it must run on the incoming DTOs before the first await of the import.
    /// </summary>
    internal void StripBlueprintAttributes(IEnumerable<RtEntityTcDto> entities,
        Func<object?, object?> convertStoredValue)
    {
        foreach (var entity in entities)
        {
            if (Classify(entity.CkTypeId) == RtBlueprintLockProtection.None)
            {
                continue;
            }

            var protectedAttributes = ProtectedAttributesOf(entity.CkTypeId);
            if (protectedAttributes.Count == 0)
            {
                continue;
            }

            var removed = entity.Attributes.RemoveAll(a => protectedAttributes.Any(p => p.Id.Equals(a.Id)));
            if (removed > 0)
            {
                _strippedAttributes += removed;
                _strippedEntities++;
            }

            if (!_existing.TryGetValue(entity.CkTypeId, out var stored) ||
                !stored.TryGetValue(entity.RtId, out var storedEntity))
            {
                continue;
            }

            foreach (var (id, name) in protectedAttributes)
            {
                if (!storedEntity.Attributes.TryGetValue(name, out var storedValue) || storedValue == null)
                {
                    continue;
                }

                entity.Attributes.Add(new RtAttributeTcDto { Id = id, Value = convertStoredValue(storedValue) });
            }
        }
    }

    /// <summary>
    ///     Reports the stripped attributes of the finished import as a warning (log and audit event). A failure to
    ///     publish never fails the import.
    /// </summary>
    internal async Task ReportAsync()
    {
        if (_strippedAttributes == 0)
        {
            return;
        }

        var message =
            $"Import by '{_subjectId}' into '{_tenantId}' removed {_strippedAttributes} blueprint attribute value(s) " +
            $"(RtBlueprintLocked, RtBlueprintSource, RtBlueprintAppliedAt) from {_strippedEntities} entity(ies) of " +
            "types protected by the blueprint lock: users cannot set them. New entities are tenant-owned.";
        _logger.LogWarning("{Message}", message);

        if (_auditEventSink == null)
        {
            return;
        }

        try
        {
            await _auditEventSink.PublishAsync(new AuditEvent(_tenantId, AuditEventLevel.Warning,
                "RtImport.BlueprintAttributesStripped", message)
            {
                Metadata = new Dictionary<string, object?>
                {
                    ["strippedAttributeCount"] = _strippedAttributes,
                    ["strippedEntityCount"] = _strippedEntities
                }
            }).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to publish the stripped-blueprint-attributes audit event");
        }
    }

    private IReadOnlyList<(RtCkId<CkAttributeId> Id, string Name)> ProtectedAttributesOf(RtCkId<CkTypeId> ckTypeId)
    {
        if (_protectedAttributes.TryGetValue(ckTypeId, out var cached))
        {
            return cached;
        }

        IReadOnlyList<(RtCkId<CkAttributeId>, string)> result = [];
        if (_ckCacheService.TryGetRtCkType(_tenantId, ckTypeId, out var graph) && graph != null)
        {
            result = graph.AllAttributes.Values
                .Where(a => RtBlueprintLockProtectionNames.ProtectedAttributeNames.Contains(a.AttributeName))
                .Select(a => (a.CkAttributeId.ToRtCkId(), a.AttributeName))
                .ToList();
        }

        _protectedAttributes[ckTypeId] = result;
        return result;
    }
}
