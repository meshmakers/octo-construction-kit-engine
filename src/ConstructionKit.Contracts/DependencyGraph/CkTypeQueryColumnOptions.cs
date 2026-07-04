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
    ///     Hard cap on the total number of collected columns. Navigation traversal over densely
    ///     connected, cyclic association graphs (e.g. a 0..1 self-association on a root type every
    ///     other type derives from) can expand combinatorially — without a cap a single collection
    ///     run allocates unbounded memory. When the cap is exceeded the collector throws a
    ///     <see cref="DependencyGraphException"/> instead of exhausting the process.
    ///     <c>null</c> disables the cap.
    /// </summary>
    public int? MaxColumns { get; init; } = DefaultMaxColumns;

    /// <summary>
    ///     Default value for <see cref="MaxColumns"/>.
    /// </summary>
    public const int DefaultMaxColumns = 50_000;

    /// <summary>
    ///     Returns the default options.
    /// </summary>
    public static CkTypeQueryColumnOptions Default => new();
}
