# Catalog-Architektur

## Übersicht

Das Catalog-System folgt einer mehrschichtigen Architektur mit klarer Trennung zwischen öffentlicher API, interner Verwaltung und konkreten Implementierungen.

## Schichtenmodell

```
┌─────────────────────────────────────────────────────────┐
│                    ICatalogService                       │  ← Öffentliche API
│                    (CatalogService)                      │
└─────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│                    ICatalogManager                       │  ← Interne Verwaltung
│                    (CatalogManager)                      │
└─────────────────────────────────────────────────────────┘
                           │
           ┌───────────────┼───────────────┐
           ▼               ▼               ▼
┌─────────────────┐ ┌─────────────┐ ┌─────────────────┐
│ EmbeddedResource│ │ LocalFile   │ │ GitHub          │    ← Katalog-
│ Catalog         │ │ SystemCatalog│ │ Catalog         │       Implementierungen
│ (Order: 0)      │ │ (Order: 10) │ │ (Order: 20/21)  │
└─────────────────┘ └─────────────┘ └─────────────────┘
```

## Design Patterns

### Strategy Pattern

Jede Katalog-Implementierung ist eine Strategie für den Zugriff auf CK-Modelle:

```csharp
public interface ICatalog
{
    Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, ...);
    Task PublishAsync(CkCompiledModelRoot model, ...);
}

// Konkrete Strategien
public class LocalFileSystemCatalog : ICatalog { ... }
public class PublicGitHubCatalog : ICatalog { ... }
```

### Composite Pattern

Der `CatalogManager` aggregiert mehrere Kataloge und bietet eine einheitliche Schnittstelle:

```csharp
internal class CatalogManager : ICatalogManager
{
    private readonly IEnumerable<ICatalog> _catalogs;

    public async Task<CkCompiledModelRoot> GetAsync(CkModelId ckModelId, ...)
    {
        foreach (var catalog in _catalogs.OrderBy(x => x.Order))
        {
            if (await catalog.IsExistingAsync(ckModelId))
            {
                return await catalog.GetAsync(ckModelId, ...);
            }
        }
        throw ModelCatalogException.ModelNotFoundInCatalogs(ckModelId);
    }
}
```

### Template Method Pattern

Die `CachedCatalog`-Basisklasse definiert das Grundgerüst für Cache-basierte Kataloge:

```csharp
public abstract class CachedCatalog : ICatalog
{
    // Template-Methode für das Caching
    public async IAsyncEnumerable<CatalogResultItem> ListAsync(...)
    {
        var cache = await LoadOrRefreshCacheAsync();
        foreach (var entry in cache.Models)
        {
            yield return MapToResultItem(entry);
        }
    }

    // Abstrakte Methoden für konkrete Implementierungen
    protected abstract Task<CacheCatalog> FetchCatalogAsync();
    protected abstract string GetCacheFilePath();
}
```

### Factory Pattern

HTTP- und GitHub-Clients werden über Factories erstellt:

```csharp
public interface IHttpClientFactory
{
    IHttpClientWrapper CreateClient(Uri baseAddress);
}

public interface IGitHubClientFactory
{
    IGitHubClientWrapper CreateClient(GitHubCatalogOptions options);
}
```

### Adapter Pattern

Wrapper-Klassen adaptieren externe Bibliotheken:

```csharp
// Adaptiert HttpClient
public class HttpClientWrapper : IHttpClientWrapper
{
    private readonly HttpClient _httpClient;

    public async Task<string?> GetStringAsync(string path)
    {
        var response = await _httpClient.GetAsync(path);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        return await response.Content.ReadAsStringAsync();
    }
}

// Adaptiert Octokit GitHubClient
public class GitHubClientWrapper : IGitHubClientWrapper
{
    private readonly GitHubClient _client;
    // ...
}
```

## Auflösungsfluss

### Modell-Lookup

```
GetAsync(modelId)
    │
    ▼
┌─────────────────────────────────────────┐
│ CatalogManager.GetAsync()               │
│   for each catalog (ordered by Order):  │
│     if catalog.IsExistingAsync():       │
│       return catalog.GetAsync()         │
└─────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────┐
│ Catalog.GetAsync()                      │
│   1. Resolve file path                  │
│   2. Load JSON from storage             │
│   3. Deserialize CkCompiledModelRoot    │
│   4. Validate schema                    │
└─────────────────────────────────────────┘
```

### Dependency Resolution

```
RestoreConstructionKitModelsAsync()
    │
    ▼
┌─────────────────────────────────────────┐
│ CatalogService                          │
│   1. Parse model configuration          │
│   2. Resolve dependencies               │
└─────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────┐
│ CatalogDependencyResolver               │
│   1. Get direct dependencies            │
│   2. Recursively resolve transitive     │
│   3. Handle circular references         │
│   4. Return ordered dependency list     │
└─────────────────────────────────────────┘
    │
    ▼
┌─────────────────────────────────────────┐
│ CatalogModelResolver                    │
│   1. Resolve inheritance                │
│   2. Resolve references                 │
│   3. Resolve variables                  │
└─────────────────────────────────────────┘
```

## Caching-Strategie

### Cache-Hierarchie

```
Memory
  │
  ▼
Local File Cache (~/.octo/ck-catalog/cache/)
  │
  ▼
Remote Source (GitHub Pages / API)
```

### Cache-Invalidierung

- **Zeitbasiert**: Cache-Dateien haben ein konfigurierbares Ablaufdatum
- **Explizit**: `RefreshCatalogCacheAsync()` erzwingt Neuladung
- **Automatisch**: Bei Publish-Operationen wird der lokale Cache aktualisiert

### Concurrent Access

```csharp
// Retry-Logik für File-Locks
private async Task<string> ReadFileWithRetryAsync(string path)
{
    for (int i = 0; i < MaxRetries; i++)
    {
        try
        {
            using var stream = new FileStream(path,
                FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // ...
        }
        catch (IOException) when (i < MaxRetries - 1)
        {
            await Task.Delay(RetryDelayMs);
        }
    }
}
```

## Fehlerbehandlung

### ModelCatalogException

Zentrale Exception-Klasse mit Factory-Methoden:

```csharp
public class ModelCatalogException : Exception
{
    public static ModelCatalogException ModelNotFound(CkModelId modelId);
    public static ModelCatalogException ModelCatalogNotFound(string catalogName);
    public static ModelCatalogException CatalogNotWritable(string catalogName);
    public static ModelCatalogException PublishFailed(string reason);
    // ...
}
```

### Fehler-Propagation

1. Katalog-spezifische Fehler werden in `ModelCatalogException` gewrappt
2. Der `CatalogManager` fängt Fehler und versucht den nächsten Katalog
3. Wenn alle Kataloge fehlschlagen, wird eine aggregierte Exception geworfen

## Thread-Safety

- `CatalogManager` ist Singleton und thread-safe
- Katalog-Implementierungen verwenden `FileShare.ReadWrite` für concurrent access
- Lazy-Initialization wird für teure Ressourcen verwendet:

```csharp
private readonly Lazy<ICatalogManager> _catalogManager;

public CatalogDependencyResolver(Lazy<ICatalogManager> catalogManager)
{
    _catalogManager = catalogManager;
}
```
