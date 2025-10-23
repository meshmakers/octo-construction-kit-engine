# Repository Structure

## Root Directory
```
octo-construction-kit-engine/
├── src/                          # Source code
├── tests/                        # Test projects
├── samples/                      # Example implementations
├── examples/                     # Additional examples
├── devops-build/                 # CI/CD pipeline definitions
├── assets/                       # Images and resources
├── .github/                      # GitHub configurations
├── .claude/                      # Claude Code configurations
├── .serena/                      # Serena MCP server data
├── bin/                          # Build output (gitignored)
├── Directory.Build.props         # Shared MSBuild properties
├── Directory.Build.targets       # Shared MSBuild targets
├── CLAUDE.md                     # Claude Code project instructions
└── README.md                     # Project documentation
```

## Source Projects (/src)

### Core Components
- **ConstructionKit.Contracts**: Shared contracts, interfaces, DTOs, and serialization
  - Contains: CkId types, model DTOs, serializers, dependency graph contracts
- **ConstructionKit.Engine**: Core processing engine
  - Contains: Compilation, validation, resolvers, caching, documentation generation
- **ConstructionKit.Compiler**: CLI tool (octo-ckc)
  - Compiles CK YAML models and generates documentation
- **ConstructionKit.SourceGeneration**: Source code generation from CK models
- **ConstructionKit.MsBuildTasks**: MSBuild integration tasks
- **ConstructionKit.Templates**: Code generation templates
- **ConstructionKit.SchemaProvider**: Docker-based schema provider service

### Runtime Components
- **Runtime.Contracts**: Runtime execution contracts and interfaces
- **Runtime.Engine**: Runtime execution engine for processing data

### Model Components
- **SystemCkModel**: Base system Construction Kit models
  - Contains YAML model definitions in `ConstructionKit/` subdirectory

## Test Projects (/tests)
- **ConstructionKit.Engine.Tests**: Unit tests for engine
- **Runtime.Engine.Tests**: Unit tests for runtime engine
- **ConstructionKit.Engine.SystemTests**: Integration/system tests
- **Runtime.Engine.SystemTests**: Runtime integration tests
- **ConstructionKit.Compiler.SystemTests**: Compiler integration tests
- **TestCkModel**: Test model definitions

## Construction Kit Model Structure
Within model projects (e.g., SystemCkModel/ConstructionKit/):
```
ConstructionKit/
├── ckModel.yaml                  # Model metadata (modelId, dependencies)
├── types/                        # Type definitions
├── attributes/                   # Attribute definitions
├── enums/                        # Enumeration definitions
├── records/                      # Record type definitions
└── associations/                 # Association definitions
```