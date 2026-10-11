using Meshmakers.Octo.ConstructionKit.Contracts;
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
///     The already published model that a candidate equals (AB#6388).
/// </summary>
/// <param name="Published">The published model id (the candidate's own version).</param>
/// <param name="CatalogName">The catalog that holds it.</param>
/// <param name="DifferenceClasses">Classes of difference normalised away; empty when the content is identical.</param>
public sealed record CkPublishedEquivalent(CkModelId Published, string CatalogName,
    IReadOnlyList<string> DifferenceClasses);

/// <summary>
///     Cascade dry run (AB#5437): checks a candidate version of a base model against every model in the catalogs that
///     depends on it, transitive dependents included. Read-only: it never publishes, registers or refreshes anything.
/// </summary>
public interface ICkCascadeService
{
    /// <summary>
    ///     Tells whether <paramref name="candidate" /> is already published to <paramref name="catalogName" /> with the
    ///     same content (AB#6388): same version, identical or equivalent after canonicalisation
    ///     (<see cref="CkPublishEquivalence" />, the rule of the publish gate's no-op). Such a candidate cannot newly
    ///     break a dependent, so the cascade check of a build skips it. Read-only; a missing, unreadable or different
    ///     published version is <c>null</c> (the candidate is then checked as usual).
    /// </summary>
    /// <param name="candidate">The compiled candidate.</param>
    /// <param name="catalogName">The target catalog of the publish.</param>
    /// <returns>The published model with the difference classes that were normalised away, or <c>null</c>.</returns>
    Task<CkPublishedEquivalent?> FindEquivalentPublishedAsync(CkCompiledModelRoot candidate, string catalogName);

    /// <summary>
    ///     Checks <paramref name="candidate" /> against its dependents. The dependents are the newest version per major
    ///     line of every model whose dependencies name the candidate (directly or through another dependent).
    /// </summary>
    /// <param name="candidate">The compiled candidate (its model id carries the version to be published).</param>
    /// <param name="catalogName">Restricts baseline and dependent discovery to this catalog; null uses every readable catalog.</param>
    Task<CkCascadeResult> AnalyzeAsync(CkCompiledModelRoot candidate, string? catalogName = null);
}
