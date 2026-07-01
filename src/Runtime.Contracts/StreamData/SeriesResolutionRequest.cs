using System;
using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Input to the resolution-aware series resolver (<c>ISeriesResolutionService</c>): a logical series
/// (addressed by an archive family plus optional rtId / OBIS scope), a time range, and the target
/// number of display points. The resolver picks the archive to query — it does not run the query.
/// See <c>concept-resolution-aware-series-queries.md</c> §5.1.
/// </summary>
/// <param name="BaseArchiveRtId">
/// The base (raw / time-range) archive of the family. Mutually complementary with
/// <see cref="TargetCkTypeId"/>: at least one must be supplied so the service can identify the
/// family. When both are set, <see cref="BaseArchiveRtId"/> wins.
/// </param>
/// <param name="TargetCkTypeId">
/// The archived CK type (e.g. <c>Basic.Energy/EnergyMeasurement</c>) used to locate the base archive
/// when <see cref="BaseArchiveRtId"/> is not supplied.
/// </param>
/// <param name="From">Inclusive start of the query window (UTC).</param>
/// <param name="To">Exclusive end of the query window (UTC).</param>
/// <param name="TargetPoints">
/// The desired number of output points (pixel-driven, ~600 default). Must be positive.
/// </param>
/// <param name="RequiredAggregation">
/// The aggregation function the series must be reduced with (decision O2: caller-supplied, never
/// guessed — energy ⇒ <see cref="CkRollupFunction.Sum"/>, demand ⇒ <see cref="CkRollupFunction.Max"/>,
/// etc.). A rollup rung is a valid source only when its stored function equals this; on the base
/// archive it is the only semantic source and drives the downsampling reducer.
/// </param>
public sealed record SeriesResolutionRequest(
    OctoObjectId? BaseArchiveRtId,
    RtCkId<CkTypeId>? TargetCkTypeId,
    DateTime From,
    DateTime To,
    int TargetPoints,
    CkRollupFunction RequiredAggregation)
{
    /// <summary>
    /// Optional runtime scope override — the source entity rtIds the series is restricted to (e.g. the
    /// EnergyMeasurement entities resolved from a selected MeteringPoint, AB#4236). Advisory to the
    /// resolver; forwarded by the caller to the downsampling query.
    /// </summary>
    public IReadOnlyList<OctoObjectId>? RtIds { get; init; }

    /// <summary>
    /// Optional OBIS-code filter narrowing the series within the archive. Advisory to the resolver;
    /// forwarded by the caller to the downsampling query's field filter.
    /// </summary>
    public string? ObisFilter { get; init; }
}
