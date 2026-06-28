using System;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Information B of the recompute model (AB#4184): one pending recompute obligation, derived by
/// propagating an <see cref="ArchiveDirtyWindow"/> through the rollup dependency graph. Identifies a
/// dependent archive and the bucket-aligned half-open range <c>[RangeStart, RangeEnd)</c> to
/// recompute, optionally scoped to a single <see cref="RtIdScope"/> (metering point / stream). Maps
/// 1:1 to the <c>CkArchiveRecomputeRange</c> record. All timestamps are UTC.
/// </summary>
/// <remarks>
/// <see cref="RtIdScope"/> is <c>null</c> for ranges derived from dirty-window propagation (which is
/// window- not rtId-granular); it is only populated when an operator triggers a manually scoped
/// recompute. The range boundaries are aligned to the dependent's own bucket boundaries, so the
/// recompute orchestrator can process them as whole buckets.
/// </remarks>
public sealed record ArchiveRecomputeRange(
    OctoObjectId DependentArchiveRtId,
    DateTime RangeStart,
    DateTime RangeEnd,
    OctoObjectId? RtIdScope,
    DateTime EnqueuedAt);
