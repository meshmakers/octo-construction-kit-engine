using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

// ReSharper disable CollectionNeverQueried.Global

namespace Meshmakers.Octo.Runtime.Contracts.RuleEngine;

/// <summary>
/// Represents a result of a rule engine execution.
/// </summary>
public record EntityRuleEngineResult<TEntity> where TEntity : RtEntity
{
    /// <summary>
    /// Creates a new instance of <see cref="EntityRuleEngineResult{TEntity}"/>.
    /// </summary>
    public EntityRuleEngineResult()
    {
        RtEntitiesToCreate = new List<TEntity>();
        RtEntitiesToUpdate = new List<TEntity>();
        RtEntitiesToDelete = new List<TEntity>();
    }

    /// <summary>
    /// Returns a list of entities to create.
    /// </summary>
    public List<TEntity> RtEntitiesToCreate { get; }
    
    /// <summary>
    /// Returns a list of entities to update.
    /// </summary>
    public List<TEntity> RtEntitiesToUpdate { get; }
    
    /// <summary>
    /// Returns a list of entities to delete.
    /// </summary>
    public List<TEntity> RtEntitiesToDelete { get; }
}
