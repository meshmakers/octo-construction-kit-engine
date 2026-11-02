using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.Blueprints;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
/// One archive entity of a blueprint seed, recorded before the seed import (AB#7991).
/// </summary>
/// <param name="RtId">Runtime id of the archive.</param>
/// <param name="WellKnownName">Well-known name from the seed, if any.</param>
/// <param name="CkTypeId">CK type of the archive entity.</param>
/// <param name="BlueprintId">The blueprint whose seed carries the archive.</param>
/// <param name="IsNew"><c>true</c> when the tenant did not hold the archive before the import.</param>
/// <param name="OptedOut"><c>true</c> when the blueprint lists the archive in <c>archiveActivation.exclude</c>.</param>
/// <param name="NotImported">
/// <c>true</c> when stream data is off on the tenant and the archive entity was left out of the seed import because its
/// CK model <c>System.StreamData</c> is not installed there (AB#7991).
/// </param>
internal sealed record BlueprintArchiveCandidate(
    OctoObjectId RtId,
    string? WellKnownName,
    RtCkId<CkTypeId> CkTypeId,
    BlueprintId BlueprintId,
    bool IsNew,
    bool OptedOut,
    bool NotImported = false);

/// <summary>
/// Activates the archives a blueprint seed created and queues the backfill of new rollups (AB#7991).
/// </summary>
/// <remarks>
/// <para>
/// Two steps around the seed import: <see cref="CollectAsync" /> runs before it and records which archive entities of
/// the seed the tenant does not hold yet; <see cref="ActivateAsync" /> runs after it and activates exactly those. An
/// archive that existed before is never touched, whatever its status - a re-apply (also forced) is therefore idempotent,
/// and an archive an operator left <c>Created</c> or <c>Disabled</c> on purpose stays that way.
/// </para>
/// <para>
/// Order: raw and time-range archives first, then rollups so that every rollup comes after the new rollups it reads
/// from (activation of a rollup requires activated sources). The seed is committed before this runs: a failure is
/// reported per archive (the archive ends <c>Failed</c>, retryable) and never fails the blueprint apply.
/// </para>
/// <para>
/// AB#7984 guard: a rollup with <c>TimeWeightedAvg</c> or <c>StateDuration</c> over a raw source is computed with the
/// LOCF carry statement, which trips the CrateDB memory breaker over a long history. Such rollups are activated, but
/// their backfill is not queued. Removing this guard is part of the fix of AB#7984.
/// </para>
/// </remarks>
internal sealed class BlueprintArchiveActivator(IStreamDataTenantServicesProvider? servicesProvider, ILogger logger)
{
    /// <summary>The work item whose fix lifts the backfill guard for time-weighted rollups over raw sources.</summary>
    internal const string LocfGuardWorkItem = "AB#7984";

