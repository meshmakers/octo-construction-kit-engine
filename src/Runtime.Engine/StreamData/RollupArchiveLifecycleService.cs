using System;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Default <see cref="IRollupArchiveLifecycleService"/> implementation. Thin orchestration over
/// <see cref="ICkRollupArchiveRuntimeStore"/> + <see cref="IArchiveAuditTrail"/>; no DB-specific
/// code. The shared archive lifecycle (activate / disable / enable / retry / delete) stays on
/// <see cref="IArchiveLifecycleService"/> since rollups inherit those semantics unchanged
/// (rollup-archives concept §2).
/// </summary>
/// <remarks>
/// Monotonicity of <see cref="CkRollupArchiveSnapshot.FrozenUntil"/> (only forward) is enforced
/// here, before the store call, so the audit event reflects the *applied* operation and the store
/// implementation can stay a thin writer. <see cref="UnfreezeAsync"/>'s gap detection is deferred
/// to a follow-up: for the MVP we set <c>FrozenUntil = null</c> unconditionally and let the
/// caller assert the <c>acceptGaps</c> flag.
/// </remarks>
public sealed class RollupArchiveLifecycleService : IRollupArchiveLifecycleService
{
    private readonly string _tenantId;
    private readonly ICkRollupArchiveRuntimeStore _rollupStore;
    private readonly IArchiveAuditTrail _audit;
    private readonly ILogger<RollupArchiveLifecycleService> _logger;

    /// <summary>
    /// Constructs the rollup lifecycle service. <paramref name="rollupStore"/> must be
    /// tenant-scoped; the audit trail can be either tenant-scoped or shared (the tenant id is
    /// passed explicitly into every audit call).
    /// </summary>
    public RollupArchiveLifecycleService(
        string tenantId,
        ICkRollupArchiveRuntimeStore rollupStore,
        IArchiveAuditTrail audit,
        ILogger<RollupArchiveLifecycleService> logger)
    {
        _tenantId = tenantId;
        _rollupStore = rollupStore;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task FreezeAsync(OctoObjectId rollupRtId, DateTime until)
    {
        var snapshot = await LoadAsync(rollupRtId);

        if (snapshot.FrozenUntil is { } current && until < current)
        {
            // Monotonic — moving FrozenUntil backwards is the UnfreezeAsync path, not freeze.
            throw new InvalidArchiveStateTransitionException(
                rollupRtId, snapshot.Status,
                $"freeze to {until:O} (current frozen-until {current:O} is later)");
        }

        await _rollupStore.SetFrozenUntilAsync(rollupRtId, until);
        await _audit.RecordFreezeAsync(_tenantId, rollupRtId, until, reason: null);

        _logger.LogInformation(
            "Rollup {RollupRtId} frozen until {FrozenUntil:O}", rollupRtId, until);
    }

    /// <inheritdoc />
    public async Task UnfreezeAsync(OctoObjectId rollupRtId, bool acceptGaps = false)
    {
        var snapshot = await LoadAsync(rollupRtId);

        if (snapshot.FrozenUntil is null)
        {
            return; // idempotent — already unfrozen
        }

        // MVP: the gap-detection guard (concept §9) is not implemented yet — when it lands it
        // sits here and throws unless acceptGaps is true. Until then the parameter is recorded
        // in the log line for traceability.
        _ = acceptGaps;

        await _rollupStore.SetFrozenUntilAsync(rollupRtId, null);

        _logger.LogInformation(
            "Rollup {RollupRtId} unfrozen (was frozen until {PreviousFrozenUntil:O}, acceptGaps={AcceptGaps})",
            rollupRtId, snapshot.FrozenUntil.Value, acceptGaps);
    }

    /// <inheritdoc />
    public async Task RewindWatermarkAsync(OctoObjectId rollupRtId, DateTime toBucketEnd)
    {
        var snapshot = await LoadAsync(rollupRtId);

        // Truncate toBucketEnd down to the bucket boundary so the next orchestrator tick re-picks
        // a clean bucket-start. The store enforces the actual write; this normalisation is just
        // input-shaping so callers can pass any timestamp inside (or at) the target bucket.
        var bucketSize = snapshot.BucketSize;
        if (bucketSize > TimeSpan.Zero)
        {
            var ticks = bucketSize.Ticks;
            toBucketEnd = new DateTime((toBucketEnd.Ticks / ticks) * ticks, toBucketEnd.Kind);
        }

        await _rollupStore.AdvanceWatermarkAsync(rollupRtId, toBucketEnd, allowRewind: true);

        _logger.LogWarning(
            "Rollup {RollupRtId} watermark rewound to {Watermark:O} (previous {PreviousWatermark:O}). " +
            "Re-aggregation will overwrite existing buckets in the rewound range.",
            rollupRtId, toBucketEnd, snapshot.LastAggregatedBucketEnd);
    }

    private async Task<CkRollupArchiveSnapshot> LoadAsync(OctoObjectId rollupRtId)
    {
        var snapshot = await _rollupStore.GetAsync(rollupRtId);
        if (snapshot is null)
        {
            throw new ArchiveNotFoundException(rollupRtId);
        }
        return snapshot;
    }
}
