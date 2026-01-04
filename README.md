# Octo Construction Kit Engine

.NET-based framework for building and managing data models with Construction Kits. Part of the Octo Mesh platform, which transforms raw data into meaningful information with proper context.

## Project Structure

```
/src
  /ConstructionKit.Contracts    # Contracts, interfaces, serialization
  /ConstructionKit.Engine       # Core engine for CK models
  /ConstructionKit.Compiler     # CLI tool for compiling CK models
  /ConstructionKit.SourceGeneration  # Code generation from CK models
  /Runtime.Engine               # Runtime engine for data processing
  /Runtime.Contracts            # Runtime contracts and interfaces
  /SystemCkModel                # Base system Construction Kit models
/tests                          # Test projects
/samples                        # Example implementations
/docs                           # Developer documentation
```

## Documentation

- [Catalog System](./docs/catalogs/README.md) - Managing and resolving CK models

## Quick Start

### Build

```bash
# Release build
dotnet build --configuration Release

# Debug build with local packages
dotnet build --configuration DebugL
```

### Tests

```bash
# Run all tests (excluding system tests)
dotnet test --configuration Release --filter "FullyQualifiedName!~SystemTests"

# Run all tests including system tests
dotnet test --configuration Release

# Run specific test project
dotnet test tests/ConstructionKit.Engine.Tests/ConstructionKit.Engine.Tests.csproj
```

### Compiler

```bash
# Compile CK model
dotnet run --project src/ConstructionKit.Compiler/ConstructionKit.Compiler.csproj -- \
  -c Compile \
  -p "path/to/ConstructionKit" \
  -o "output/path"

# Generate documentation
dotnet run --project src/ConstructionKit.Compiler/ConstructionKit.Compiler.csproj -- \
  -c generateDocs \
  -f "path/to/compiled-model.yaml" \
  -o "docs/output/path"
```

## Requirements

- .NET 9.0 SDK
- (Optional) GitHub Personal Access Token for CK catalog access

## Configuration

### Build Configurations

| Configuration | Description |
|---------------|-------------|
| Debug | Standard debug build |
| Release | Release build |
| DebugL | Local development with version 999.0.0 |

### MSBuild Properties

| Property | Default | Description |
|----------|---------|-------------|
| OctoCompileCkModel | true | Compile CK model during build |
| OctoPublishCkModel | false | Publish CK model after build |
| OctoGenerateCkModelServiceClass | true | Generate service classes |

## Construction Kit Models

CK models are defined in YAML files:

```yaml
modelId: "MyModel-1.0.0"
dependencies:
  - "System-2.0.0"
```

### Model Structure

```
ConstructionKit/
├── model.yaml           # Model definition
├── types/               # Type definitions
├── attributes/          # Attribute definitions
├── enums/               # Enum definitions
├── records/             # Record definitions
└── associations/        # Association definitions
```

## License

See [LICENSE](./LICENSE) for details.
