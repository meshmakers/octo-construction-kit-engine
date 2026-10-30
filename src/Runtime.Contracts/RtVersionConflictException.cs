using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
///     Thrown when a write that carries an expected <see cref="RepositoryEntities.RtEntity.RtVersion" />
///     (optimistic concurrency, AB#7769) finds the stored entity at a different version: another writer
///     changed the entity after the caller read it. Nothing was written for the conflicting entity. The caller
///     re-reads the entity, re-applies its change and writes again (typically with one retry).
/// </summary>
/// <remarks>
///     Distinct from "entity not found" (a missing entity keeps its existing not-found error) and from the silent
///     skip of <see cref="AttributeNewerThanGuard" />. In a transactional session the whole transaction is rolled
///     back when the exception leaves the session scope.
/// </remarks>
[Serializable]
public class RtVersionConflictException : PersistenceException
{
    /// <summary>
    ///     Creates a new instance of <see cref="RtVersionConflictException" />.
    /// </summary>
    /// <param name="rtEntityId">The entity the write was refused for</param>
    /// <param name="expectedRtVersion">The version the caller expected</param>
    public RtVersionConflictException(RtEntityId rtEntityId, ulong expectedRtVersion)
        : base($"Entity '{rtEntityId}' was changed by another writer: expected rtVersion {expectedRtVersion} " +
               "does not match the stored version. Nothing was written; read the entity again and retry.")
    {
        RtEntityId = rtEntityId;
        ExpectedRtVersion = expectedRtVersion;
    }

    /// <summary>
    ///     The entity the write was refused for.
    /// </summary>
    public RtEntityId RtEntityId { get; }

    /// <summary>
    ///     The version the caller expected the stored entity to have.
    /// </summary>
    public ulong ExpectedRtVersion { get; }
}
