using System;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Progress of a computed-column backfill (AB#8002), reported by the repository after every committed
/// chunk.
/// </summary>
/// <param name="Rows">Rows written by this run so far.</param>
/// <param name="CompletedThrough">
/// Checkpoint: every row whose time key is before this instant has been written by this run (or by an
/// earlier run it resumed). A backfill resumed at this instant skips them. <c>null</c> while the
/// repository cannot give one, e.g. before the first time slice is complete or on an unsliced walk.
/// </param>
public readonly record struct ComputedColumnBackfillProgress(long Rows, DateTime? CompletedThrough);
