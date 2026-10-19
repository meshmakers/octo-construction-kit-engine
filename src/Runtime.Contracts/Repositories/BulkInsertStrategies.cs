namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Defines options for bulk insert operations.
/// </summary>
public enum BulkInsertStrategies
{
    /// <summary>
    /// Insert new entities only, do not replace existing ones. In the MongoDB implementation an entity that exists
    /// only as an ARCHIVED document with the same identifier is not an existing one: it is replaced and active again
    /// (AB#7018). Other implementations (for example the local directory repository) still treat every existing
    /// identifier as taken.
    /// </summary>
    InsertOnly,

    /// <summary>
    /// Insert new entities or replace existing ones based on their identifiers.
    /// </summary>
    Upsert,
}