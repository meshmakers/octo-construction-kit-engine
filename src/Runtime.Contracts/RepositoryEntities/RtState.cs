namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// The State of a RtEntity or RtAssociation
/// </summary>
public enum RtState
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