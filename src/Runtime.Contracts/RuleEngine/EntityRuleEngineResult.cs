using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

// ReSharper disable CollectionNeverQueried.Global

namespace Meshmakers.Octo.Runtime.Contracts.RuleEngine;

/// <summary>
///     Represents a result of a rule engine execution.
/// </summary>
public record EntityRuleEngineResult<TEntity> where TEntity : RtEntity
{
    /// <summary>
    ///     Creates a new instance of <see cref="EntityRuleEngineResult{TEntity}" />.
    /// </summary>
    public EntityRuleEngineResult()
    {
        RtEntitiesToInsert = new List<TEntity>();
        RtEntitiesToUpdate = new Dictionary<RtEntityId, TEntity>();
        RtEntitiesToReplace = new Dictionary<RtEntityId, TEntity>();
        RtEntitiesToDelete = new List<RtEntityId>();
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityRuleEngineResult{TEntity}" />.
    /// </summary>
    /// <param name="rtEntitiesToInsert">List of entities to create.</param>
    /// <param name="rtEntitiesToUpdate">List of entities to update.</param>
    /// <param name="rtEntitiesToReplace">List of entities to replace.</param>
    /// <param name="rtEntitiesToDelete">List of entities to delete.</param>
    public EntityRuleEngineResult(List<TEntity> rtEntitiesToInsert, Dictionary<RtEntityId, TEntity> rtEntitiesToUpdate,
        Dictionary<RtEntityId, TEntity> rtEntitiesToReplace, List<RtEntityId> rtEntitiesToDelete)
    {
        RtEntitiesToInsert = rtEntitiesToInsert;
        RtEntitiesToUpdate = rtEntitiesToUpdate;
        RtEntitiesToReplace = rtEntitiesToReplace;
        RtEntitiesToDelete = rtEntitiesToDelete;
    }

    /// <summary>
    ///     Returns a list of entities to create.
    /// </summary>
    public List<TEntity> RtEntitiesToInsert { get; }

    /// <summary>
    ///     Returns a list of entities to update.
    /// </summary>
    public Dictionary<RtEntityId, TEntity> RtEntitiesToUpdate { get; }

    /// <summary>
    ///     Returns a list of entities to replace.
    /// </summary>
    public Dictionary<RtEntityId, TEntity> RtEntitiesToReplace { get; }

    /// <summary>
    ///     Returns a list of entities to delete.
    /// </summary>
    public List<RtEntityId> RtEntitiesToDelete { get; }
}