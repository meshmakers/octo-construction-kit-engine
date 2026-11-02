using org.mariuszgromada.math.mxparser;

namespace Meshmakers.Octo.Runtime.Engine.Formulas;

/// <summary>
/// <c>isnull(x)</c>: <c>1</c> when <c>x</c> has no value, otherwise <c>0</c>. "No value" is a NULL
/// operand (bound as <see cref="double.NaN"/>, e.g. a column a row does not carry) or the engine's
/// <c>null</c> constant (<see cref="double.NegativeInfinity"/>). Pairs with the built-in
/// <c>coalesce(x, default, …)</c>, which returns its first non-NaN argument (AB#8060).
/// </summary>
internal sealed class IsNullFunction : FunctionExtension
{
    private double _value;

    public IsNullFunction()
    {
        _value = double.NaN;
    }

    private IsNullFunction(double value)
    {
        _value = value;
    }

    public int getParametersNumber()
    {
        return 1;
    }

    public void setParameterValue(int parameterIndex, double parameterValue)
    {
        if (parameterIndex == 0)
        {
            _value = parameterValue;
        }
    }

    public string getParameterName(int parameterIndex)
    {
        return parameterIndex == 0 ? "x" : "";
    }

    public double calculate()
    {
        return IsNull(_value) ? 1d : 0d;
    }

    public FunctionExtension clone()
    {
        return new IsNullFunction(_value);
    }

    /// <summary>True for a NULL operand (NaN) and for the <c>null</c> sentinel (negative infinity).</summary>
    internal static bool IsNull(double value) => double.IsNaN(value) || double.IsNegativeInfinity(value);
}
