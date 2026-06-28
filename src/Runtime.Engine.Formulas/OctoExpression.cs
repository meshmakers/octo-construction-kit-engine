using org.mariuszgromada.math.mxparser;

namespace Meshmakers.Octo.Runtime.Engine.Formulas;

/// <summary>
/// mXparser <see cref="Expression"/> pre-loaded with the OctoMesh formula extensions:
/// the <c>now(addMinutes)</c> and <c>startOfDay(dayCount)</c> functions and the <c>null</c>
/// constant (mapped to <see cref="double.NegativeInfinity"/>, the engine's null sentinel).
/// </summary>
public class OctoExpression : Expression
{
    private readonly Function _nowFunction = new("now", new NowFunction());

    private readonly Constant _nullFunction = new("null", double.NegativeInfinity);
    private readonly Function _startOfDayFunction = new("startOfDay", new StartOfDayFunction());

    /// <summary>
    /// Creates an expression for <paramref name="expressionString"/> with the OctoMesh
    /// functions and constants registered.
    /// </summary>
    public OctoExpression(string expressionString) : base(expressionString)
    {
        addDefinitions(_startOfDayFunction);
        addDefinitions(_nowFunction);
        addDefinitions(_nullFunction);
    }
}
