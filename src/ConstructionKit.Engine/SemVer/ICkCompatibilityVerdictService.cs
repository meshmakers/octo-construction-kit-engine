using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Computes the compatibility verdict (baseline model, diff, classification, validation of the declared version)
///     of a compiled model for an already resolved baseline (AB#6294). One shared engine service for every
///     compatibility gate, so that <c>ValidateVersion</c>, the compile gate and the publish gate (F2.3) always agree.
/// </summary>
/// <remarks>
///     The service only reads: it loads the baseline model from the catalog that holds it and never publishes,
///     refreshes or writes anything. Resolving the baseline stays with <see cref="ICkBaselineResolver" />, because
///     the callers decide what to do with the cases without a baseline (first publication, unreachable source).
/// </remarks>
public interface ICkCompatibilityVerdictService
{
    /// <summary>
    ///     Compares <paramref name="current" /> with the baseline of <paramref name="resolution" />.
    /// </summary>
    /// <param name="resolution">A resolution with a <see cref="CkBaselineResolution.Baseline" />.</param>
    /// <param name="current">The compiled model under test; its model id carries the declared version.</param>
    /// <param name="catalogName">Load the baseline from this catalog instead of the one the resolver found it in.</param>
    /// <returns>The verdict.</returns>
    /// <exception cref="Meshmakers.Octo.ConstructionKit.Contracts.CompilerException">The baseline model cannot be loaded.</exception>
    Task<CkCompatibilityVerdict> EvaluateAsync(CkBaselineResolution resolution, CkCompiledModelRoot current,
        string? catalogName = null);
}
