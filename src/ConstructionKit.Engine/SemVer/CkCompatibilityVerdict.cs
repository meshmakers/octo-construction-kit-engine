using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     The compatibility verdict of a compiled model against its baseline (AB#6294): the single result that
///     <c>ValidateVersion</c>, the compile gate (<c>CkCompile</c>) and later the publish gate are rendered from.
/// </summary>
public sealed record CkCompatibilityVerdict
{
    /// <summary>The baseline decision the verdict was computed for.</summary>
    public required CkBaselineResolution Resolution { get; init; }

    /// <summary>The baseline model the current model was compared against.</summary>
    public required CkCompiledModelRoot BaselineModel { get; init; }

    /// <summary>All changes between baseline and current model, classified, in diff order.</summary>
    public required IReadOnlyList<CkClassifiedModelChange> ClassifiedChanges { get; init; }

    /// <summary>The minimum bump level the declared version has to satisfy.</summary>
    public required CkSemVerLevel RequiredLevel { get; init; }

    /// <summary>The validation of the declared version against the baseline and the required level.</summary>
    public required CkSemVerValidationResult Validation { get; init; }

    /// <summary>
    ///     The level of the bump the declared version actually makes over the baseline version
    ///     (<see cref="CkSemVerLevel.None" /> when it equals it, <see cref="CkSemVerLevel.Major" /> for a higher major).
    /// </summary>
    public required CkSemVerLevel CoveredLevel { get; init; }

    /// <summary>
    ///     The changes whose level is higher than <see cref="CoveredLevel" />: what the declared version does not
    ///     cover. Empty when the declared version is valid. A downgrade leaves it empty as well, because the
    ///     declared version is wrong as a whole, not because of an individual change.
    /// </summary>
    public required IReadOnlyList<CkClassifiedModelChange> UncoveredChanges { get; init; }

    /// <summary>The baseline version id.</summary>
    public CkModelId BaselineId => Resolution.Baseline!;
}
