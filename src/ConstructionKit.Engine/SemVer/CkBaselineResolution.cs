using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     The baseline decision of <see cref="ICkBaselineResolver" /> (AB#5450).
/// </summary>
public sealed record CkBaselineResolution
{
    /// <summary>Name of the model.</summary>
    public required string ModelName { get; init; }

    /// <summary>The declared version the baseline was resolved for.</summary>
    public required CkVersion DeclaredVersion { get; init; }

    /// <summary>The baseline, or null for a first publication or an unreachable catalog source.</summary>
    public CkModelId? Baseline { get; init; }

    /// <summary>The catalog that holds <see cref="Baseline" />.</summary>
    public string? CatalogName { get; init; }

    /// <summary>True when <see cref="Baseline" /> comes from the local file-system catalog (not published).</summary>
    public bool IsLocal { get; init; }

    /// <summary>
    ///     True when the declared major has no version yet and <see cref="Baseline" /> is the newest version of the
    ///     highest lower major (a new major is still diffed against the previous line).
    /// </summary>
    public bool IsFromLowerMajor { get; init; }

    /// <summary>Cache timestamp of the catalog that answered <see cref="Baseline" />, if cache-backed.</summary>
    public DateTime? CacheUpdatedAt { get; init; }

    /// <summary>True when at least one queried catalog could not reach its source during the last cache refresh.</summary>
    public bool SourceUnreachable { get; init; }

    /// <summary>
    ///     Local file-system catalog entries of the declared major at or above the declared version, ascending. They come
    ///     from earlier local builds or sibling registrations, never from a publish, and are never the baseline.
    /// </summary>
    public IReadOnlyList<CkModelId> IgnoredLocalEntries { get; init; } = [];

    /// <summary>True when no catalog knows any usable version and every source was reachable.</summary>
    public bool IsFirstPublication => Baseline == null && !SourceUnreachable;
}
