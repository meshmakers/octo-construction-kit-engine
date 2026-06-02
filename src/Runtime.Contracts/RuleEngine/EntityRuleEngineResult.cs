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
        RtEntitiesToInsert = [];
        RtEntitiesToUpdate = new Dictionary<RtEntityId, TEntity>();
        RtEntitiesToReplace = new Dictionary<RtEntityId, TEntity>();
        RtEntitiesToDelete = [];
        UpdateGuards = new Dictionary<RtEntityId, AttributeNewerThanGuard>();
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityRuleEngineResult{TEntity}" />.
    /// </summary>
    /// <param name="rtEntitiesToInsert">List of entities to create.</param>
    /// <param name="rtEntitiesToUpdate">List of entities to update.</param>
    /// <param name="rtEntitiesToReplace">List of entities to replace.</param>
    /// <param name="rtEntitiesToDelete">List of entities to delete.</param>
    /// <param name="updateGuards">
    ///     Optional optimistic-concurrency guards keyed by entity id. Entries are present
    ///     only for entries in <paramref name="rtEntitiesToUpdate" /> that were created via
    ///     <see cref="EntityUpdateInfo{TEntity}.CreateConditionalUpdate" />.
    /// </param>
    public EntityRuleEngineResult(List<TEntity> rtEntitiesToInsert, Dictionary<RtEntityId, TEntity> rtEntitiesToUpdate,
        Dictionary<RtEntityId, TEntity> rtEntitiesToReplace, List<RtEntityId> rtEntitiesToDelete,
        Dictionary<RtEntityId, AttributeNewerThanGuard>? updateGuards = null)
    {
        RtEntitiesToInsert = rtEntitiesToInsert;
        RtEntitiesToUpdate = rtEntitiesToUpdate;
        RtEntitiesToReplace = rtEntitiesToReplace;
        RtEntitiesToDelete = rtEntitiesToDelete;
        UpdateGuards = updateGuards ?? new Dictionary<RtEntityId, AttributeNewerThanGuard>();
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

    /// <summary>
    ///     Optimistic-concurrency guards keyed by entity id. An entry indicates that the
    ///     corresponding update in <see cref="RtEntitiesToUpdate" /> must be applied
    ///     conditionally — see <see cref="AttributeNewerThanGuard" /> for semantics.
    ///     Entities without a guard are updated unconditionally.
    /// </summary>
    public Dictionary<RtEntityId, AttributeNewerThanGuard> UpdateGuards { get; }
}