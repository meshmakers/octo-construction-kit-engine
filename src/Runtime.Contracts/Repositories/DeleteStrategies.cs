namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Defines the type of delete operation
/// </summary>
public enum DeleteStrategies
{
    /// <summary>
    /// Does not delete the entity, but marks it as deleted.
    /// </summary>
    Archive = 0,

    /// <summary>
    /// Erases the entity from the repository
    /// </summary>
    Erase = 1,
}