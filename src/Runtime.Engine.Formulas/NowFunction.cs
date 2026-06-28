using org.mariuszgromada.math.mxparser;

namespace Meshmakers.Octo.Runtime.Engine.Formulas;

internal class NowFunction : FunctionExtension
{
    private double _minutesCount;

    public NowFunction()
    {
        _minutesCount = double.NaN;
    }

    public NowFunction(double minutesCount)
    {
        _minutesCount = minutesCount;
    }

    public int getParametersNumber()
    {
        return 1;
    }

    public void setParameterValue(int parameterIndex, double parameterValue)
    {
        if (parameterIndex == 0)
        {
            _minutesCount = parameterValue;
        }
    }

    public string getParameterName(int parameterIndex)
    {
        if (parameterIndex == 0)
        {
            return "addMinutes";
        }

        return "";
    }

    public double calculate()
    {
        return DateTime.Now.AddMinutes(_minutesCount).Ticks;
    }

    public FunctionExtension clone()
    {
        return new NowFunction(_minutesCount);
    }
}
