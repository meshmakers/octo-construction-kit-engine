# Construction Kit Model Catalogs

Das Catalog-System ermöglicht das Verwalten, Auffinden und Publizieren von kompilierten Construction Kit Modellen aus verschiedenen Quellen.

## Übersicht

Kataloge sind Speicherorte für kompilierte CK-Modelle. Das System unterstützt mehrere Katalog-Typen mit unterschiedlichen Prioritäten:

| Katalog | Order | Beschreibung | Lesen | Schreiben |
|---------|-------|--------------|-------|-----------|
| EmbeddedResourceCatalog | 0 | Eingebettete System-Modelle | ✓ | ✗ |
| LocalFileSystemCatalog | 10 | Lokales Dateisystem | ✓ | ✓ |
| PublicGitHubCatalog | 20 | Öffentliches GitHub Repository | ✓ | ✓ |
| PrivateGitHubCatalog | 21 | Privates GitHub Repository | ✓ | ✓ |

**Hinweis:** Je niedriger die Order, desto höher die Priorität bei der Modell-Auflösung.

## Schnellstart

### Modelle suchen

```csharp
var catalogService = serviceProvider.GetRequiredService<ICatalogService>();

// In allen Katalogen suchen
var results = await catalogService.SearchAsync("MyModel", skip: 0, take: 10);

// In spezifischem Katalog suchen
var results = await catalogService.SearchAsync("local", "MyModel", skip: 0, take: 10);
```

### Modell abrufen

```csharp
var operationResult = new OperationResult();
var modelId = new CkModelId("MyModel", new Version(1, 0, 0));

var model = await catalogService.GetAsync(modelId, operationResult);
```

### Modell publizieren

```csharp
await catalogService.PublishAsync(
    catalogName: "local",
    ckCompiledModel: compiledModel,
    originFileResolver: resolver,
    isForced: false
);
```

## Dokumentation

- [Architektur](./architecture.md) - Systemarchitektur und Design Patterns
- [Katalog-Typen](./catalog-types.md) - Detaillierte Beschreibung der Katalog-Implementierungen
- [Konfiguration](./configuration.md) - Dependency Injection und Options
- [Erweiterbarkeit](./extending.md) - Eigene Kataloge implementieren

## Verzeichnisstruktur

```
~/.octo/
├── local-catalog/              # LocalFileSystemCatalog Speicherort
│   └── ck-models/v2/
│       ├── catalog.json        # Root-Katalog
│       └── {letter}/
│           └── {modelname}/
│               ├── catalog.json
│               └── {major}/
│                   ├── catalog.json
│                   └── {model}.json
└── ck-catalog/
    └── cache/                  # Katalog-Cache
        ├── local-catalog-cache.json
        ├── public-github-catalog-cache.json
        └── private-github-catalog-cache.json
```

## Kernkomponenten

### ICatalog

Basis-Interface für alle Katalog-Implementierungen:

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

Öffentliche API für den Zugriff auf Kataloge:

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

Interne Verwaltung aller registrierten Kataloge:

```csharp
internal interface ICatalogManager
{
    Task<CkCompiledModelRoot?> TryGetAsync(CkModelId ckModelId, ...);
    Task<CkCompiledModelRoot> GetAsync(CkModelId ckModelId, ...);
    Task PublishAsync(string catalogName, CkCompiledModelRoot model, ...);
    Task RefreshCatalogCacheAsync(string catalogName, ...);
}
```

## Siehe auch

- [ConstructionKit.Contracts](../../src/ConstructionKit.Contracts/) - Contracts und Interfaces
- [ConstructionKit.Engine](../../src/ConstructionKit.Engine/) - Engine-Implementierung
