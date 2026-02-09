# Construction Kit Compiler Architecture

## Overview

The Construction Kit (CK) Compiler is a build-time tool that transforms YAML-based model definitions into compiled model files. It validates, resolves dependencies, and produces optimized output for runtime consumption by the Octo Mesh platform.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Construction Kit Compiler                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐                   │
│  │   YAML       │    │   Schema     │    │   Element    │                   │
│  │   Files      │───>│   Validator  │───>│   Resolver   │                   │
│  │              │    │              │    │              │                   │
│  └──────────────┘    └──────────────┘    └──────────────┘                   │
│         │                                       │                           │
│         │                                       ▼                           │
│         │            ┌──────────────┐    ┌──────────────┐                   │
│         │            │  Reference   │<───│  CkModel     │                   │
│         └───────────>│  Resolver    │    │  Graph       │                   │
│                      │              │    │              │                   │
│                      └──────────────┘    └──────────────┘                   │
│                             │                   │                           │
│                             ▼                   │                           │
│                      ┌──────────────┐           │                           │
│                      │ Inheritance  │           │                           │
│                      │  Resolver    │───────────┘                           │
│                      │              │                                       │
│                      └──────────────┘                                       │
│                             │                                               │
│                             ▼                                               │
│                      ┌──────────────┐    ┌──────────────┐                   │
│                      │  Compiled    │───>│   Output     │                   │
│                      │  Model       │    │   Files      │                   │
│                      │              │    │  (.yaml)     │                   │
│                      └──────────────┘    └──────────────┘                   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Entry Points

### CLI Entry Point
**File**: `src/ConstructionKit.Compiler/Program.cs`

The compiler CLI is a console application that:
1. Sets up dependency injection container
2. Parses command-line arguments
3. Delegates to the appropriate command handler

```bash
# Basic usage
dotnet run --project src/ConstructionKit.Compiler -- \
  -c Compile \
  -p "path/to/ConstructionKit" \
  -o "output/path"
```

### Command Handler
**File**: `src/ConstructionKit.Compiler/Commands/Implementations/CompileCommand.cs`

Handles the `Compile` command with these arguments:

| Argument | Description |
|----------|-------------|
| `-p, --path` | Path to the ConstructionKit folder |
| `-o, --output` | Output path for compiled model |
| `-c, --cache` | Generate cache file for faster runtime loading |
| `-cr, --compileResult` | Output compile result JSON |
| `-lce, --localCatalogEnabled` | Enable local catalog for dependencies |

### Compiler Service
**File**: `src/ConstructionKit.Engine/Services/CompilerService.cs`

The main compilation engine that orchestrates the entire process:

```csharp
public async Task<CkCompileResult> CompileAsync(
    string ckModelPath,
    string outputPath,
    bool outputAsCache,
    bool localCatalogEnabled)
```

## Compilation Pipeline

### Phase 1: Input Loading

#### 1.1 Metadata Reading
The compiler first reads the `ck-meta.yaml` file which defines:
- `modelId`: Unique model identifier (e.g., "System-1.0.1")
- `version`: Semantic version
- `description`: Model description
- `dependencies`: List of dependent models

**File**: `src/ConstructionKit.Contracts/DataTransferObjects/CkMetaRootDto.cs`

#### 1.2 YAML Deserialization
**File**: `src/ConstructionKit.Engine/Serialization/CkYamlSerializer.cs`

The YAML serializer:
1. Reads YAML files from designated folders
2. Deserializes into DTOs using YamlDotNet
3. Handles custom type converters for ID types

**Supported folders**:
- `types/` - Type definitions
- `attributes/` - Attribute definitions
- `records/` - Record (value object) definitions
- `enums/` - Enumeration definitions
- `associations/` - Association role definitions

### Phase 2: Schema Validation

**File**: `src/ConstructionKit.Engine/Serialization/CkSchemaValidator.cs`

JSON Schema validation ensures YAML files conform to the expected structure:

```csharp
public bool Validate(string yaml, CkSchemaType schemaType, OperationResult operationResult)
```

