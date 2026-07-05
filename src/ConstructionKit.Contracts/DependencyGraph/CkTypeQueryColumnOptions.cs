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
    ///     When true, attribute value navigation columns (<c>nav.type-&gt;attribute</c>) are also
    ///     produced for inbound associations and for navigations with multiplicity N. Such columns
    ///     resolve per row to the first matching target entity (deterministic order, optionally
    ///     narrowed by an entity selector in the path). Off by default because the inbound/N
    ///     fan-out multiplies the column count on densely connected models — pickers should
    ///     request these columns explicitly.
    /// </summary>
    public bool IncludeManyNavigations { get; init; }

    /// <summary>
    ///     Returns the default options.
    /// </summary>
    public static CkTypeQueryColumnOptions Default => new();
}
