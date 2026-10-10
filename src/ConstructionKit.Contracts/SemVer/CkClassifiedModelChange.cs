namespace Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

/// <summary>
///     A structural model change together with the semantic version level assigned by the
///     classifier rule set and a human readable reasoning.
/// </summary>
public sealed record CkClassifiedModelChange
{
    /// <summary>
    ///     The underlying structural change.
    /// </summary>
    public required CkModelChange Change { get; init; }

    /// <summary>
    ///     The semantic version level required by this change.
    /// </summary>
    public required CkSemVerLevel Level { get; init; }

    /// <summary>
    ///     Human readable reasoning of the classification (mirrors the documented rule set).
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    ///     AB#6270: the change keeps the schema compatible but changes runtime behaviour (default values, display rules,
    ///     auto-complete / auto-increment, change streams, non-unique indexes, method timeouts). Reported under
    ///     "Behavioural changes" in the verdict and the changelog; the level is unaffected.
    /// </summary>
    public bool IsBehavioural { get; init; }

    /// <summary>
    ///     AB#6269 / AB#6270: the change must be acknowledged by the publisher (a security-sensitive access tightening,
    ///     a unique index on a stable base). F2.1 only marks it; the acknowledge mechanism is F2.2 / F2.3.
    /// </summary>
    public bool RequiresAcknowledge { get; init; }
}
