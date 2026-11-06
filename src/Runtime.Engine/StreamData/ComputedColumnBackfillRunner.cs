using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
/// Runs computed-column backfills as jobs (AB#8002). Adding a computed column or changing its formula
/// used to backfill every existing row inside the request; on an archive with a real history that
/// outlived the client timeout, the abort cancelled the CrateDB job and left the column
/// <c>Failed</c>, and the long statements starved the service's request threads. The lifecycle
/// service now only prepares the column and queues a job; this runner executes it — from the
/// background drain, never under a request's cancellation token.
/// </summary>
/// <remarks>
/// <para>
/// <b>The queue is the archive state.</b> A column waits for its backfill while it is
/// <see cref="ComputedColumnState.Backfilling"/> (add, retry) or carries a pending formula (formula
/// change). Both are persisted on the archive, so queued work survives a restart. The job entity is
/// the observable record of a run: it is created when the work is queued (so the caller can poll it)
/// and adopted by the drain; a column whose job got lost gets a new one.
/// </para>
/// <para>
/// <b>One at a time.</b> The drain runs the backfills of a tenant sequentially, oldest job first, so
/// several columns added at once queue instead of competing for CrateDB.
/// </para>
/// <para>
/// <b>Interruption is not failure.</b> A run cut short by shutdown hands its job back as
/// <see cref="RecomputeJobState.Pending"/> and leaves the column queued; the next drain repeats it
/// (the backfill overwrites every row, so a repeat is harmless). A run whose process died is
/// recognised by its silent heartbeat and adopted the same way.
/// </para>
/// </remarks>
public sealed class ComputedColumnBackfillRunner
{
    /// <summary>Default interval at which a running job stamps its heartbeat and progress.</summary>
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Default age of a running job's heartbeat after which the job is taken to belong to a dead
    /// process and is run again.
    /// </summary>
    public static readonly TimeSpan DefaultStaleJobTimeout = TimeSpan.FromMinutes(5);

    private const int JobHistoryScan = 200;

    private readonly IArchiveRuntimeStore _store;
    private readonly IStreamDataRepository _repository;
    private readonly IRecomputeJobStore? _jobStore;
    private readonly ILogger _logger;
    private readonly Func<DateTime> _clock;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _staleJobTimeout;

