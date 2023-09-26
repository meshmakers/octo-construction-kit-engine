using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

// ReSharper disable CollectionNeverQueried.Global

namespace Meshmakers.Octo.Runtime.Contracts.RuleEngine;

/// <summary>
/// Represents a graph rule engine result.
/// </summary>
public record GraphRuleEngineResult
{
    /// <summary>
    /// Creates a new instance of <see cref="GraphRuleEngineResult"/>.
    /// </summary>
    public GraphRuleEngineResult()
    {
        RtAssociationsToCreate = new List<RtAssociation>();
        RtAssociationsToDelete = new List<RtAssociation>();
    }

    /// <summary>
    /// Returns a list of associations to create.
    /// </summary>
    public List<RtAssociation> RtAssociationsToCreate { get; }
    
    /// <summary>
    /// Returns a list of associations to delete.
    /// </summary>
    public List<RtAssociation> RtAssociationsToDelete { get; }
}