    /// <summary>
    /// Records the archive entities of <paramref name="seedEntities" /> and whether the tenant already holds each one.
    /// Runs before the seed import. Warnings (archives without rtId, exclude entries that match nothing) are added to
    /// <paramref name="warnings" />.
    /// </summary>
    /// <param name="repository">Repository of the tenant.</param>
    /// <param name="seedEntities">The entities the import is about to write.</param>
    /// <param name="isArchive">Tells whether a CK type is an archive type of the tenant.</param>
    /// <param name="blueprint">The blueprint whose seed is imported (its <c>archiveActivation</c> block).</param>
    /// <param name="warnings">Collects warnings for the caller's result.</param>
    public static async Task<List<BlueprintArchiveCandidate>> CollectAsync(
        IRuntimeRepository repository,
        IEnumerable<RtEntityTcDto> seedEntities,
        Func<RtCkId<CkTypeId>, bool> isArchive,
        BlueprintMetaRootDto blueprint,
        List<string> warnings)
    {
        var candidates = new List<BlueprintArchiveCandidate>();
        var archives = seedEntities.Where(e => isArchive(e.CkTypeId)).ToList();
        var exclude = (blueprint.ArchiveActivation?.Exclude ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (archives.Count == 0)
        {
            ReportUnmatchedExcludes(blueprint, exclude, [], warnings);
            return candidates;
        }

        var session = await repository.GetSessionAsync().ConfigureAwait(false);
        foreach (var typeGroup in archives.GroupBy(e => e.CkTypeId))
        {
            var withId = typeGroup.Where(e => !e.RtId.Equals(OctoObjectId.Empty)).ToList();
            foreach (var withoutId in typeGroup.Where(e => e.RtId.Equals(OctoObjectId.Empty)))
            {
                warnings.Add(
                    $"Archive '{withoutId.RtWellKnownName ?? typeGroup.Key.ToString()}' of blueprint " +
                    $"'{blueprint.BlueprintId.FullName}' has no rtId in the seed and is not activated automatically; " +
                    "give it an rtId or activate it manually.");
            }

            if (withId.Count == 0)
            {
                continue;
            }

            HashSet<OctoObjectId> existing;
            try
            {
                var found = await repository.GetRtEntitiesByIdAsync(session, typeGroup.Key,
                    withId.Select(e => e.RtId).ToList(), RtEntityQueryOptions.Create()).ConfigureAwait(false);
                existing = found.Items.Select(e => e.RtId).ToHashSet();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never activate what cannot be proven new: treat the whole group as pre-existing.
                warnings.Add(
                    $"Could not check which archives of type '{typeGroup.Key}' are new ({ex.Message}); none of them is " +
                    "activated automatically.");
                existing = withId.Select(e => e.RtId).ToHashSet();
            }

            foreach (var entity in withId)
            {
                var optedOut = exclude.Contains(entity.RtId.ToString()) ||
                               (entity.RtWellKnownName != null && exclude.Contains(entity.RtWellKnownName));
                candidates.Add(new BlueprintArchiveCandidate(entity.RtId, entity.RtWellKnownName, typeGroup.Key,
                    blueprint.BlueprintId, !existing.Contains(entity.RtId), optedOut));
            }
        }

        ReportUnmatchedExcludes(blueprint, exclude, archives, warnings);
        return candidates;
    }

    private static void ReportUnmatchedExcludes(BlueprintMetaRootDto blueprint, HashSet<string> exclude,
        IReadOnlyList<RtEntityTcDto> archives, List<string> warnings)
    {
        foreach (var entry in exclude.Where(x => !archives.Any(a =>
                     string.Equals(a.RtId.ToString(), x, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(a.RtWellKnownName, x, StringComparison.OrdinalIgnoreCase))))
        {
            warnings.Add(
                $"archiveActivation.exclude entry '{entry}' of blueprint '{blueprint.BlueprintId.FullName}' matches no " +
                "archive of the seed.");
        }
    }

    /// <summary>
    /// <c>true</c> when the stream-data backend knows the tenant and stream data is switched off there (AB#7991). Without a
    /// backend (or an unknown tenant) the answer is <c>false</c>: nothing is known, the blueprint is applied as before.
    /// </summary>
    public async Task<bool> IsStreamDataOffAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (servicesProvider == null)
        {
            return false;
        }

        var services = await servicesProvider.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return services is { IsStreamDataEnabled: false };
    }

    /// <summary>
    /// Activates the new, not opted-out archives of <paramref name="candidates" /> and queues the backfill of the new
    /// rollups. Runs after the seed import committed. Never throws for a single archive: the outcome of each is in the
    /// returned list and warnings are handed to <paramref name="warn" />.
    /// </summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="candidates">The archives recorded by <see cref="CollectAsync" />, in seed order.</param>
    /// <param name="warn">Receives one warning text per skipped group, failed archive or backfill that was not queued.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<IReadOnlyList<BlueprintArchiveActivation>> ActivateAsync(
        string tenantId,
        IReadOnlyList<BlueprintArchiveCandidate> candidates,
        Action<string> warn,
        CancellationToken cancellationToken)
    {
        // The same archive can be listed twice (a dependency and the root carry it); the first record wins.
        var unique = candidates
            .GroupBy(c => c.RtId)
            .Select(g => g.First())
            .ToList();
        var results = unique.ToDictionary(c => c.RtId, c => new BlueprintArchiveActivation
        {
            ArchiveRtId = c.RtId.ToString(),
            WellKnownName = c.WellKnownName,
            CkTypeId = c.CkTypeId.ToString(),
            BlueprintId = c.BlueprintId.FullName
        });

        var toActivate = new List<BlueprintArchiveCandidate>();
        var notImported = new List<BlueprintArchiveCandidate>();
        foreach (var candidate in unique)
        {
            var result = results[candidate.RtId];
            if (candidate.NotImported)
            {
                result.Outcome = BlueprintArchiveActivationOutcome.SkippedStreamDataDisabled;
                result.Reason = $"Stream data is not enabled on tenant '{tenantId}'; the archive was not imported because " +
                                "its CK model System.StreamData is not installed there.";
                notImported.Add(candidate);
            }
            else if (!candidate.IsNew)
            {
                result.Outcome = BlueprintArchiveActivationOutcome.SkippedPreExisting;
                result.Reason = "The archive already existed on the tenant; its status was not changed.";
            }
            else if (candidate.OptedOut)
            {
                result.Outcome = BlueprintArchiveActivationOutcome.SkippedOptOut;
                result.Reason = $"Listed in archiveActivation.exclude of blueprint '{candidate.BlueprintId.FullName}'.";
            }
            else
            {
                toActivate.Add(candidate);
            }
        }

        if (notImported.Count > 0)
        {
            warn($"Stream data is not enabled on tenant '{tenantId}': {notImported.Count} archive(s) of the blueprint were " +
                 $"not imported ({Names(notImported)}). Enable stream data, then re-apply the blueprint (force) to create " +
                 "and activate them.");
        }

        if (toActivate.Count == 0)
        {
            return Ordered(unique, results);
        }

        var services = servicesProvider == null
            ? null
            : await servicesProvider.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (services is not { IsStreamDataEnabled: true, LifecycleService: not null, ArchiveStore: not null })
        {
            foreach (var candidate in toActivate)
            {
                var result = results[candidate.RtId];
                result.Outcome = BlueprintArchiveActivationOutcome.SkippedStreamDataDisabled;
                result.Reason = $"Stream data is not enabled on tenant '{tenantId}'; the archive stays Created.";
            }

            warn($"Stream data is not enabled on tenant '{tenantId}': {toActivate.Count} new archive(s) of the blueprint " +
                 $"stay Created ({Names(toActivate)}). Enable stream data and activate them manually.");
            return Ordered(unique, results);
        }

        var lifecycle = services.LifecycleService;
        var archiveStore = services.ArchiveStore;

        // Load what decides the order: the storage shape, and for rollups their sources and aggregations.
        var raw = new List<BlueprintArchiveCandidate>();
        var rollups = new Dictionary<OctoObjectId, RollupArchiveSnapshot>();
        foreach (var candidate in toActivate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = results[candidate.RtId];
            try
            {
                var snapshot = await archiveStore.GetAsync(candidate.RtId).ConfigureAwait(false);
                if (snapshot == null)
                {
                    Fail(result, "The archive was not found after the seed import.", warn);
                    continue;
                }

                if (snapshot.RollupAggregations == null)
                {
                    raw.Add(candidate);
                    continue;
                }

                result.IsRollup = true;
                var rollup = services.RollupStore == null
                    ? null
                    : await services.RollupStore.GetAsync(candidate.RtId).ConfigureAwait(false);
                if (rollup == null)
                {
                    await FailAndMarkAsync(archiveStore, result, candidate.RtId,
                        "Rollup support is not available on this tenant.", warn).ConfigureAwait(false);
                    continue;
                }

                rollups[candidate.RtId] = rollup;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not read archive {ArchiveRtId} after the seed import", candidate.RtId);
                Fail(result, ex.Message, warn);
            }
        }

        var rollupOrder = SortRollupsBySource(toActivate.Where(c => rollups.ContainsKey(c.RtId)).ToList(), rollups,
            out var cyclic);
        foreach (var candidate in cyclic)
        {
            await FailAndMarkAsync(archiveStore, results[candidate.RtId], candidate.RtId,
                "The rollup is part of a source cycle among the new rollups.", warn).ConfigureAwait(false);
        }

        foreach (var candidate in raw)
        {
            await ActivateOneAsync(lifecycle, archiveStore, results[candidate.RtId], candidate.RtId, warn)
                .ConfigureAwait(false);
        }

        foreach (var candidate in rollupOrder)
        {
            var result = results[candidate.RtId];
            var blockedSource = rollups[candidate.RtId].Sources
                .Select(s => results.TryGetValue(s.SourceArchiveRtId, out var r) ? r : null)
                .FirstOrDefault(r => r != null &&
                                     r.Outcome != BlueprintArchiveActivationOutcome.Activated &&
                                     r.Outcome != BlueprintArchiveActivationOutcome.SkippedPreExisting);
            if (blockedSource is { } source)
            {
                await FailAndMarkAsync(archiveStore, result, candidate.RtId,
                    $"Its source archive {Label(source)} was not activated ({source.Outcome}).", warn)
                    .ConfigureAwait(false);
                continue;
            }

            await ActivateOneAsync(lifecycle, archiveStore, result, candidate.RtId, warn).ConfigureAwait(false);
        }

        // Backfills after all activations, in source order: a backfill reads its sources, which are activated now.
        foreach (var candidate in rollupOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = results[candidate.RtId];
            if (result.Outcome != BlueprintArchiveActivationOutcome.Activated)
            {
                continue;
            }

            await QueueBackfillAsync(services, rollups[candidate.RtId], result, results, warn, cancellationToken)
                .ConfigureAwait(false);
        }

        return Ordered(unique, results);
    }

