using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Default <see cref="IRollupArchiveLifecycleService"/> implementation. Thin orchestration over
/// <see cref="IRollupArchiveRuntimeStore"/> + <see cref="IArchiveAuditTrail"/>; no DB-specific
/// code. The shared archive lifecycle (activate / disable / enable / retry / delete) stays on
/// <see cref="IArchiveLifecycleService"/> since rollups inherit those semantics unchanged
/// (rollup-archives concept §2).
/// </summary>
/// <remarks>
/// Monotonicity of <see cref="RollupArchiveSnapshot.FrozenUntil"/> (only forward) is enforced
/// here, before the store call, so the audit event reflects the *applied* operation and the store
/// implementation can stay a thin writer. <see cref="UnfreezeAsync"/>'s gap detection is deferred
/// to a follow-up: for the MVP we set <c>FrozenUntil = null</c> unconditionally and let the
/// caller assert the <c>acceptGaps</c> flag.
/// </remarks>
public sealed class RollupArchiveLifecycleService : IRollupArchiveLifecycleService
{
    private readonly string _tenantId;
    private readonly IRollupArchiveRuntimeStore _rollupStore;
    private readonly IArchiveRuntimeStore _archiveStore;
    private readonly IArchiveAuditTrail _audit;
    private readonly ILogger<RollupArchiveLifecycleService> _logger;

    /// <summary>
    /// Constructs the rollup lifecycle service. <paramref name="rollupStore"/> and
    /// <paramref name="archiveStore"/> must be tenant-scoped (the source archive lookup in
    /// <see cref="CreateAsync"/> uses the same Mongo polymorphism that handles both raw archives
    /// and chained rollups via the shared CkArchive base). The audit trail can be either
    /// tenant-scoped or shared (the tenant id is passed explicitly into every audit call).
    /// </summary>
    public RollupArchiveLifecycleService(
        string tenantId,
        IRollupArchiveRuntimeStore rollupStore,
        IArchiveRuntimeStore archiveStore,
        IArchiveAuditTrail audit,
        ILogger<RollupArchiveLifecycleService> logger)
    {
        _tenantId = tenantId;
        _rollupStore = rollupStore;
        _archiveStore = archiveStore;
        _audit = audit;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OctoObjectId> CreateAsync(
        string? rtWellKnownName,
        OctoObjectId sourceArchiveRtId,
        TimeSpan bucketSize,
        TimeSpan watermarkLag,
        IReadOnlyList<CkRollupAggregationSpec> aggregations,
        BucketAlignment bucketAlignment = BucketAlignment.FixedSize)
    {
        if (aggregations is null) throw new ArgumentNullException(nameof(aggregations));
        if (aggregations.Count == 0)
        {
            throw new ArgumentException("At least one aggregation is required.", nameof(aggregations));
        }
        if (bucketSize <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketSize), "BucketSize must be positive.");
        }
        if (watermarkLag < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(watermarkLag), "WatermarkLag must be non-negative.");
        }
        if (!Enum.IsDefined(typeof(BucketAlignment), bucketAlignment))
        {
            throw new ArgumentOutOfRangeException(nameof(bucketAlignment), bucketAlignment,
                "BucketAlignment value is not a known enum member.");
        }

        // Source archive lookup goes through the shared CkArchive store: thanks to Mongo
        // polymorphism the result is the same regardless of whether the source is a raw archive
        // or another rollup (chained rollups, concept §10). The snapshot carries the inherited
        // TargetCkTypeId that the new rollup must mirror.
        var source = await _archiveStore.GetAsync(sourceArchiveRtId)
            ?? throw new ArchiveNotFoundException(sourceArchiveRtId);

        // Pure-function derivation lives in Runtime.Contracts; ports (e.g. studio) mirror this rule.
        var columns = RollupColumnGenerator.Generate(aggregations);

        var rtId = await _rollupStore.InsertAsync(
            rtWellKnownName,
            source.TargetCkTypeId,
            sourceArchiveRtId,
            bucketSize,
            watermarkLag,
            aggregations,
            columns,
            bucketAlignment);

        _logger.LogInformation(
            "Rollup {RollupRtId} created from source {SourceRtId} with {AggregationCount} aggregations / {ColumnCount} derived columns, alignment={Alignment} (tenant {TenantId})",
            rtId, sourceArchiveRtId, aggregations.Count, columns.Count, bucketAlignment, _tenantId);

        return rtId;
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

    private async Task<RollupArchiveSnapshot> LoadAsync(OctoObjectId rollupRtId)
    {
        var snapshot = await _rollupStore.GetAsync(rollupRtId);
        if (snapshot is null)
        {
            throw new ArchiveNotFoundException(rollupRtId);
        }
        return snapshot;
    }
}
