using System;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// The measured time range an archive actually holds data for (AB#5157), read from the storage
/// layer on request: <c>MIN(window_start)</c> / <c>MAX(window_end)</c> for windowed archives
/// (rollup, time-range) and <c>MIN(timestamp)</c> / <c>MAX(timestamp)</c> for raw archives.
/// </summary>
/// <remarks>
/// <para>
/// Coverage is archive-wide, not per entity rtId, and is a single from/to pair: gaps inside the
/// range are deliberately not represented. An archive without rows or without a backing table has
/// <em>no</em> coverage — that is expressed as a <c>null</c> <see cref="ArchiveCoverage"/>, never
/// as a sentinel range and never as an error.
/// </para>
/// <para>
/// See <c>concept-multi-source-rollups.md</c> §7 for the timestamp convention and the caching
/// rules of <see cref="IArchiveCoverageProvider"/>.
/// </para>
/// </remarks>
/// <param name="AvailableFrom">Earliest timestamp the archive holds data for (inclusive).</param>
/// <param name="AvailableTo">Latest timestamp the archive holds data for.</param>
public sealed record ArchiveCoverage(DateTime AvailableFrom, DateTime AvailableTo);
