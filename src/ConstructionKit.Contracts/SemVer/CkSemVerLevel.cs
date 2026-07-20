namespace Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

/// <summary>
///     Semantic version bump level of a construction kit model change.
///     Higher numeric values represent more severe changes; the highest level over a model diff
///     determines the minimum required version bump relative to the last published version.
/// </summary>
public enum CkSemVerLevel
{
    /// <summary>
    ///     No structural change — no version bump is required.
    /// </summary>
    None = 0,

    /// <summary>
    ///     Purely documentational change (e.g. descriptions) — requires at least a revision bump.
    /// </summary>
    Patch = 1,

    /// <summary>
    ///     Additive or relaxing change — requires at least a minor bump.
    /// </summary>
    Minor = 2,

    /// <summary>
    ///     Breaking change — requires a major bump.
    /// </summary>
    Major = 3
}
