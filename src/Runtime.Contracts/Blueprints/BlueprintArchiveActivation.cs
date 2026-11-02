namespace Meshmakers.Octo.Runtime.Contracts.Blueprints;

/// <summary>
/// What a blueprint install or update did with one archive of its seed (AB#7991).
/// </summary>
public enum BlueprintArchiveActivationOutcome
{
    /// <summary>The seed created the archive and it was activated.</summary>
    Activated = 0,

    /// <summary>The archive already existed on the tenant; its status was not touched.</summary>
    SkippedPreExisting = 1,

    /// <summary>The blueprint lists the archive in <c>archiveActivation.exclude</c>; it stays <c>Created</c>.</summary>
    SkippedOptOut = 2,

    /// <summary>Stream data is not enabled (or not available) on the tenant; the archive stays <c>Created</c>.</summary>
    SkippedStreamDataDisabled = 3,

    /// <summary>
    /// The archive could not be activated; it is <c>Failed</c> (retry with <c>RetryArchiveActivation</c>) and
    /// <see cref="BlueprintArchiveActivation.Reason" /> says why. The blueprint apply itself succeeded.
    /// </summary>
    Failed = 4
}

/// <summary>
/// Whether a backfill was queued for a rollup a blueprint created (AB#7991).
/// </summary>
public enum BlueprintRollupBackfillOutcome
{
    /// <summary>Not a rollup, or the rollup was not activated - no backfill applies.</summary>
    NotApplicable = 0,

    /// <summary>A durable background backfill job was queued (<see cref="BlueprintArchiveActivation.BackfillJobRtId" />).</summary>
    Queued = 1,

    /// <summary>The sources hold no data yet; nothing to backfill.</summary>
    NothingToBackfill = 2,

    /// <summary>
    /// The backfill was deliberately not queued (for example the AB#7984 guard for time-weighted aggregations over a
    /// raw source); <see cref="BlueprintArchiveActivation.BackfillReason" /> says why.
    /// </summary>
    NotQueued = 3,

    /// <summary>Queueing the backfill failed; <see cref="BlueprintArchiveActivation.BackfillReason" /> says why.</summary>
    Failed = 4
}

/// <summary>
/// One archive of a blueprint seed and what the install or update did with it (AB#7991): newly created archives are
/// activated (raw and time-range archives first, then rollups in source order) and every newly activated rollup gets a
/// backfill over the history of its sources. Archives that already existed are listed but never touched.
/// </summary>
public sealed class BlueprintArchiveActivation
{
    /// <summary>Runtime id of the archive entity.</summary>
    public required string ArchiveRtId { get; init; }

    /// <summary>Well-known name of the archive, when the seed sets one.</summary>
    public string? WellKnownName { get; init; }

    /// <summary>CK type of the archive entity (e.g. <c>System.StreamData/RollupArchive</c>).</summary>
    public required string CkTypeId { get; init; }

    /// <summary>The blueprint whose seed delivered the archive.</summary>
    public required string BlueprintId { get; init; }

    /// <summary><c>true</c> when the archive is a rollup.</summary>
    public bool IsRollup { get; set; }

    /// <summary>What was done with the archive.</summary>
    public BlueprintArchiveActivationOutcome Outcome { get; set; }

    /// <summary>Why the archive was skipped or failed; <c>null</c> when it was activated.</summary>
    public string? Reason { get; set; }

    /// <summary>Whether a backfill was queued for the rollup.</summary>
    public BlueprintRollupBackfillOutcome Backfill { get; set; } = BlueprintRollupBackfillOutcome.NotApplicable;

    /// <summary>Runtime id of the queued backfill job (pollable through the recompute jobs); <c>null</c> otherwise.</summary>
    public string? BackfillJobRtId { get; set; }

    /// <summary>Why no backfill was queued or why queueing failed; <c>null</c> otherwise.</summary>
    public string? BackfillReason { get; set; }
}