**Schema files** (embedded resources in `ConstructionKit.Contracts`):
- `construction-kit-elements.schema.json` - Type, Record, Attribute, Enum, Association schemas
- `construction-kit-meta.schema.json` - Model metadata schema
- `construction-kit-compiled.schema.json` - Compiled output schema
- `ck-migration-meta.schema.json` - Migration metadata schema
- `ck-migration.schema.json` - Migration script schema

### Phase 3: Element Resolution

**File**: `src/ConstructionKit.Engine/Resolvers/ElementResolver.cs`

The `ElementResolver` validates and processes each model element:

```csharp
public void Resolve(
    CkModelRootBase modelRootBase,
    CkModelGraph ckModelGraph,
    IVariableResolver variableResolver,
    IOriginFileResolver originFileResolver,
    OperationResult operationResult)
```

#### Validation performed:
1. **Character validation**: IDs contain only allowed characters (`A-Z`, `a-z`, `0-9`, `.`, `_`)
2. **Uniqueness**: No duplicate IDs within the model
3. **Required fields**: Record/Enum references are set when required
4. **Enum values**: No negative keys, no empty names, no duplicate keys

#### Variable Resolution
**File**: `src/ConstructionKit.Engine/Resolvers/VariableResolver.cs`

Variables like `${this}` and `${System}` are resolved to actual model references:
- `${this}` → Current model ID
- `${ModelName}` → Reference to dependent model

### Phase 4: Reference Resolution

**File**: `src/ConstructionKit.Engine/Resolvers/ReferenceResolver.cs`

Validates cross-references between elements:

```csharp
public void CheckCkTypes(CkModelGraph ckModelGraph, OperationResult operationResult)
public void CheckCkRecords(CkModelGraph ckModelGraph, OperationResult operationResult)
public void CheckCkAttributes(CkModelGraph ckModelGraph, OperationResult operationResult)
public void CheckCkAssociationRoles(CkModelGraph ckModelGraph, OperationResult operationResult)
```

#### Validated references:
- Attribute references in types and records
- Record references from attributes (`ValueCkRecordId`)
- Enum references from attributes (`ValueCkEnumId`)
- Association role references in types
- Base type/record references for inheritance
- Target type references in associations

### Phase 5: Inheritance Resolution

**File**: `src/ConstructionKit.Engine/Resolvers/InheritanceResolver.cs`

Resolves the inheritance hierarchy for types and records:

```csharp
public CkTypeGraph? GetAndUpdateTypeGraph(
    CkModelGraph ckModelGraph,
    CkId<CkTypeId> ckTypeId,
    OperationResult operationResult)
```

#### Inheritance validation:
- Base types/records exist in the model graph
- No inheritance from `final` types/records
- No duplicate attribute IDs through inheritance chain
- No duplicate attribute names through inheritance
- No conflicting association definitions from base types

### Phase 6: Dependency Resolution

**File**: `src/ConstructionKit.Engine/Resolvers/Catalog/CatalogDependencyResolver.cs`

Resolves model dependencies using a catalog service:

1. Loads dependent models from catalog (local or remote)
2. Validates dependency graph for circular dependencies
3. Merges dependent model graphs

### Phase 7: Output Generation

#### Compiled Model
**File**: `src/ConstructionKit.Engine/Services/CompilerService.cs`

Generates the compiled model file (`ck-{modelId}.yaml`):

```yaml
modelId: System-1.0.1
version: 1.0.1
types:
  - typeId: Entity
    attributes: [...]
    associations: [...]
records: [...]
enums: [...]
associationRoles: [...]
migrations:              # Included when ConstructionKit/migrations/ exists
  meta:
    ckModelId: System-1.0.1
    migrations:
      - fromVersion: "1.0.0"
        toVersion: "1.0.1"
        scriptPath: "1.0.0-to-1.0.1.yaml"
  scripts: [...]
```

#### Cache Generation (Optional)
Generates a binary cache file (`ck-{modelId}.cache.json`) containing serialized `CkModelGraph` for faster runtime loading.

#### Migration Embedding (Optional)
If a `ConstructionKit/migrations/` directory exists and contains a `migration-meta.yaml`, the compiler parses the metadata and all referenced migration scripts, then embeds them in the compiled model's `migrations` field as a `CkCompiledMigrationDataDto`. Migration files are validated against the `ck-migration-meta.schema.json` and `ck-migration.schema.json` schemas. Missing or invalid script files are treated as errors and will fail the build.