    /// <summary>
    /// Constructs the runner for one tenant. Without a <paramref name="jobStore"/> the backfills still
    /// run, they only leave no job record.
    /// </summary>
    public ComputedColumnBackfillRunner(
        IArchiveRuntimeStore store,
        IStreamDataRepository repository,
        IRecomputeJobStore? jobStore,
        ILogger logger,
        Func<DateTime>? clock = null,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? staleJobTimeout = null)
    {
        if (heartbeatInterval is { } interval && interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatInterval), interval, "Heartbeat interval must be positive.");
        }

        if (staleJobTimeout is { } stale && stale <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(staleJobTimeout), stale, "Stale-job timeout must be positive.");
        }

        _store = store;
        _repository = repository;
        _jobStore = jobStore;
        _logger = logger;
        _clock = clock ?? (() => DateTime.UtcNow);
        _heartbeatInterval = heartbeatInterval ?? DefaultHeartbeatInterval;
        _staleJobTimeout = staleJobTimeout ?? DefaultStaleJobTimeout;
    }

    /// <summary>
    /// Persists a new backfill job for a column, or returns <c>null</c> without a job store.
    /// </summary>
    public async Task<RecomputeJobSnapshot?> CreateJobAsync(
        OctoObjectId archiveRtId, string columnName, RecomputeJobState state, RecomputeTrigger trigger)
    {
        if (_jobStore is null)
        {
            return null;
        }

        var job = RecomputeJobSnapshot.ForComputedColumn(archiveRtId, columnName, state, trigger, _clock());
        var rtId = await _jobStore.CreateAsync(job);
        return job with { RtId = rtId };
    }

    /// <summary>
    /// Marks a job that never ran as failed — used when queuing the work failed after the job was
    /// created. A no-op for <c>null</c>.
    /// </summary>
    public async Task FailUnstartedJobAsync(RecomputeJobSnapshot? job, string reason)
    {
        if (_jobStore is null || job is null)
        {
            return;
        }

        var now = _clock();
        await _jobStore.UpdateAsync(job with
        {
            State = RecomputeJobState.Failed, FinishedAt = now, DurationMs = 0, ErrorReason = reason,
        });
    }

    /// <summary>
    /// The newest active (Pending / Running / Swapping) backfill job of a column, or <c>null</c>.
    /// </summary>
    public async Task<RecomputeJobSnapshot?> GetActiveJobAsync(OctoObjectId archiveRtId, string columnName)
    {
        var jobs = await GetActiveJobsAsync(archiveRtId, columnName);
        return jobs.Count == 0 ? null : jobs[0];
    }

    /// <summary>
    /// Whether a job is being run right now by a live process: Running with a heartbeat younger than
    /// the stale-job timeout.
    /// </summary>
    public bool IsLive(RecomputeJobSnapshot job)
    {
        if (job.State is not (RecomputeJobState.Running or RecomputeJobState.Swapping))
        {
            return false;
        }

        var heartbeat = job.LastProgressAt ?? job.StartedAt;
        return heartbeat is not null && _clock() - heartbeat.Value <= _staleJobTimeout;
    }

    /// <summary>
    /// Runs every queued backfill of the tenant, one after the other, oldest job first. Returns the
    /// number that completed. A failing backfill is recorded on its job and column and does not stop
    /// the drain; cancellation does.
    /// </summary>
    public async Task<int> DrainAsync(CancellationToken cancellationToken)
    {
        var queue = new List<(OctoObjectId ArchiveRtId, string Column, RecomputeJobSnapshot? Job)>();
        var repairs = new List<RecomputeJobSnapshot>();
        await foreach (var archive in _store.EnumerateAsync().WithCancellation(cancellationToken))
        {
            if (archive.Status != CkArchiveStatus.Activated)
            {
                // A disabled archive keeps its queued columns; they run once it is activated again.
                continue;
            }

            // AB#8331: queued repairs of active columns. One job-store read per archive that has an active
            // computed column; archives without one cost nothing.
            if (_jobStore is not null
                && archive.Columns.Any(c => c.IsComputed && c.ComputedState == ComputedColumnState.Active))
            {
                var archiveJobs = await _jobStore.GetForArchiveAsync(archive.RtId, JobHistoryScan);
                repairs.AddRange(archiveJobs.Where(j => j.IsComputedColumnRepair && j.IsActive && !IsLive(j)));
            }

            foreach (var column in archive.Columns)
            {
                if (!NeedsBackfill(column) || column.Name is null)
                {
                    continue;
                }

                var jobs = await GetActiveJobsAsync(archive.RtId, column.Name);
                if (jobs.Any(IsLive))
                {
                    // Already running — in this process (an inline add) or another replica.
                    continue;
                }

                queue.Add((archive.RtId, column.Name, jobs.Count == 0 ? null : jobs[0]));
            }
        }

        // Oldest job first (object ids are time-ordered); columns without a job (lost, or queued by
        // an older version) go last.
        var ordered = queue
            .OrderBy(item => item.Job is null ? 1 : 0)
            .ThenBy(item => item.Job?.RtId.ToString(), StringComparer.Ordinal)
            .ToList();

        var completed = 0;
        foreach (var (archiveRtId, column, job) in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Re-check right before running: the queue was built before the earlier backfills of this
            // drain ran, so another process (or an inline add) may have started this column meanwhile.
            var current = await GetActiveJobsAsync(archiveRtId, column);
            if (current.Any(IsLive))
            {
                continue;
            }

            if (current.Count == 0 && job is not null && !await StillQueuedAsync(archiveRtId, column))
            {
                continue; // finished by someone else since the queue was built
            }

            var adopted = current.Count > 0
                ? current[0]
                : await CreateJobAsync(archiveRtId, column, RecomputeJobState.Pending, RecomputeTrigger.Periodic);
            var result = await RunAsync(archiveRtId, column, adopted, rethrowFailure: false, cancellationToken);
            if (result?.State == RecomputeJobState.Completed || (result is null && _jobStore is null))
            {
                completed++;
            }
        }

        // Repairs after the backfills, oldest first. Re-checked right before running like a backfill.
        foreach (var repair in repairs.OrderBy(j => j.RtId.ToString(), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = _jobStore is null ? null : await _jobStore.GetAsync(repair.RtId);
            if (current is null || !current.IsActive || IsLive(current))
            {
                continue;
            }

            var result = await RunRepairAsync(current, cancellationToken);
            if (result.State == RecomputeJobState.Completed)
            {
                completed++;
            }
        }

        return completed;
    }

    /// <summary>
    /// Persists a new repair job for an active computed column over <c>[from, to)</c> (AB#8331), or returns
    /// <c>null</c> without a job store.
    /// </summary>
    public async Task<RecomputeJobSnapshot?> CreateRepairJobAsync(
        OctoObjectId archiveRtId, string columnName, DateTime from, DateTime to)
    {
        if (_jobStore is null)
        {
            return null;
        }

        var job = RecomputeJobSnapshot.ForComputedColumnRepair(
            archiveRtId, columnName, from, to, RecomputeTrigger.Manual, _clock());
        var rtId = await _jobStore.CreateAsync(job);
        return job with { RtId = rtId };
    }

    /// <summary>The newest active (Pending / Running) repair job of a column, or <c>null</c> (AB#8331).</summary>
    public async Task<RecomputeJobSnapshot?> GetActiveRepairJobAsync(OctoObjectId archiveRtId, string columnName)
    {
        if (_jobStore is null)
        {
            return null;
        }

        var jobs = await _jobStore.GetForArchiveAsync(archiveRtId, JobHistoryScan);
        return jobs.FirstOrDefault(j => j.IsActive && j.IsComputedColumnRepair
                                        && string.Equals(j.ComputedColumnName, columnName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Runs one repair job (AB#8331): re-evaluates the NULL cells of the active column whose operands are not
    /// NULL in <c>[RangeStart, RangeEnd)</c> of the job. The job's <see cref="RecomputeJobSnapshot.RangeStart"/>
    /// is the checkpoint: a run cut short resumes there. The column state is never changed — a failed repair
    /// leaves the column Active with the cells it had; the job records the failure and how far it got.
    /// Cancellation hands the job back as Pending and is thrown.
    /// </summary>
    public async Task<RecomputeJobSnapshot> RunRepairAsync(RecomputeJobSnapshot job, CancellationToken cancellationToken)
    {
        var columnName = job.ComputedColumnName
                         ?? throw new ArgumentException("Not a computed-column repair job.", nameof(job));
        var archiveRtId = job.ArchiveRtId;
        var startedAt = _clock();
        var resumeFrom = job.RangeStart;
        var rowsBefore = job.RowsProcessed ?? 0;
        job = (await UpdateJobAsync(job, j => j with
        {
            State = RecomputeJobState.Running,
            StartedAt = startedAt,
            FinishedAt = null,
            DurationMs = null,
            ErrorReason = null,
            LastProgressAt = startedAt,
        }))!;

        var progress = new RowProgress(rowsBefore, resumeFrom);
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = RunHeartbeatAsync(job, progress, heartbeatStop.Token);
        try
        {
            var snapshot = await _store.GetAsync(archiveRtId) ?? throw new ArchiveNotFoundException(archiveRtId);
            if (snapshot.Status != CkArchiveStatus.Activated)
            {
                throw new ArchiveNotActivatedException(archiveRtId, snapshot.Status);
            }

            var column = FindComputed(snapshot, columnName)
                ?? throw new InvalidOperationException(
                    $"Computed column '{columnName}' no longer exists on archive {archiveRtId}.");
            if (column.ComputedState != ComputedColumnState.Active)
            {
                throw new InvalidOperationException(
                    $"Computed column '{columnName}' on archive {archiveRtId} is {column.ComputedState}, not Active; its backfill writes every row.");
            }

            _logger.LogInformation(
                "Repair of computed column '{Column}' on archive {Archive} runs over [{From:O}, {To:O}).",
                columnName, archiveRtId, resumeFrom, job.RangeEnd);
            await _repository.RepairComputedColumnAsync(snapshot, columnName, resumeFrom, job.RangeEnd, progress, cancellationToken);

            await StopHeartbeatAsync(heartbeatStop, heartbeat);
            var finishedAt = _clock();
            job = (await UpdateJobAsync(job, j => j with
            {
                State = RecomputeJobState.Completed,
                RowsProcessed = progress.RowsAsInt,
                RangeStart = progress.CompletedThrough ?? j.RangeStart,
                FinishedAt = finishedAt,
                DurationMs = (int)(finishedAt - startedAt).TotalMilliseconds,
                LastProgressAt = finishedAt,
            }))!;
            _logger.LogInformation(
                "Computed column '{Column}' on archive {Archive} repaired ({Rows} rows re-evaluated, {DurationMs} ms).",
                columnName, archiveRtId, progress.Rows, (int)(finishedAt - startedAt).TotalMilliseconds);
            return job;
        }
        catch (Exception interruption) when (cancellationToken.IsCancellationRequested)
        {
            await StopHeartbeatAsync(heartbeatStop, heartbeat);
            await UpdateJobAsync(job, j => j with
            {
                State = RecomputeJobState.Pending,
                StartedAt = null,
                RowsProcessed = progress.RowsAsInt,
                RangeStart = progress.CompletedThrough ?? j.RangeStart,
                LastProgressAt = _clock(),
                ErrorReason = "Interrupted by a shutdown; the repair resumes from its checkpoint on the next start.",
            });
            if (interruption is OperationCanceledException)
            {
                throw;
            }

            throw new OperationCanceledException(
                $"Repair of computed column '{columnName}' interrupted by shutdown.", interruption, cancellationToken);
        }
        catch (Exception ex)
        {
            await StopHeartbeatAsync(heartbeatStop, heartbeat);
            var finishedAt = _clock();
            var checkpoint = progress.CompletedThrough ?? resumeFrom;
            var reason =
                $"Repair of computed column '{columnName}' failed; rows from {checkpoint:O} to {job.RangeEnd:O} are not repaired " +
                $"(re-queue the repair to continue there): {ex.Message}";
            job = (await UpdateJobAsync(job, j => j with
            {
                State = RecomputeJobState.Failed,
                RowsProcessed = progress.RowsAsInt,
                RangeStart = checkpoint,
                FinishedAt = finishedAt,
                DurationMs = (int)(finishedAt - startedAt).TotalMilliseconds,
                ErrorReason = reason,
                LastProgressAt = finishedAt,
            }))!;
            _logger.LogError(ex, "Repair of computed column '{Column}' on archive {Archive} failed.", columnName, archiveRtId);
            return job;
        }
    }

    /// <summary>
    /// Runs the backfill of one column under <paramref name="job"/> (which may be <c>null</c> without a
    /// job store) and ends the column <c>Active</c> (add / retry), swaps its formula version (formula
    /// change), or records the failure: an added column goes <c>Failed</c>, a formula change reverts
    /// to the previous formula. With <paramref name="rethrowFailure"/> the failure is thrown after it
    /// was recorded (the inline add path); otherwise it is logged. Cancellation hands the job back as
    /// Pending and is always thrown. Returns the job as it ended.
    /// </summary>
    public async Task<RecomputeJobSnapshot?> RunAsync(
        OctoObjectId archiveRtId,
        string columnName,
        RecomputeJobSnapshot? job,
        bool rethrowFailure,
        CancellationToken cancellationToken)
    {
        var startedAt = _clock();
        // Checkpoint (AB#8002): a job that an earlier run had to give up (shutdown, rollout, dead process)
        // carries the last completed time slice in RangeStart; resume there instead of starting over.
        // A new job starts at UnixEpoch, i.e. from the first row.
        var resumeFrom = job is { RangeStart: var checkpoint } && checkpoint > DateTime.UnixEpoch
            ? checkpoint
            : (DateTime?)null;
        var rowsBefore = resumeFrom is null ? 0L : job?.RowsProcessed ?? 0;
        job = await UpdateJobAsync(job, j => j with
        {
            State = RecomputeJobState.Running,
            StartedAt = startedAt,
            FinishedAt = null,
            DurationMs = null,
            ErrorReason = null,
            RowsProcessed = (int)Math.Min(int.MaxValue, rowsBefore),
            LastProgressAt = startedAt,
        });

        if (resumeFrom is not null)
        {
            _logger.LogInformation(
                "Backfill of computed column '{Column}' on archive {Archive} resumes at {Checkpoint:O} ({Rows} rows already written).",
                columnName, archiveRtId, resumeFrom, rowsBefore);
        }

        var progress = new RowProgress(rowsBefore, resumeFrom);
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = RunHeartbeatAsync(job, progress, heartbeatStop.Token);
        var formulaChange = false;
        // Only a run that got as far as writing rows changes the column on failure. A precondition that
        // fails before (column already Active, removed, archive not activated) must never demote a
        // healthy column to Failed; it only fails the job.
        var backfillStarted = false;

        try
        {
            var snapshot = await _store.GetAsync(archiveRtId) ?? throw new ArchiveNotFoundException(archiveRtId);
            var column = FindComputed(snapshot, columnName)
                ?? throw new InvalidOperationException(
                    $"Computed column '{columnName}' no longer exists on archive {archiveRtId}.");

            if (snapshot.Status != CkArchiveStatus.Activated)
            {
                throw new ArchiveNotActivatedException(archiveRtId, snapshot.Status);
            }

            formulaChange = column.HasPendingFormula;
            if (!formulaChange && column.ComputedState != ComputedColumnState.Backfilling)
            {
                throw new InvalidOperationException(
                    $"Computed column '{columnName}' on archive {archiveRtId} is {column.ComputedState}, not queued for a backfill.");
            }

            // The physical column exists: the add (and the retry) provision it before the column is
            // queued, a formula change provisions the pending column before it marks the formula.
            backfillStarted = true;
            await _repository.BackfillComputedColumnAsync(snapshot, columnName, resumeFrom, progress, cancellationToken);

            // The column may have been removed or re-formulated while the rows were written.
            var after = await _store.GetAsync(archiveRtId) ?? throw new ArchiveNotFoundException(archiveRtId);
            var current = FindComputed(after, columnName)
                ?? throw new InvalidOperationException(
                    $"Computed column '{columnName}' was removed from archive {archiveRtId} while it was backfilled.");

            if (formulaChange)
            {
                if (!string.Equals(current.PendingFormula, column.PendingFormula, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"The formula of computed column '{columnName}' on archive {archiveRtId} changed while it was backfilled.");
                }

                // Atomic swap: the version pointer flip is the commit (concept §8).
                await _store.SwapComputedColumnFormulaAsync(
                    archiveRtId, columnName, column.PendingFormula!, column.ComputedVersion + 1);
            }
            else
            {
                // The single state write that makes the column visible (concept §8).
                await _store.SetComputedColumnStateAsync(archiveRtId, columnName, ComputedColumnState.Active);
            }

            await StopHeartbeatAsync(heartbeatStop, heartbeat);
            var finishedAt = _clock();
            job = await UpdateJobAsync(job, j => j with
            {
                State = RecomputeJobState.Completed,
                RowsProcessed = progress.RowsAsInt,
                FinishedAt = finishedAt,
                DurationMs = (int)(finishedAt - startedAt).TotalMilliseconds,
                LastProgressAt = finishedAt,
            });

            _logger.LogInformation(
                "Computed column '{Column}' on archive {Archive} backfilled ({Rows} rows, {DurationMs} ms){Swap}.",
                columnName, archiveRtId, progress.Rows, (int)(finishedAt - startedAt).TotalMilliseconds,
                formulaChange ? $"; formula version {column.ComputedVersion + 1} is live" : "; now Active");
            return job;
        }
        // Any exception while the run's token is cancelled is the shutdown, not a failure: cancelling a running
        // CrateDB statement surfaces as the server's "XX000: Job killed" (a PostgresException), not as an
        // OperationCanceledException (test-2, 2026-11-01: a rollout failed DischargePower this way).
        catch (Exception interruption) when (cancellationToken.IsCancellationRequested)
        {
            await StopHeartbeatAsync(heartbeatStop, heartbeat);
            // Shutdown: the column stays queued and the job goes back to Pending, so the next drain
            // picks both up again without counting a failure.
            // Keep the checkpoint and the row count: the next run resumes where this one stopped.
            await UpdateJobAsync(job, j => j with
            {
                State = RecomputeJobState.Pending,
                StartedAt = null,
                RowsProcessed = progress.RowsAsInt,
                RangeStart = progress.CompletedThrough ?? j.RangeStart,
                LastProgressAt = _clock(),
                ErrorReason = "Interrupted by a shutdown; the backfill resumes from its checkpoint on the next start.",
            });
            _logger.LogInformation(
                "Backfill of computed column '{Column}' on archive {Archive} interrupted by shutdown; it is queued again.",
                columnName, archiveRtId);
            if (interruption is OperationCanceledException)
            {
                throw;
            }

            // Callers (the drain, the hosted service) treat cancellation as a shutdown; give them one.
            throw new OperationCanceledException(
                $"Backfill of computed column '{columnName}' interrupted by shutdown.", interruption, cancellationToken);
        }
        catch (Exception ex)
        {
            await StopHeartbeatAsync(heartbeatStop, heartbeat);
            if (backfillStarted)
            {
                await RecordColumnFailureAsync(archiveRtId, columnName, formulaChange);
            }

            var finishedAt = _clock();
            var reason = $"Backfill of computed column '{columnName}' failed: {ex.Message}";
            job = await UpdateJobAsync(job, j => j with
            {
                State = RecomputeJobState.Failed,
                RowsProcessed = progress.RowsAsInt,
                FinishedAt = finishedAt,
                DurationMs = (int)(finishedAt - startedAt).TotalMilliseconds,
                ErrorReason = reason,
                LastProgressAt = finishedAt,
            });

            _logger.LogError(ex,
                formulaChange
                    ? "Formula-change backfill of computed column '{Column}' on archive {Archive} failed; reverted to the previous formula."
                    : "Backfill of computed column '{Column}' on archive {Archive} failed; column marked Failed.",
                columnName, archiveRtId);

            if (rethrowFailure)
            {
                throw;
            }

            return job;
        }
    }

    /// <summary>
    /// Whether a column waits for a backfill: an added (or retried) column in Backfilling, or a
    /// formula change in progress.
    /// </summary>
    public static bool NeedsBackfill(CkArchiveColumnSpec column) =>
        column.IsComputed && (column.HasPendingFormula || column.ComputedState == ComputedColumnState.Backfilling);

    private async Task<bool> StillQueuedAsync(OctoObjectId archiveRtId, string columnName)
    {
        var snapshot = await _store.GetAsync(archiveRtId);
        return snapshot is { Status: CkArchiveStatus.Activated }
               && FindComputed(snapshot, columnName) is { } column
               && NeedsBackfill(column);
    }

    private async Task RecordColumnFailureAsync(OctoObjectId archiveRtId, string columnName, bool formulaChange)
    {
        try
        {
            var snapshot = await _store.GetAsync(archiveRtId);
            if (snapshot is null || FindComputed(snapshot, columnName) is null)
            {
                return; // the archive or the column is gone; nothing to mark
            }

            if (formulaChange)
            {
                await _store.ClearPendingFormulaAsync(archiveRtId, columnName);
            }
            else
            {
                await _store.SetComputedColumnStateAsync(archiveRtId, columnName, ComputedColumnState.Failed);
            }
        }
        catch (Exception ex)
        {
            // The job still records the failure; the column stays queued and is tried again.
            _logger.LogWarning(ex,
                "Could not record the failed backfill on computed column '{Column}' of archive {Archive}.",
                columnName, archiveRtId);
        }
    }

    private async Task<List<RecomputeJobSnapshot>> GetActiveJobsAsync(OctoObjectId archiveRtId, string columnName)
    {
        if (_jobStore is null)
        {
            return new List<RecomputeJobSnapshot>();
        }

        var jobs = await _jobStore.GetForArchiveAsync(archiveRtId, JobHistoryScan);
        return jobs
            .Where(j => j.IsActive && !j.IsComputedColumnRepair
                        && string.Equals(j.ComputedColumnName, columnName, StringComparison.Ordinal))
            .ToList();
    }

    private async Task<RecomputeJobSnapshot?> UpdateJobAsync(
        RecomputeJobSnapshot? job, Func<RecomputeJobSnapshot, RecomputeJobSnapshot> change)
    {
        if (_jobStore is null || job is null)
        {
            return job;
        }

        var updated = change(job);
        try
        {
            await _jobStore.UpdateAsync(updated);
        }
        catch (Exception ex)
        {
            // The job record is observability; a failed write must not fail or hide the backfill.
            _logger.LogWarning(ex, "Could not update computed-column backfill job {JobId}.", job.RtId);
        }

        return updated;
    }

    private async Task RunHeartbeatAsync(RecomputeJobSnapshot? job, RowProgress progress, CancellationToken stop)
    {
        if (_jobStore is null || job is null)
        {
            return;
        }

        try
        {
            while (true)
            {
                await Task.Delay(_heartbeatInterval, stop);
                var rows = progress.RowsAsInt;
                var checkpoint = progress.CompletedThrough;
                job = await UpdateJobAsync(job, j => j with
                {
                    RowsProcessed = rows,
                    RangeStart = checkpoint ?? j.RangeStart,
                    LastProgressAt = _clock(),
                });
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // stopped by the run
        }
    }

    private static async Task StopHeartbeatAsync(CancellationTokenSource stop, Task heartbeat)
    {
        await stop.CancelAsync();
        await heartbeat;
    }

    private static CkArchiveColumnSpec? FindComputed(ArchiveSnapshot snapshot, string columnName) =>
        snapshot.Columns.FirstOrDefault(c => c.IsComputed && string.Equals(c.Name, columnName, StringComparison.Ordinal));

    /// <summary>
    /// Thread-safe running row count and checkpoint reported by the repository after every chunk. Counts
    /// on from the rows an interrupted earlier run had written; the checkpoint never moves backwards.
    /// </summary>
    private sealed class RowProgress(long rowsBefore, DateTime? resumeFrom) : IProgress<ComputedColumnBackfillProgress>
    {
        private readonly object _lock = new();
        private readonly long _rowsBefore = rowsBefore;
        private long _rows = rowsBefore;
        private DateTime? _completedThrough = resumeFrom;

        public long Rows
        {
            get
            {
                lock (_lock)
                {
                    return _rows;
                }
            }
        }

        public int RowsAsInt => (int)Math.Min(int.MaxValue, Rows);

        public DateTime? CompletedThrough
        {
            get
            {
                lock (_lock)
                {
                    return _completedThrough;
                }
            }
        }

        public void Report(ComputedColumnBackfillProgress value)
        {
            lock (_lock)
            {
                _rows = _rowsBefore + value.Rows;
                if (value.CompletedThrough is { } through && (_completedThrough is null || through > _completedThrough))
                {
                    _completedThrough = through;
                }
            }
        }
    }
}
