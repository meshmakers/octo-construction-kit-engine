namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// The State of an RtEntity
/// </summary>
public enum RtEntityState
{
    /// <summary>
    /// Default state of the entity
    /// </summary>
    Active = 0,
    /// <summary>
    /// The entity is deleted
    /// </summary>
    Deleted = 1,
}