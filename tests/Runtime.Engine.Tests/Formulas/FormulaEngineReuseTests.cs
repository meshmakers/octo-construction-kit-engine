using System;
using System.Collections.Generic;
using System.IO;
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
/// a silent NaN.
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

    /// <summary>
    /// Meshmakers holds no commercial mXparser licence (AB#8364). mXparser is dual-licensed from 4.4.3 on
    /// (5.x, 6.x: commercial use needs a purchased licence); the engine ships 4.4.2, the last version under the
    /// Simplified BSD licence. 4.4.2 has no <c>License</c> class, so nothing can confirm a use type; an upgrade
    /// to a dual-licensed version fails here.
    /// </summary>
    [Fact]
    public void MxParser_IsTheLastBsdLicensedVersion()
    {
        var assembly = typeof(Expression).Assembly;

        Assert.Equal(new Version(4, 4, 2), TakeThree(assembly.GetName().Version));
        Assert.Null(assembly.GetType("org.mariuszgromada.math.mxparser.License"));
    }

    /// <summary>
    /// mXparser 5.x/6.x wrote up to ~1000 licence warning blocks per process to the console. 4.4.2 writes
    /// nothing: no line on stdout or stderr while formulas are checked, validated and evaluated.
    /// </summary>
    [Fact]
    public void FormulaEvaluations_WriteNoLineToTheConsole()
    {
        var engine = new FormulaEngine();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        Console.SetError(captured);
        try
        {
            engine.CheckSyntax("isnull(a) + now(0) * 0 + startOfDay(0) * 0", ["a"]);
            engine.Validate("a > 0 ? a : 0", new Dictionary<string, double> { ["a"] = 2 });
            for (var i = 0; i < 1_000; i++)
            {
                engine.EvaluateRaw($"if(a > {i}, a, coalesce(b, 0))", new Dictionary<string, double> { ["a"] = i, ["b"] = double.NaN });
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Equal(string.Empty, captured.ToString());
    }

    /// <summary>
    /// Dialect of 4.4.2 (AB#8364): implied multiplication and the Unicode operators came with mXparser 5.0, so a
    /// formula must write the operator. Documented in docs/concept-computed-columns.md §8.3b.
    /// </summary>
    [Theory]
    [InlineData("2x", false)]
    [InlineData("2(x + 1)", false)]
    [InlineData("√x", false)]
    [InlineData("2*x", true)]
    [InlineData("2*(x + 1)", true)]
    [InlineData("sqrt(x)", true)]
    public void ImpliedMultiplication_IsNotPartOfTheDialect(string formula, bool valid)
    {
        Assert.Equal(valid, _engine.CheckSyntax(formula, ["x"]).IsValid);
    }

    private static Version? TakeThree(Version? version) =>
        version is null ? null : new Version(version.Major, version.Minor, version.Build);

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
        // mXparser 6.1.1 kept ~400 B per calculate() of an 'if' formula on a reused expression (~400 MB for a
        // million rows); 4.4.2 retains nothing (AB#8364), so the engine reuses a parsed formula without limit.
        // This bound guards against a regression of the reuse or of mXparser.
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
    /// mXparser 6.x split names around built-in constants under implied multiplication
    /// (<c>value</c> = <c>valu·e</c>); the engine binds whole names from its own identifier scan.
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

    /// <summary>
    /// mXparser 4.4.2 lets an exception from inside <c>calculate()</c> propagate (6.x returned NaN); the engine
    /// wraps it, wherever it is raised in the formula, so no caller stores NULL for an aborted evaluation.
    /// </summary>
    [Theory]
    [InlineData("boom(x)")]
    [InlineData("if(x > 0, boom(x), 0)")]
    [InlineData("coalesce(boom(x), 0)")]
    [InlineData("sum(i, 1, 2, boom(x)) / 2")]
    public void ExceptionInsideMxParser_IsThrown_NotReturnedAsNaN(string formula)
    {
        var thrower = new ThrowingFunction();
        var prepared = PreparedFormula.Create(formula, formula, new[] { "x" }, new Function("boom", thrower));
        var row = new Dictionary<string, double> { ["x"] = 4 };
        Assert.Equal(4, prepared.Evaluate(row));

        thrower.Throw = true;
        var ex = Assert.Throws<FormulaEvaluationException>(() => prepared.Evaluate(row));

        Assert.Equal(formula, ex.Expression);
        Assert.Contains(nameof(OutOfMemoryException), ex.Message);
        Assert.IsType<OutOfMemoryException>(ex.InnerException);
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
