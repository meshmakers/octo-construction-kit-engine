#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using System;
using System.Collections.Generic;
using System.Linq;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// What the rollup validator checks (AB#5967 slice 2; Gerald's consistency stories of 2026-10-13). Every check uses counts
/// and MAX/MIN statements only: no row is exported.
/// </summary>
public enum RollupConsistencyCheck
{
    /// <summary>(a) A pointer entry names a generation that has no rows in its range.</summary>
    PointerWithoutRows = 0,

    /// <summary>(b) Rows of a generation above the one the pointer selects, while no recompute is running for the rollup.</summary>
    OrphansAbovePointer = 1,

    /// <summary>(c) The watermark in the metadata lies ahead of the last rollup row although the source holds data in between.</summary>
    WatermarkAheadOfRows = 2,

    /// <summary>(d) A rollup archive without its generation map table.</summary>
    MissingGenerationMap = 3
}

/// <summary>One thing the validator found wrong with a rollup.</summary>
/// <param name="Check">Which check failed.</param>
/// <param name="Message">English text for logs, reports and the CLI.</param>
/// <param name="RangeFromUtc">Inclusive start of the range concerned, when there is one.</param>
/// <param name="RangeToUtc">Exclusive end of the range concerned, when there is one.</param>
/// <param name="Generation">The generation concerned (the pointer's for (a), the highest orphan for (b)).</param>
/// <param name="Rows">Rows concerned (orphans for (b), source rows in the gap for (c)).</param>
/// <param name="Gap">For (c): watermark minus the end of the last rollup row.</param>
public sealed record RollupConsistencyFinding(
    RollupConsistencyCheck Check,
    string Message,
    DateTime? RangeFromUtc = null,
    DateTime? RangeToUtc = null,
    long? Generation = null,
    long? Rows = null,
    TimeSpan? Gap = null);

/// <summary>Why the validator could not check a rollup (or a part of it).</summary>
public enum RollupNotCheckedCause
{
    /// <summary>The rollup has no table (status other than Created): a restore that failed to load it leaves this state.</summary>
    NoLiveTable = 0,

    /// <summary>The rollup was never activated (status Created) and has no table.</summary>
    NeverActivated = 1,

    /// <summary>A source archive of the rollup is missing (no entity, or an activated source without a table), so the watermark check cannot be decided.</summary>
    SourceMissing = 2
}

/// <summary>What the validator could not check of a rollup, and why.</summary>
/// <param name="Cause">The kind of gap.</param>
/// <param name="Reason">English text for logs, reports and the CLI.</param>
public sealed record RollupNotChecked(RollupNotCheckedCause Cause, string Reason);

/// <summary>The validator's result for one rollup.</summary>
/// <param name="RollupRtId">The rollup.</param>
/// <param name="Findings">What the checks that could run found wrong.</param>
/// <param name="NotChecked">Set when the rollup (or a part of it) could not be checked; <c>null</c> when every check ran.</param>
public sealed record RollupConsistencyEntry(
    OctoObjectId RollupRtId, IReadOnlyList<RollupConsistencyFinding> Findings, RollupNotChecked? NotChecked = null)
{
    /// <summary>True when a check could not run (see <see cref="NotChecked"/>).</summary>
    public bool IsNotChecked => NotChecked is not null;

    /// <summary>True when every check ran and none found anything. An entry that was not checked is never ok.</summary>
    public bool IsOk => Findings.Count == 0 && NotChecked is null;
}

/// <summary>The validator's result for a tenant: one entry per rollup.</summary>
public sealed record RollupConsistencyReport(IReadOnlyList<RollupConsistencyEntry> Rollups, DateTime GeneratedAtUtc)
{
    /// <summary>True when every rollup is checked and consistent (also when there is none). A rollup that was not checked makes it false.</summary>
    public bool IsOk => Rollups.All(r => r.IsOk);

    /// <summary>True when at least one rollup could not be checked.</summary>
    public bool HasNotChecked => Rollups.Any(r => r.IsNotChecked);
}

/// <summary>What a repair step does.</summary>
public enum RollupRepairKind
{
    /// <summary>Rewind the watermark to the end of the rollup data; forward aggregation fills the gap.</summary>
    RewindWatermark = 0,

    /// <summary>Recompute the range of a pointer whose generation has no rows; the new generation replaces the pointer in one write.</summary>
    RecomputePointerRange = 1,

    /// <summary>Delete the rows above the generation the pointer selects.</summary>
    DeleteOrphans = 2,

    /// <summary>Create the missing generation map table.</summary>
    CreateGenerationMap = 3
}

public enum RollupRepairStatus
{
    /// <summary>Dry run: listed, not executed.</summary>
    Planned = 0,
    Done = 1,
    Failed = 2,

    /// <summary>Not run because an earlier step of the same rollup failed.</summary>
    Skipped = 3
}

/// <summary>One step of a repair.</summary>
public sealed record RollupRepairAction(
    OctoObjectId RollupRtId,
    RollupRepairKind Kind,
    string Description,
    RollupRepairStatus Status,
    DateTime? RangeFromUtc = null,
    DateTime? RangeToUtc = null,
    long? Generation = null,
    string? Error = null);

/// <summary>Where a queued rollup repair stands.</summary>
public enum RollupRepairRequestState
{
    /// <summary>Recorded, not started: the host's recompute tick picks it up.</summary>
    Pending = 0,

    /// <summary>A tick is running it.</summary>
    Running = 1,

    /// <summary>Finished without a failing step and the final validation is ok; the restore reconcile marker is cleared.</summary>
    Done = 2,

    /// <summary>A step failed or the tenant is still inconsistent; the report names the step, the marker stays.</summary>
    Failed = 3
}

/// <summary>
/// A repair that was queued with <see cref="IStreamDataRepository.EnqueueRollupRepairAsync"/>: durable (a restart does not lose it)
/// and run by the host's recompute tick, not under the request that queued it.
/// </summary>
/// <param name="RequestId">Identifies the request for <see cref="IStreamDataRepository.GetRollupRepairRequestAsync"/>.</param>
/// <param name="State">Where it stands.</param>
/// <param name="RequestedAtUtc">When it was queued.</param>
/// <param name="FinishedAtUtc">When it ended; <c>null</c> while pending or running.</param>
/// <param name="PlannedActions">The steps the repair planned when it was queued (a dry run).</param>
/// <param name="Report">The report of the run (validate, repair, validate); <c>null</c> until it ended.</param>
public sealed record RollupRepairRequest(
    string RequestId,
    RollupRepairRequestState State,
    DateTime RequestedAtUtc,
    DateTime? FinishedAtUtc,
    IReadOnlyList<RollupRepairAction> PlannedActions,
    RollupRepairReport? Report);

/// <summary>
/// What <see cref="IStreamDataRepository.RepairRollupsAsync"/> did: the validation before, every step, the validation after.
/// </summary>
/// <param name="DryRun">True when nothing was changed.</param>
/// <param name="Before">The validator's report before any step.</param>
/// <param name="Actions">The steps in the order they were (or would be) run.</param>
/// <param name="After">The validator's report after the steps; <c>null</c> on a dry run.</param>
/// <param name="FailedStep">The first failed step, named; <c>null</c> when none failed.</param>
public sealed record RollupRepairReport(
    bool DryRun,
    RollupConsistencyReport Before,
    IReadOnlyList<RollupRepairAction> Actions,
    RollupConsistencyReport? After,
    string? FailedStep)
{
    /// <summary>True when nothing failed and (unless dry run) the tenant validates ok.</summary>
    public bool IsOk => FailedStep is null && (DryRun || After is { IsOk: true });
}
