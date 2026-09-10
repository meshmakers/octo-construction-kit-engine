using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Default <see cref="IArchiveFamilyCoverageService"/> (AB#5157): walks the queried archive first
/// and then every rollup transitively reachable from it in the breadth-first order of
/// <see cref="IRollupDependencyGraph"/> — each rung exactly once, a diamond or a two-source rollup
/// included — and pairs every rung with its measured coverage from the
/// <see cref="IArchiveCoverageProvider"/>.
/// </summary>
/// <remarks>
/// The queried archive may itself be a rollup; it is then reported with its rollup facts (bucket
/// size, alignment, declared functions) and <see cref="ArchiveCoverageRung.IsBase"/> false. A base
/// archive reports its time-range <c>Period</c> as bucket size (<c>null</c> for a raw archive),
/// <see cref="BucketAlignment.FixedSize"/> and no stored functions. Coverage is asked once per rung;
/// a rung without data reports <c>null</c> / <c>null</c>.
/// </remarks>
public sealed class ArchiveFamilyCoverageService : IArchiveFamilyCoverageService
{
    private readonly IArchiveRuntimeStore _archiveStore;
    private readonly IRollupArchiveRuntimeStore _rollupStore;
    private readonly IRollupDependencyGraph _dependencyGraph;
    private readonly IArchiveCoverageProvider _coverageProvider;

    /// <summary>
    /// Creates the service for one tenant from that tenant's stores, dependency graph and coverage
    /// provider.
    /// </summary>
    public ArchiveFamilyCoverageService(
        IArchiveRuntimeStore archiveStore,
        IRollupArchiveRuntimeStore rollupStore,
        IRollupDependencyGraph dependencyGraph,
        IArchiveCoverageProvider coverageProvider)
    {
        _archiveStore = archiveStore ?? throw new ArgumentNullException(nameof(archiveStore));
        _rollupStore = rollupStore ?? throw new ArgumentNullException(nameof(rollupStore));
        _dependencyGraph = dependencyGraph ?? throw new ArgumentNullException(nameof(dependencyGraph));
        _coverageProvider = coverageProvider ?? throw new ArgumentNullException(nameof(coverageProvider));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArchiveCoverageRung>> GetFamilyCoverageAsync(
        OctoObjectId archiveRtId,
        CancellationToken cancellationToken = default)
    {
        var archive = await _archiveStore.GetAsync(archiveRtId).ConfigureAwait(false);
        if (archive is null)
        {
            return Array.Empty<ArchiveCoverageRung>();
        }

        var rungs = new List<ArchiveCoverageRung>();

        // The queried archive comes first. Its rollup view (when it is one) carries the facts the
        // base view lacks — bucket size, alignment, declared aggregation functions.
        var rootRollup = await _rollupStore.GetAsync(archiveRtId).ConfigureAwait(false);
        rungs.Add(rootRollup is not null
            ? await BuildRollupRungAsync(rootRollup, cancellationToken).ConfigureAwait(false)
            : await BuildBaseRungAsync(archive, cancellationToken).ConfigureAwait(false));

        // Transitive dependents, breadth-first and already deduplicated by rollup rtId (a rollup
        // reachable via several parents is listed once).
        var dependents = await _dependencyGraph.GetTransitiveDependentsAsync(archiveRtId).ConfigureAwait(false);
        foreach (var rollup in dependents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (rollup.RtId == archiveRtId)
            {
                continue; // defensive: a cycle the model failed to reject must not list the root twice
            }

            rungs.Add(await BuildRollupRungAsync(rollup, cancellationToken).ConfigureAwait(false));
        }

        return rungs;
    }

    private async Task<ArchiveCoverageRung> BuildBaseRungAsync(ArchiveSnapshot archive, CancellationToken cancellationToken)
    {
        var coverage = await _coverageProvider.GetCoverageAsync(archive.RtId, cancellationToken).ConfigureAwait(false);

        // A time-range base reports its declared window length; a raw archive has no grain.
        long? bucketSizeMs = archive.IsTimeRange && archive.Period is { } period && period > TimeSpan.Zero
            ? (long)period.TotalMilliseconds
            : null;

        return new ArchiveCoverageRung(
            archive.RtId,
            archive.RtWellKnownName,
            IsBase: true,
            archive.Status,
            bucketSizeMs,
            BucketAlignment.FixedSize,
            Array.Empty<CkRollupFunction>(),
            coverage?.AvailableFrom,
            coverage?.AvailableTo);
    }

    private async Task<ArchiveCoverageRung> BuildRollupRungAsync(RollupArchiveSnapshot rollup, CancellationToken cancellationToken)
    {
        var coverage = await _coverageProvider.GetCoverageAsync(rollup.RtId, cancellationToken).ConfigureAwait(false);

        return new ArchiveCoverageRung(
            rollup.RtId,
            rollup.RtWellKnownName,
            IsBase: false,
            rollup.Status,
            (long)rollup.BucketSize.TotalMilliseconds,
            rollup.BucketAlignment,
            rollup.Aggregations.Select(a => a.Function).Distinct().ToList(),
            coverage?.AvailableFrom,
            coverage?.AvailableTo);
    }
}
