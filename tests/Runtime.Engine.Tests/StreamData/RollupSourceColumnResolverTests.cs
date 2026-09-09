using System;
using System.Linq;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Formulas;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// AB#5157 §3 — per-source resolution of a rollup's logical aggregation specs: rule 1 (a declared
/// column addressed verbatim) and rule 2 (the same logical aggregation stored by a rollup source).
/// </summary>
public class RollupSourceColumnResolverTests
{
    private static readonly OctoObjectId BaseRt = OctoObjectId.GenerateNewId();
    private static readonly OctoObjectId HourlyRt = OctoObjectId.GenerateNewId();
    private static readonly RtCkId<CkTypeId> TargetType = new("Test", new CkTypeId("Meter"));

    private static CkRollupAggregationSpec Spec(
        string path, CkRollupFunction function, string? targetColumnName = null) =>
        new(path, function, targetColumnName);

    /// <summary>A time-range base archive capturing the given attribute paths verbatim.</summary>
    private static ArchiveSnapshot Base(params string[] paths) =>
        new(BaseRt, TargetType, CkArchiveStatus.Activated, null,
            Array.ConvertAll(paths, p => new CkArchiveColumnSpec(p, true, false)))
        {
            IsTimeRange = true,
            Period = TimeSpan.FromMinutes(15),
        };

    /// <summary>
    /// An hourly rollup source: the archive-level snapshot declares the generated physical columns,
    /// the rollup snapshot carries the logical child specs.
    /// </summary>
    private static (ArchiveSnapshot Archive, RollupArchiveSnapshot Rollup) Hourly(params CkRollupAggregationSpec[] childSpecs)
    {
        var archive = new ArchiveSnapshot(HourlyRt, TargetType, CkArchiveStatus.Activated, null,
            RollupColumnGenerator.Generate(childSpecs))
        {
            RollupAggregations = childSpecs,
            Period = TimeSpan.FromHours(1),
        };
        var rollup = new RollupArchiveSnapshot(
            HourlyRt, TargetType, CkArchiveStatus.Activated, null,
            new[] { new RollupSourceReference(BaseRt) },
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(5), null, childSpecs, null);
        return (archive, rollup);
    }

    // ---- NormalisePath ----------------------------------------------------------------------

    [Theory]
    [InlineData("Amount.Value", "amountvalue")]
    [InlineData("Sensor.Reading.Value", "sensorreadingvalue")]
    [InlineData("MixedCase", "mixedcase")]
    [InlineData("amountvalue_sum", "amountvalue_sum")]
    [InlineData("amountvalue", "amountvalue")]
    public void NormalisePath_StripsDotsAndLowerCases_LeavingPhysicalNamesUnchanged(string path, string expected)
    {
        Assert.Equal(expected, RollupSourceColumnResolver.NormalisePath(path));
    }

    [Fact]
    public void NormalisePath_IsIdempotent()
    {
        var once = RollupSourceColumnResolver.NormalisePath("Amount.Value");

        Assert.Equal(once, RollupSourceColumnResolver.NormalisePath(once));
    }

    [Fact]
    public void NormalisePath_MatchesTheColumnGeneratorsDefaultBaseName()
    {
        // The rule the generated column names are built from — a rule-2 match therefore denotes the
        // physical column the child actually stores.
        var column = RollupColumnGenerator.TargetColumnNamesFor(Spec("Amount.Value", CkRollupFunction.Sum)).Single();

        Assert.Equal(RollupSourceColumnResolver.NormalisePath("Amount.Value") + "_sum", column);
    }

