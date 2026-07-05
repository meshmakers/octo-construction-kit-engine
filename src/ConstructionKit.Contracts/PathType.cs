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
    ArrayIndex = 1,

    /// <summary>
    /// Access to a navigation property
    /// </summary>
    Navigation = 2,

    /// <summary>
    /// Access to a target construction kit type id
    /// </summary>
    TargetCkTypeId = 3,

    /// <summary>
    /// Access to an association meta property (e.g., totalCount, exists).
    /// Used with the :: separator in path syntax.
    /// </summary>
    AssociationMeta = 4,

    /// <summary>
    /// An entity selector pinning the navigation target entity, e.g. [rtId=...],
    /// [wellKnownName=...] or [attributeName=value]. Follows a TargetCkTypeId term
    /// in bracket syntax.
    /// </summary>
    EntitySelector = 5,
}