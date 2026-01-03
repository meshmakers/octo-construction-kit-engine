# Katalog-Typen

## Übersicht

Das System bietet vier Katalog-Implementierungen für unterschiedliche Anwendungsfälle.

## EmbeddedResourceCatalog

### Beschreibung

Read-only Katalog für CK-Modelle, die als Embedded Resources in der Anwendung eingebettet sind. Wird primär für System-Modelle verwendet.

### Eigenschaften

| Eigenschaft | Wert |
|-------------|------|
| Order | 0 (höchste Priorität) |
| CanRead | true |
| CanWrite | false |
| CatalogName | "embedded" |

### Implementierung

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

### Verwendung

Modelle müssen `ICkEmbeddedModel` implementieren:

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

### Beschreibung

Dateisystem-basierter Katalog für lokale Entwicklung und Offline-Nutzung. Speichert Modelle im Home-Verzeichnis des Benutzers.

### Eigenschaften

| Eigenschaft | Wert |
|-------------|------|
| Order | 10 |
| CanRead | true (konfigurierbar) |
| CanWrite | true |
| CatalogName | "local" |

### Speicherstruktur

```
~/.octo/local-catalog/ck-models/v2/
├── catalog.json                           # Root-Katalog
├── m/
│   └── mymodel/
│       ├── catalog.json                   # Model-Katalog
│       └── 1/
│           ├── catalog.json               # Version-Katalog
│           └── mymodel-1.0.0.json         # Kompiliertes Modell
└── s/
    └── system/
        ├── catalog.json
        └── 2/
            ├── catalog.json
            └── system-2.0.0.json
```

### Katalog-Dateien

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

### Konfiguration

```csharp
services.Configure<LocalFileSystemCatalogOptions>(options =>
{
    options.RootPath = "/custom/path/to/catalog";
    options.IsEnabled = true;
});
```

### Optionen

| Option | Typ | Default | Beschreibung |
|--------|-----|---------|--------------|
| RootPath | string | ~/.octo/local-catalog | Wurzelverzeichnis |
| IsEnabled | bool | true | Aktiviert/deaktiviert Katalog |
| CacheFileName | string | local-catalog-cache.json | Cache-Dateiname |

---

## PublicGitHubCatalog

### Beschreibung

GitHub-basierter Katalog für öffentlich zugängliche CK-Modelle. Nutzt GitHub Pages für schnellen HTTP-Zugriff und die GitHub API für Schreiboperationen.

### Eigenschaften

| Eigenschaft | Wert |
|-------------|------|
| Order | 20 |
| CanRead | true |
| CanWrite | true (mit Token) |
| CatalogName | "github-public" |

### Standard-Repository

- **Owner:** meshmakers
- **Repository:** meshmakers.github.io
- **Branch:** main
- **Pages URL:** https://meshmakers.github.io/

### Zugriffsmodi

#### Ohne Token (Read-only via HTTP)
```csharp
// Liest direkt von GitHub Pages
var url = "https://meshmakers.github.io/ck-models/v2/catalog.json";
var catalog = await httpClient.GetStringAsync(url);
```

#### Mit Token (Read/Write via API)
```csharp
// Verwendet Octokit für GitHub API
var client = new GitHubClient(new ProductHeaderValue("Octo.CK"));
client.Credentials = new Credentials(token);
```

### Konfiguration

```csharp
services.Configure<PublicGitHubCatalogOptions>(options =>
{
    options.GitHubApiToken = "ghp_xxxxx";  // Optional für Schreibzugriff
    options.GitHubRepositoryOwner = "meshmakers";
    options.GitHubRepositoryName = "meshmakers.github.io";
    options.GitHubRepositoryBranch = "main";
    options.GitHubPagesUri = new Uri("https://meshmakers.github.io/");
});
```

### Optionen

| Option | Typ | Default | Beschreibung |
|--------|-----|---------|--------------|
| GitHubApiToken | string? | null | GitHub Personal Access Token |
| GitHubRepositoryOwner | string | meshmakers | Repository-Besitzer |
| GitHubRepositoryName | string | meshmakers.github.io | Repository-Name |
| GitHubRepositoryBranch | string | main | Branch für Commits |
| GitHubPagesUri | Uri | https://meshmakers.github.io/ | GitHub Pages URL |
| ProductName | string | Meshmakers.Octo.CK.Engine | User-Agent Header |

---

## PrivateGitHubCatalog

### Beschreibung

GitHub-basierter Katalog für private/interne CK-Modelle. Erfordert immer einen API-Token für den Zugriff.

### Eigenschaften

| Eigenschaft | Wert |
|-------------|------|
| Order | 21 |
| CanRead | true (mit Token) |
| CanWrite | true (mit Token) |
| CatalogName | "github-private" |

### Standard-Repository

- **Owner:** meshmakers
- **Repository:** construction-kit-libraries-build
- **Branch:** main
- **Pages URL:** https://meshmakers.github.io/construction-kit-libraries-build/

### Konfiguration

```csharp
services.Configure<PrivateGitHubCatalogOptions>(options =>
{
    options.GitHubApiToken = "ghp_xxxxx";  // Erforderlich
    options.GitHubRepositoryOwner = "meshmakers";
    options.GitHubRepositoryName = "construction-kit-libraries-build";
});
```

### Unterschiede zum PublicGitHubCatalog

| Aspekt | Public | Private |
|--------|--------|---------|
| Token erforderlich | Nein (nur lesen) | Ja (immer) |
| HTTP-Fallback | Ja | Nein |
| Standard-Order | 20 | 21 |

---

## Katalog-Auswahl

### Prioritätsbasierte Auflösung

Modelle werden in der Reihenfolge der `Order`-Eigenschaft gesucht:

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

Kataloge können über `sourceIdentifier` gefiltert werden:

```csharp
// Nur lokalen Katalog verwenden
await catalogService.GetAsync(modelId, result,
    sourceIdentifier: new LocalFileSystemSourceIdentifier());

// Nur GitHub verwenden
await catalogService.GetAsync(modelId, result,
    sourceIdentifier: new GitHubSourceIdentifier("myorg", "myrepo"));
```

### Explizite Katalog-Auswahl

```csharp
// Direkt aus spezifischem Katalog laden
await catalogService.GetAsync("local", modelId, operationResult);
await catalogService.GetAsync("github-public", modelId, operationResult);
```
