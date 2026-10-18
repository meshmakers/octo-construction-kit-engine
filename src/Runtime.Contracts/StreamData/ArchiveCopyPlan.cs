#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// The physical tables an archive consists of for the native copy. A rollup archive has a generation map next to
/// its data table (<c>archive_&lt;rtId&gt;__genmap</c>); raw and time-range archives have the data table only.
/// </summary>
public enum ArchiveCopyTable
{
    /// <summary>The table with the archive's rows (<c>archive_&lt;rtId&gt;</c>).</summary>
    Data = 0,

    /// <summary>The per-window active-generation pointers of a rollup archive.</summary>
    GenerationMap = 1
}

/// <summary>A column of an archive table with its backend data type (<c>information_schema.columns</c>).</summary>
public sealed record ArchiveCopyColumn(string Name, string DataType);

/// <summary>
/// Every column of one archive table with its backend data type. Written into the dump manifest and compared
/// with the freshly created table before a restore loads anything.
/// </summary>
public sealed record ArchiveTableDescription(
    OctoObjectId ArchiveRtId,
    ArchiveCopyTable Table,
    IReadOnlyList<ArchiveCopyColumn> Columns);

/// <summary>
/// One unit of a native copy: the rows of one archive table in one time range (or the whole generation map). A
/// slice is one <c>COPY</c> statement, one folder in the staging area and one entry of the dump manifest.
/// </summary>
/// <remarks>
/// <see cref="RowCount"/> is the number counted before the dump copied the slice, and the number a restore has
/// to load back. <see cref="StagedBytes"/> is the size of the slice's files in the staging area when known (the
/// dump job sums it after the copy); a restore uses it for the free disk space check.
/// </remarks>
public sealed record ArchiveCopySlice
{
    private const int MaxKeyLength = 64;
    private const string DataFolder = "data";
    private const string GenerationMapFolder = "genmap";
    private const string GenerationMapKey = "all";
    private const string KeyTimeFormat = "yyyyMMdd'T'HHmmssfff";

    [JsonConstructor]
    public ArchiveCopySlice(
        ArchiveCopyTable table, string key, long rowCount, DateTime? fromUtc = null, DateTime? toUtc = null,
        long? stagedBytes = null)
    {
        if (!Enum.IsDefined(table))
        {
            throw new ArgumentOutOfRangeException(nameof(table));
        }

        // The key is a folder name in the staging area: no separators, no dots at the start.
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength || !IsKeyChar(key[0], first: true) ||
            !key.All(c => IsKeyChar(c, first: false)))
        {
            throw new ArgumentException(
                $"A slice key must be 1 to {MaxKeyLength} letters, digits, '-', '_' or '.', not starting with '.'.",
                nameof(key));
        }

        if (rowCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowCount), "A slice cannot hold a negative number of rows.");
        }

        if (stagedBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stagedBytes));
        }

        FromUtc = fromUtc is null ? null : AsUtc(fromUtc.Value);
        ToUtc = toUtc is null ? null : AsUtc(toUtc.Value);
        if (FromUtc > ToUtc)
        {
            throw new ArgumentException("The end of a slice lies before its start.", nameof(toUtc));
        }

        Table = table;
        Key = key;
        RowCount = rowCount;
        StagedBytes = stagedBytes;
    }

    public ArchiveCopyTable Table { get; }

    /// <summary>The folder name of the slice below <c>&lt;rtId&gt;/data</c> or <c>&lt;rtId&gt;/genmap</c>.</summary>
    public string Key { get; }

    public long RowCount { get; }

    /// <summary>Inclusive start of the time range, <c>null</c> for the generation map.</summary>
    public DateTime? FromUtc { get; }

    /// <summary>Exclusive end of the time range, <c>null</c> for the generation map.</summary>
    public DateTime? ToUtc { get; }

    public long? StagedBytes { get; }

    /// <summary>A time slice of a data table: <c>[fromUtc, toUtc)</c> with the rows counted in it.</summary>
    public static ArchiveCopySlice ForTimeRange(DateTime fromUtc, DateTime toUtc, long rowCount)
    {
        var from = AsUtc(fromUtc);
        var to = AsUtc(toUtc);
        var key = from.ToString(KeyTimeFormat, CultureInfo.InvariantCulture) + "_" +
                  to.ToString(KeyTimeFormat, CultureInfo.InvariantCulture);
        return new ArchiveCopySlice(ArchiveCopyTable.Data, key, rowCount, from, to);
    }

    /// <summary>The single slice of a rollup's generation map.</summary>
    public static ArchiveCopySlice ForGenerationMap(long rowCount) =>
        new(ArchiveCopyTable.GenerationMap, GenerationMapKey, rowCount);

    /// <summary>The same slice with the size of its staged files.</summary>
    public ArchiveCopySlice WithStagedBytes(long stagedBytes) =>
        new(Table, Key, RowCount, FromUtc, ToUtc, stagedBytes);

    /// <summary>
    /// The folder of the slice below the staging root: <c>&lt;rtId&gt;/data/&lt;key&gt;</c> or
    /// <c>&lt;rtId&gt;/genmap/all</c> (concept §4). <paramref name="archiveRtId"/> is the archive the files were
    /// dumped from, <see cref="ArchiveCopyPlan.ArchiveRtId"/>.
    /// </summary>
    public string GetStagingPath(OctoObjectId archiveRtId) =>
        $"{archiveRtId}/{(Table == ArchiveCopyTable.Data ? DataFolder : GenerationMapFolder)}/{Key}";

    private static bool IsKeyChar(char c, bool first) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' || (!first && c == '.');

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

/// <summary>
/// What a native copy of one archive consists of: its tables with their columns and the slices in copy order
/// (ascending in time, the generation map last). Produced by <see cref="IStreamDataRepository.PlanArchiveCopyAsync"/>
/// for a dump, written by the bot into the manifest, and handed back to
/// <see cref="IStreamDataRepository.CopyFromStagingAsync"/> for the restore.
/// </summary>
/// <param name="ArchiveRtId">
/// The archive the plan was made for. Its id names the folders in the staging area, so a restore into another
/// archive id still reads the files of the source.
/// </param>
/// <param name="Tables">The tables with their columns as the dump found them.</param>
/// <param name="Slices">The slices in copy order.</param>
public sealed record ArchiveCopyPlan(
    OctoObjectId ArchiveRtId,
    IReadOnlyList<ArchiveTableDescription> Tables,
    IReadOnlyList<ArchiveCopySlice> Slices)
{
    [JsonIgnore]
    public long TotalRows => Slices.Sum(s => s.RowCount);
}
