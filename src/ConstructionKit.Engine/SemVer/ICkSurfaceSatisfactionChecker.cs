using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     The surface satisfaction checker (AB#5437, concept §4.3.3 gate 4): decides whether one dependent keeps working
///     with a candidate version of a base model. Pure and read-only; reused by the cascade dry run
///     (<c>ValidateCascade</c>), the publish gate (F2.3) and the tenant upgrade guard (F2.5).
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item><b>Range-retaining dependent</b> (<c>DependencyRanges</c>, <c>usedSurface</c>): the range must admit the
///             candidate and its floor must be met; every element the dependent references (and every used member) must
///             exist in the candidate, be public and not be changed incompatibly (a Major change of the classifier) =
///             <see cref="CkDependentVerdict.Compatible" />.</item>
///         <item><b>Exact-pinned dependent</b>: the same reference check against the candidate; without findings it is
///             <see cref="CkDependentVerdict.NeedsRepin" /> with the level of today's dependency rule.</item>
///         <item><b>Name collisions</b> (rows H6 / N2): an attribute or method the candidate newly declares on a type
///             collides with a member declared by a derived type of the dependent.</item>
///         <item><b>Transitive dependent</b> without a dependency entry on the candidate: <see cref="CkDependentVerdict.Compatible" />
///             (it is affected only through the dependents it depends on, which are checked on their own).</item>
///         <item>Not provable here: behavioural changes (<c>IsBehavioural</c>) and runtime data; the dry run only
///             proves that the surface still binds.</item>
///     </list>
/// </remarks>
public interface ICkSurfaceSatisfactionChecker
{
    /// <summary>
    ///     Checks <paramref name="dependent" /> against <paramref name="candidate" />.
    /// </summary>
    CkDependentCheck Check(CkSurfaceCandidate candidate, CkCompiledModelRoot dependent);
}
