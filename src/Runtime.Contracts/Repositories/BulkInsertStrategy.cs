namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Defines options for bulk insert operations.
/// </summary>
public enum BulkInsertStrategy
{
    /// <summary>
    /// Insert new entities only, do not replace existing ones.
    /// </summary>
    InsertOnly,

    /// <summary>
    /// Insert new entities or replace existing ones based on their identifiers.
    /// </summary>
    Upsert,
}