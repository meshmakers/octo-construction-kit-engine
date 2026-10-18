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

/// <summary>The validator's result for one rollup.</summary>
public sealed record RollupConsistencyEntry(OctoObjectId RollupRtId, IReadOnlyList<RollupConsistencyFinding> Findings)
{
    /// <summary>True when no check found anything.</summary>
    public bool IsOk => Findings.Count == 0;
}

/// <summary>The validator's result for a tenant: one entry per rollup that was checked.</summary>
public sealed record RollupConsistencyReport(IReadOnlyList<RollupConsistencyEntry> Rollups, DateTime GeneratedAtUtc)
{
    /// <summary>True when every rollup is ok (also when there is none).</summary>
    public bool IsOk => Rollups.All(r => r.IsOk);
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
