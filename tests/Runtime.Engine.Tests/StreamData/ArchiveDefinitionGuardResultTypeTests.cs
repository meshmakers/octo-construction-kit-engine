using Meshmakers.Octo.Runtime.Contracts.Formulas;
using Meshmakers.Octo.Runtime.Engine.StreamData;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
/// AB#7997: the result type of a computed column a seed adds is read by name or key; a value that names no defined type
/// is null, so the addition fails with "needs a ResultType" instead of reaching the lifecycle with an undefined enum.
/// </summary>
public class ArchiveDefinitionGuardResultTypeTests
{
    [Theory]
    [InlineData("Double", FormulaResultType.Double)]
    [InlineData("double", FormulaResultType.Double)]
    [InlineData("Boolean", FormulaResultType.Boolean)]
    [InlineData(3, FormulaResultType.Double)]
    [InlineData(3L, FormulaResultType.Double)]
    [InlineData("3", FormulaResultType.Double)]
    [InlineData(FormulaResultType.Int64, FormulaResultType.Int64)]
    public void DefinedValues_AreRead(object value, FormulaResultType expected)
    {
        Assert.Equal(expected, ArchiveDefinitionGuard.ToFormulaResultType(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(99)]
    [InlineData("99")]
    [InlineData(-1)]
    [InlineData("Decimal")]
    [InlineData("")]
    public void UndefinedValues_AreNull(object? value)
    {
        Assert.Null(ArchiveDefinitionGuard.ToFormulaResultType(value));
    }
}
