namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
///     Options for querying construction kit type query column paths.
/// </summary>
public record CkTypeQueryColumnOptions
{
    /// <summary>
    ///     When true, navigation properties are ignored.
    /// </summary>
    public bool IgnoreNavigationProperties { get; init; }

    /// <summary>
    ///     When set, limits the depth of navigation property traversal.
    /// </summary>
    public int? MaxDepth { get; init; }

    /// <summary>
    ///     Returns the default options.
    /// </summary>
    public static CkTypeQueryColumnOptions Default => new();
}
