using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Meshmakers.Octo.Runtime.Contracts.Formulas;
using Meshmakers.Octo.Runtime.Engine.Formulas;
using org.mariuszgromada.math.mxparser;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Formulas;

/// <summary>
/// AB#8331: a computed-column backfill of ~2 M rows on prod-1 ran the asset repository out of memory
/// because every evaluation built a new mXparser expression (~200 KiB garbage per row). The engine now
/// parses a formula once per thread and reuses it; exceptions inside mXparser are no longer turned into
/// a silent NaN; the mXparser use type is confirmed once.
/// <remarks>
/// Not run in parallel with other collections: the retained-memory assertion reads the process-wide live heap.
/// </remarks>
[Collection(FormulaMemoryCollection.Name)]
public class FormulaEngineReuseTests(ITestOutputHelper output)
{
    private readonly FormulaEngine _engine = new();

    private static Dictionary<string, double> Row(double activePower) => new(StringComparer.Ordinal)
    {
        ["activepower"] = activePower,
        ["reactivepower"] = 3,
        ["voltage"] = 230,
        ["current"] = 12,
        ["frequency"] = 50,
    };

    [Fact]
    public void LicenseIsConfirmed_OnceTheEngineIsUsed()
    {
        _engine.EvaluateRaw("1 + 1", new Dictionary<string, double>());

        Assert.True(MxParserLicense.IsConfirmed);
    }

    [Fact]
    public void ReusedFormula_ComputesEveryRowWithItsOwnValues()
    {
        const string formula = "if(activepower > 0, activepower, 0)";
        for (var i = -50; i <= 50; i++)
        {
            Assert.Equal(Math.Max(i, 0), _engine.EvaluateRaw(formula, Row(i)));
        }
    }

    /// <summary>
    /// The large synthetic archive of the acceptance criteria at the formula level: one million rows of a
    /// prod-1 formula. Before the fix every evaluation allocated ~200 KiB (≈ 190 GiB for this loop); a reused
    /// formula stays in the single-digit KiB.
    /// </summary>
    [Fact]
    public void MillionRows_AllocateOnlyAFewKiBPerEvaluation()
    {
        const int rows = 1_000_000;
        const string formula = "if(activepower > 0, activepower, 0)";
        var row = Row(0);
        _engine.EvaluateRaw(formula, row); // prepare outside the measurement

        var liveBefore = GC.GetTotalMemory(true);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var started = DateTime.UtcNow;
        double sum = 0;
        for (var i = 0; i < rows; i++)
        {
            row["activepower"] = (i % 200) - 100;
            sum += _engine.EvaluateRaw(formula, row);
        }

        var perEvaluation = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)rows;
        var retainedMiB = (GC.GetTotalMemory(true) - liveBefore) / 1024.0 / 1024.0;
        output.WriteLine(
            $"{rows:N0} evaluations: {perEvaluation:F0} B allocated per evaluation, {retainedMiB:F1} MiB retained, " +
            $"{(DateTime.UtcNow - started).TotalMilliseconds:F0} ms, sum {sum}");

