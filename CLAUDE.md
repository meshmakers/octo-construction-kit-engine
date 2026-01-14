# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Octo Construction Kit Engine is a .NET-based framework for building and managing data models with Construction Kits. It's part of the Octo Mesh platform, which transforms raw data into meaningful information with proper context.

## Architecture

The codebase follows a modular architecture with clear separation of concerns:

### Core Components
- **ConstructionKit.Contracts**: Shared contracts, interfaces, and serialization logic for Construction Kit models
- **ConstructionKit.Engine**: Core engine for processing Construction Kit models
- **ConstructionKit.Compiler**: CLI tool for compiling Construction Kit YAML models
- **ConstructionKit.SourceGeneration**: Source code generation from CK models
- **Runtime.Engine**: Runtime execution engine for processing data
- **Runtime.Contracts**: Runtime contracts and interfaces
- **SystemCkModel**: Base system Construction Kit models

### Construction Kit Model Structure
Construction Kit models are defined in YAML files following a specific schema:
- Models have a unique `modelId` (e.g., "System-1.0.1")
- Models can depend on other models via `dependencies`
- Model definitions are organized in folders: `types/`, `attributes/`, `enums/`, `records/`, `associations/`
- Optional: `migrations/` folder for CK model version migrations

## Build Commands

```bash
# Build the entire solution
dotnet build --configuration Release

# Build in debug mode with local packages
dotnet build --configuration DebugL

# Build specific project
dotnet build src/ConstructionKit.Compiler/ConstructionKit.Compiler.csproj
```

## Test Commands

```bash
# Run all tests except system tests
dotnet test --configuration Release --filter "FullyQualifiedName!~SystemTests"

# Run all tests including system tests
dotnet test --configuration Release

# Run tests for specific project
dotnet test tests/ConstructionKit.Engine.Tests/ConstructionKit.Engine.Tests.csproj

# Run specific test
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"
```

## Compiler Usage

The ConstructionKit.Compiler is used to compile YAML model definitions:

```bash
# Compile a Construction Kit model
dotnet run --project src/ConstructionKit.Compiler/ConstructionKit.Compiler.csproj -- \
  -c Compile \
  -p "path/to/ConstructionKit" \
  -o "output/path"

# Generate documentation from compiled model
dotnet run --project src/ConstructionKit.Compiler/ConstructionKit.Compiler.csproj -- \
  -c generateDocs \
  -f "path/to/compiled-model.yaml" \
  -o "docs/output/path" \
  -l "/docs/technologyGuide/constructionKits/libraries/"
```

## Development Configuration

The project uses:
- .NET 9.0 (as of latest update)
- xUnit v3 for testing
- Three build configurations: Debug, Release, DebugL
  - DebugL uses version 999.0.0 for local development
  - DebugL looks for packages in `../nuget` directory

Key MSBuild properties (from Directory.Build.props):
- `OctoCompileCkModel`: Controls CK model compilation (default: true)
- `OctoPublishCkModel`: Controls CK model publishing (default: false)
- `OctoGenerateCkModelServiceClass`: Generate service classes (default: true)

## Code Quality

```bash
# The project enforces warnings as errors - fix all warnings before committing
# C# nullable reference types are enabled - handle nullability appropriately
# Latest major C# language version is used
```

## Working with Construction Kit Models

When modifying CK models:
1. YAML files in `ConstructionKit/` folders define the model structure
2. Models must follow the schema at `https://schemas.meshmakers.cloud/construction-kit-meta.schema.json`
3. After changes, recompile the model using the compiler
4. Generated code will be created based on the model definitions

## Repository Structure

```
/src                    # Source code
  /ConstructionKit.*    # CK-related components
  /Runtime.*            # Runtime components
  /SystemCkModel        # Base system models
/tests                  # Test projects
/samples                # Example implementations
/devops-build          # CI/CD pipeline definitions
```

## Blueprints and Migrations

The project supports two types of migrations:

### Blueprints
Blueprints initialize tenants with pre-configured CK models and runtime data. See `docs/blueprints.md` for details.

Key services:
- `IBlueprintService` - Applies blueprints to tenants
- `IBlueprintComposer` - Resolves blueprint hierarchies
- `ITenantBackupService` - Creates/restores backups

### CK Model Migrations
CK Model Migrations update runtime entities when CK model versions change. See `docs/ck-model-migrations.md` for details.

Key services:
- `ICkModelMigrationService` - Executes migrations between CK model versions
- `ICkMigrationContentProvider` - Provides migration scripts (embedded resources or file system)
- `ICkMigrationParser` - Parses YAML migration scripts

Migration scripts location: `ConstructionKit/migrations/`

MSBuild property to control embedding: `OctoEmbedCkMigrations` (default: true)

## Key Interfaces

| Interface | Description |
|-----------|-------------|
| `ICkModelMigrationService` | Executes CK model migrations |
| `ICkMigrationContentProvider` | Provides migration content (embedded/file system) |
| `IRuntimeRepositoryProvider` | Provides runtime repositories for tenants |
| `IBlueprintService` | Applies blueprints to tenants |
| `ICatalogService` | Manages CK model catalog |

## Important Notes

- The solution uses Azure Pipelines for CI/CD (devops-build/azure-pipelines.yml)
- NuGet packages can be published to either public or private feeds depending on configuration
- Local development uses the DebugL configuration with local package sources
- Migration YAML files in `ConstructionKit/migrations/` are automatically embedded as resources