## Data Structures

### ID Types

| Type | Description | Example |
|------|-------------|---------|
| `CkTypeId` | Type identifier | `Entity`, `ApiResource` |
| `CkRecordId` | Record identifier | `FieldFilter`, `UserClaim` |
| `CkAttributeId` | Attribute identifier | `AttributePath`, `Name` |
| `CkEnumId` | Enum identifier | `FieldFilterOperator` |
| `CkAssociationRoleId` | Association role identifier | `ParentChild`, `Related` |
| `CkId<T>` | Composite ID (ModelId + ElementId) | `System-1.0.1/Entity` |

### DTOs (Data Transfer Objects)

**Location**: `src/ConstructionKit.Contracts/DataTransferObjects/`

| DTO | Purpose |
|-----|---------|
| `CkTypeDto` | Type definition with attributes, associations, indexes |
| `CkRecordDto` | Value object definition with attributes |
| `CkAttributeDto` | Attribute definition with value type, defaults |
| `CkEnumDto` | Enumeration with values |
| `CkAssociationRoleDto` | Association role with multiplicity |

### Dependency Graph

**File**: `src/ConstructionKit.Engine/DependencyGraph/CkModelGraph.cs`

In-memory representation of the compiled model:

```csharp
public class CkModelGraph
{
    public Dictionary<CkId<CkTypeId>, CkTypeGraph> Types { get; }
    public Dictionary<CkId<CkAttributeId>, CkAttributeGraph> Attributes { get; }
    public Dictionary<CkId<CkRecordId>, CkRecordGraph> Records { get; }
    public Dictionary<CkId<CkEnumId>, CkEnumGraph> Enums { get; }
    public Dictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph> AssociationRoles { get; }
}
```

## Error Handling

### Operation Result
**File**: `src/ConstructionKit.Contracts/OperationResult.cs`

Collects errors, warnings, and info messages during compilation:

```csharp
public class OperationResult
{
    public bool HasErrors { get; }
    public bool HasFatalErrors { get; }
    public bool HasWarnings { get; }
    public void AddMessage(OperationMessage message);
    public void WriteMessagesToLogger(ILogger logger);
}
```

### Message Levels

| Level | Description | Compilation Result |
|-------|-------------|-------------------|
| `Information` | Informational message | Continues |
| `Warning` | Non-critical issue | Continues |
| `Error` | Validation error | Continues, output may be incomplete |
| `FatalError` | Critical error | May stop compilation |

### Message Codes
**File**: `src/ConstructionKit.Engine/Messages/MessageCodes.cs`

Generated from `messages.t4` template, provides type-safe error messages:

```csharp
MessageCodes.CkTypeIdContainsInvalidCharacters(location, ckTypeId)
MessageCodes.AttributeIdNotUnique(location, ckAttributeId)
MessageCodes.CircularDependency(location, modelId, dependentModelId)
```

## Extension Points

### Custom Validators
Implement `IElementResolver` to add custom validation logic:

```csharp
internal interface IElementResolver
{
    void Resolve(
        CkModelRootBase modelRootBase,
        CkModelGraph ckModelGraph,
        IVariableResolver variableResolver,
        IOriginFileResolver originFileResolver,
        OperationResult operationResult);
}
```

### Custom Catalog Providers
Implement `ICatalogService` to provide custom model resolution:

```csharp
public interface ICatalogService
{
    Task<CkModelGraph?> GetModelGraphAsync(CkModelId modelId);
    Task<IEnumerable<CkModelId>> GetAvailableModelsAsync();
}
```

## Configuration

### Build Configuration
Three configurations available:

| Config | Version | Package Source | Use Case |
|--------|---------|----------------|----------|
| `DebugL` | 999.0.0 | `../nuget` (local) | Local development |
| `Debug` | varies | NuGet.org | Standard debugging |
| `Release` | varies | NuGet.org | Production builds |

### MSBuild Properties
Defined in `Directory.Build.props`:

