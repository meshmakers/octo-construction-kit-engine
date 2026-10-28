using System;
using System.Collections.Generic;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// The platform fields that record the <see cref="ArchiveRowOrigin" /> of every raw and time-range row
/// (AB#5356) and, for a manual row, who wrote it, when and why (AB#7593). They are system fields: an archive
/// does not declare them in its columns, yet a reader can select them and a rollup can aggregate them
/// (AB#7727).
/// </summary>
/// <remarks>
/// An automated row carries <c>NULL</c> in the three manual fields, so <see cref="CkRollupFunction.Count" />
/// over any of them counts the manual rows of a bucket, and a row written before the origin was recorded
/// counts as delivered. Rollup rows do not carry these fields: they are derived by the platform.
/// </remarks>
public static class ArchiveRowOriginFields
{
    /// <summary>Origin of the row: <c>manual</c> or <c>automated</c>.</summary>
    public const string RowOrigin = "row_origin";

    /// <summary>Writer of a manual row; <c>NULL</c> on automated rows.</summary>
    public const string ManualWrittenBy = "manual_written_by";

    /// <summary>Write time of a manual row; <c>NULL</c> on automated rows.</summary>
    public const string ManualWrittenAt = "manual_written_at";

    /// <summary>Optional reason of a manual write; <c>NULL</c> on automated rows.</summary>
    public const string ManualReason = "manual_reason";

    /// <summary>All origin fields, in the order the storage layer binds them.</summary>
    public static IReadOnlyList<string> Names { get; } =
        new[] { RowOrigin, ManualWrittenBy, ManualWrittenAt, ManualReason };

    /// <summary>True when <paramref name="path" /> names one of the origin fields (case-insensitive).</summary>
    public static bool IsOriginField(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        foreach (var name in Names)
        {
            if (string.Equals(name, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when a rollup may aggregate the origin field <paramref name="path" /> with
    /// <paramref name="function" />: <see cref="CkRollupFunction.Count" /> over a manual field (the number of
    /// manual rows), and <see cref="CkRollupFunction.Min" /> / <see cref="CkRollupFunction.Max" /> over
    /// <see cref="ManualWrittenAt" /> (the earliest / latest manual entry). Every other function produces
    /// nothing meaningful on a text or timestamp field, and <see cref="RowOrigin" /> is set on every row, so
    /// a count over it would count all rows.
    /// </summary>
    public static bool SupportsRollupAggregation(string path, CkRollupFunction function)
    {
        if (IsField(path, ManualWrittenAt))
        {
            return function is CkRollupFunction.Count or CkRollupFunction.Min or CkRollupFunction.Max;
        }

        if (IsField(path, ManualWrittenBy) || IsField(path, ManualReason))
        {
            return function == CkRollupFunction.Count;
        }

        return false;
    }

    /// <summary>
    /// True when a rollup aggregation over <paramref name="path" /> with <paramref name="function" /> yields a
    /// timestamp — <see cref="CkRollupFunction.Min" /> or <see cref="CkRollupFunction.Max" /> over
    /// <see cref="ManualWrittenAt" />, the logical path also used by the rollups chained on top of it. The
    /// storage layer stores such a column as a timestamp, not as a number.
    /// </summary>
    public static bool IsTimestampAggregation(string path, CkRollupFunction function)
        => IsField(path, ManualWrittenAt) && function is CkRollupFunction.Min or CkRollupFunction.Max;

    private static bool IsField(string? path, string field)
        => string.Equals(path, field, StringComparison.OrdinalIgnoreCase);
}
