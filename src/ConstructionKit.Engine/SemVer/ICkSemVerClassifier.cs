using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Assigns a semantic version level to every structural model change according to the fixed,
///     built-in rule set (documented in <c>docs/ck-semver-rules.md</c>) and applies the version
///     validation rule. All members are pure functions — deterministic and free of IO.
/// </summary>
public interface ICkSemVerClassifier
{
    /// <summary>
    ///     Classifies every change of a model diff. Changes not covered by an explicit rule are
    ///     defensively classified as <see cref="CkSemVerLevel.Major" /> — since only a minimum
    ///     level is enforced, an overly strict classification is annoying but never wrong,
    ///     an overly lax one is dangerous.
    /// </summary>
    /// <param name="changes">The changes produced by <see cref="ICkModelDiffService.Diff" /></param>
    /// <param name="baseline">The baseline model the diff was computed against</param>
    /// <param name="current">The current model the diff was computed against</param>
    /// <returns>The classified changes, in the order of the input changes</returns>
    IReadOnlyList<CkClassifiedModelChange> Classify(IReadOnlyList<CkModelChange> changes,
        CkCompiledModelRoot baseline, CkCompiledModelRoot current);

    /// <summary>
    ///     AB#6585: classifies like <see cref="Classify(IReadOnlyList{CkModelChange}, CkCompiledModelRoot, CkCompiledModelRoot)" />
    ///     and additionally judges the security exception against the published history of the major line:
    ///     tightening <c>access</c> of an attribute is a Minor + acknowledge only when the attribute is
    ///     <c>securitySensitive</c> in the baseline, in the current model and in every version of
    ///     <paramref name="earlierInMajor" /> that has it; an attribute that became sensitive within the major
    ///     line needs a Major bump.
    /// </summary>
    /// <param name="changes">The changes produced by <see cref="ICkModelDiffService.Diff" /></param>
    /// <param name="baseline">The baseline model the diff was computed against</param>
    /// <param name="current">The current model the diff was computed against</param>
    /// <param name="earlierInMajor">
    ///     The published versions of the same major below the baseline (any order); empty for the first version of a major
    ///     line or when the history is not available.
    /// </param>
    /// <returns>The classified changes, in the order of the input changes</returns>
    IReadOnlyList<CkClassifiedModelChange> Classify(IReadOnlyList<CkModelChange> changes,
        CkCompiledModelRoot baseline, CkCompiledModelRoot current, IReadOnlyList<CkCompiledModelRoot> earlierInMajor);

    /// <summary>
    ///     AB#6585: true when a change of this diff is judged against the history of the major line (an access
    ///     tightening of an attribute that is security-sensitive in the baseline and the current model, or one marked
    ///     sensitive in a ckLanguage 2 candidate whose baseline is ckLanguage 1: the first-release exception of
    ///     AB#7771 is closed by any earlier ckLanguage 2 version). Callers load the earlier versions of the major only
    ///     then.
    /// </summary>
    bool NeedsMajorLineHistory(IReadOnlyList<CkModelChange> changes, CkCompiledModelRoot baseline,
        CkCompiledModelRoot current);

    /// <summary>
    ///     AB#7771: true when the history passed to <c>Classify</c> must hold the earlier versions of <b>every</b> major of
    ///     the model, not only of the baseline's major: the first-ckLanguage-2-release exception is once per model, so
    ///     a ckLanguage 2 version of an older major closes it even after the model dropped back to ckLanguage 1.
    ///     Implies <see cref="NeedsMajorLineHistory" />; the T7 major-line rule still looks at the baseline's major only.
    /// </summary>
    bool NeedsFullModelHistory(IReadOnlyList<CkModelChange> changes, CkCompiledModelRoot baseline,
        CkCompiledModelRoot current);

    /// <summary>
    ///     Returns the highest level of the classified changes — the minimum bump level the
    ///     declared version must satisfy. Returns <see cref="CkSemVerLevel.None" /> for an
    ///     empty diff.
    /// </summary>
    /// <param name="classifiedChanges">The classified changes</param>
    /// <returns>The required minimum bump level</returns>
    CkSemVerLevel GetRequiredLevel(IEnumerable<CkClassifiedModelChange> classifiedChanges);

    /// <summary>
    ///     Applies the version validation rule: the declared version must be at least the
    ///     published version bumped by the required level; higher versions are accepted
    ///     (minimum level semantics), downgrades are always invalid.
    /// </summary>
    /// <param name="publishedVersion">The last published version (baseline)</param>
    /// <param name="declaredVersion">The version declared in <c>ckModel.yaml</c></param>
    /// <param name="requiredLevel">The required minimum bump level of the diff</param>
    /// <returns>The validation result including the computed minimum version</returns>
    CkSemVerValidationResult ValidateDeclaredVersion(CkVersion publishedVersion, CkVersion declaredVersion,
        CkSemVerLevel requiredLevel);
}
