namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Outcome of a single <see cref="IArchiveRecomputeExecutor"/> run (AB#4184): how much work the
/// staging-compute + atomic-swap actually did, surfaced onto the <see cref="RecomputeJobSnapshot"/>
/// history for observability.
/// </summary>
/// <param name="RowsProcessed">Number of rows written into the staging table during the recompute.</param>
/// <param name="WindowsProcessed">Number of buckets / windows recomputed.</param>
public sealed record RecomputeExecutionResult(int RowsProcessed, int WindowsProcessed);
