using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Rollup-specific lifecycle operations layered on top of <see cref="IArchiveLifecycleService"/>.
/// Status transitions (Activate / Disable / Enable / RetryActivation / Delete) go through the
/// shared archive lifecycle service since rollups inherit those semantics unchanged (concept §2);
/// this interface only adds the operations that are unique to rollups: freeze / unfreeze and
/// watermark rewind. Backs the GraphQL mutations defined in concept §9.
/// </summary>
public interface IRollupArchiveLifecycleService
{
    /// <summary>
    /// Creates a new CkRollupArchive entity in <see cref="CkArchiveStatus.Created"/> from a
    /// rollup-specific input. The inherited CkArchive attributes <c>TargetCkTypeId</c> (from the
    /// first source archive) and <c>Columns</c> (from
    /// <see cref="RollupColumnGenerator.Generate"/>) are resolved server-side so callers don't have
    /// to duplicate the column-derivation rule. Concept §4. Throws
    /// <see cref="ArchiveNotFoundException"/> if any entry of <paramref name="sources"/> doesn't
    /// resolve.
    /// </summary>
    /// <param name="rtWellKnownName">Optional human-readable name. Null falls back to the rtId.</param>
    /// <param name="sources">
    /// The source archives this rollup aggregates from, each with an optional validity span
    /// (AB#5157). At least one entry is required. Persisted as declared — the deprecated single
    /// <c>SourceArchiveRtId</c> scalar is never written.
    /// </param>
    /// <param name="bucketSize">Bucket width.</param>
    /// <param name="watermarkLag">How long the orchestrator waits after bucket-end before aggregating.</param>
    /// <param name="aggregations">User-defined aggregation specs, logical over every source.</param>
    /// <param name="bucketAlignment">Bucket-boundary alignment; defaults to <see cref="BucketAlignment.FixedSize"/>.</param>
    /// <param name="referenceTimeZone">Optional IANA reference time zone for calendar alignments; null ⇒ UTC.</param>
    /// <param name="carryLookback">
    /// Optionally bounds the <see cref="CkRollupFunction.TimeWeightedAvg"/> carry-in scan (LOCF
    /// opening state); <c>null</c> ⇒ the engine default of 35 days. Only meaningful when the
    /// aggregations include TimeWeightedAvg. AB#4336 / decision D1.
    /// </param>
    /// <returns>The generated runtime id of the new rollup archive.</returns>
    /// <remarks>
    /// The full aggregation rules, the source-span rules and the transitive cycle check all run
    /// <em>before</em> the entity is inserted (AB#5157), so an invalid declaration fails fast at
    /// create time instead of only at activation.
    /// </remarks>
    Task<OctoObjectId> CreateAsync(
        string? rtWellKnownName,
        IReadOnlyList<RollupSourceReference> sources,
        TimeSpan bucketSize,
        TimeSpan watermarkLag,
        IReadOnlyList<CkRollupAggregationSpec> aggregations,
        BucketAlignment bucketAlignment = BucketAlignment.FixedSize,
        string? referenceTimeZone = null,
        TimeSpan? carryLookback = null);

    /// <summary>
    /// Sets <see cref="RollupArchiveSnapshot.FrozenUntil"/> to <paramref name="until"/>.
    /// Monotonic: rejected when <paramref name="until"/> is earlier than the current value
    /// (use <see cref="UnfreezeAsync"/> instead). When set, the orchestrator will not produce new
    /// buckets whose <c>bucketEnd</c> falls within the frozen range; already-aggregated rows in
    /// that range are preserved. Concept §6, §9.
    /// </summary>
    Task FreezeAsync(OctoObjectId rollupRtId, DateTime until);

    /// <summary>
    /// Clears <see cref="RollupArchiveSnapshot.FrozenUntil"/>. Rejected when source data inside
    /// the previously frozen range has been truncated and <paramref name="acceptGaps"/> is false,
    /// because unfreezing would produce visible gaps once the orchestrator catches up. The
    /// override is intentional so an operator can knowingly accept the inconsistency. Concept §9.
    /// </summary>
    Task UnfreezeAsync(OctoObjectId rollupRtId, bool acceptGaps = false);

    /// <summary>
    /// Resets <see cref="RollupArchiveSnapshot.LastAggregatedBucketEnd"/> to
    /// <paramref name="toBucketEnd"/> (truncated to the bucket boundary). Subsequent orchestrator
    /// ticks re-aggregate the rewound range. Destructive: previously committed rows in that range
    /// are temporarily out of sync until the orchestrator catches up. Requires elevated
    /// permission (the GraphQL field enforces the <c>StreamDataAdmin</c> role). Concept §5, §9.
    /// </summary>
    Task RewindWatermarkAsync(OctoObjectId rollupRtId, DateTime toBucketEnd);
}
