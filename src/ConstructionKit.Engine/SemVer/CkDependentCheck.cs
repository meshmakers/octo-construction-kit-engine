using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     What a candidate version of a base model does to one dependent (AB#5437).
/// </summary>
public enum CkDependentVerdict
{
    /// <summary>The dependent keeps working with the candidate unchanged (range-retaining dependent: its range admits it).</summary>
    Compatible,

    /// <summary>
    ///     An exact-pinned (ckLanguage 1) dependent still resolves everything it references, but must be recompiled and
    ///     republished against the candidate; <see cref="CkDependentCheck.RequiredLevel" /> is the bump the dependency
    ///     rule demands.
    /// </summary>
    NeedsRepin,

    /// <summary>The dependent would break: a referenced element is gone, internal or changed incompatibly, the floor is not met, or a name collides.</summary>
    Breaks,

    /// <summary>The dependent's range or pin does not cover the candidate's major (or the candidate is not newer than the pin): not affected.</summary>
    NotInRange
}

/// <summary>
///     The verdict of the surface satisfaction check for one dependent (AB#5437).
/// </summary>
public sealed record CkDependentCheck
{
    /// <summary>The checked dependent (newest version of its major line).</summary>
    public required CkModelId Dependent { get; init; }

    /// <summary>True for a range-retaining dependent (ranges and used surface), false for exact pins.</summary>
    public required bool IsRangeRetaining { get; init; }

    /// <summary>The verdict.</summary>
    public required CkDependentVerdict Verdict { get; init; }

    /// <summary>The bump level the dependent needs for a re-pin; only set for <see cref="CkDependentVerdict.NeedsRepin" />.</summary>
    public CkSemVerLevel? RequiredLevel { get; init; }

    /// <summary>Why: one entry per finding (member named), or the reason for Compatible / NotInRange.</summary>
    public required IReadOnlyList<string> Reasons { get; init; }
}
