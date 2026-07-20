using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Computes the structural difference between two compiled construction kit models.
///     The diff is a pure function over (baseline model, current model) — deterministic and
///     free of IO — and is the input for the semantic version classifier.
/// </summary>
public interface ICkModelDiffService
{
    /// <summary>
    ///     Diffs two compiled construction kit models structurally across the five element lists
    ///     (types, attributes, enums, records, association roles — keyed by their ids) as well as
    ///     the dependencies and the model meta data.
    /// </summary>
    /// <param name="baseline">The last published version of the model</param>
    /// <param name="current">The currently compiled model</param>
    /// <returns>The list of typed changes; empty when the models are structurally identical</returns>
    IReadOnlyList<CkModelChange> Diff(CkCompiledModelRoot baseline, CkCompiledModelRoot current);
}
