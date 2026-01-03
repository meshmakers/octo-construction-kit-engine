# Konfiguration

## Dependency Injection

### Basis-Registrierung

Die Katalog-Services werden über Extension Methods registriert:

```csharp
public static IServiceCollection AddConstructionKitEngine(
    this IServiceCollection services)
{
    // Manager und Services
    services.AddSingleton<ICatalogManager, CatalogManager>();
    services.AddTransient<ICatalogService, CatalogService>();

    // Resolver
    services.AddTransient<ICatalogDependencyResolver, CatalogDependencyResolver>();
    services.AddTransient<ICatalogModelResolver, CatalogModelResolver>();

    // Katalog-Implementierungen
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

### Service-Lebensdauer

| Service | Lebensdauer | Begründung |
|---------|-------------|------------|
| ICatalogManager | Singleton | Zentrale Verwaltung, Thread-safe |
| ICatalogService | Transient | Leichtgewichtig, delegiert an Manager |
| ICatalog | Transient | Ermöglicht Konfigurationsänderungen |
| Resolver | Transient | Zustandslos |

---

## Options-Klassen

### LocalFileSystemCatalogOptions

```csharp
public class LocalFileSystemCatalogOptions : CatalogOptions
{
    /// <summary>
    /// Wurzelverzeichnis für lokale Modelle.
    /// Default: ~/.octo/local-catalog
    /// </summary>
    public string RootPath { get; set; }

    /// <summary>
    /// Aktiviert oder deaktiviert den Katalog.
    /// Default: true
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
```

### GitHubCatalogOptions (Basis)

```csharp
public abstract class GitHubCatalogOptions : CatalogOptions
{
    /// <summary>
    /// GitHub Personal Access Token für API-Zugriff.
    /// Erforderlich für Schreiboperationen und private Repos.
    /// </summary>
    public string? GitHubApiToken { get; set; }

    /// <summary>
    /// GitHub Repository Owner (User oder Organisation).
    /// </summary>
    public string GitHubRepositoryOwner { get; set; }

    /// <summary>
    /// GitHub Repository Name.
    /// </summary>
    public string GitHubRepositoryName { get; set; }

    /// <summary>
    /// Branch für Commits.
    /// Default: main
    /// </summary>
    public string GitHubRepositoryBranch { get; set; } = "main";

    /// <summary>
    /// GitHub Pages URI für HTTP-Zugriff.
    /// </summary>
    public Uri? GitHubPagesUri { get; set; }

    /// <summary>
    /// Product Name für User-Agent Header.
    /// Default: Meshmakers.Octo.ConstructionKit.Engine
    /// </summary>
    public string ProductName { get; set; } = "Meshmakers.Octo.ConstructionKit.Engine";
}
```

### CatalogOptions (Basis)

```csharp
public class CatalogOptions
{
    /// <summary>
    /// Name der Cache-Datei.
    /// </summary>
    public string CacheFileName { get; }

    /// <summary>
    /// Verzeichnis für Cache-Dateien.
    /// Default: ~/.octo/ck-catalog/cache
    /// </summary>
    public string CacheDirectory { get; set; }
}
```

---

## Konfiguration via appsettings.json

### Beispiel-Konfiguration

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

### Binding im Startup

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

## Umgebungsvariablen

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

### Benutzerdefinierte Pfade

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

## Cache-Konfiguration

### Cache-Verzeichnis

Standard: `~/.octo/ck-catalog/cache/`

```csharp
services.Configure<CatalogOptions>(options =>
{
    options.CacheDirectory = "/custom/cache/directory";
});
```

### Cache-Dateien

| Katalog | Cache-Datei |
|---------|-------------|
| Local | local-catalog-cache.json |
| GitHub Public | public-github-catalog-cache.json |
| GitHub Private | private-github-catalog-cache.json |

### Cache-Invalidierung

```csharp
// Einzelnen Katalog-Cache aktualisieren
await catalogService.RefreshCatalogCacheAsync("local");

// Alle Caches aktualisieren
await catalogService.RefreshAllCatalogCachesAsync();
```

---

## Logging

### Logger-Kategorien

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

### Wichtige Log-Events

| Event | Level | Beschreibung |
|-------|-------|--------------|
| Catalog lookup | Information | Modell wird in Katalogen gesucht |
| Model found | Information | Modell gefunden in Katalog |
| Cache refresh | Debug | Cache wird aktualisiert |
| Publish | Information | Modell wird publiziert |
| Error | Error | Fehler bei Katalog-Operation |

---

## Validierung

### Options-Validierung

```csharp
services.AddOptions<PrivateGitHubCatalogOptions>()
    .Validate(options =>
    {
        if (string.IsNullOrEmpty(options.GitHubApiToken))
        {
            return false; // Token erforderlich für private Repos
        }
        return true;
    }, "GitHub API Token is required for private catalogs");
```

### Runtime-Prüfungen

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
