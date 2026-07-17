namespace Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

/// <summary>
///     Verdict of validating the declared model version against the published baseline version
///     and the minimum level required by the classified diff.
/// </summary>
public enum CkSemVerVerdict
{
    /// <summary>
    ///     The declared version satisfies the required minimum version.
    /// </summary>
    Valid,

    /// <summary>
    ///     The diff is empty but the version was bumped anyway (e.g. a purely semantic change) —
    ///     valid, reported as a note.
    /// </summary>
    ValidBumpWithoutStructuralChange,

    /// <summary>
    ///     The declared version is below the minimum version required by the diff
    ///     (in particular: the version was left untouched).
    /// </summary>
    VersionTooLow,

    /// <summary>
    ///     The declared version is lower than the published version.
    /// </summary>
    Downgrade
}

/// <summary>
///     Result of the semantic version validation rule (declared version vs. published baseline
///     version and required bump level).
/// </summary>
public sealed record CkSemVerValidationResult
{
    /// <summary>
    ///     The validation verdict.
    /// </summary>
    public required CkSemVerVerdict Verdict { get; init; }

    /// <summary>
    ///     The last published version of the model (the baseline).
    /// </summary>
    public required CkVersion PublishedVersion { get; init; }

    /// <summary>
    ///     The version declared in <c>ckModel.yaml</c>.
    /// </summary>
    public required CkVersion DeclaredVersion { get; init; }

    /// <summary>
    ///     The minimum bump level required by the classified diff.
    /// </summary>
    public required CkSemVerLevel RequiredLevel { get; init; }

    /// <summary>
    ///     The lowest version that satisfies <see cref="RequiredLevel" /> relative to
    ///     <see cref="PublishedVersion" /> (equals the published version when no bump is required).
    /// </summary>
    public required CkVersion MinimumVersion { get; init; }

    /// <summary>
    ///     True when the declared version is valid.
    /// </summary>
    public bool IsValid => Verdict is CkSemVerVerdict.Valid or CkSemVerVerdict.ValidBumpWithoutStructuralChange;
}
