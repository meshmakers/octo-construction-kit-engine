# Construction Kit Model Catalogs

The Catalog system enables managing, discovering, and publishing compiled Construction Kit models from various sources.

## Overview

Catalogs are storage locations for compiled CK models. The system supports multiple catalog types with different priorities:

| Catalog | Order | Description | Read | Write |
|---------|-------|-------------|------|-------|
| EmbeddedResourceCatalog | 0 | Embedded system models | ✓ | ✗ |
| LocalFileSystemCatalog | 10 | Local file system | ✓ | ✓ |
| PublicGitHubCatalog | 20 | Public GitHub repository | ✓ | ✓ |
| PrivateGitHubCatalog | 21 | Private GitHub repository | ✓ | ✓ |

**Note:** The lower the order, the higher the priority during model resolution.

## Quick Start

### Searching Models

```csharp
var catalogService = serviceProvider.GetRequiredService<ICatalogService>();

// Search across all catalogs
var results = await catalogService.SearchAsync("MyModel", skip: 0, take: 10);

// Search in specific catalog
var results = await catalogService.SearchAsync("local", "MyModel", skip: 0, take: 10);
```

### Retrieving a Model

```csharp
var operationResult = new OperationResult();
var modelId = new CkModelId("MyModel", new Version(1, 0, 0));

var model = await catalogService.GetAsync(modelId, operationResult);
```

### Publishing a Model

```csharp
await catalogService.PublishAsync(
    catalogName: "local",
    ckCompiledModel: compiledModel,
    originFileResolver: resolver,
    isForced: false
);
```

## Documentation

- [Architecture](./architecture.md) - System architecture and design patterns
- [Catalog Types](./catalog-types.md) - Detailed description of catalog implementations
- [Configuration](./configuration.md) - Dependency injection and options
- [Extending](./extending.md) - Implementing custom catalogs

## Directory Structure

```
~/.octo/
├── local-catalog/              # LocalFileSystemCatalog storage location
│   └── ck-models/v2/
│       ├── catalog.json        # Root catalog
│       └── {letter}/
│           └── {modelname}/
│               ├── catalog.json
│               └── {major}/
│                   ├── catalog.json
│                   └── {model}.json
└── ck-catalog/
    └── cache/                  # Catalog cache
        ├── local-catalog-cache.json
        ├── public-github-catalog-cache.json
        └── private-github-catalog-cache.json
```

## Core Components

### ICatalog

Base interface for all catalog implementations:

```csharp
public interface ICatalog
{
    int Order { get; }
    string CatalogName { get; }
    string Description { get; }
    bool CanWrite { get; }
    bool CanRead { get; }

    Task<ModelExistingResult> IsExistingAsync(CkModelIdVersionRange modelIdVersionRange, ...);
    Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, ...);
    Task PublishAsync(CkCompiledModelRoot ckCompiledModel, ...);
    IAsyncEnumerable<CatalogResultItem> ListAsync(...);
    IAsyncEnumerable<CatalogResultItem> SearchAsync(string searchTerm, ...);
}
```

### ICatalogService

Public API for catalog access:

```csharp
public interface ICatalogService
{
    Task<ModelSearchResult> SearchAsync(string searchTerm, int skip, int take, ...);
    Task<ModelListResult> ListAsync(int skip, int take, ...);
    Task<CkCompiledModelRoot?> GetAsync(CkModelId ckModelId, ...);
    Task PublishAsync(string catalogName, CkCompiledModelRoot model, ...);
    IEnumerable<Tuple<string, string>> GetCatalogList(...);
}
```

### ICatalogManager

Internal management of all registered catalogs:

```csharp
internal interface ICatalogManager
{
    Task<CkCompiledModelRoot?> TryGetAsync(CkModelId ckModelId, ...);
    Task<CkCompiledModelRoot> GetAsync(CkModelId ckModelId, ...);
    Task PublishAsync(string catalogName, CkCompiledModelRoot model, ...);
    Task RefreshCatalogCacheAsync(string catalogName, ...);
}
```

## See Also

- [ConstructionKit.Contracts](../../src/ConstructionKit.Contracts/) - Contracts and interfaces
- [ConstructionKit.Engine](../../src/ConstructionKit.Engine/) - Engine implementation
