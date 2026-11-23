# AGENTS.md

Instructions for coding agents in this repository. Detail lives in `docs/`; read the matching file before changing the
code it covers.

## What this repo is

### Project Overview

Octo Construction Kit Engine is a .NET-based framework for building and managing data models with Construction Kits. It's part of the Octo Mesh platform, which transforms raw data into meaningful information with proper context.

### Architecture

The codebase follows a modular architecture with clear separation of concerns:

### Core Components
- **ConstructionKit.Contracts**: Shared contracts, interfaces, and serialization logic for Construction Kit models
- **ConstructionKit.Engine**: Core engine for processing Construction Kit models
- **ConstructionKit.Compiler**: CLI tool for compiling Construction Kit YAML models
- **ConstructionKit.SourceGeneration**: Source code generation from CK models
- **Runtime.Engine**: Runtime execution engine for processing data
- **Runtime.Contracts**: Runtime contracts and interfaces
- **SystemCkModel**: Base system Construction Kit models
- **StreamDataCkModel**: `System.StreamData` model (archives, time-series storage); depends on System
- **SystemFilesCkModel**: `System.Files` model (platform file system: folder roots, folders, files); depends on System

### Construction Kit Model Structure
Construction Kit models are defined in YAML files following a specific schema:
- Models have a unique `modelId` (e.g., "System-1.0.1")
- Models can depend on other models via `dependencies`
- Model definitions are organized in folders: `types/`, `attributes/`, `enums/`, `records/`, `associations/`, and (CK v2, `ckLanguage: 2`) `interfaces/`
- Optional: `migrations/` folder for CK model version migrations

### Repository Structure

```
/src                    # Source code
  /ConstructionKit.*    # CK-related components
  /Runtime.*            # Runtime components
  /SystemCkModel        # Base system models
  /StreamDataCkModel    # System.StreamData
  /SystemFilesCkModel   # System.Files (platform file system)
/tests                  # Test projects
/samples                # Example implementations
azure-pipelines.yml     # CI/CD pipeline definition (the legacy /devops-build folder is gone)
/scripts                # createDocumentation.ps1 and other CI helper scripts
```

### Important Notes

- The solution uses Azure Pipelines for CI/CD (`azure-pipelines.yml` in the repo root)
- NuGet packages can be published to either public or private feeds depending on configuration
- Local development uses the DebugL configuration with local package sources
- Migration YAML files in `ConstructionKit/migrations/` are automatically embedded as resources
## Read before you change

