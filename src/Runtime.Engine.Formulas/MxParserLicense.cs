using org.mariuszgromada.math.mxparser;

namespace Meshmakers.Octo.Runtime.Engine.Formulas;

/// <summary>
/// Confirms the mXparser use type once per process (AB#8331). Without the confirmation mXparser writes
/// a ten-line "License - use type not confirmed" warning to stderr on every <c>calculate()</c> and every
/// new <c>Argument</c> / <c>Function</c>, up to 1000 times per process; on prod-1 that flooded the asset
/// repository log during a computed-column backfill. The confirmation is only logged by mXparser, it does
/// not change the evaluation. Every entry point of this assembly that creates an mXparser element calls
/// <see cref="EnsureConfirmed"/> first (<see cref="FormulaEngine"/> and <see cref="OctoExpression"/>).
/// </summary>
public static class MxParserLicense
{
    /// <summary>The signature recorded with the confirmation.</summary>
    public const string Signature = "meshmakers GmbH - OctoMesh";

    private static readonly object Gate = new();
    private static volatile bool _confirmed;

    /// <summary>Whether mXparser reports the use type as confirmed.</summary>
    public static bool IsConfirmed => License.checkIfUseTypeConfirmed();

    /// <summary>
    /// Confirms the commercial use type with <see cref="Signature"/>. Idempotent and thread-safe; only the
    /// first call reaches mXparser (it accepts exactly one confirmation per process).
    /// </summary>
    public static void EnsureConfirmed()
    {
        if (_confirmed)
        {
            return;
        }

        lock (Gate)
        {
            if (_confirmed)
            {
                return;
            }

            // Not checkIfUseTypeConfirmed() first: it prints a warning itself while nothing is confirmed.
            License.iConfirmCommercialUse(Signature);

            _confirmed = true;
        }
    }
}