        Assert.Equal(rows / 200.0 * Enumerable.Range(1, 99).Sum(), sum);
        Assert.True(perEvaluation < 16 * 1024, $"Expected < 16 KiB per evaluation, measured {perEvaluation:F0} B.");
        // mXparser 6.1.1 keeps ~400 B per calculate() of an 'if' formula on a reused expression; the engine
        // parses again every 4096 evaluations, so a million rows must not retain ~400 MB.
        Assert.True(retainedMiB < 16, $"Expected < 16 MiB retained after a full collection, measured {retainedMiB:F1} MiB.");
    }

    [Fact]
    public void UnreferencedValueNamedLikeABuiltIn_NoLongerBreaksTheFormula()
    {
        // Before AB#8331 every supplied value was bound, and mXparser refuses a duplicate keyword ('e').
        var args = new Dictionary<string, double> { ["x"] = 1, ["e"] = 5 };

        Assert.Equal(2, _engine.EvaluateRaw("x + 1", args));
    }

    [Fact]
    public void ReferencedValueNamedLikeABuiltIn_StaysNaN_AsBefore()
    {
        var args = new Dictionary<string, double> { ["e"] = 5 };

        Assert.True(double.IsNaN(_engine.EvaluateRaw("e + 1", args)));
        Assert.Equal(Math.E + 1, _engine.EvaluateRaw("e + 1", new Dictionary<string, double>()), 9);
    }

    [Fact]
    public void ReferencedNameNotSupplied_IsNaN_AndTheNextRowWithTheNameComputes()
    {
        const string formula = "a + b";

        Assert.True(double.IsNaN(_engine.EvaluateRaw(formula, new Dictionary<string, double> { ["a"] = 1 })));
        Assert.Equal(3, _engine.EvaluateRaw(formula, new Dictionary<string, double> { ["a"] = 1, ["b"] = 2 }));
    }

    [Fact]
    public void NullOperandSemantics_AreKept()
    {
        const string formula = "coalesce(x, 7) + isnull(y)";

        Assert.Equal(8, _engine.EvaluateRaw(formula, new Dictionary<string, double> { ["x"] = double.NaN, ["y"] = double.NaN }));
        Assert.Equal(2, _engine.EvaluateRaw(formula, new Dictionary<string, double> { ["x"] = 2, ["y"] = 1 }));
        Assert.Null(_engine.Evaluate("x * 2", new Dictionary<string, double> { ["x"] = double.NaN }, FormulaResultType.Double));
    }

    /// <summary>
    /// mXparser's list of missing arguments splits names around built-in constants under implied
    /// multiplication (<c>value</c> = <c>valu·e</c>); the engine must bind the whole name.
    /// </summary>
    [Theory]
    [InlineData("value > 0")]
    [InlineData("if(activepower > 0, activepower, 0)")]
    [InlineData("chargepower + exportenergy * 0")]
    public void NamesContainingBuiltInLetters_AreBoundAsWholeNames(string formula)
    {
        var args = new Dictionary<string, double> { ["value"] = 5, ["activepower"] = 5, ["chargepower"] = 5, ["exportenergy"] = 1 };

        Assert.Equal(formula.StartsWith("value", StringComparison.Ordinal) ? 1 : 5, _engine.EvaluateRaw(formula, args));
    }

    [Fact]
    public void ManyRowsWithChangingColumns_StillComputeCorrectly()
    {
        const string formula = "coalesce(a, 0) + coalesce(b, 0)";
        for (var i = 0; i < 200; i++)
        {
            var args = new Dictionary<string, double>();
            if (i % 2 == 0) args["a"] = i;
            if (i % 3 == 0) args["b"] = 1;
            var expected = (i % 2 == 0 && i % 3 == 0) ? i + 1 : double.NaN; // an unbound name is no NULL operand
            var result = _engine.EvaluateRaw(formula, args);
            if (double.IsNaN(expected)) Assert.True(double.IsNaN(result)); else Assert.Equal(expected, result);
        }
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1e300")]
    public void DateTimeResultOutsideTheRange_IsNull_NotAnException(string formula)
    {
        // Since AB#8331 an exception fails the caller's write; a bad value must stay a NULL cell.
        Assert.Null(_engine.Evaluate(formula, new Dictionary<string, double>(), FormulaResultType.DateTime));
    }

    [Fact]
    public void InvalidSyntax_StaysNaN()
    {
        Assert.True(double.IsNaN(_engine.EvaluateRaw("a +* b", new Dictionary<string, double> { ["a"] = 1, ["b"] = 2 })));
    }

    [Fact]
    public async Task ParallelThreads_EvaluateIndependently()
    {
        const string formula = "a * 2 + b";
        var tasks = Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 20_000; i++)
            {
                var result = _engine.EvaluateRaw(formula, new Dictionary<string, double> { ["a"] = t, ["b"] = i });
                Assert.Equal(t * 2 + i, result);
            }
        }));

        await Task.WhenAll(tasks);
    }

    [Fact]
    public void ExceptionInsideMxParser_IsThrown_NotReturnedAsNaN()
    {
        var thrower = new ThrowingFunction();
        var prepared = PreparedFormula.Create("boom(x)", "boom(x)", new[] { "x" }, new Function("boom", thrower));
        var row = new Dictionary<string, double> { ["x"] = 4 };
        Assert.Equal(4, prepared.Evaluate(row));

        thrower.Throw = true;
        var ex = Assert.Throws<FormulaEvaluationException>(() => prepared.Evaluate(row));

        Assert.Equal("boom(x)", ex.Expression);
        Assert.Contains(nameof(OutOfMemoryException), ex.Message);
    }

    [Fact]
    public void LegitimateNaN_IsNotMistakenForAnException()
    {
        var prepared = PreparedFormula.Create("x / 0 * 0", "x / 0 * 0", new[] { "x" });

        Assert.True(double.IsNaN(prepared.Evaluate(new Dictionary<string, double> { ["x"] = 0 })));
        Assert.True(double.IsNaN(prepared.Evaluate(new Dictionary<string, double> { ["x"] = double.NaN })));
    }

    private sealed class ThrowingFunction : FunctionExtension
    {
        private double _value;

        public bool Throw { get; set; }

        public int getParametersNumber() => 1;

        public void setParameterValue(int parameterIndex, double parameterValue) => _value = parameterValue;

        public string getParameterName(int parameterIndex) => "v";

        public double calculate() => Throw ? throw new OutOfMemoryException() : _value;

        public FunctionExtension clone() => this;
    }
}

/// <summary>Runs memory measurements alone, so other test collections cannot move the process-wide live heap.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FormulaMemoryCollection
{
    /// <summary>Collection name.</summary>
    public const string Name = "Formula memory measurements";
}