<!-- >>> generated: routing -->
| When you change | Read first |
|---|---|
| `src/Runtime.Contracts/StreamData/**`, `src/StreamDataCkModel/**` | `docs/archive-hidden-columns.md` |
| `src/Runtime.Engine/AuditTrails/**`, `src/Runtime.Contracts/AuditTrails/**` | `docs/audit-trail.md` |
| `src/Runtime.Engine/Blueprints/**`, `src/Runtime.Contracts/Blueprints/**`, `src/Runtime.Engine/Exchange/**`, `src/Runtime.Contracts/Exchange/**` | `docs/blueprint-engine.md` |
| `src/Runtime.Engine/Blueprints/**`, `src/Runtime.Contracts/Blueprints/**`, `src/BlueprintManager/**`, `src/ConstructionKit.Engine/BlueprintCatalogs/**` | `docs/blueprints.md` |
| `Directory.Build.props`, `Directory.Build.targets`, `src/ConstructionKit.Templates/**`, `src/ConstructionKit.MsBuildTasks/**` | `docs/build-and-packaging.md` |
| `src/ConstructionKit.Engine/Versioning/**` | `docs/ck-engine-version.md` |
| `src/ConstructionKit.Contracts/**`, `src/ConstructionKit.Engine/Serialization/**`, `src/ConstructionKit.Engine/Resolvers/**` | `docs/ck-field-touch-points.md` |
| `src/ConstructionKit.Analyzers/**`, `scripts/prove-additive-system-releases.sh`, `scripts/ab6676/**` | `docs/ck-id-guard-and-additive-proof.md` |
| `src/Runtime.Engine/CkModelMigrations/**`, `src/Runtime.Contracts/CkModelMigrations/**` | `docs/ck-model-migrations.md` |
| `azure-pipelines.yml`, `src/ConstructionKit.Engine/ModelCatalogs/**`, `src/ConstructionKit.Engine/ModelRepositories/**` | `docs/ck-publishing.md` |
| `src/ConstructionKit.SchemaProvider/**`, `src/ConstructionKit.Contracts/Serialization/Schema/**`, `src/Runtime.Contracts/Serialization/**`, `src/ConstructionKit.Contracts/RtCkIdJsonShim.cs` | `docs/ck-schema-and-json-serialization.md` |
| `src/ConstructionKit.Engine/SemVer/**` | `docs/ck-semver-rules.md` |
| `src/ConstructionKit.Contracts/DataTransferObjects/CkTypeWriteAccessDto.cs`, `src/Runtime.Engine/Security/**` | `docs/ck-type-write-access.md` |
| `src/ConstructionKit.Engine/Resolvers/**`, `src/ConstructionKit.Engine/DependencyGraph/**`, `scripts/audit-ck-ranges.sh` | `docs/ck-v2-range-retention.md` |
| `src/ConstructionKit.Analyzers/**` | `docs/ck-versioned-id-guard.md` |
| `src/ConstructionKit.Compiler/**`, `src/ConstructionKit.Engine/Services/**` | `docs/compiler-architecture.md` |
| `src/ConstructionKit.Compiler/**`, `src/ConstructionKit.Engine/Messages/**`, `src/ConstructionKit.Engine/Text.resx` | `docs/compiler-rules.md` |
| `src/ConstructionKit.Contracts/DisplayRules/**`, `src/Runtime.Contracts/DisplayRules/**` | `docs/concept-display-name-rules.md` |
| `src/Runtime.Contracts/DataPermissions/**`, `src/Runtime.Engine/Security/**` | `docs/data-permissions.md` |
| `src/ConstructionKit.Engine/DependencyGraph/**`, `src/Runtime.Contracts/Repositories/Query/**` | `docs/query-columns.md` |
| `src/Runtime.Contracts/RtPathEvaluator.cs`, `src/Runtime.Contracts/RtPathNullHandling.cs` | `docs/rt-path-evaluator-set-value.md` |
| `src/Runtime.Engine/CkModelMigrations/**`, `src/Runtime.Contracts/CkModelMigrations/**`, `src/ConstructionKit.Engine/ModelCatalogs/**`, `src/ConstructionKit.Engine/BlueprintCatalogs/**` | `docs/runtime-engine-interfaces.md` |
| `src/Runtime.Contracts/RtPathEvaluator.cs`, `src/Runtime.Contracts/RtPathNullHandling.cs`, `src/Runtime.Contracts/RtVersionConflictException.cs`, `src/Runtime.Contracts/UnacknowledgedWriteClassifier.cs`, `src/Runtime.Engine.Formulas/**` | `docs/runtime-semantics.md` |
| `src/Runtime.Engine/Blueprints/**`, `src/Runtime.Engine/Exchange/**`, `src/Runtime.Contracts/Exchange/**`, `src/ConstructionKit.MsBuildTasks/**` | `docs/runtime-state-preservation.md` |
| `src/SystemCkModel/**`, `src/SystemFilesCkModel/**` | `docs/system-ck-models.md` |
| `src/StreamDataCkModel/**`, `src/Runtime.Contracts/StreamData/**` | `docs/system-streamdata-model.md` |
<!-- <<< end generated: routing -->

## Build & test

### Build Commands

```bash
# Build in debug mode with local packages - the default for local work
dotnet build --configuration DebugL

# Build in Release mode against a published release (version must be passed explicitly)
dotnet build --configuration Release -p:OctoVersion=3.5.1

# Build specific project
dotnet build src/ConstructionKit.Compiler/ConstructionKit.Compiler.csproj
```

Versions come only from the pipeline (AB#6291): `Directory.Build.props` has no release fallback, so a
Debug/Release build without `-p:OctoVersion=X.Y.Z` (or `-p:OctoNugetPrivateServer=<feed>` for the main
line `0.1.*`) fails fast with MSBuild error `OCTO0001`. Use `-c DebugL` locally.

### Test Commands

```bash
# Run all tests except system tests
dotnet test --configuration DebugL --filter "FullyQualifiedName!~SystemTests"

# Run all tests including system tests (CI runs these too; they need no running services)
dotnet test --configuration DebugL

# Run tests for specific project
dotnet test tests/ConstructionKit.Engine.Tests/ConstructionKit.Engine.Tests.csproj

# Run specific test
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"
```

### Code Quality


```bash
# The project enforces warnings as errors - fix all warnings before committing
# C# nullable reference types are enabled - handle nullability appropriately
# Latest major C# language version is used
```
## Before you commit

## Rules
