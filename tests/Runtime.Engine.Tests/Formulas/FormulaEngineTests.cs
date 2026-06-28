using System;
using System.Collections.Generic;
using Meshmakers.Octo.Runtime.Contracts.Formulas;
using Meshmakers.Octo.Runtime.Engine.Formulas;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Formulas;

public class FormulaEngineTests
{
    private readonly FormulaEngine _engine = new();

    private static IReadOnlyDictionary<string, double> Args(params (string Name, double Value)[] pairs)
    {
        var dict = new Dictionary<string, double>();
        foreach (var (name, value) in pairs)
        {
            dict[name] = value;
        }

        return dict;
    }

    // ---- NormalizeTernary -------------------------------------------------

    [Fact]
    public void NormalizeTernary_NoTernary_ReturnsUnchanged()
    {
        Assert.Equal("value + 1", _engine.NormalizeTernary("value + 1"));
    }

    [Fact]
    public void NormalizeTernary_SimpleTernary_ConvertsToIf()
    {
        Assert.Equal("if(value > 0, value, 0)", _engine.NormalizeTernary("value > 0 ? value : 0"));
    }

    [Fact]
    public void NormalizeTernary_NestedTernary_ConvertsAllLevels()
    {
        // Parentheses around the inner ternary are preserved verbatim.
        var result = _engine.NormalizeTernary("a > 0 ? (b > 0 ? 1 : 2) : 3");
        Assert.Equal("if(a > 0, (if(b > 0, 1, 2)), 3)", result);
    }

    // ---- EvaluateRaw ------------------------------------------------------

    [Fact]
    public void EvaluateRaw_Arithmetic_BindsArgumentsByName()
    {
        var result = _engine.EvaluateRaw("activePower / apparentPower",
            Args(("activePower", 8.0), ("apparentPower", 10.0)));

        Assert.Equal(0.8, result, 10);
    }

    [Fact]
    public void EvaluateRaw_TernarySugar_IsNormalized()
    {
        var result = _engine.EvaluateRaw("value > 0 ? value : 0", Args(("value", -5.0)));
        Assert.Equal(0.0, result, 10);
    }

    [Fact]
    public void EvaluateRaw_NullConstant_ReturnsNegativeInfinity()
    {
        var result = _engine.EvaluateRaw("null", Args());
        Assert.True(double.IsNegativeInfinity(result));
    }

    // ---- Evaluate (cast-back ladder) --------------------------------------

    [Fact]
    public void Evaluate_Double_ReturnsRawValue()
    {
        var result = _engine.Evaluate("3.5 * 2", Args(), FormulaResultType.Double);
        Assert.Equal(7.0, Assert.IsType<double>(result), 10);
    }

    [Theory]
    [InlineData(5.0, true)]
    [InlineData(-5.0, false)]
    public void Evaluate_Boolean_ComparisonResult(double value, bool expected)
    {
        var result = _engine.Evaluate("value > 0", Args(("value", value)), FormulaResultType.Boolean);
        Assert.Equal(expected, Assert.IsType<bool>(result));
    }

    [Theory]
    [InlineData(5.0, true)]
    [InlineData(0.0, false)]
    public void Evaluate_Boolean_NonZeroIsTrue(double value, bool expected)
    {
        var result = _engine.Evaluate("value", Args(("value", value)), FormulaResultType.Boolean);
        Assert.Equal(expected, Assert.IsType<bool>(result));
    }

    [Fact]
    public void Evaluate_Int_TruncatesToInt32()
    {
        var result = _engine.Evaluate("7.9", Args(), FormulaResultType.Int);
        Assert.Equal(7, Assert.IsType<int>(result));
    }

    [Fact]
    public void Evaluate_Int64_TruncatesToInt64()
    {
        var result = _engine.Evaluate("4000000000", Args(), FormulaResultType.Int64);
        Assert.Equal(4_000_000_000L, Assert.IsType<long>(result));
    }

    [Fact]
    public void Evaluate_DateTime_InterpretsResultAsTicks()
    {
        var ticks = new DateTime(2026, 6, 28, 12, 0, 0, DateTimeKind.Utc).Ticks;
        var result = _engine.Evaluate(ticks.ToString(), Args(), FormulaResultType.DateTime);
        Assert.Equal(ticks, Assert.IsType<DateTime>(result).Ticks);
    }

    [Fact]
    public void Evaluate_NullConstant_ReturnsNull()
    {
        var result = _engine.Evaluate("null", Args(), FormulaResultType.Double);
        Assert.Null(result);
    }

    [Fact]
    public void Evaluate_NaN_ReturnsNull()
    {
        // 0/0 is NaN in mXparser
        var result = _engine.Evaluate("0 / 0", Args(), FormulaResultType.Double);
        Assert.Null(result);
    }

    // ---- Validate ---------------------------------------------------------

    [Fact]
    public void Validate_EmptyExpression_IsInvalid()
    {
        var result = _engine.Validate("  ", Args());
        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Validate_ValidExpression_IsValidAndNormalized()
    {
        var result = _engine.Validate("value > 0 ? value : 0", Args(("value", 42.0)));
        Assert.True(result.IsValid);
        Assert.Equal("if(value > 0, value, 0)", result.NormalizedExpression);
        Assert.NotNull(result.Result);
    }

    [Fact]
    public void Validate_BindsSuppliedTestValue()
    {
        var result = _engine.Validate("value", Args(("value", 99.0)));
        Assert.True(result.IsValid);
        Assert.Equal(99.0, result.Result);
    }

    [Fact]
    public void Validate_UnknownArgument_IsInvalid()
    {
        // 'apparentPower' is not declared → syntax check fails.
        var result = _engine.Validate("activePower / apparentPower", Args(("activePower", 42.0)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_SyntaxError_IsInvalid()
    {
        var result = _engine.Validate("(value + 1", Args(("value", 42.0)));
        Assert.False(result.IsValid);
    }

    // ---- CheckSyntax (no evaluation) --------------------------------------

    [Fact]
    public void CheckSyntax_ValidReferences_IsValid()
    {
        var result = _engine.CheckSyntax("a / b", new[] { "a", "b" });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void CheckSyntax_DoesNotEvaluate_NoNaNFalsePositive()
    {
        // 'a / (b - b)' is a division by zero at runtime, but syntactically valid — CheckSyntax must
        // not reject it (unlike Validate, which evaluates and would see NaN).
        var result = _engine.CheckSyntax("a / (b - b)", new[] { "a", "b" });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void CheckSyntax_UnknownReference_IsInvalid()
    {
        var result = _engine.CheckSyntax("a / c", new[] { "a" });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CheckSyntax_SyntaxError_IsInvalid()
    {
        var result = _engine.CheckSyntax("(a + ", new[] { "a" });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CheckSyntax_Empty_IsInvalid()
    {
        Assert.False(_engine.CheckSyntax("  ", Array.Empty<string>()).IsValid);
    }
}
