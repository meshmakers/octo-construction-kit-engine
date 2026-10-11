namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
///     Optional capability of a writable catalog (AB#6386): whether a published version may be replaced with different
///     content. Published versions of the private and the public catalog are immutable; only a development lane catalog
///     (see <see cref="GitHubCatalog.LaneCatalogRepositoryName" />) keeps the replace behaviour. A catalog that does not
///     implement the interface is treated as immutable.
/// </summary>
public interface IReplaceablePublishCatalog
{
    /// <summary>True when a published version may be replaced with different content (<c>octo-ckc publish -r</c>).</summary>
    bool AllowsReplacingPublishedVersions { get; }
}
