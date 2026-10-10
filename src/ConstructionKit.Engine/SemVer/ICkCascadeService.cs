using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     The result of a cascade dry run (AB#5437).
/// </summary>
public sealed record CkCascadeResult
{
    /// <summary>The compiled candidate.</summary>
    public required CkCompiledModelRoot Candidate { get; init; }

    /// <summary>The baseline resolution of the candidate (what it is compared with).</summary>
    public required CkBaselineResolution Baseline { get; init; }

    /// <summary>The candidate's own verdict (declared version vs. required level); null for a first publication.</summary>
    public CkCompatibilityVerdict? CandidateVerdict { get; init; }

    /// <summary>One entry per dependent, Breaks first.</summary>
    public required IReadOnlyList<CkDependentCheck> Dependents { get; init; }

    /// <summary>Models that could not be loaded from a catalog (not checked), with the reason.</summary>
    public IReadOnlyList<string> LoadWarnings { get; init; } = [];

    /// <summary>True when at least one dependent would break.</summary>
    public bool HasBreaks => Dependents.Any(d => d.Verdict == CkDependentVerdict.Breaks);
}

/// <summary>
///     Cascade dry run (AB#5437): checks a candidate version of a base model against every model in the catalogs that
///     depends on it, transitive dependents included. Read-only: it never publishes, registers or refreshes anything.
/// </summary>
public interface ICkCascadeService
{
    /// <summary>
    ///     Checks <paramref name="candidate" /> against its dependents. The dependents are the newest version per major
    ///     line of every model whose dependencies name the candidate (directly or through another dependent).
    /// </summary>
    /// <param name="candidate">The compiled candidate (its model id carries the version to be published).</param>
    /// <param name="catalogName">Restricts baseline and dependent discovery to this catalog; null uses every readable catalog.</param>
    Task<CkCascadeResult> AnalyzeAsync(CkCompiledModelRoot candidate, string? catalogName = null);
}
