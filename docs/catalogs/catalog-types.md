# Catalog Types

## Overview

The system provides four catalog implementations for different use cases.

## EmbeddedResourceCatalog

### Description

Read-only catalog for CK models embedded as resources in the application. Primarily used for system models.

### Properties

| Property | Value |
|----------|-------|
| Order | 0 (highest priority) |
| CanRead | true |
| CanWrite | false |
| CatalogName | "embedded" |

### Implementation

```csharp
public class EmbeddedResourceCatalog : ICatalog
{
    private readonly IEnumerable<ICkEmbeddedModel> _embeddedModels;

    public async Task<CkCompiledModelRoot> GetAsync(CkModelId modelId, ...)
    {
        var embedded = _embeddedModels
            .FirstOrDefault(m => m.ModelId == modelId);
        return embedded?.GetModel();
    }
}
```

### Usage

Models must implement `ICkEmbeddedModel`:

```csharp
public class SystemCkModel : ICkEmbeddedModel
{
    public CkModelId ModelId => new("System", new Version(2, 0, 0));

    public CkCompiledModelRoot GetModel()
    {
        var stream = GetType().Assembly
            .GetManifestResourceStream("System.ck.json");
        return JsonSerializer.Deserialize<CkCompiledModelRoot>(stream);
    }
}
```

---

## LocalFileSystemCatalog

### Description

File system-based catalog for local development and offline use. Stores models in the user's home directory.

### Properties

| Property | Value |
|----------|-------|
| Order | 10 |
| CanRead | true (configurable) |
| CanWrite | true |
| CatalogName | "local" |

### Storage Structure

```
~/.octo/local-catalog/ck-models/v2/
├── catalog.json                           # Root catalog
├── m/
│   └── mymodel/
│       ├── catalog.json                   # Model catalog
│       └── 1/
│           ├── catalog.json               # Version catalog
│           └── mymodel-1.0.0.json         # Compiled model
└── s/
    └── system/
        ├── catalog.json
        └── 2/
            ├── catalog.json
            └── system-2.0.0.json
```

### Catalog Files

**Root catalog.json:**
```json
{
  "models": [
    {
      "name": "MyModel",
      "catalogPath": "m/mymodel/catalog.json"
    }
  ]
}
```

**Model catalog.json:**
```json
{
  "name": "MyModel",
  "versions": [
    {
      "major": 1,
      "catalogPath": "1/catalog.json"
    }
  ]
}
```

**Version catalog.json:**
```json
{
  "name": "MyModel",
  "major": 1,
  "versions": [
    {
      "version": "1.0.0",
      "path": "mymodel-1.0.0.json",
      "publishedAt": "2024-01-15T10:30:00Z"
    }
  ]
}
```

### Configuration

```csharp
services.Configure<LocalFileSystemCatalogOptions>(options =>
{
    options.RootPath = "/custom/path/to/catalog";
    options.IsEnabled = true;
});
```

### Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| RootPath | string | ~/.octo/local-catalog | Root directory |
| IsEnabled | bool | true | Enable/disable catalog |
| CacheFileName | string | local-catalog-cache.json | Cache file name |

---

## PublicGitHubCatalog

### Description

GitHub-based catalog for publicly accessible CK models. Uses GitHub Pages for fast HTTP access and the GitHub API for write operations.

### Properties

| Property | Value |
|----------|-------|
| Order | 20 |
| CanRead | true |
| CanWrite | true (with token) |
| CatalogName | "github-public" |

### Default Repository

- **Owner:** meshmakers
- **Repository:** meshmakers.github.io
- **Branch:** main
- **Pages URL:** https://meshmakers.github.io/

### Access Modes

#### Without Token (Read-only via HTTP)
```csharp
// Reads directly from GitHub Pages
var url = "https://meshmakers.github.io/ck-models/v2/catalog.json";
var catalog = await httpClient.GetStringAsync(url);
```

#### With Token (Read/Write via API)
```csharp
// Uses Octokit for GitHub API
var client = new GitHubClient(new ProductHeaderValue("Octo.CK"));
client.Credentials = new Credentials(token);
```

### Configuration

```csharp
services.Configure<PublicGitHubCatalogOptions>(options =>
{
    options.GitHubApiToken = "ghp_xxxxx";  // Optional for write access
    options.GitHubRepositoryOwner = "meshmakers";
    options.GitHubRepositoryName = "meshmakers.github.io";
    options.GitHubRepositoryBranch = "main";
    options.GitHubPagesUri = new Uri("https://meshmakers.github.io/");
});
```

### Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| GitHubApiToken | string? | null | GitHub Personal Access Token |
| GitHubRepositoryOwner | string | meshmakers | Repository owner |
| GitHubRepositoryName | string | meshmakers.github.io | Repository name |
| GitHubRepositoryBranch | string | main | Branch for commits |
| GitHubPagesUri | Uri | https://meshmakers.github.io/ | GitHub Pages URL |
| ProductName | string | Meshmakers.Octo.CK.Engine | User-Agent header |

---

## PrivateGitHubCatalog

### Description

GitHub-based catalog for private/internal CK models. Always requires an API token for access.

### Properties

| Property | Value |
|----------|-------|
| Order | 21 |
| CanRead | true (with token) |
| CanWrite | true (with token) |
| CatalogName | "github-private" |

### Default Repository

- **Owner:** meshmakers
- **Repository:** construction-kit-libraries-build
- **Branch:** main
- **Pages URL:** https://meshmakers.github.io/construction-kit-libraries-build/

### Configuration

```csharp
services.Configure<PrivateGitHubCatalogOptions>(options =>
{
    options.GitHubApiToken = "ghp_xxxxx";  // Required
    options.GitHubRepositoryOwner = "meshmakers";
    options.GitHubRepositoryName = "construction-kit-libraries-build";
});
```

### Differences from PublicGitHubCatalog

| Aspect | Public | Private |
|--------|--------|---------|
| Token required | No (read only) | Yes (always) |
| HTTP fallback | Yes | No |
| Default order | 20 | 21 |

---

## Catalog Selection

### Priority-based Resolution

Models are searched in order of the `Order` property:

```csharp
foreach (var catalog in _catalogs.OrderBy(x => x.Order))
{
    if (await catalog.IsExistingAsync(modelId))
    {
        return await catalog.GetAsync(modelId);
    }
}
```

### Source Identifier

Catalogs can be filtered using `sourceIdentifier`:

```csharp
// Use only local catalog
await catalogService.GetAsync(modelId, result,
    sourceIdentifier: new LocalFileSystemSourceIdentifier());

// Use only GitHub
await catalogService.GetAsync(modelId, result,
    sourceIdentifier: new GitHubSourceIdentifier("myorg", "myrepo"));
```

### Explicit Catalog Selection

```csharp
// Load directly from specific catalog
await catalogService.GetAsync("local", modelId, operationResult);
await catalogService.GetAsync("github-public", modelId, operationResult);
```
