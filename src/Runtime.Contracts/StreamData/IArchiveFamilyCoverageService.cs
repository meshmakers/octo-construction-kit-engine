using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// One rung of an archive family with its measured coverage (AB#5157) — the projection behind the
/// <c>coverageFor</c> GraphQL query, the REST coverage endpoint and the MCP coverage tool.
/// </summary>
/// <param name="ArchiveRtId">Runtime id of the archive this rung describes.</param>
/// <param name="RtWellKnownName">Optional human-readable name; <c>null</c> falls back to the rtId.</param>
/// <param name="IsBase">
/// True when the rung is <em>not</em> a rollup — i.e. the raw or time-range archive the family is
/// keyed by. The base takes part in the resolver's coverage filter like any other rung.
/// </param>
/// <param name="Status">The rung's archive lifecycle status, so a client can tell a disabled rung from an empty one.</param>
/// <param name="BucketSizeMs">
/// The rung's native bucket width in milliseconds — a rollup's <c>BucketSize</c>, a time-range
/// base archive's <c>Period</c>, and <c>null</c> for a raw base archive (no declared grain).
/// </param>
/// <param name="Alignment">
/// Bucket-boundary alignment of the rung; <see cref="BucketAlignment.FixedSize"/> for a base
/// archive, which has no calendar alignment of its own.
/// </param>
/// <param name="StoredFunctions">
/// The aggregation functions this rung <em>declares</em> (the distinct functions over its
/// aggregation specs). Empty for a base archive, which stores no aggregation.
/// </param>
/// <param name="AvailableFrom">Measured earliest timestamp with data; <c>null</c> when the rung has no coverage.</param>
/// <param name="AvailableTo">Measured latest timestamp with data; <c>null</c> when the rung has no coverage.</param>
public sealed record ArchiveCoverageRung(
    OctoObjectId ArchiveRtId,
    string? RtWellKnownName,
    bool IsBase,
    CkArchiveStatus Status,
    long? BucketSizeMs,
    BucketAlignment Alignment,
    IReadOnlyCollection<CkRollupFunction> StoredFunctions,
    DateTime? AvailableFrom,
    DateTime? AvailableTo);

/// <summary>
/// Answers "which resolution is available for which time range?" for a whole archive family
/// (AB#5157): the archive addressed by the caller plus every rollup transitively reachable from it,
/// each with its bucket size, alignment, stored functions and measured coverage.
/// </summary>
/// <remarks>
/// The family stays keyed by any archive rtId, and a multi-source rollup therefore belongs to the
/// family of <em>each</em> of its sources, reporting the same measured coverage in each — it is
/// still listed exactly once per family. See <c>concept-multi-source-rollups.md</c> §7.
/// </remarks>
public interface IArchiveFamilyCoverageService
{
    /// <summary>
    /// Returns the coverage rungs of the family reachable from <paramref name="archiveRtId"/> in
    /// breadth-first order with the queried archive first. An unknown rtId — or a tenant without
    /// stream data — yields an empty list rather than an error.
    /// </summary>
    /// <param name="archiveRtId">Runtime id of any archive of the family.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<ArchiveCoverageRung>> GetFamilyCoverageAsync(
        OctoObjectId archiveRtId,
        CancellationToken cancellationToken = default);
}
