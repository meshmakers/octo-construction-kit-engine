namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Identifies which property of the navigation target entity an entity selector pins.
/// </summary>
public enum NavigationEntitySelectorKind
{
    /// <summary>
    /// Selects by the target entity's RtId. Note that RtIds are environment-specific;
    /// wellKnownName or attribute selectors are preferred for portable queries.
    /// </summary>
    RtId = 0,

    /// <summary>
    /// Selects by the target entity's RtWellKnownName.
    /// </summary>
    WellKnownName = 1,

    /// <summary>
    /// Selects by an attribute value of the target entity
    /// (see <see cref="NavigationEntitySelector.AttributeName"/>).
    /// </summary>
    Attribute = 2,
}

/// <summary>
/// An entity selector attached to a navigation pair, pinning the exact target entity of a
/// navigation across an N-multiplicity association — path syntax
/// <c>nav.type[rtId=...]-&gt;attr</c>, <c>nav.type[wellKnownName=...]-&gt;attr</c> or
/// <c>nav.type[attributeName=value]-&gt;attr</c>.
/// </summary>
/// <param name="Kind">Which target property the selector matches.</param>
/// <param name="AttributeName">The attribute name (PascalCase) when <paramref name="Kind"/> is
/// <see cref="NavigationEntitySelectorKind.Attribute"/>; otherwise null.</param>
/// <param name="Value">The comparison value as written in the path (quotes stripped).</param>
public record NavigationEntitySelector(
    NavigationEntitySelectorKind Kind,
    string? AttributeName,
    string Value);
