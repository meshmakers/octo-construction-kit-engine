using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;

/// <summary>
/// Defines the GitHub Options for public version of GitHub construction kit catalog
/// </summary>
public class PublicGitHubCatalogOptions : GitHubCatalogOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PublicGitHubCatalogOptions"/> class.
    /// </summary>
    public PublicGitHubCatalogOptions() : base("public-github-catalog-cache.json")
    {
        GitHubRepositoryOwner = "meshmakers";
        GitHubRepositoryName = "meshmakers.github.io";
        GitHubRepositoryBranch = "main";
        GitHubPagesUri = "https://meshmakers.github.io/";
    }
}

/// <summary>
/// Defines the GitHub Options for private version of GitHub construction kit catalog
/// </summary>
public class PrivateGitHubCatalogOptions : GitHubCatalogOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PublicGitHubCatalogOptions"/> class.
    /// </summary>
    public PrivateGitHubCatalogOptions() : base("private-github-catalog-cache.json")
    {
        GitHubRepositoryOwner = "meshmakers";
        GitHubRepositoryName = "construction-kit-libraries-build";
        GitHubRepositoryBranch = "main";
        GitHubPagesUri = "https://meshmakers.github.io/construction-kit-libraries-build/";
    }
}

/// <summary>
/// Defines the GitHub Options for GitHub construction kit catalog
/// </summary>
public abstract class GitHubCatalogOptions(string cacheFileName) : CatalogOptions(cacheFileName), IGitHubOptions
{
    /// <summary>
    /// API Token for GitHub
    /// </summary>
    public string? GitHubApiToken { get; set; }

    /// <summary>
    /// Name of the GitHub repository
    /// </summary>
    public string GitHubRepositoryName { get; set; } = null!;

    /// <summary>
    /// Owner of the GitHub repository
    /// </summary>
    public string GitHubRepositoryOwner { get; set; } = null!;

    /// <summary>
    /// GitHub repository branch
    /// </summary>
    public string GitHubRepositoryBranch { get; set; } = null!;

    /// <summary>
    /// The uri to the GitHub pages site where the construction kit models are stored.
    /// If null or empty, the repository will use GitHub API for reading models.
    /// </summary>
    public string GitHubPagesUri { get; set; } = null!;

    /// <summary>
    /// Represents the product name to be used in the User-Agent header when making requests to GitHub API.
    /// </summary>
    public string ProductName { get; set; } = "Meshmakers.Octo.ConstructionKit.Engine";

    /// <summary>
    /// When true, this GitHub catalog is enabled for reading and writing models.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// How often a catalog refresh retries reading an index file that is listed in the root
    /// catalog but answers 404. GitHub Pages briefly serves 404 while a deployment is being
    /// replaced (AB#4872), so a listed-but-missing index is usually transient. The count is a
    /// budget shared across one refresh, so a full site swap cannot stall a refresh for minutes.
    /// </summary>
    public int RefreshNotFoundRetryCount { get; set; } = 3;

    /// <summary>
    /// Delay in seconds between the retries of <see cref="RefreshNotFoundRetryCount"/>.
    /// </summary>
    public int RefreshNotFoundRetryDelaySeconds { get; set; } = 10;

    /// <summary>
    /// AB#6719: whether a process that holds an API token may use the GitHub Contents API for READS (the source-of-truth
    /// read of dependency resolution, <c>ValidateVersion</c>, the cascade check and the API fallback of a model read).
    /// <see cref="CatalogApiReadMode.Auto" /> (default): on a CI build only. A token that merely happens to be configured on a
    /// developer machine does not switch local builds to API reads; they use the Pages mirror and the cached catalogs and
    /// need no token. A publish needs the token and always reads authoritatively, whatever this says.
    /// </summary>
    public CatalogApiReadMode ApiReads { get; set; } =
        ParseApiReads(Environment.GetEnvironmentVariable("OctoCkCatalogApiReads")) ?? CatalogApiReadMode.Auto;

    /// <summary>Parses <c>Auto</c> / <c>Enabled</c> / <c>Disabled</c> (case-insensitive); null for an empty or unknown value.</summary>
    public static CatalogApiReadMode? ParseApiReads(string? value) =>
        value != null && value.Trim().Length > 0 && Enum.TryParse<CatalogApiReadMode>(value.Trim(), true, out var mode)
            ? mode
            : null;

    /// <summary>The effective value of <see cref="ApiReads" />: true when reads may use the API (a token is still required).</summary>
    public bool ApiReadsEnabled => ApiReads switch
    {
        CatalogApiReadMode.Enabled => true,
        CatalogApiReadMode.Disabled => false,
        _ => IsContinuousIntegrationEnvironment()
    };

    /// <summary>True on an Azure Pipelines build (<c>TF_BUILD</c>), a build that sets <c>ContinuousIntegrationBuild</c> or <c>CI</c>.</summary>
    public static bool IsContinuousIntegrationEnvironment() =>
        IsTrue(Environment.GetEnvironmentVariable("TF_BUILD")) ||
        IsTrue(Environment.GetEnvironmentVariable("ContinuousIntegrationBuild")) ||
        IsTrue(Environment.GetEnvironmentVariable("CI"));

    private static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>When a GitHub catalog may read through the Contents API instead of the Pages mirror (AB#6719).</summary>
public enum CatalogApiReadMode
{
    /// <summary>On a CI build (TF_BUILD / ContinuousIntegrationBuild / CI), not on a developer machine.</summary>
    Auto,

    /// <summary>Always, when a token is present.</summary>
    Enabled,

    /// <summary>Never; reads use the mirror and the cache only.</summary>
    Disabled
}
