using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Result for one model of <see cref="ICkCatalogValidationService" />.
/// </summary>
public sealed record CkCatalogModelCheck
{
    /// <summary>The checked model: the newest version of its name in the catalog.</summary>
    public required CkModelId Model { get; init; }

    /// <summary>True for a range-retaining model (ranges and used surface), false for exact pins.</summary>
    public required bool IsRangeRetaining { get; init; }

    /// <summary>True when everything the model depends on resolves at the latest versions of the catalog.</summary>
    public required bool IsResolved { get; init; }

    /// <summary>Why the model does not resolve: one finding per problem, naming dependency and member.</summary>
    public IReadOnlyList<string> Findings { get; init; } = [];

    /// <summary>
    ///     Information that is not a defect: a newer major of a dependency that lies outside the range
    ///     (<c>not in range</c>), the baseline the surface was judged against, a model without dependencies.
    /// </summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>The dependents' verdicts of the surface checker, per dependency (range-retaining models).</summary>
    public IReadOnlyList<CkDependentCheck> SurfaceChecks { get; init; } = [];
}

/// <summary>
///     One model name that exists in several spellings which differ only in case.
/// </summary>
/// <param name="Variants">Every spelling with the newest version and the catalog path of its model directory.</param>
public sealed record CkCaseVariantGroup(IReadOnlyList<CkCaseVariant> Variants);

/// <summary>
///     One spelling of a model name.
/// </summary>
/// <param name="Spelling">The model name as published.</param>
/// <param name="NewestVersion">Its newest version.</param>
/// <param name="CatalogPath">The model directory below the catalog root, e.g. <c>ck-models/v3/s/System.Bot/</c>.</param>
public sealed record CkCaseVariant(string Spelling, CkVersion NewestVersion, string CatalogPath);

/// <summary>
///     The result of a catalog validation (AB#4469).
/// </summary>
public sealed record CkCatalogValidationResult
{
    /// <summary>The validated catalog.</summary>
    public required string CatalogName { get; init; }

    /// <summary>
    ///     True when defects are errors (Meshmakers-run catalogs: the private and the public GitHub catalog), false when
    ///     they are only warnings (any other configured catalog).
    /// </summary>
    public required bool DefectsAreErrors { get; init; }

    /// <summary>The checked models, defects first.</summary>
    public required IReadOnlyList<CkCatalogModelCheck> Models { get; init; }

    /// <summary>Model names that differ only in case.</summary>
    public IReadOnlyList<CkCaseVariantGroup> CaseVariants { get; init; } = [];

    /// <summary>Number of models that do not resolve.</summary>
    public int BrokenCount => Models.Count(m => !m.IsResolved);

    /// <summary>True when there is a defect: a model that does not resolve or a case-only name variant.</summary>
    public bool HasDefects => BrokenCount > 0 || CaseVariants.Count > 0;

    /// <summary>True when the run fails: a defect in a catalog where defects are errors.</summary>
    public bool HasErrors => DefectsAreErrors && HasDefects;
}

/// <summary>
///     Whole-catalog consistency check (AB#4469, <c>octo-ckc -c ValidateCatalog</c>): per-model validation proves each
///     model is individually honest; this proves that the catalog as a whole still resolves when every model is loaded at
///     its latest version. Read-only; reads both catalog roots (<c>ck-models/v2/</c> and <c>v3/</c>).
/// </summary>
/// <remarks>
///     "Resolves at latest" for the newest version of a model M: <b>exact pins</b> (ckLanguage 1) = every pinned dependency
///     version exists in the catalog; <b>range-retaining</b> = the range admits at least one installed version of each
///     dependency, and against the newest admitted version every referenced element and <c>usedSurface</c> entry exists,
///     is public and is not changed incompatibly (the surface checker of the cascade dry run, so both agree). A newer
///     major outside the range is reported as <c>not in range</c>, not as broken. Models whose names differ only in
///     case are reported as defects.
/// </remarks>
public interface ICkCatalogValidationService
{
    /// <summary>
    ///     Validates the catalog.
    /// </summary>
    /// <param name="catalogName">The catalog to validate.</param>
    /// <param name="models">
    ///     Restricts the check to these models and their dependents (transitive); null or empty checks the whole catalog.
    /// </param>
    /// <exception cref="ModelValidationException">A name in <paramref name="models" /> is not in the catalog.</exception>
    Task<CkCatalogValidationResult> ValidateAsync(string catalogName, IReadOnlyCollection<string>? models = null);
}
