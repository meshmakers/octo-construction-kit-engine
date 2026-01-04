# Configuration

## Dependency Injection

### Base Registration

Catalog services are registered via extension methods:

```csharp
public static IServiceCollection AddConstructionKitEngine(
    this IServiceCollection services)
{
    // Manager and services
    services.AddSingleton<ICatalogManager, CatalogManager>();
    services.AddTransient<ICatalogService, CatalogService>();

    // Resolvers
    services.AddTransient<ICatalogDependencyResolver, CatalogDependencyResolver>();
    services.AddTransient<ICatalogModelResolver, CatalogModelResolver>();

    // Catalog implementations
    services.AddTransient<ICatalog, EmbeddedResourceCatalog>();
    services.AddTransient<ICatalog, LocalFileSystemCatalog>();
    services.AddTransient<ICatalog, PublicGitHubCatalog>();
    services.AddTransient<ICatalog, PrivateGitHubCatalog>();

    // Factories
    services.AddTransient<IHttpClientFactory, HttpClientFactory>();
    services.AddTransient<IGitHubClientFactory, GitHubClientFactory>();

    return services;
}
```

### Service Lifetime

| Service | Lifetime | Rationale |
|---------|----------|-----------|
| ICatalogManager | Singleton | Central management, thread-safe |
| ICatalogService | Transient | Lightweight, delegates to manager |
| ICatalog | Transient | Allows configuration changes |
| Resolvers | Transient | Stateless |

---

## Options Classes

### LocalFileSystemCatalogOptions

```csharp
public class LocalFileSystemCatalogOptions : CatalogOptions
{
    /// <summary>
    /// Root directory for local models.
    /// Default: ~/.octo/local-catalog
    /// </summary>
    public string RootPath { get; set; }

    /// <summary>
    /// Enables or disables the catalog.
    /// Default: true
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
```

### GitHubCatalogOptions (Base)

```csharp
public abstract class GitHubCatalogOptions : CatalogOptions
{
    /// <summary>
    /// GitHub Personal Access Token for API access.
    /// Required for write operations and private repos.
    /// </summary>
    public string? GitHubApiToken { get; set; }

    /// <summary>
    /// GitHub Repository Owner (user or organization).
    /// </summary>
    public string GitHubRepositoryOwner { get; set; }

    /// <summary>
    /// GitHub Repository Name.
    /// </summary>
    public string GitHubRepositoryName { get; set; }

    /// <summary>
    /// Branch for commits.
    /// Default: main
    /// </summary>
    public string GitHubRepositoryBranch { get; set; } = "main";

    /// <summary>
    /// GitHub Pages URI for HTTP access.
    /// </summary>
    public Uri? GitHubPagesUri { get; set; }

    /// <summary>
    /// Product Name for User-Agent header.
    /// Default: Meshmakers.Octo.ConstructionKit.Engine
    /// </summary>
    public string ProductName { get; set; } = "Meshmakers.Octo.ConstructionKit.Engine";
}
```

### CatalogOptions (Base)

```csharp
public class CatalogOptions
{
    /// <summary>
    /// Name of the cache file.
    /// </summary>
    public string CacheFileName { get; }

    /// <summary>
    /// Directory for cache files.
    /// Default: ~/.octo/ck-catalog/cache
    /// </summary>
    public string CacheDirectory { get; set; }
}
```

---

## Configuration via appsettings.json

### Example Configuration

```json
{
  "ConstructionKit": {
    "Catalogs": {
      "Local": {
        "RootPath": "/custom/path/to/catalog",
        "IsEnabled": true
      },
      "GitHubPublic": {
        "GitHubApiToken": null,
        "GitHubRepositoryOwner": "meshmakers",
        "GitHubRepositoryName": "meshmakers.github.io",
        "GitHubRepositoryBranch": "main",
        "GitHubPagesUri": "https://meshmakers.github.io/"
      },
      "GitHubPrivate": {
        "GitHubApiToken": "ghp_xxxxxxxxxxxx",
        "GitHubRepositoryOwner": "meshmakers",
        "GitHubRepositoryName": "construction-kit-libraries-build",
        "GitHubRepositoryBranch": "main"
      }
    }
  }
}
```

### Binding in Startup

```csharp
public void ConfigureServices(IServiceCollection services)
{
    var config = Configuration.GetSection("ConstructionKit:Catalogs");

    services.Configure<LocalFileSystemCatalogOptions>(
        config.GetSection("Local"));

    services.Configure<PublicGitHubCatalogOptions>(
        config.GetSection("GitHubPublic"));

    services.Configure<PrivateGitHubCatalogOptions>(
        config.GetSection("GitHubPrivate"));

    services.AddConstructionKitEngine();
}
```

---

## Environment Variables

### GitHub Token

```bash
# Linux/macOS
export GITHUB_TOKEN="ghp_xxxxxxxxxxxx"

# Windows PowerShell
$env:GITHUB_TOKEN = "ghp_xxxxxxxxxxxx"
```

```csharp
services.Configure<PublicGitHubCatalogOptions>(options =>
{
    options.GitHubApiToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
});
```

### Custom Paths

```bash
export OCTO_CATALOG_ROOT="/custom/catalog/path"
export OCTO_CACHE_DIR="/custom/cache/path"
```

```csharp
services.Configure<LocalFileSystemCatalogOptions>(options =>
{
    options.RootPath = Environment.GetEnvironmentVariable("OCTO_CATALOG_ROOT")
        ?? Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile), ".octo", "local-catalog");
});
```

---

## Cache Configuration

### Cache Directory

Default: `~/.octo/ck-catalog/cache/`

```csharp
services.Configure<CatalogOptions>(options =>
{
    options.CacheDirectory = "/custom/cache/directory";
});
```

### Cache Files

| Catalog | Cache File |
|---------|------------|
| Local | local-catalog-cache.json |
| GitHub Public | public-github-catalog-cache.json |
| GitHub Private | private-github-catalog-cache.json |

### Cache Invalidation

```csharp
// Refresh single catalog cache
await catalogService.RefreshCatalogCacheAsync("local");

// Refresh all caches
await catalogService.RefreshAllCatalogCachesAsync();
```

---

## Logging

### Logger Categories

```csharp
// In appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs": "Debug",
      "Meshmakers.Octo.ConstructionKit.Engine.Services": "Information"
    }
  }
}
```

### Important Log Events

| Event | Level | Description |
|-------|-------|-------------|
| Catalog lookup | Information | Model is being searched in catalogs |
| Model found | Information | Model found in catalog |
| Cache refresh | Debug | Cache is being updated |
| Publish | Information | Model is being published |
| Error | Error | Error during catalog operation |

---

## Validation

### Options Validation

```csharp
services.AddOptions<PrivateGitHubCatalogOptions>()
    .Validate(options =>
    {
        if (string.IsNullOrEmpty(options.GitHubApiToken))
        {
            return false; // Token required for private repos
        }
        return true;
    }, "GitHub API Token is required for private catalogs");
```

### Runtime Checks

```csharp
// In GitHubCatalog
public async Task PublishAsync(CkCompiledModelRoot model, ...)
{
    if (string.IsNullOrEmpty(_options.GitHubApiToken))
    {
        throw ModelCatalogException.GitHubTokenMissing();
    }

    if (_options.GitHubPagesUri == null)
    {
        throw ModelCatalogException.GitHubPagesUriMissing();
    }
}
```
