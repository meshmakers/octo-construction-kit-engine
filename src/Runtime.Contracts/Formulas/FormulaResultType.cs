namespace Meshmakers.Octo.Runtime.Contracts.Formulas;

/// <summary>
/// The declared output type a formula's numeric (<see cref="double"/>) result is cast back to.
/// Mirrors the cast-back ladder already used by the runtime-query field-filter resolver
/// (<c>FieldFilterResolver.ResolveSearchAttributeValue</c>).
/// <para>
/// <c>String</c> and non-scalar types are deliberately unsupported — the underlying mXparser
/// engine is numeric. Booleans round-trip as <c>0</c>/<c>1</c> and <see cref="System.DateTime"/>
/// as ticks.
/// </para>
/// </summary>
public enum FormulaResultType
{
    /// <summary>Result is <c>true</c> when the numeric value is non-zero.</summary>
    Boolean,

    /// <summary>Result is cast to a 32-bit integer (truncating).</summary>
    Int,

    /// <summary>Result is cast to a 64-bit integer (truncating).</summary>
    Int64,

    /// <summary>Result is the raw <see cref="double"/>.</summary>
    Double,

    /// <summary>Result is interpreted as ticks (<c>new DateTime((long)result)</c>).</summary>
    DateTime
}
