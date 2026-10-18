using System;
using System.Linq;
using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
///     AB#5967 (slice AB#6967): plan, slice and result records are written into the dump manifest by the bot, so
///     they must survive System.Text.Json without loss, and a slice key must be a safe folder name.
/// </summary>
public class ArchiveCopyContractTypesTests
{
    private static readonly OctoObjectId Rt = OctoObjectId.GenerateNewId();

    private static ArchiveCopyPlan SamplePlan() => new(
        Rt,
        [
            new ArchiveTableDescription(Rt, ArchiveCopyTable.Data,
                [new ArchiveCopyColumn("timestamp", "timestamp with time zone"), new ArchiveCopyColumn("value", "double precision")]),
            new ArchiveTableDescription(Rt, ArchiveCopyTable.GenerationMap,
                [new ArchiveCopyColumn("range_start", "timestamp with time zone")])
        ],
        [
            ArchiveCopySlice.ForTimeRange(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 1_999_999).WithStagedBytes(123_456_789),
            ArchiveCopySlice.ForTimeRange(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 2, 0, 0, 0, 123, DateTimeKind.Utc), 0),
            ArchiveCopySlice.ForGenerationMap(16)
        ]);

    [Fact]
    public void Plan_RoundTripsThroughSystemTextJson_WithoutLoss()
    {
        var plan = SamplePlan();

        var json = JsonSerializer.Serialize(plan);
        var back = JsonSerializer.Deserialize<ArchiveCopyPlan>(json)!;

        Assert.Equal(plan.ArchiveRtId, back.ArchiveRtId);
        Assert.Equal(plan.Slices, back.Slices); // record equality per slice, incl. staged bytes and UTC kind
        Assert.Equal(plan.Tables.Count, back.Tables.Count);
        for (var i = 0; i < plan.Tables.Count; i++)
        {
            Assert.Equal(plan.Tables[i].ArchiveRtId, back.Tables[i].ArchiveRtId);
            Assert.Equal(plan.Tables[i].Table, back.Tables[i].Table);
            Assert.Equal(plan.Tables[i].Columns, back.Tables[i].Columns);
        }

        Assert.Equal(plan.TotalRows, back.TotalRows);
        Assert.Equal(2_000_015, back.TotalRows);
        Assert.All(back.Slices.Where(s => s.FromUtc is not null), s => Assert.Equal(DateTimeKind.Utc, s.FromUtc!.Value.Kind));
        Assert.Equal(json, JsonSerializer.Serialize(back));
    }

    [Fact]
    public void SliceResults_RoundTripThroughSystemTextJson_WithoutLoss()
    {
        var plan = SamplePlan();
        var results = new[]
        {
            new ArchiveCopySliceResult(plan.Slices[0], ArchiveCopySliceStatus.Succeeded, 1_999_999, 0,
                TimeSpan.FromSeconds(3.25)),
            new ArchiveCopySliceResult(plan.Slices[1], ArchiveCopySliceStatus.Succeeded, 3, 0,
                TimeSpan.FromMilliseconds(40), "Copied 3 rows, 0 were counted before the copy."),
            new ArchiveCopySliceResult(plan.Slices[2], ArchiveCopySliceStatus.Failed, 0, 4, TimeSpan.Zero,
                "Rows were rejected.")
        };

        var back = JsonSerializer.Deserialize<ArchiveCopySliceResult[]>(JsonSerializer.Serialize(results))!;

        Assert.Equal(results, back);
        Assert.True(back[0].IsSuccess);
        Assert.False(back[2].IsSuccess);
        Assert.Equal(3, back[1].RowCountDifference);
    }

    [Fact]
    public void ForTimeRange_NamesTheFolderByItsRange_AndLocalAndUnspecifiedTimesBecomeUtc()
    {
        var slice = ArchiveCopySlice.ForTimeRange(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            new DateTime(2026, 1, 2, 3, 4, 5, 6, DateTimeKind.Utc), 7);

        Assert.Equal("20260101T000000000_20260102T030405006", slice.Key);
        Assert.Equal(DateTimeKind.Utc, slice.FromUtc!.Value.Kind);
        Assert.Equal($"{Rt}/data/20260101T000000000_20260102T030405006", slice.GetStagingPath(Rt));
        Assert.Equal(ArchiveCopyTable.Data, slice.Table);
    }

    [Fact]
    public void ForGenerationMap_LivesInItsOwnFolder()
    {
        var slice = ArchiveCopySlice.ForGenerationMap(16);

        Assert.Equal($"{Rt}/genmap/all", slice.GetStagingPath(Rt));
        Assert.Null(slice.FromUtc);
        Assert.Null(slice.ToUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData(".hidden")]
    [InlineData("a b")]
    [InlineData("a\\b")]
    public void Slice_RefusesAKeyThatIsNotASafeFolderName(string key)
    {
        Assert.Throws<ArgumentException>(() => new ArchiveCopySlice(ArchiveCopyTable.Data, key, 1));
    }

    [Fact]
    public void Slice_RefusesAKeyThatIsTooLong_AnInvertedRange_AndNegativeCounts()
    {
        Assert.Throws<ArgumentException>(() => new ArchiveCopySlice(ArchiveCopyTable.Data, new string('a', 65), 1));
        Assert.Throws<ArgumentException>(() => ArchiveCopySlice.ForTimeRange(
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchiveCopySlice(ArchiveCopyTable.Data, "a", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ArchiveCopySlice(ArchiveCopyTable.Data, "a", 1, stagedBytes: -1));
    }

    [Fact]
    public void Options_HaveTheLocalTrialAsDefaults_AndRefuseUnusableValues()
    {
        var options = new ArchiveCopyOptions();
        options.Validate();

        Assert.Equal(2_000_000, options.TargetSliceRows);
        Assert.Equal(TimeSpan.FromMinutes(10), options.CommandTimeout);
        Assert.True(options.OptimizeAfterLoad);
        Assert.False(options.OverwriteDuplicates);

        Assert.Throws<ArgumentOutOfRangeException>(() => (options with { TargetSliceRows = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (options with { CommandTimeout = TimeSpan.Zero }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (options with { OptimizeTimeout = TimeSpan.FromSeconds(-1) }).Validate());
    }

    [Fact]
    public void ArchiveCopyExceptions_AreStreamDataExceptionsThatCarryTheArchive()
    {
        var notSupported = new ArchiveCopyNotSupportedException(Rt);
        var refused = new ArchiveCopyRefusedException(ArchiveCopyRefusalReason.ColumnMissing,
            "Column 'x' is missing.", Rt);

        Assert.IsAssignableFrom<StreamDataException>(notSupported);
        Assert.Equal(Rt, notSupported.ArchiveRtId);
        Assert.Equal(ArchiveCopyRefusalReason.ColumnMissing, refused.Reason);
        Assert.Equal(Rt, refused.ArchiveRtId);
    }
}
