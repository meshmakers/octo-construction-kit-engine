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
    Text = 2
}