    private async Task ActivateOneAsync(IArchiveLifecycleService lifecycle, IArchiveRuntimeStore archiveStore,
        BlueprintArchiveActivation result, OctoObjectId rtId, Action<string> warn)
    {
        try
        {
            await lifecycle.ActivateAsync(rtId).ConfigureAwait(false);
            result.Outcome = BlueprintArchiveActivationOutcome.Activated;
            result.Reason = null;
            logger.LogInformation("Blueprint {BlueprintId} activated archive {ArchiveRtId}", result.BlueprintId, rtId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Blueprint {BlueprintId} could not activate archive {ArchiveRtId}",
                result.BlueprintId, rtId);
            await FailAndMarkAsync(archiveStore, result, rtId, ex.Message, warn).ConfigureAwait(false);
        }
    }

    private async Task QueueBackfillAsync(StreamDataTenantServices services, RollupArchiveSnapshot rollup,
        BlueprintArchiveActivation result, IReadOnlyDictionary<OctoObjectId, BlueprintArchiveActivation> results,
        Action<string> warn, CancellationToken cancellationToken)
    {
        try
        {
            if (rollup.Aggregations.Any(a => a.Function is CkRollupFunction.TimeWeightedAvg or CkRollupFunction.StateDuration)
                && await HasRawSourceAsync(services.ArchiveStore!, rollup).ConfigureAwait(false))
            {
                result.Backfill = BlueprintRollupBackfillOutcome.NotQueued;
                result.BackfillReason =
                    $"The rollup uses TimeWeightedAvg or StateDuration over a raw source; its backfill would trip the " +
                    $"CrateDB memory breaker over a long history ({LocfGuardWorkItem}). The rollup is activated and " +
                    "aggregates new buckets; backfill its history only after " + LocfGuardWorkItem + " is fixed.";
                warn($"Backfill of rollup {Label(result)} not queued: {result.BackfillReason}");
                return;
            }

            if (services.RecomputeOrchestrator == null)
            {
                result.Backfill = BlueprintRollupBackfillOutcome.NotQueued;
                result.BackfillReason = "Rollup recompute is not available on this tenant.";
                warn($"Backfill of rollup {Label(result)} not queued: {result.BackfillReason}");
                return;
            }

            var job = await services.RecomputeOrchestrator
                .EnqueueBackfillFromSourceAsync(rollup.RtId, cancellationToken).ConfigureAwait(false);
            if (job == null)
            {
                result.Backfill = BlueprintRollupBackfillOutcome.NothingToBackfill;
                var pendingSource = rollup.Sources
                    .Select(s => results.TryGetValue(s.SourceArchiveRtId, out var r) ? r : null)
                    .FirstOrDefault(r => r is { Backfill: BlueprintRollupBackfillOutcome.Queued });
                result.BackfillReason = pendingSource == null
                    ? "The sources hold no data yet; nothing to backfill."
                    : $"The sources hold no data yet; the rollup fills when the backfill of its source {Label(pendingSource)} " +
                      "has been propagated.";
                return;
            }

            result.Backfill = BlueprintRollupBackfillOutcome.Queued;
            result.BackfillJobRtId = job.RtId.ToString();
            logger.LogInformation("Blueprint {BlueprintId} queued backfill job {JobRtId} for rollup {RollupRtId}",
                result.BlueprintId, job.RtId, rollup.RtId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Blueprint {BlueprintId} could not queue the backfill of rollup {RollupRtId}",
                result.BlueprintId, rollup.RtId);
            result.Backfill = BlueprintRollupBackfillOutcome.Failed;
            result.BackfillReason = ex.Message;
            warn($"Backfill of rollup {Label(result)} could not be queued: {ex.Message}");
        }
    }

