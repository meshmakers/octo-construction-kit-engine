# Design Patterns and Guidelines

## Architecture Patterns

### Dependency Injection
The project heavily uses Microsoft.Extensions.DependencyInjection:
- Services are registered via `ServiceCollectionExtensions`
- Use constructor injection for dependencies
- Interfaces are in Contracts projects, implementations in Engine projects

### Repository Pattern
Model repositories implement `ICkModelRepository`:
- `LocalFileSystemCkModelRepository`: Local file system access
- `GitHubCkModelRepository`: GitHub integration
- `EmbeddedResourceCkModelRepository`: Embedded resources
- `CkModelRepositoryManager`: Manages multiple repositories

### Service Layer Pattern
Services encapsulate business logic:
- `ICompilerService` / `CompilerService`: Compilation logic
- `ICkValidationService` / `CkValidationService`: Validation logic
- `ICkCacheService` / `CkCacheService`: Caching logic
- `ICkModelRepositoryManager`: Repository management

### Resolver Pattern
Resolvers handle model element resolution:
- `IDependencyResolver`: Resolve model dependencies
- `IInheritanceResolver`: Resolve inheritance hierarchies
- `IReferenceResolver`: Resolve cross-references
- `IElementResolver`: Resolve individual elements
- `IVariableResolver`: Resolve variables in models
- `IModelResolver`: Overall model resolution

### Graph Pattern
Dependency graphs represent model relationships:
- `ICkModelGraph`: Root interface for model graphs
- `CkModelGraph`: Main implementation
- Specific graph types: `CkTypeGraph`, `CkAttributeGraph`, `CkEnumGraph`, etc.
- Supports traversal and inheritance resolution

## Code Organization Patterns

### Separation of Concerns
- **Contracts**: Interfaces, DTOs, exceptions, serialization contracts
- **Engine**: Business logic, services, resolvers, compilation
- **Compiler**: CLI application layer
- **Runtime**: Execution engine (separate from design-time)

### DTO Pattern
Data Transfer Objects in `DataTransferObjects/` namespace:
- Suffix with `Dto`
- Used for serialization/deserialization
- Kept separate from domain logic
- Examples: `CkTypeDto`, `CkAttributeDto`, `CkModelRootBase`

### Exception Hierarchy
Specialized exceptions for different error scenarios:
- `CompilerException`: Compilation errors
- `ModelValidationException`: Validation errors
- `ModelParseException`: Parsing errors
- `ModelRepositoryException`: Repository access errors
- `CkCacheException`: Cache-related errors
- `DependencyGraphException`: Graph operation errors

## Serialization Pattern

### Custom Converters
System.Text.Json converters for CK types:
- `CkIdConverter`, `CkModelIdConverter`, `CkTypeIdConverter`, etc.
- `OctoObjectIdConverter`: For object identifiers
- `RtEntityIdConverter`: For runtime entity IDs
- Located in `Serialization/` namespace

### Dual Format Support
- `ICkJsonSerializer` / `CkJsonSerializer`: JSON serialization
- `ICkYamlSerializer` / `CkYamlSerializer`: YAML serialization
- Both support validation via `ICkSchemaValidator`

## Validation Pattern
- Schema-based validation using JSON Schema
- `CkSchemaValidator` validates against schema
- `OctoValidatingJsonConverter` for inline validation during deserialization
- Message-based error reporting via `OperationMessage`

## Documentation Generation Pattern
- `IContentGenerator` / `ContentGenerator`: Generate markdown documentation
- `IMermaidGenerator` / `MermaidGenerator`: Generate Mermaid diagrams
- Extension methods on graph types for documentation extraction
- `ILinkHelpers` / `LinkHelpers`: Generate cross-references

## Key Design Principles

### Immutability
- CK identifiers (CkId, CkModelId, etc.) are typically immutable structs
- DTOs use init-only properties where appropriate

### Nullable Reference Types
- All projects enable nullable reference types
- Explicit null handling required
- Use nullable annotations appropriately

### Interface Segregation
- Small, focused interfaces (e.g., `ICkYamlSerializer`, `ICkJsonSerializer` separate)
- Services implement specific interfaces

### Extension Methods
- Graph extensions for querying (e.g., `CkTypeGraphExtensions`)
- Type extensions for utilities (e.g., `CkTypeIdExtensions`)
- Keeps core types clean

### Message Templates
- Use T4 templates for message code generation (`MessageCodes.tt`)
- Resource files (.resx) for localization-ready messages
- Structured error reporting via `OperationMessage`

## Testing Patterns

### Test Organization
- Unit tests in `*.Tests` projects
- System/integration tests in `*SystemTests` projects
- Test data in `sampleData/` folders
- One test class per component/feature

### Test Naming
- Test classes suffix with `Tests` (e.g., `CkModelIdTests`)
- Test methods describe the scenario being tested