    [Fact]
    public void NormalisePath_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RollupSourceColumnResolver.NormalisePath(null!));
    }

    // ---- CapturedPaths ----------------------------------------------------------------------

    [Fact]
    public void CapturedPaths_IngestedByPath_ComputedByName()
    {
        var archive = new ArchiveSnapshot(BaseRt, TargetType, CkArchiveStatus.Activated, null, new[]
        {
            new CkArchiveColumnSpec("Amount.Value", true, false),
            new CkArchiveColumnSpec(string.Empty, Indexed: true, Required: false)
            {
                Name = "powerFactor",
                Formula = "activepower / apparentpower",
                ResultType = FormulaResultType.Double,
            },
        });

        var captured = RollupSourceColumnResolver.CapturedPaths(archive);

        Assert.Equal(new[] { "Amount.Value", "powerFactor" }, captured.OrderBy(p => p, StringComparer.Ordinal));
    }

    // ---- Rule 1: declared column --------------------------------------------------------------

    [Fact]
    public void TryResolve_BaseArchiveDeclaringThePath_ResolvesAsDeclaredColumn()
    {
        var resolution = RollupSourceColumnResolver.TryResolve(
            Spec("Amount.Value", CkRollupFunction.Sum), Base("Amount.Value"), sourceRollup: null);

        Assert.NotNull(resolution);
        Assert.Equal(RollupSourceColumnResolutionKind.DeclaredColumn, resolution.Kind);
        Assert.Null(resolution.ChildAggregation);
    }

    [Fact]
    public void TryResolve_BaseArchiveLackingThePath_ReturnsNull()
    {
        Assert.Null(RollupSourceColumnResolver.TryResolve(
            Spec("Amount.Value", CkRollupFunction.Sum), Base("Amount.Other"), sourceRollup: null));
    }

    [Fact]
    public void TryResolve_DeclaredColumnIsMatchedVerbatim_NotNormalised()
    {
        // Rule 1 is the pre-AB#5157 verbatim rule: a base archive capturing 'Amount.Value' does not
        // serve a spec naming 'amountvalue' — only rule 2 normalises, and only over rollup sources.
        Assert.Null(RollupSourceColumnResolver.TryResolve(
            Spec("amountvalue", CkRollupFunction.Sum), Base("Amount.Value"), sourceRollup: null));
    }

    [Fact]
    public void TryResolve_ComputedColumnByName_ResolvesAsDeclaredColumn()
    {
        var archive = new ArchiveSnapshot(BaseRt, TargetType, CkArchiveStatus.Activated, null, new[]
        {
            new CkArchiveColumnSpec(string.Empty, Indexed: true, Required: false)
            {
                Name = "powerFactor",
                Formula = "activepower / apparentpower",
                ResultType = FormulaResultType.Double,
            },
        });

        var resolution = RollupSourceColumnResolver.TryResolve(
            Spec("powerFactor", CkRollupFunction.Avg), archive, sourceRollup: null);

        Assert.NotNull(resolution);
        Assert.Equal(RollupSourceColumnResolutionKind.DeclaredColumn, resolution.Kind);
    }

    // The pre-AB#5157 chained style: the parent names the child's physical column and applies Sum.
    [Fact]
    public void TryResolve_PhysicalNameChainedSpecOverRollupSource_ResolvesAsDeclaredColumn()
    {
        var (archive, rollup) = Hourly(Spec("Amount.Value", CkRollupFunction.Sum));

        var resolution = RollupSourceColumnResolver.TryResolve(
            Spec("amountvalue_sum", CkRollupFunction.Sum), archive, rollup);

        Assert.NotNull(resolution);
        Assert.Equal(RollupSourceColumnResolutionKind.DeclaredColumn, resolution.Kind);
        Assert.Null(resolution.ChildAggregation);
    }

    // ---- Rule 2: child aggregation of a rollup source ---------------------------------------

    [Fact]
    public void TryResolve_LogicalSpecOverRollupStoringTheSameFunction_ResolvesAsChildAggregation()
    {
        var child = Spec("Amount.Value", CkRollupFunction.Sum);
        var (archive, rollup) = Hourly(child);

        var resolution = RollupSourceColumnResolver.TryResolve(
            Spec("Amount.Value", CkRollupFunction.Sum), archive, rollup);

        Assert.NotNull(resolution);
        Assert.Equal(RollupSourceColumnResolutionKind.ChildAggregation, resolution.Kind);
        Assert.Same(child, resolution.ChildAggregation);
        Assert.Equal(new[] { "amountvalue_sum" }, RollupColumnGenerator.TargetColumnNamesFor(resolution.ChildAggregation!));
    }

    [Fact]
    public void TryResolve_LogicalSpecOverPhysicallyChainedRollup_ResolvesAsChildAggregation()
    {
        // A seeded pre-AB#5157 rollup stores its own child's physical column and pins the stored name
        // with TargetColumnName ("amountvalue_sum"), so its spec names that physical column, not the
        // logical path — its normalised path never equals the logical parent's. A new logical rollup
        // stacked on top must still resolve against it (WI AB#5157 review: the sbeg quarter rung over
        // the seeded EC monthly rung). The bridge matches on the generated column instead.
        var chained = Spec("amountvalue_sum", CkRollupFunction.Sum, "amountvalue_sum");
        var (archive, rollup) = Hourly(chained);

        var resolution = RollupSourceColumnResolver.TryResolve(
            Spec("Amount.Value", CkRollupFunction.Sum), archive, rollup);

        Assert.NotNull(resolution);
        Assert.Equal(RollupSourceColumnResolutionKind.ChildAggregation, resolution.Kind);
        Assert.Same(chained, resolution.ChildAggregation);
        Assert.Equal(new[] { "amountvalue_sum" }, RollupColumnGenerator.TargetColumnNamesFor(resolution.ChildAggregation!));
    }

    [Fact]
    public void TryResolve_LogicalSpecOverPhysicallyChainedRollup_WrongFunction_ReturnsNull()
    {
        // The generated column matches by name, but the child aggregated with a different function —
        // the Function guard keeps a Max column from serving a Sum parent even under the same name.
        var chained = Spec("amountvalue_sum", CkRollupFunction.Max, "amountvalue_sum");
        var (archive, rollup) = Hourly(chained);

        Assert.Null(RollupSourceColumnResolver.TryResolve(
            Spec("Amount.Value", CkRollupFunction.Sum), archive, rollup));
    }

    [Fact]
    public void TryResolve_LogicalSpecOverPhysicallyChainedRollupProducingAnotherColumn_ReturnsNull()
    {
        // The chained child stores a different physical column, so it is not the parent's series.
        var chained = Spec("other_sum", CkRollupFunction.Sum, "other_sum");
        var (archive, rollup) = Hourly(chained);

        Assert.Null(RollupSourceColumnResolver.TryResolve(
            Spec("Amount.Value", CkRollupFunction.Sum), archive, rollup));
    }

    [Fact]
    public void TryResolve_LogicalSpecOverRollupStoringAnotherFunction_ReturnsNull()
    {
        var (archive, rollup) = Hourly(Spec("Amount.Value", CkRollupFunction.Sum));

        Assert.Null(RollupSourceColumnResolver.TryResolve(
            Spec("Amount.Value", CkRollupFunction.Avg), archive, rollup));
    }

    [Fact]
    public void TryResolve_LogicalSpecOverRollupStoringAnotherPath_ReturnsNull()
    {
        var (archive, rollup) = Hourly(Spec("Amount.Other", CkRollupFunction.Sum));

        Assert.Null(RollupSourceColumnResolver.TryResolve(
            Spec("Amount.Value", CkRollupFunction.Sum), archive, rollup));
    }

    [Fact]
    public void TryResolve_LogicalAvgOverRollupStoringAvg_ResolvesToThePairColumns()
    {
        var child = Spec("Amount.Value", CkRollupFunction.Avg);
        var (archive, rollup) = Hourly(child);

        var resolution = RollupSourceColumnResolver.TryResolve(
            Spec("Amount.Value", CkRollupFunction.Avg), archive, rollup);

        Assert.NotNull(resolution);
        Assert.Equal(RollupSourceColumnResolutionKind.ChildAggregation, resolution.Kind);
        Assert.Equal(
            new[] { "amountvalue_avg_sum", "amountvalue_avg_count" },
            RollupColumnGenerator.TargetColumnNamesFor(resolution.ChildAggregation!));
    }

    [Fact]
    public void TryResolve_PicksTheChildSpecWithTheMatchingFunctionAmongSeveral()
    {
        var sum = Spec("Amount.Value", CkRollupFunction.Sum);
        var max = Spec("Amount.Value", CkRollupFunction.Max);
        var (archive, rollup) = Hourly(sum, max);

        var resolution = RollupSourceColumnResolver.TryResolve(
            Spec("Amount.Value", CkRollupFunction.Max), archive, rollup);

        Assert.NotNull(resolution);
        Assert.Same(max, resolution.ChildAggregation);
    }

    [Theory]
    [InlineData("amount.value")]
    [InlineData("AmountValue")]
    [InlineData("AMOUNT.VALUE")]
    public void TryResolve_ParentAndChildPathsMatchAfterNormalisation(string parentPath)
    {
        var child = Spec("Amount.Value", CkRollupFunction.Sum);
        var (archive, rollup) = Hourly(child);

        var resolution = RollupSourceColumnResolver.TryResolve(
            Spec(parentPath, CkRollupFunction.Sum), archive, rollup);

        Assert.NotNull(resolution);
        Assert.Equal(RollupSourceColumnResolutionKind.ChildAggregation, resolution.Kind);
        Assert.Same(child, resolution.ChildAggregation);
    }

    [Fact]
    public void TryResolve_WithoutTheSourceRollupSnapshot_AppliesRuleOneOnly()
    {
        // The archive-level snapshot of a rollup declares physical names only; without its rollup
        // snapshot a logical spec has nothing to match against and does not resolve.
        var (archive, _) = Hourly(Spec("Amount.Value", CkRollupFunction.Sum));

        Assert.Null(RollupSourceColumnResolver.TryResolve(
            Spec("Amount.Value", CkRollupFunction.Sum), archive, sourceRollup: null));
    }

    // ---- Precedence ---------------------------------------------------------------------------

    [Fact]
    public void TryResolve_BothRulesApply_RuleOneWins()
    {
        // The child stores ('x', Sum) under the explicit column name 'x', so the parent's ('x', Sum)
        // matches the declared column verbatim (rule 1) AND the child spec (rule 2).
        var child = Spec("x", CkRollupFunction.Sum, targetColumnName: "x");
        var (archive, rollup) = Hourly(child);
        Assert.Contains("x", RollupSourceColumnResolver.CapturedPaths(archive));

        var resolution = RollupSourceColumnResolver.TryResolve(Spec("x", CkRollupFunction.Sum), archive, rollup);

        Assert.NotNull(resolution);
        Assert.Equal(RollupSourceColumnResolutionKind.DeclaredColumn, resolution.Kind);
        Assert.Null(resolution.ChildAggregation);
    }

    // ---- The AC1 shape: one logical spec over a base archive AND a rollup of it -------------

    [Fact]
    public void TryResolve_OneLogicalSpec_ResolvesOnBothATimeRangeBaseAndAnHourlyRollup()
    {
        var spec = Spec("Amount.Value", CkRollupFunction.Sum);
        var (hourlyArchive, hourlyRollup) = Hourly(Spec("Amount.Value", CkRollupFunction.Sum));

        var onBase = RollupSourceColumnResolver.TryResolve(spec, Base("Amount.Value"), sourceRollup: null);
        var onHourly = RollupSourceColumnResolver.TryResolve(spec, hourlyArchive, hourlyRollup);

        Assert.Equal(RollupSourceColumnResolutionKind.DeclaredColumn, onBase?.Kind);
        Assert.Equal(RollupSourceColumnResolutionKind.ChildAggregation, onHourly?.Kind);
    }

    // ---- StateDuration: the compared state is part of the identity of the aggregation ---------

    [Fact]
    public void TryResolve_StateDurationOverAChildComparingTheSameState_Resolves()
    {
        var child = new CkRollupAggregationSpec("Lamp.On", CkRollupFunction.StateDuration, null, "true");
        var (archive, rollup) = Hourly(child);

        var resolution = RollupSourceColumnResolver.TryResolve(
            new CkRollupAggregationSpec("Lamp.On", CkRollupFunction.StateDuration, null, "true"), archive, rollup);

        Assert.NotNull(resolution);
        Assert.Equal(RollupSourceColumnResolutionKind.ChildAggregation, resolution.Kind);
        Assert.Same(child, resolution.ChildAggregation);
    }

    [Fact]
    public void TryResolve_StateDurationOverAChildComparingAnotherState_ReturnsNull()
    {
        // Summing the child's 'false' durations into the parent's 'true' column would silently
        // produce wrong numbers, so the mismatch must not resolve (the validator then refuses it).
        var (archive, rollup) = Hourly(
            new CkRollupAggregationSpec("Lamp.On", CkRollupFunction.StateDuration, null, "false"));

        Assert.Null(RollupSourceColumnResolver.TryResolve(
            new CkRollupAggregationSpec("Lamp.On", CkRollupFunction.StateDuration, null, "true"), archive, rollup));
    }

    [Fact]
    public void TryResolve_StateDurationPicksTheChildWithTheMatchingState()
    {
        var onSpec = new CkRollupAggregationSpec("Lamp.On", CkRollupFunction.StateDuration, "lamp_on", "true");
        var offSpec = new CkRollupAggregationSpec("Lamp.On", CkRollupFunction.StateDuration, "lamp_off", "false");
        var (archive, rollup) = Hourly(offSpec, onSpec);

        var resolution = RollupSourceColumnResolver.TryResolve(
            new CkRollupAggregationSpec("Lamp.On", CkRollupFunction.StateDuration, null, "true"), archive, rollup);

        Assert.NotNull(resolution);
        Assert.Same(onSpec, resolution.ChildAggregation);
    }

    [Fact]
    public void TryResolve_NonStateDurationFunctionsIgnoreTheComparisonValue()
    {
        var child = new CkRollupAggregationSpec("Amount.Value", CkRollupFunction.Sum, null, "true");
        var (archive, rollup) = Hourly(child);

        var resolution = RollupSourceColumnResolver.TryResolve(
            new CkRollupAggregationSpec("Amount.Value", CkRollupFunction.Sum, null, "false"), archive, rollup);

        Assert.NotNull(resolution);
        Assert.Same(child, resolution.ChildAggregation);
    }

    [Fact]
    public void TryResolve_NullSpecOrSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RollupSourceColumnResolver.TryResolve(null!, Base("Amount.Value"), null));
        Assert.Throws<ArgumentNullException>(() =>
            RollupSourceColumnResolver.TryResolve(Spec("Amount.Value", CkRollupFunction.Sum), null!, null));
    }
}
