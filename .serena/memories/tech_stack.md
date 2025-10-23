# Technology Stack

## .NET Platform
- **Target Frameworks**: .NET 9.0 (primary), netstandard2.0 (compatibility)
- **Language**: C# with latest major language version
- **Nullable Reference Types**: Enabled across all projects
- **Implicit Usings**: Enabled
- **Warnings as Errors**: All warnings must be fixed before compilation succeeds

## Testing
- **Framework**: xUnit v3
- **Test Projects**: 
  - `*.Tests.csproj` - Unit tests
  - `*SystemTests.csproj` - System/integration tests

## Dependencies
- Microsoft.Extensions.DependencyInjection.Abstractions 9.0.9
- Microsoft.Extensions.Configuration 9.0.9
- Microsoft.Extensions.Options 9.0.9
- Octokit 14.0.0 (GitHub integration)
- NLog 6.0.4 (Logging)
- Meshmakers.Common.* 4.1.22 (or 999.0.0 in DebugL)

## Model Definition
- **Format**: YAML
- **Schema**: https://schemas.meshmakers.cloud/construction-kit-meta.schema.json
- **Serialization**: Custom JSON/YAML serializers with validation

## CI/CD
- **Platform**: Azure Pipelines
- **Configuration**: devops-build/azure-pipelines.yml
- **Artifact Framework**: net9.0
- **Docker**: Used for ConstructionKit.SchemaProvider

## Build Configurations
- **Debug**: Standard debug build
- **Release**: Production build with optimizations
- **DebugL**: Local development build (version 999.0.0, uses ../nuget for packages)