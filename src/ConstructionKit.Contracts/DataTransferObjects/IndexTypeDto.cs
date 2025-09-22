namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     Defines index types
/// </summary>
public enum IndexTypeDto
{
    /// <summary>
    ///     No index.
    /// </summary>
    None = 0,

    /// <summary>
    ///     Ascending index.
    /// </summary>
    Ascending = 1,

    /// <summary>
    ///     Full text search index.
    /// </summary>
    Text = 2,

    /// <summary>
    ///     Unique index. Ensures the indexed attribute value is unique across all entities of this type.
    /// </summary>
    Unique = 3,

    /// <summary>
    ///     Unique index for non-deleted entities. Ensures uniqueness only when RtState is not set (default) or when RtState != 1 (not deleted).
    /// </summary>
    UniqueNotDeleted = 4
}