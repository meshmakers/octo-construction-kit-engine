using System;
using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Formulas;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

public class RollupValidatorTests
{
    private static readonly OctoObjectId RollupRt = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId SourceRt = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId SecondSourceRt = OctoObjectId.GenerateNewId();
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("CkRollupArchive"));
    private static readonly RtCkId<CkTypeId> OtherTargetType = new("Test", new CkTypeId("OtherType"));

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0) =>
        new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private static RollupArchiveSnapshot Rollup(
        OctoObjectId? sourceRt = null,
        CkRollupAggregationSpec[]? aggregations = null) =>
        RollupWithSources(new[] { new RollupSourceReference(sourceRt ?? SourceRt) }, aggregations);

    private static RollupArchiveSnapshot RollupWithSources(
        RollupSourceReference[] sources,
        CkRollupAggregationSpec[]? aggregations = null) =>
        new(
            RollupRt, TargetType, CkArchiveStatus.Created, null,
            sources,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5),
            null,
            aggregations ?? new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) },
            null);

    private static ArchiveSnapshot Source(
        CkArchiveStatus status = CkArchiveStatus.Activated,
        params string[] paths) =>
        Source(SourceRt, status, paths);

    private static ArchiveSnapshot Source(
        OctoObjectId rtId,
        CkArchiveStatus status = CkArchiveStatus.Activated,
        params string[] paths) =>
        new(
            rtId, TargetType, status, null,
            Array.ConvertAll(paths, p => new CkArchiveColumnSpec(p, true, false)));

    /// <summary>Wraps one source archive snapshot as the activation input for the rollup's first source.</summary>
    private static IReadOnlyList<RollupActivationSource> Activation(RollupArchiveSnapshot rollup, params ArchiveSnapshot?[] archives)
    {
        var result = new List<RollupActivationSource>(rollup.Sources.Count);
        for (var i = 0; i < rollup.Sources.Count; i++)
        {
            result.Add(new RollupActivationSource(rollup.Sources[i], i < archives.Length ? archives[i] : null));
        }

        return result;
    }

    // ---- ValidateForSave ----

    [Fact]
    public void ValidateForSave_HappyPath_DoesNotThrow()
    {
        RollupValidator.ValidateForSave(Rollup());
    }

    [Fact]
    public void ValidateForSave_NoAggregations_Throws()
    {
        var rollup = Rollup(aggregations: Array.Empty<CkRollupAggregationSpec>());

        Assert.Throws<RollupAggregationsRequiredException>(
            () => RollupValidator.ValidateForSave(rollup));
    }

    // TC-VAL-17: a rollup that lists itself among its sources is rejected.
    [Fact]
    public void ValidateForSave_DirectSelfCycle_Throws()
    {
        var rollup = Rollup(sourceRt: RollupRt);

        Assert.Throws<RollupCycleException>(() => RollupValidator.ValidateForSave(rollup));
    }

    [Fact]
    public void ValidateForSave_SelfAmongSeveralSources_Throws()
    {
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: Utc(2026, 1, 1)),
            new RollupSourceReference(RollupRt, ValidFrom: Utc(2026, 1, 1)),
        });

        Assert.Throws<RollupCycleException>(() => RollupValidator.ValidateForSave(rollup));
    }

    [Fact]
    public void ValidateForSave_DuplicatePathFunction_Throws()
    {
        var rollup = Rollup(aggregations: new[]
        {
            new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null),
            new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, "different_name"),
        });

        var ex = Assert.Throws<DuplicateRollupAggregationException>(
            () => RollupValidator.ValidateForSave(rollup));
        Assert.Equal("voltage", ex.SourcePath);
        Assert.Equal(CkRollupFunction.Avg, ex.Function);
    }

    [Fact]
    public void ValidateForSave_SamePathDifferentFunctions_OK()
    {
        var rollup = Rollup(aggregations: new[]
        {
            new CkRollupAggregationSpec("voltage", CkRollupFunction.Min, null),
            new CkRollupAggregationSpec("voltage", CkRollupFunction.Max, null),
        });

        RollupValidator.ValidateForSave(rollup);
    }

    // ---- AB#5157: ValidateSourcesForSave ----

    [Fact]
    public void ValidateSourcesForSave_SingleUnboundedSource_DoesNotThrow()
    {
        RollupValidator.ValidateSourcesForSave(Rollup());
    }

    // TC-NORM-04: both storage forms set and disagreeing — the conflict flagged by the
    // normalisation point is rejected instead of silently picking one declaration.
    [Fact]
    public void ValidateSourcesForSave_ConflictingDeprecatedScalar_Throws()
    {
        var deprecated = OctoObjectId.GenerateNewId();
        var rollup = Rollup() with { ConflictingSourceArchiveRtId = deprecated };

        var ex = Assert.Throws<RollupSourceDeclarationConflictException>(
            () => RollupValidator.ValidateSourcesForSave(rollup));
        Assert.Equal(deprecated, ex.DeprecatedSourceArchiveRtId);
        Assert.Equal(SourceRt, ex.FirstSourceArchiveRtId);
        Assert.Contains(deprecated.ToString(), ex.Message);
        Assert.Contains(SourceRt.ToString(), ex.Message);
    }

    // TC-VAL-19 / TC-NORM-05: neither storage form set — no source at all.
    [Fact]
    public void ValidateSourcesForSave_EmptySources_Throws()
    {
        var rollup = RollupWithSources(Array.Empty<RollupSourceReference>());

        Assert.Throws<RollupSourcesRequiredException>(() => RollupValidator.ValidateSourcesForSave(rollup));
    }

    // TC-VAL-20 / TC-X-VAL-06: the same archive listed twice is rejected as a duplicate source,
    // never silently deduplicated — whatever spans the two entries carry.
    [Fact]
    public void ValidateSourcesForSave_SameArchiveTwice_ThrowsDuplicate()
    {
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: Utc(2026, 1, 1)),
            new RollupSourceReference(SourceRt),
        });

        var ex = Assert.Throws<DuplicateRollupSourceException>(
            () => RollupValidator.ValidateSourcesForSave(rollup));
        Assert.Equal(SourceRt, ex.SourceArchiveRtId);
        Assert.Contains(SourceRt.ToString(), ex.Message);
    }

    // TC-X-VAL-01: ValidFrom == ValidTo is an empty span.
    [Fact]
    public void ValidateSourcesForSave_EmptySpan_Throws()
    {
        var boundary = Utc(2026, 1, 1);
        var rollup = RollupWithSources(new[] { new RollupSourceReference(SourceRt, boundary, boundary) });

        var ex = Assert.Throws<RollupSourceSpanInvertedException>(
            () => RollupValidator.ValidateSourcesForSave(rollup));
        Assert.Equal(SourceRt, ex.SourceArchiveRtId);
        Assert.Contains(SourceRt.ToString(), ex.Message);
    }

    // TC-X-VAL-02: ValidFrom after ValidTo.
    [Fact]
    public void ValidateSourcesForSave_InvertedSpan_Throws()
    {
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, Utc(2026, 2, 1), Utc(2026, 1, 1)),
        });

        var ex = Assert.Throws<RollupSourceSpanInvertedException>(
            () => RollupValidator.ValidateSourcesForSave(rollup));
        Assert.Equal(Utc(2026, 2, 1), ex.ValidFrom);
        Assert.Equal(Utc(2026, 1, 1), ex.ValidTo);
    }

    // TC-VAL-04: two sources with an open start.
    [Fact]
    public void ValidateSourcesForSave_TwoOpenStarts_Throws()
    {
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: Utc(2026, 1, 1)),
            new RollupSourceReference(SecondSourceRt, ValidTo: Utc(2026, 2, 1)),
        });

        var ex = Assert.Throws<RollupSourceSpanOpenEndConflictException>(
            () => RollupValidator.ValidateSourcesForSave(rollup));
        Assert.True(ex.IsOpenStart);
        Assert.Equal(SecondSourceRt, ex.SourceArchiveRtId);
        Assert.Contains(SecondSourceRt.ToString(), ex.Message);
    }

    // TC-VAL-05: two sources with an open end.
    [Fact]
    public void ValidateSourcesForSave_TwoOpenEnds_Throws()
    {
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidFrom: Utc(2026, 1, 1)),
            new RollupSourceReference(SecondSourceRt, ValidFrom: Utc(2026, 2, 1)),
        });

        var ex = Assert.Throws<RollupSourceSpanOpenEndConflictException>(
            () => RollupValidator.ValidateSourcesForSave(rollup));
        Assert.False(ex.IsOpenStart);
        Assert.Equal(SecondSourceRt, ex.SourceArchiveRtId);
    }

    // TC-VAL-06 / TC-VAL-02: one open start plus one open end, abutting at the cutover.
    [Fact]
    public void ValidateSourcesForSave_OpenStartPlusOpenEndAbutting_DoesNotThrow()
    {
        var cutover = Utc(2026, 1, 1);
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: cutover),
            new RollupSourceReference(SecondSourceRt, ValidFrom: cutover),
        });

        RollupValidator.ValidateSourcesForSave(rollup);
    }

    // TC-VAL-01: overlapping spans.
    [Fact]
    public void ValidateSourcesForSave_OverlappingSpans_Throws()
    {
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, Utc(2026, 1, 1), Utc(2026, 3, 1)),
            new RollupSourceReference(SecondSourceRt, Utc(2026, 2, 1), Utc(2026, 4, 1)),
        });

        var ex = Assert.Throws<RollupSourceSpanOverlapException>(
            () => RollupValidator.ValidateSourcesForSave(rollup));
        Assert.Equal(SecondSourceRt, ex.SourceArchiveRtId);
        Assert.Equal(SourceRt, ex.OverlappingSourceArchiveRtId);
        Assert.Contains(SecondSourceRt.ToString(), ex.Message);
    }

    // TC-VAL-03: the overlap is between the first and the third of three sources.
    [Fact]
    public void ValidateSourcesForSave_FirstAndThirdOverlap_Throws()
    {
        var third = OctoObjectId.GenerateNewId();
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, Utc(2026, 1, 1), Utc(2026, 5, 1)),
            new RollupSourceReference(SecondSourceRt, Utc(2026, 2, 1), Utc(2026, 3, 1)),
            new RollupSourceReference(third, Utc(2026, 4, 1), Utc(2026, 6, 1)),
        });

        Assert.Throws<RollupSourceSpanOverlapException>(
            () => RollupValidator.ValidateSourcesForSave(rollup));
    }

    // TC-X-VAL-05: an unbounded reference overlaps every bounded span.
    [Fact]
    public void ValidateSourcesForSave_UnboundedSourceSwallowingABoundedOne_Throws()
    {
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt),
            new RollupSourceReference(SecondSourceRt, Utc(2026, 1, 1), Utc(2026, 2, 1)),
        });

        Assert.Throws<RollupSourceSpanOverlapException>(
            () => RollupValidator.ValidateSourcesForSave(rollup));
    }

    [Fact]
    public void ValidateSourcesForSave_ThreeDisjointSpans_DoesNotThrow()
    {
        // TC-X-VAL-04's save-time half: the design is not limited to two sources.
        var third = OctoObjectId.GenerateNewId();
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: Utc(2026, 1, 1)),
            new RollupSourceReference(SecondSourceRt, Utc(2026, 1, 1), Utc(2026, 2, 1)),
            new RollupSourceReference(third, ValidFrom: Utc(2026, 2, 1)),
        });

        RollupValidator.ValidateSourcesForSave(rollup);
    }

    // TC-VAL-07: a boundary that is not on the rollup's bucket grid.
    [Fact]
    public void ValidateSourcesForSave_BoundaryOffTheFixedSizeGrid_Throws()
    {
        // 1 h buckets on the epoch grid: 00:30 is not a boundary.
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: Utc(2026, 1, 1, 0, 30)),
        }) with
        {
            BucketSize = TimeSpan.FromHours(1),
        };

        var ex = Assert.Throws<RollupSourceSpanNotOnBucketBoundaryException>(
            () => RollupValidator.ValidateSourcesForSave(rollup));
        Assert.Equal(SourceRt, ex.SourceArchiveRtId);
        Assert.Equal(nameof(RollupSourceReference.ValidTo), ex.BoundaryName);
        Assert.Contains(SourceRt.ToString(), ex.Message);
    }

    [Fact]
    public void ValidateSourcesForSave_ValidFromOffTheGrid_NamesValidFrom()
    {
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidFrom: Utc(2026, 1, 1, 0, 30)),
        }) with
        {
            BucketSize = TimeSpan.FromHours(1),
        };

        var ex = Assert.Throws<RollupSourceSpanNotOnBucketBoundaryException>(
            () => RollupValidator.ValidateSourcesForSave(rollup));
        Assert.Equal(nameof(RollupSourceReference.ValidFrom), ex.BoundaryName);
    }

    // TC-VAL-08: aligned in UTC but not in the reference time zone.
    [Fact]
    public void ValidateSourcesForSave_BoundaryAlignedInUtcButNotInReferenceZone_Throws()
    {
        // UTC midnight is 01:00 local in Vienna (winter) — not a local calendar-day boundary.
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: Utc(2026, 1, 15)),
        }) with
        {
            BucketSize = TimeSpan.FromDays(1),
            BucketAlignment = BucketAlignment.CalendarDay,
            ReferenceTimeZone = "Europe/Vienna",
        };

        Assert.Throws<RollupSourceSpanNotOnBucketBoundaryException>(
            () => RollupValidator.ValidateSourcesForSave(rollup));
    }

    // TC-X-VAL-03: aligned in the reference time zone but not in UTC — accepted.
    [Fact]
    public void ValidateSourcesForSave_BoundaryAlignedInReferenceZoneOnly_DoesNotThrow()
    {
        // Local midnight 2026-01-15 Vienna (UTC+1) = 2026-01-14 23:00 UTC.
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: Utc(2026, 1, 14, 23)),
        }) with
        {
            BucketSize = TimeSpan.FromDays(1),
            BucketAlignment = BucketAlignment.CalendarDay,
            ReferenceTimeZone = "Europe/Vienna",
        };

        RollupValidator.ValidateSourcesForSave(rollup);
    }

    // TC-VAL-09: a quarter boundary on a CalendarQuarter rollup.
    [Fact]
    public void ValidateSourcesForSave_QuarterBoundaryOnQuarterRollup_DoesNotThrow()
    {
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: Utc(2026, 4, 1)),
        }) with
        {
            BucketSize = TimeSpan.FromDays(90),
            BucketAlignment = BucketAlignment.CalendarQuarter,
        };

        RollupValidator.ValidateSourcesForSave(rollup);
    }

    // TC-VAL-10: a month boundary that is not a quarter boundary.
    [Fact]
    public void ValidateSourcesForSave_NonQuarterMonthBoundaryOnQuarterRollup_Throws()
    {
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: Utc(2026, 5, 1)),
        }) with
        {
            BucketSize = TimeSpan.FromDays(90),
            BucketAlignment = BucketAlignment.CalendarQuarter,
        };

        var ex = Assert.Throws<RollupSourceSpanNotOnBucketBoundaryException>(
            () => RollupValidator.ValidateSourcesForSave(rollup));
        Assert.Equal(Utc(2026, 5, 1), ex.Boundary);
    }

    // ---- ValidateForActivation ----

    [Fact]
    public void ValidateForActivation_HappyPath_DoesNotThrow()
    {
        var rollup = Rollup();
        RollupValidator.ValidateForActivation(rollup, Activation(rollup, Source(paths: "voltage")));
    }

    // TC-VAL-15: a source rtId that does not resolve.
    [Fact]
    public void ValidateForActivation_SourceNull_ThrowsRollupSourceMissing()
    {
        var rollup = Rollup();

        var ex = Assert.Throws<RollupSourceMissingException>(
            () => RollupValidator.ValidateForActivation(rollup, Activation(rollup, (ArchiveSnapshot?)null)));
        Assert.Contains(SourceRt.ToString(), ex.Message);
    }

    [Theory]
    [InlineData(CkArchiveStatus.Created)]
    [InlineData(CkArchiveStatus.Disabled)]
    [InlineData(CkArchiveStatus.Failed)]
    public void ValidateForActivation_SourceNotActivated_Throws(CkArchiveStatus status)
    {
        var rollup = Rollup();

        var ex = Assert.Throws<RollupSourceNotActivatedException>(
            () => RollupValidator.ValidateForActivation(rollup, Activation(rollup, Source(status, "voltage"))));
        Assert.Equal(status, ex.SourceStatus);
    }

    // TC-VAL-14 / TC-X-VAL-07: the SECOND of two sources is not activated.
    [Theory]
    [InlineData(CkArchiveStatus.Created)]
    [InlineData(CkArchiveStatus.Disabled)]
    public void ValidateForActivation_SecondSourceNotActivated_Throws(CkArchiveStatus status)
    {
        var cutover = Utc(2026, 1, 1);
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: cutover),
            new RollupSourceReference(SecondSourceRt, ValidFrom: cutover),
        });

        var ex = Assert.Throws<RollupSourceNotActivatedException>(
            () => RollupValidator.ValidateForActivation(rollup, Activation(
                rollup,
                Source(SourceRt, CkArchiveStatus.Activated, "voltage"),
                Source(SecondSourceRt, status, "voltage"))));
        Assert.Equal(status, ex.SourceStatus);
        Assert.Contains(SecondSourceRt.ToString(), ex.Message);
    }

    // TC-VAL-11: sources with different target CK types.
    [Fact]
    public void ValidateForActivation_SecondSourceOtherTargetType_Throws()
    {
        var cutover = Utc(2026, 1, 1);
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: cutover),
            new RollupSourceReference(SecondSourceRt, ValidFrom: cutover),
        });
        var otherType = new ArchiveSnapshot(
            SecondSourceRt, OtherTargetType, CkArchiveStatus.Activated, null,
            new[] { new CkArchiveColumnSpec("voltage", true, false) });

        var ex = Assert.Throws<RollupSourceTargetTypeMismatchException>(
            () => RollupValidator.ValidateForActivation(rollup, Activation(
                rollup, Source(SourceRt, CkArchiveStatus.Activated, "voltage"), otherType)));
        Assert.Equal(SecondSourceRt, ex.SourceArchiveRtId);
        Assert.Equal(TargetType, ex.ExpectedTargetCkTypeId);
        Assert.Equal(OtherTargetType, ex.ActualTargetCkTypeId);
        Assert.Contains(SecondSourceRt.ToString(), ex.Message);
    }

    // TC-VAL-16: the aggregation path resolves on the first source but not on the second (strict).
    [Fact]
    public void ValidateForActivation_PathMissingOnOneSource_Throws()
    {
        var cutover = Utc(2026, 1, 1);
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: cutover),
            new RollupSourceReference(SecondSourceRt, ValidFrom: cutover),
        });

        var ex = Assert.Throws<RollupSourcePathMissingException>(
            () => RollupValidator.ValidateForActivation(rollup, Activation(
                rollup,
                Source(SourceRt, CkArchiveStatus.Activated, "voltage"),
                Source(SecondSourceRt, CkArchiveStatus.Activated, "current"))));
        Assert.Equal("voltage", ex.SourcePath);
        Assert.Equal(SecondSourceRt, ex.SourceArchiveRtId);
        Assert.Contains(SecondSourceRt.ToString(), ex.Message);
    }

    [Fact]
    public void ValidateForActivation_SourcePathMissing_Throws()
    {
        var rollup = Rollup();

        var ex = Assert.Throws<RollupSourcePathMissingException>(
            () => RollupValidator.ValidateForActivation(rollup, Activation(rollup, Source(paths: "current"))));
        Assert.Equal("voltage", ex.SourcePath);
    }

    [Fact]
    public void ValidateForActivation_AlsoRunsSaveTimeChecks()
    {
        var rollup = Rollup(aggregations: Array.Empty<CkRollupAggregationSpec>());

        Assert.Throws<RollupAggregationsRequiredException>(
            () => RollupValidator.ValidateForActivation(rollup, Activation(rollup, Source(paths: "voltage"))));
    }

    [Fact]
    public void ValidateForActivation_AlsoRunsSourceSpanChecks()
    {
        var rollup = RollupWithSources(Array.Empty<RollupSourceReference>());

        Assert.Throws<RollupSourcesRequiredException>(
            () => RollupValidator.ValidateForActivation(rollup, Activation(rollup)));
    }

    // ---- AB#4289: bucket interval vs source granularity (FixedSize rollups) ----

    [Theory]
    [InlineData(5)]   // finer than the 15-min source window
    [InlineData(7)]   // finer and not aligned
    [InlineData(20)]  // coarser but not an integer multiple of 15 min
    [InlineData(25)]
    public void ValidateForActivation_BucketFinerOrNotMultipleOfWindowedSource_Throws(int bucketMinutes)
    {
        var rollup = Rollup() with { BucketSize = TimeSpan.FromMinutes(bucketMinutes) };
        var source = Source(paths: "voltage") with { Period = TimeSpan.FromMinutes(15) };

        var ex = Assert.Throws<RollupBucketIntervalException>(
            () => RollupValidator.ValidateForActivation(rollup, Activation(rollup, source)));
        Assert.Equal(TimeSpan.FromMinutes(bucketMinutes), ex.BucketSize);
        Assert.Equal(TimeSpan.FromMinutes(15), ex.SourceGranularity);
    }

    [Theory]
    [InlineData(15)]  // equal to the source granularity
    [InlineData(30)]  // an integer multiple
    [InlineData(60)]
    public void ValidateForActivation_BucketEqualOrMultipleOfWindowedSource_DoesNotThrow(int bucketMinutes)
    {
        var rollup = Rollup() with { BucketSize = TimeSpan.FromMinutes(bucketMinutes) };
        var source = Source(paths: "voltage") with { Period = TimeSpan.FromMinutes(15) };

        RollupValidator.ValidateForActivation(rollup, Activation(rollup, source));
    }

    [Fact]
    public void ValidateForActivation_RawSourceUndeclaredGranularity_DoesNotThrow()
    {
        // A raw source carries no Period, so the bucket-vs-source relationship cannot be validated;
        // an otherwise-fine 1-min bucket must not be rejected on a guess.
        var rollup = Rollup() with { BucketSize = TimeSpan.FromMinutes(1) };
        var source = Source(paths: "voltage"); // Period == null

        RollupValidator.ValidateForActivation(rollup, Activation(rollup, source));
    }

    // TC-VAL-12: the target bucket is finer than the SECOND of two sources.
    [Fact]
    public void ValidateForActivation_BucketFinerThanOneOfSeveralSources_Throws()
    {
        var cutover = Utc(2026, 1, 1);
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: cutover),
            new RollupSourceReference(SecondSourceRt, ValidFrom: cutover),
        }) with { BucketSize = TimeSpan.FromMinutes(30) };

        var fine = Source(SourceRt, CkArchiveStatus.Activated, "voltage") with { Period = TimeSpan.FromMinutes(15) };
        var coarse = Source(SecondSourceRt, CkArchiveStatus.Activated, "voltage") with { Period = TimeSpan.FromHours(1) };

        var ex = Assert.Throws<RollupBucketIntervalException>(
            () => RollupValidator.ValidateForActivation(rollup, Activation(rollup, fine, coarse)));
        Assert.Equal(TimeSpan.FromHours(1), ex.SourceGranularity);
    }

    // AB#5157: with several sources the window-length violation must name the offending one.
    [Fact]
    public void ValidateForActivation_BucketFinerThanOneOfSeveralSources_NamesThatSource()
    {
        var cutover = Utc(2026, 1, 1);
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: cutover),
            new RollupSourceReference(SecondSourceRt, ValidFrom: cutover),
        }) with { BucketSize = TimeSpan.FromMinutes(30) };

        var fine = Source(SourceRt, CkArchiveStatus.Activated, "voltage") with { Period = TimeSpan.FromMinutes(15) };
        var coarse = Source(SecondSourceRt, CkArchiveStatus.Activated, "voltage") with { Period = TimeSpan.FromHours(1) };

        var ex = Assert.Throws<RollupBucketIntervalException>(
            () => RollupValidator.ValidateForActivation(rollup, Activation(rollup, fine, coarse)));
        Assert.Equal(SecondSourceRt, ex.SourceArchiveRtId);
        Assert.Null(ex.SourceAlignment);
        Assert.Contains(SecondSourceRt.ToString(), ex.Message);
    }

    // TC-VAL-13: a bucket that is an integer multiple of every source window.
    [Fact]
    public void ValidateForActivation_BucketMultipleOfEverySourceWindow_DoesNotThrow()
    {
        var cutover = Utc(2026, 1, 1);
        var rollup = RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: cutover),
            new RollupSourceReference(SecondSourceRt, ValidFrom: cutover),
        }) with { BucketSize = TimeSpan.FromHours(1) };

        var quarterHour = Source(SourceRt, CkArchiveStatus.Activated, "voltage") with { Period = TimeSpan.FromMinutes(15) };
        var halfHour = Source(SecondSourceRt, CkArchiveStatus.Activated, "voltage") with { Period = TimeSpan.FromMinutes(30) };

        RollupValidator.ValidateForActivation(rollup, Activation(rollup, quarterHour, halfHour));
    }

    // ---- AB#5157: the reformulated granularity rule for calendar-aligned rollups ----

    private static RollupArchiveSnapshot CalendarRollup(BucketAlignment alignment) =>
        Rollup() with { BucketAlignment = alignment, BucketSize = TimeSpan.FromDays(1) };

    private static IReadOnlyList<RollupActivationSource> CalendarSource(
        RollupArchiveSnapshot rollup, BucketAlignment sourceAlignment, TimeSpan sourceBucketSize,
        string? sourceReferenceTimeZone = null)
    {
        var sourceRollup = new RollupArchiveSnapshot(
            SourceRt, TargetType, CkArchiveStatus.Activated, null,
            new[] { new RollupSourceReference(OctoObjectId.GenerateNewId()) },
            sourceBucketSize, TimeSpan.FromMinutes(5), null,
            new[] { new CkRollupAggregationSpec("voltage", CkRollupFunction.Avg, null) }, null)
        {
            BucketAlignment = sourceAlignment,
            ReferenceTimeZone = sourceReferenceTimeZone,
        };

        return new[]
        {
            new RollupActivationSource(rollup.Sources[0], Source(paths: "voltage"), sourceRollup),
        };
    }

    // AB#5157 review: calendar nesting is a statement about boundaries, and boundaries are local.
    // A UTC month and a Europe/Vienna quarter share no cut point, so the source windows at every
    // edge straddle two target buckets and the fully-contained window rule drops them.
    [Fact]
    public void ValidateForActivation_CalendarChainWhoseSourceAnchorsToAnotherZone_Throws()
    {
        var rollup = CalendarRollup(BucketAlignment.CalendarQuarter) with { ReferenceTimeZone = "Europe/Vienna" };

        var ex = Assert.Throws<RollupCalendarZoneMismatchException>(() => RollupValidator.ValidateForActivation(
            rollup, CalendarSource(rollup, BucketAlignment.CalendarMonth, TimeSpan.FromDays(28))));

        Assert.Equal("Europe/Vienna", ex.TargetReferenceTimeZone);
        Assert.Equal("UTC", ex.SourceReferenceTimeZone);
    }

    [Fact]
    public void ValidateForActivation_CalendarChainSharingOneReferenceZone_DoesNotThrow()
    {
        var rollup = CalendarRollup(BucketAlignment.CalendarQuarter) with { ReferenceTimeZone = "Europe/Vienna" };

        RollupValidator.ValidateForActivation(
            rollup,
            CalendarSource(rollup, BucketAlignment.CalendarMonth, TimeSpan.FromDays(28), "Europe/Vienna"));
    }

    [Fact]
    public void ValidateForActivation_QuarterOverCalendarMonthRollup_DoesNotThrow()
    {
        var rollup = CalendarRollup(BucketAlignment.CalendarQuarter);

        RollupValidator.ValidateForActivation(
            rollup, CalendarSource(rollup, BucketAlignment.CalendarMonth, TimeSpan.FromDays(28)));
    }

    [Fact]
    public void ValidateForActivation_QuarterOverCalendarYearRollup_Throws()
    {
        var rollup = CalendarRollup(BucketAlignment.CalendarQuarter);

        Assert.Throws<RollupBucketIntervalException>(() => RollupValidator.ValidateForActivation(
            rollup, CalendarSource(rollup, BucketAlignment.CalendarYear, TimeSpan.FromDays(365))));
    }

    [Fact]
    public void ValidateForActivation_Iso8601WeekOverCalendarMonthRollup_Throws()
    {
        var rollup = CalendarRollup(BucketAlignment.Iso8601Week);

        Assert.Throws<RollupBucketIntervalException>(() => RollupValidator.ValidateForActivation(
            rollup, CalendarSource(rollup, BucketAlignment.CalendarMonth, TimeSpan.FromDays(28))));
    }

    // AB#5157: the calendar-nesting rejection names the source and its alignment instead of the
    // window-length wording, which does not apply to calendar-aligned rollups.
    [Fact]
    public void ValidateForActivation_CalendarNestingViolation_NamesTheSourceAndItsAlignment()
    {
        var rollup = CalendarRollup(BucketAlignment.Iso8601Week);

        var ex = Assert.Throws<RollupBucketIntervalException>(() => RollupValidator.ValidateForActivation(
            rollup, CalendarSource(rollup, BucketAlignment.CalendarMonth, TimeSpan.FromDays(28))));

        Assert.Equal(SourceRt, ex.SourceArchiveRtId);
        Assert.Equal(BucketAlignment.CalendarMonth, ex.SourceAlignment);
        Assert.Contains(SourceRt.ToString(), ex.Message);
        Assert.Contains("do not nest", ex.Message);
    }

    [Fact]
    public void ValidateForActivation_CalendarMonthOverCalendarDayRollup_DoesNotThrow()
    {
        var rollup = CalendarRollup(BucketAlignment.CalendarMonth);

        RollupValidator.ValidateForActivation(
            rollup, CalendarSource(rollup, BucketAlignment.CalendarDay, TimeSpan.FromDays(1)));
    }

    [Fact]
    public void ValidateForActivation_QuarterOver92DayTimeRangeSource_DoesNotThrow()
    {
        // The sbeg cutover shape: legacy quarterly totals stored as a 92 d time-range archive feed
        // a CalendarQuarter rollup.
        var rollup = CalendarRollup(BucketAlignment.CalendarQuarter);
        var source = Source(paths: "voltage") with { IsTimeRange = true, Period = TimeSpan.FromDays(92) };

        RollupValidator.ValidateForActivation(rollup, Activation(rollup, source));
    }

    [Fact]
    public void ValidateForActivation_QuarterOverYearlyTimeRangeSource_Throws()
    {
        var rollup = CalendarRollup(BucketAlignment.CalendarQuarter);
        var source = Source(paths: "voltage") with { IsTimeRange = true, Period = TimeSpan.FromDays(365) };

        Assert.Throws<RollupBucketIntervalException>(
            () => RollupValidator.ValidateForActivation(rollup, Activation(rollup, source)));
    }

    // ---- AB#4189: a rollup may aggregate a source computed column (by its Name) ----

    private static ArchiveSnapshot SourceWithComputed() =>
        new(
            SourceRt, TargetType, CkArchiveStatus.Activated, null,
            new[]
            {
                new CkArchiveColumnSpec("activePower", true, false),
                new CkArchiveColumnSpec(string.Empty, Indexed: true, Required: false)
                {
                    Name = "powerFactor",
                    Formula = "activepower / apparentpower",
                    ResultType = FormulaResultType.Double,
                },
            });

    [Fact]
    public void ValidateForActivation_AggregatesSourceComputedColumn_DoesNotThrow()
    {
        var rollup = Rollup(aggregations: new[]
        {
            new CkRollupAggregationSpec("powerFactor", CkRollupFunction.Avg, null),
        });

        RollupValidator.ValidateForActivation(rollup, Activation(rollup, SourceWithComputed()));
    }

    [Fact]
    public void ValidateForActivation_UnknownComputedName_Throws()
    {
        var rollup = Rollup(aggregations: new[]
        {
            new CkRollupAggregationSpec("nonexistent", CkRollupFunction.Avg, null),
        });

        var ex = Assert.Throws<RollupSourcePathMissingException>(
            () => RollupValidator.ValidateForActivation(rollup, Activation(rollup, SourceWithComputed())));
        Assert.Equal("nonexistent", ex.SourcePath);
    }

    // ---- StateDuration (AB#4336) ----

    [Fact]
    public void ValidateForSave_StateDurationWithoutComparisonValue_Throws()
    {
        var rollup = Rollup(aggregations: new[]
        {
            new CkRollupAggregationSpec("isOn", CkRollupFunction.StateDuration, null),
        });

        Assert.Throws<RollupComparisonValueRequiredException>(() => RollupValidator.ValidateForSave(rollup));
    }

    [Fact]
    public void ValidateForSave_StateDurationWithComparisonValue_Passes()
    {
        var rollup = Rollup(aggregations: new[]
        {
            new CkRollupAggregationSpec("isOn", CkRollupFunction.StateDuration, null, "true"),
        });

        RollupValidator.ValidateForSave(rollup); // must not throw
    }

    // ---- AB#5157 §3: per-source resolution of logical aggregation specs (RollupSourceColumnResolver) ----

    private static readonly DateTime MixedCutover = Utc(2026, 1, 1);

    /// <summary>
    /// The AC1 / sbeg shape: a daily fixed-size rollup over a 15-min time-range base archive before
    /// the cutover and an hourly rollup of that base from the cutover on.
    /// </summary>
    private static RollupArchiveSnapshot MixedSourcesRollup(params CkRollupAggregationSpec[] aggregations) =>
        RollupWithSources(new[]
        {
            new RollupSourceReference(SourceRt, ValidTo: MixedCutover),
            new RollupSourceReference(SecondSourceRt, ValidFrom: MixedCutover),
        }, aggregations) with { BucketSize = TimeSpan.FromDays(1) };

    private static ArchiveSnapshot TimeRangeBase(params string[] paths) =>
        Source(SourceRt, CkArchiveStatus.Activated, paths) with { IsTimeRange = true, Period = TimeSpan.FromMinutes(15) };

    /// <summary>
    /// An hourly rollup source: its archive-level snapshot declares the generated physical columns
    /// (lower-cased, e.g. <c>amountvalue_sum</c>), its rollup snapshot the logical child specs.
    /// </summary>
    private static RollupActivationSource HourlyRollupSource(
        RollupSourceReference reference, params CkRollupAggregationSpec[] childSpecs)
    {
        var rollup = new RollupArchiveSnapshot(
            SecondSourceRt, TargetType, CkArchiveStatus.Activated, null,
            new[] { new RollupSourceReference(SourceRt) },
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(5), null, childSpecs, null);
        var archive = new ArchiveSnapshot(SecondSourceRt, TargetType, CkArchiveStatus.Activated, null,
            RollupColumnGenerator.Generate(childSpecs))
        {
            RollupAggregations = childSpecs,
            Period = TimeSpan.FromHours(1),
        };

        return new RollupActivationSource(reference, archive, rollup);
    }

    // AC1: one logical spec ('Amount.Value', Sum) is accepted on the time-range base (rule 1, verbatim
    // path) AND on the hourly rollup (rule 2, child spec with the same function) in one rung.
    [Fact]
    public void ValidateForActivation_LogicalSpecOverMixedBaseAndRollupSources_DoesNotThrow()
    {
        var rollup = MixedSourcesRollup(new CkRollupAggregationSpec("Amount.Value", CkRollupFunction.Sum, null));

        RollupValidator.ValidateForActivation(rollup, new[]
        {
            new RollupActivationSource(rollup.Sources[0], TimeRangeBase("Amount.Value")),
            HourlyRollupSource(rollup.Sources[1], new CkRollupAggregationSpec("Amount.Value", CkRollupFunction.Sum, null)),
        });
    }

    // A parent Avg cannot be recombined from a child that only stores Sum — refused, naming the rollup source.
    [Fact]
    public void ValidateForActivation_LogicalSpecFunctionNotStoredByRollupSource_ThrowsNamingThatSource()
    {
        var rollup = MixedSourcesRollup(new CkRollupAggregationSpec("Amount.Value", CkRollupFunction.Avg, null));

        var ex = Assert.Throws<RollupSourcePathMissingException>(
            () => RollupValidator.ValidateForActivation(rollup, new[]
            {
                new RollupActivationSource(rollup.Sources[0], TimeRangeBase("Amount.Value")),
                HourlyRollupSource(rollup.Sources[1], new CkRollupAggregationSpec("Amount.Value", CkRollupFunction.Sum, null)),
            }));
        Assert.Equal("Amount.Value", ex.SourcePath);
        Assert.Equal(SecondSourceRt, ex.SourceArchiveRtId);
        Assert.Contains(SecondSourceRt.ToString(), ex.Message);
    }

    // AC2, the cutover shape that actually shipped wrong (AB#5157 validation finding 1, tenant
    // ab5157live/invalid-path-missing): the legacy base has Reactive, the native rollup after the
    // cutover only ever aggregated Energy — and both the parent and that child pin the stored name
    // "energy_sum". Resolving on the stored name let the rollup activate and backfill, so the buckets
    // before the cutover carried Reactive sums and the ones after carried Energy sums, in one column,
    // with no warning anywhere. It has to be refused, naming the source that cannot serve the path.
    [Fact]
    public void ValidateForActivation_RollupSourceAggregatingAnotherAttributeUnderTheSameColumnName_ThrowsNamingThatSource()
    {
        var rollup = MixedSourcesRollup(
            new CkRollupAggregationSpec("Reactive", CkRollupFunction.Sum, "energy_sum"));

        var ex = Assert.Throws<RollupSourcePathMissingException>(
            () => RollupValidator.ValidateForActivation(rollup, new[]
            {
                new RollupActivationSource(rollup.Sources[0], TimeRangeBase("Reactive")),
                HourlyRollupSource(rollup.Sources[1],
                    new CkRollupAggregationSpec("Energy", CkRollupFunction.Sum, "energy_sum")),
            }));
        Assert.Equal("Reactive", ex.SourcePath);
        Assert.Equal(SecondSourceRt, ex.SourceArchiveRtId);
        Assert.Contains(SecondSourceRt.ToString(), ex.Message);
    }

    // The base archive lacking the path is still refused (rule 1 is the only rule for a base archive).
    [Fact]
    public void ValidateForActivation_BaseSourceLackingThePath_ThrowsNamingThatSource()
    {
        var rollup = MixedSourcesRollup(new CkRollupAggregationSpec("Amount.Value", CkRollupFunction.Sum, null));

        var ex = Assert.Throws<RollupSourcePathMissingException>(
            () => RollupValidator.ValidateForActivation(rollup, new[]
            {
                new RollupActivationSource(rollup.Sources[0], TimeRangeBase("Amount.Other")),
                HourlyRollupSource(rollup.Sources[1], new CkRollupAggregationSpec("Amount.Value", CkRollupFunction.Sum, null)),
            }));
        Assert.Equal("Amount.Value", ex.SourcePath);
        Assert.Equal(SourceRt, ex.SourceArchiveRtId);
    }

    // The pre-AB#5157 chained style — the parent names the child's PHYSICAL column — keeps working via rule 1.
    [Fact]
    public void ValidateForActivation_PhysicalNameChainedSpecOverRollupSource_DoesNotThrow()
    {
        var rollup = RollupWithSources(
            new[] { new RollupSourceReference(SecondSourceRt) },
            new[] { new CkRollupAggregationSpec("amountvalue_sum", CkRollupFunction.Sum, null) })
            with { BucketSize = TimeSpan.FromDays(1) };

        RollupValidator.ValidateForActivation(rollup, new[]
        {
            HourlyRollupSource(rollup.Sources[0], new CkRollupAggregationSpec("Amount.Value", CkRollupFunction.Sum, null)),
        });
    }

    // Without the source's rollup snapshot only rule 1 applies: a logical spec over a rollup source
    // is refused because the archive-level snapshot declares physical names only.
    [Fact]
    public void ValidateForActivation_LogicalSpecOverRollupSourceWithoutItsRollupSnapshot_Throws()
    {
        var rollup = RollupWithSources(
            new[] { new RollupSourceReference(SecondSourceRt) },
            new[] { new CkRollupAggregationSpec("Amount.Value", CkRollupFunction.Sum, null) })
            with { BucketSize = TimeSpan.FromDays(1) };
        var hourly = HourlyRollupSource(rollup.Sources[0], new CkRollupAggregationSpec("Amount.Value", CkRollupFunction.Sum, null));

        var ex = Assert.Throws<RollupSourcePathMissingException>(
            () => RollupValidator.ValidateForActivation(rollup, new[] { hourly with { Rollup = null } }));
        Assert.Equal(SecondSourceRt, ex.SourceArchiveRtId);
    }
}