    private static async Task<bool> HasRawSourceAsync(IArchiveRuntimeStore archiveStore, RollupArchiveSnapshot rollup)
    {
        foreach (var source in rollup.Sources)
        {
            var snapshot = await archiveStore.GetAsync(source.SourceArchiveRtId).ConfigureAwait(false);
            // An unreadable source is treated as raw: the guard errs on the side of not starting the heavy statement.
            if (snapshot is not { UsesWindowedStorage: true })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Topologically sorts the new rollups so that each comes after the new rollups it reads from (seed order among
    /// independent ones). Rollups on a cycle are returned in <paramref name="cyclic" />.
    /// </summary>
    internal static List<BlueprintArchiveCandidate> SortRollupsBySource(
        IReadOnlyList<BlueprintArchiveCandidate> rollupCandidates,
        IReadOnlyDictionary<OctoObjectId, RollupArchiveSnapshot> snapshots,
        out List<BlueprintArchiveCandidate> cyclic)
    {
        var ids = rollupCandidates.Select(c => c.RtId).ToHashSet();
        var pending = rollupCandidates.ToList();
        var placed = new HashSet<OctoObjectId>();
        var ordered = new List<BlueprintArchiveCandidate>();

        bool progress;
        do
        {
            progress = false;
            foreach (var candidate in pending.ToList())
            {
                var dependsOnPending = snapshots[candidate.RtId].Sources
                    .Any(s => ids.Contains(s.SourceArchiveRtId) && !placed.Contains(s.SourceArchiveRtId) &&
                              !s.SourceArchiveRtId.Equals(candidate.RtId));
                if (dependsOnPending)
                {
                    continue;
                }

                ordered.Add(candidate);
                placed.Add(candidate.RtId);
                pending.Remove(candidate);
                progress = true;
            }
        } while (progress && pending.Count > 0);

        cyclic = pending;
        return ordered;
    }

    private async Task FailAndMarkAsync(IArchiveRuntimeStore archiveStore, BlueprintArchiveActivation result,
        OctoObjectId rtId, string reason, Action<string> warn)
    {
        Fail(result, reason, warn);

        // Leave the archive retryable: RetryArchiveActivation only accepts Failed. A failure of the lifecycle's own
        // provisioning already set it; a refused validation leaves it Created.
        try
        {
            var snapshot = await archiveStore.GetAsync(rtId).ConfigureAwait(false);
            if (snapshot is { Status: CkArchiveStatus.Created or CkArchiveStatus.Disabled })
            {
                await archiveStore.SetStatusAsync(rtId, CkArchiveStatus.Failed).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not mark archive {ArchiveRtId} as Failed", rtId);
        }
    }

    private static void Fail(BlueprintArchiveActivation result, string reason, Action<string> warn)
    {
        result.Outcome = BlueprintArchiveActivationOutcome.Failed;
        result.Reason = reason;
        warn($"Archive {Label(result)} of blueprint '{result.BlueprintId}' could not be activated: {reason} " +
             "The archive is Failed; retry with RetryArchiveActivation once the cause is fixed.");
    }

    private static IReadOnlyList<BlueprintArchiveActivation> Ordered(
        IEnumerable<BlueprintArchiveCandidate> candidates,
        IReadOnlyDictionary<OctoObjectId, BlueprintArchiveActivation> results) =>
        candidates.Select(c => results[c.RtId]).ToList();

    private static string Names(IEnumerable<BlueprintArchiveCandidate> candidates) =>
        string.Join(", ", candidates.Select(c => c.WellKnownName ?? c.RtId.ToString()));

    private static string Label(BlueprintArchiveActivation result) =>
        result.WellKnownName == null ? result.ArchiveRtId : $"'{result.WellKnownName}' ({result.ArchiveRtId})";
}
