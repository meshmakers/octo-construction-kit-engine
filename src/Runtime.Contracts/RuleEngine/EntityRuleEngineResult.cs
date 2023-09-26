using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

// ReSharper disable CollectionNeverQueried.Global

namespace Meshmakers.Octo.Runtime.Contracts.RuleEngine;

/// <summary>
/// Represents a result of a rule engine execution.
/// </summary>
public record EntityRuleEngineResult
{
    /// <summary>
    /// Creates a new instance of <see cref="EntityRuleEngineResult"/>.
    /// </summary>
    public EntityRuleEngineResult()
    {
        RtEntitiesToCreate = new List<RtEntity>();
        RtEntitiesToUpdate = new List<RtEntity>();
        RtEntitiesToDelete = new List<RtEntity>();
    }

    /// <summary>
    /// Returns a list of entities to create.
    /// </summary>
    public List<RtEntity> RtEntitiesToCreate { get; }
    
    /// <summary>
    /// Returns a list of entities to update.
    /// </summary>
    public List<RtEntity> RtEntitiesToUpdate { get; }
    
    /// <summary>
    /// Returns a list of entities to delete.
    /// </summary>
    public List<RtEntity> RtEntitiesToDelete { get; }
}
