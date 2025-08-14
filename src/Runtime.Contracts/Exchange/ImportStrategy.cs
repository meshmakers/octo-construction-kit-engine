namespace Meshmakers.Octo.Runtime.Contracts.Exchange;

/// <summary>
/// Defines the import strategy
/// </summary>
public enum ImportStrategy
{
    /// <summary>
    ///  Inserts only data to repository, if an entity or association with the same ID already exists, an error is thrown.
    /// </summary>
    Insert = 0,

    /// <summary>
    /// Inserts or updates a repository
    /// </summary>
    Upsert = 1,
}