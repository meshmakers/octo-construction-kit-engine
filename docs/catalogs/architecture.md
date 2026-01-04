# Catalog Architecture

## Overview

The Catalog system follows a layered architecture with clear separation between public API, internal management, and concrete implementations.

## Layer Model

```
┌─────────────────────────────────────────────────────────┐
│                    ICatalogService                       │  ← Public API
│                    (CatalogService)                      │
└─────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│                    ICatalogManager                       │  ← Internal Management
│                    (CatalogManager)                      │
└─────────────────────────────────────────────────────────┘
                           │
           ┌───────────────┼───────────────┐
           ▼               ▼               ▼
┌─────────────────┐ ┌─────────────┐ ┌─────────────────┐
│ EmbeddedResource│ │ LocalFile   │ │ GitHub          │    ← Catalog
│ Catalog         │ │ SystemCatalog│ │ Catalog         │       Implementations
│ (Order: 0)      │ │ (Order: 10) │ │ (Order: 20/21)  │
└─────────────────┘ └─────────────┘ └─────────────────┘
```

## Design Patterns

### Strategy Pattern

Each catalog implementation is a strategy for accessing CK models:

```csharp
public interface ICatalog
{
    Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, ...);
    Task PublishAsync(CkCompiledModelRoot model, ...);
}

// Concrete strategies
public class LocalFileSystemCatalog : ICatalog { ... }
public class PublicGitHubCatalog : ICatalog { ... }
```

### Composite Pattern

The `CatalogManager` aggregates multiple catalogs and provides a unified interface:

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

The `CachedCatalog` base class defines the skeleton for cache-based catalogs:

```csharp
public abstract class CachedCatalog : ICatalog
{
    // Template method for caching
    public async IAsyncEnumerable<CatalogResultItem> ListAsync(...)
    {
        var cache = await LoadOrRefreshCacheAsync();
        foreach (var entry in cache.Models)
        {
            yield return MapToResultItem(entry);
        }
    }

    // Abstract methods for concrete implementations
    protected abstract Task<CacheCatalog> FetchCatalogAsync();
    protected abstract string GetCacheFilePath();
}
```

### Factory Pattern

HTTP and GitHub clients are created through factories:

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

Wrapper classes adapt external libraries:

```csharp
// Adapts HttpClient
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

// Adapts Octokit GitHubClient
public class GitHubClientWrapper : IGitHubClientWrapper
{
    private readonly GitHubClient _client;
    // ...
}
```

## Resolution Flow

### Model Lookup

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

## Caching Strategy

### Cache Hierarchy

```
Memory
  │
  ▼
Local File Cache (~/.octo/ck-catalog/cache/)
  │
  ▼
Remote Source (GitHub Pages / API)
```

### Cache Invalidation

- **Time-based**: Cache files have a configurable expiration
- **Explicit**: `RefreshCatalogCacheAsync()` forces reload
- **Automatic**: Local cache is updated on publish operations

### Concurrent Access

```csharp
// Retry logic for file locks
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

## Error Handling

### ModelCatalogException

Central exception class with factory methods:

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

### Error Propagation

1. Catalog-specific errors are wrapped in `ModelCatalogException`
2. The `CatalogManager` catches errors and tries the next catalog
3. If all catalogs fail, an aggregated exception is thrown

## Thread Safety

- `CatalogManager` is a singleton and thread-safe
- Catalog implementations use `FileShare.ReadWrite` for concurrent access
- Lazy initialization is used for expensive resources:

```csharp
private readonly Lazy<ICatalogManager> _catalogManager;

public CatalogDependencyResolver(Lazy<ICatalogManager> catalogManager)
{
    _catalogManager = catalogManager;
}
```
