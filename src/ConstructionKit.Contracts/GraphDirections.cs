namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Defines graph directions in graph queries
/// </summary>
public enum GraphDirections
{
    /// <summary>
    ///     All directions
    /// </summary>
    Any = Inbound | Outbound,

    /// <summary>
    ///     All inbound directions (e. g. parent to child)
    /// </summary>
    Inbound = 1,

    /// <summary>
    ///     All outbound directions (e. g. child to parent)
    /// </summary>
    Outbound = 2
}