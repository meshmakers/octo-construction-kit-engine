namespace Meshmakers.Octo.Runtime.Contracts.Exchange;

/// <summary>
/// Defines the import strategy
/// </summary>
public enum ImportStrategy
{
    /// <summary>
    ///  Inserts only data to repository, if an entity or association with the same ID already exists, an error is thrown.
    ///  An entity that exists only as an ARCHIVED document (the default delete archives, it does not erase) does not
    ///  count as existing: it is replaced by the imported entity, which is active again. An active entity with the
    ///  same ID still fails, and the message names its state. Values of a plain <c>Unique</c> CK index stay blocked by
    ///  archived entities (use <c>UniqueNotDeleted</c> for business keys); the message says so.
    /// </summary>
    Insert = 0,

    /// <summary>
    /// Inserts or updates a repository
    /// </summary>
    Upsert = 1,
}