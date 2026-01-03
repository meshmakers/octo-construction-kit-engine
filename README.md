# Octo Construction Kit Engine

.NET-basiertes Framework zum Erstellen und Verwalten von Datenmodellen mit Construction Kits. Teil der Octo Mesh Plattform, die Rohdaten in aussagekräftige Informationen mit dem richtigen Kontext transformiert.

## Projektstruktur

```
/src
  /ConstructionKit.Contracts    # Contracts, Interfaces, Serialisierung
  /ConstructionKit.Engine       # Core Engine für CK-Modelle
  /ConstructionKit.Compiler     # CLI-Tool zum Kompilieren von CK-Modellen
  /ConstructionKit.SourceGeneration  # Code-Generierung aus CK-Modellen
  /Runtime.Engine               # Runtime-Engine für Datenverarbeitung
  /Runtime.Contracts            # Runtime Contracts und Interfaces
  /SystemCkModel                # Basis System Construction Kit Modelle
/tests                          # Test-Projekte
/samples                        # Beispiel-Implementierungen
/docs                           # Entwicklerdokumentation
```

## Dokumentation

- [Catalog System](./docs/catalogs/README.md) - Verwaltung und Auflösung von CK-Modellen

## Quick Start

### Build

```bash
# Release-Build
dotnet build --configuration Release

# Debug-Build mit lokalen Paketen
dotnet build --configuration DebugL
```

### Tests

```bash
# Alle Tests ausführen (ohne System-Tests)
dotnet test --configuration Release --filter "FullyQualifiedName!~SystemTests"

# Alle Tests inkl. System-Tests
dotnet test --configuration Release

# Spezifisches Test-Projekt
dotnet test tests/ConstructionKit.Engine.Tests/ConstructionKit.Engine.Tests.csproj
```

### Compiler

```bash
# CK-Modell kompilieren
dotnet run --project src/ConstructionKit.Compiler/ConstructionKit.Compiler.csproj -- \
  -c Compile \
  -p "path/to/ConstructionKit" \
  -o "output/path"

# Dokumentation generieren
dotnet run --project src/ConstructionKit.Compiler/ConstructionKit.Compiler.csproj -- \
  -c generateDocs \
  -f "path/to/compiled-model.yaml" \
  -o "docs/output/path"
```

## Voraussetzungen

- .NET 9.0 SDK
- (Optional) GitHub Personal Access Token für CK-Katalog-Zugriff

## Konfiguration

### Build-Konfigurationen

| Konfiguration | Beschreibung |
|---------------|--------------|
| Debug | Standard-Debug-Build |
| Release | Release-Build |
| DebugL | Lokale Entwicklung mit Version 999.0.0 |

### MSBuild-Properties

| Property | Default | Beschreibung |
|----------|---------|--------------|
| OctoCompileCkModel | true | CK-Modell während Build kompilieren |
| OctoPublishCkModel | false | CK-Modell nach Build publizieren |
| OctoGenerateCkModelServiceClass | true | Service-Klassen generieren |

## Construction Kit Modelle

CK-Modelle werden in YAML-Dateien definiert:

```yaml
modelId: "MyModel-1.0.0"
dependencies:
  - "System-2.0.0"
```

### Modell-Struktur

```
ConstructionKit/
├── model.yaml           # Modell-Definition
├── types/               # Typ-Definitionen
├── attributes/          # Attribut-Definitionen
├── enums/               # Enum-Definitionen
├── records/             # Record-Definitionen
└── associations/        # Assoziations-Definitionen
```

## Lizenz

Siehe [LICENSE](./LICENSE) für Details.
