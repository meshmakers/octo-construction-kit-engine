using System;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// A half-open UTC time window <c>[FromUtc, ToUtc)</c> used to scope an archive-data export to a
/// slice of the archive instead of the whole table. <see cref="FromUtc"/> is inclusive,
/// <see cref="ToUtc"/> is exclusive. Both bounds are interpreted as UTC. The predicate rides on the
/// already time-ordered keyset scan in <see cref="IStreamDataRepository.ExportRowsAsync"/>, so a
/// windowed export is no more expensive than a full one. Archive data export/import concept (AB#4230) §3.1.
/// </summary>
public sealed record TimeWindow(DateTime FromUtc, DateTime ToUtc);