| Property | Default | Description |
|----------|---------|-------------|
| `OctoCompileCkModel` | true | Enable CK model compilation |
| `OctoPublishCkModel` | false | Publish compiled model to output |
| `OctoGenerateCkModelServiceClass` | true | Generate service registration classes |
| `OctoEmbedCkMigrations` | true | Embed migration scripts as resources |

## File Structure

```
src/
├── ConstructionKit.Compiler/
│   ├── Program.cs                    # CLI entry point
│   ├── Runner.cs                     # Main execution handler
│   └── Commands/
│       └── Implementations/
│           └── CompileCommand.cs     # Compile command handler
│
├── ConstructionKit.Engine/
│   ├── Services/
│   │   └── CompilerService.cs        # Main compilation engine
│   ├── Serialization/
│   │   ├── CkYamlSerializer.cs       # YAML parsing
│   │   └── CkSchemaValidator.cs      # JSON Schema validation
│   ├── Resolvers/
│   │   ├── ElementResolver.cs        # Element validation
│   │   ├── ReferenceResolver.cs      # Cross-reference validation
│   │   ├── InheritanceResolver.cs    # Inheritance handling
│   │   ├── VariableResolver.cs       # Variable substitution
│   │   └── Catalog/
│   │       ├── CatalogModelResolver.cs
│   │       └── CatalogDependencyResolver.cs
│   ├── DependencyGraph/
│   │   └── CkModelGraph.cs           # In-memory model
│   ├── Messages/
│   │   ├── messages.t4               # T4 template
│   │   └── MessageCodes.cs           # Generated error codes
│   └── CompilerStatics.cs            # Constants and regex patterns
│
├── ConstructionKit.Contracts/
│   ├── DataTransferObjects/          # DTOs
│   ├── Serialization/
│   │   └── Schema/                   # JSON Schema files
│   ├── CkTypeId.cs                   # ID types
│   ├── CkRecordId.cs
│   ├── CkAttributeId.cs
│   ├── CkEnumId.cs
│   ├── CkAssociationRoleId.cs
│   └── OperationResult.cs            # Error collection
│
└── SystemCkModel/
    └── ConstructionKit/              # Base system model
        ├── ck-meta.yaml
        ├── types/
        ├── attributes/
        ├── records/
        ├── enums/
        └── associations/
```

## Common Error Scenarios

### Character Validation Errors
```
Error: CkTypeId 'my_type' contains invalid characters.
       Allowed characters are A-Z, a-z, 0-9, . and _.
```

### Reference Errors
```
Error: CkAttributeId 'System-1.0.1/UnknownAttr' of CkTypeId 'MyModel/MyType'
       does not exist. Check dependency configuration.
```

### Inheritance Errors
```
Error: CkTypeId 'BaseType' is final, but CkTypeId 'DerivedType' is derived from it.
```

### Circular Dependency
```
Error: CkModelId 'ModelA' has defined a dependency to 'ModelB'
       that results in circular dependencies.
```

## Testing

### Unit Tests
**Location**: `tests/ConstructionKit.Engine.Tests/`

Test categories:
- `Resolvers/` - Resolver unit tests
- `Serialization/` - YAML/JSON parsing tests
- `DependencyGraph/` - Graph manipulation tests

### Integration Tests
**Location**: `tests/ConstructionKit.Engine.Tests/`

Uses test models from:
- `tests/TestCkModel/` - Full test model
- `tests/TestCkModel/CkTest/` - Additional test scenarios

### Running Tests
```bash
# Run all tests except system tests
dotnet test --configuration DebugL --filter "FullyQualifiedName!~SystemTests"

# Run specific test class
dotnet test --filter "FullyQualifiedName~ElementResolverTests"
```

## Performance Considerations

1. **Compiled regex patterns**: Use `RegexOptions.Compiled` for frequently used patterns
2. **Lazy loading**: Schema files are loaded lazily on first use
3. **Cache generation**: Optional cache files speed up runtime loading
4. **Parallel processing**: Consider parallelizing independent validations for large models

## Versioning

The compiler follows semantic versioning:
- Model versions: `{Name}-{Major}.{Minor}.{Patch}` (e.g., `System-1.0.1`)
- Element versions: Embedded in ID (e.g., `Entity-1`)
- Backward compatibility maintained through migration system
