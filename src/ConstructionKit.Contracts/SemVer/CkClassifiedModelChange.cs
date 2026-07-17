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
}
