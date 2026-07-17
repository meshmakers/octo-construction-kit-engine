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
