using System;
using System.Collections.Generic;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// The platform fields that carry the <see cref="ArchiveRowOrigin" /> of archive data. No archive declares
/// them. Every raw and time-range row records its origin (AB#5356) and, for a manual row, who wrote it, when
/// and why (AB#7593); every rollup row reports how many of its values were entered manually and when the
/// latest of them was written (AB#7727).
/// </summary>
public static class ArchiveRowOriginFields
{
    /// <summary>Raw and time-range rows: origin of the row, <c>manual</c> or <c>automated</c>.</summary>
    public const string RowOrigin = "row_origin";

    /// <summary>Raw and time-range rows: writer of a manual row; <c>NULL</c> on automated rows.</summary>
    public const string ManualWrittenBy = "manual_written_by";

    /// <summary>Raw and time-range rows: write time of a manual row; <c>NULL</c> on automated rows.</summary>
    public const string ManualWrittenAt = "manual_written_at";

    /// <summary>Raw and time-range rows: optional reason of a manual write; <c>NULL</c> on automated rows.</summary>
    public const string ManualReason = "manual_reason";

    /// <summary>
    /// Rollup rows: the number of manually entered source values of the bucket. <c>NULL</c> in a row that was
    /// aggregated before the column existed; it reads as 0.
    /// </summary>
    public const string ManualCount = "manual_count";

    /// <summary>Rollup rows: the write time of the latest manual entry of the bucket; <c>NULL</c> without one.</summary>
    public const string LastManualAt = "last_manual_at";

    /// <summary>The fields of a rollup row. No user-defined column of any archive can take these names.</summary>
    public static IReadOnlyList<string> RollupFields { get; } = new[] { ManualCount, LastManualAt };

    /// <summary>True when <paramref name="columnName" /> is one of <see cref="RollupFields" /> (case-insensitive).</summary>
    public static bool IsRollupField(string? columnName)
        => string.Equals(columnName, ManualCount, StringComparison.OrdinalIgnoreCase)
           || string.Equals(columnName, LastManualAt, StringComparison.OrdinalIgnoreCase);
}
