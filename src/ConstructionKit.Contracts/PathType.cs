namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Defines the type of path term
/// </summary>
public enum PathType
{
    /// <summary>
    /// Access to an attribute
    /// </summary>
    Attribute = 0,

    /// <summary>
    /// Access to an array index
    /// </summary>
    ArrayIndex = 1
}