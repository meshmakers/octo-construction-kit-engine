# Build and Test Configuration

## Build Configurations

### Three Build Configurations
1. **Debug**
   - Standard debug build
   - Debug symbols: Full
   - Optimization: Off
   - Defines: DEBUG, TRACE
   - Version: Based on OctoVersion

2. **Release**
   - Production build
   - Optimization: On
   - Version: 3.2.* (public) or 0.1.* (private feed)
   - Used for CI/CD deployments

3. **DebugL** (Local Development)
   - Version: 999.0.0 (for all packages)
   - Debug symbols: Full
   - Optimization: Off
   - Defines: DEBUG, TRACE
   - Restore sources: Includes `$(OctoRepoRootPath)../nuget`
   - Purpose: Local development with local package dependencies

## MSBuild Properties

### Version Management
- `OctoVersion`: Base version for packages
  - DebugL: 999.0.0
  - Private server: 0.1.*
  - Public: 3.2.*
- `MeshmakerVersion`: 4.1.26 (999.0.0 in DebugL)

### Package Sources
- `OctoNugetPrivateServer`: Private NuGet feed URL
- `RestoreSources`: Includes private server and/or ../nuget for DebugL

### CK Model Build Options
- `OctoCompileCkModel`: Compile CK models during build (default: true)
- `OctoCreateCacheFile`: Create cache files (default: true)
- `OctoPublishCkModel`: Publish CK models (default: false)
- `OctoPublishCkModelRepo`: Repository for publishing (default: LocalRepository)
- `OctoGenerateCkModelServiceClass`: Generate service classes (default: true)
- `OctoGenerateCkDocumentation`: Generate docs (default: false)
- `OctoCkLinkPath`: Documentation link path

### Common Properties
- `LangVersion`: latestmajor
- `Nullable`: enable
- `TreatWarningsAsErrors`: true
- `ImplicitUsings`: true
- `RepositoryUrl`: https://github.com/meshmakers/octo-construction-kit-engine

## Test Configuration

### Test Project Types
- **Unit Tests**: `*.Tests.csproj`
  - Fast, isolated tests
  - Run in CI pipeline
- **System Tests**: `*SystemTests.csproj`
  - Integration/end-to-end tests
  - May be slower, can be excluded in CI

### Test Framework
- xUnit v3
- Test runner: dotnet test

### Test Execution Patterns

#### Standard CI Test Run (Excludes System Tests)
```bash
dotnet test --configuration Release --filter "FullyQualifiedName!~SystemTests"
```

#### Full Test Run (Includes System Tests)
```bash
dotnet test --configuration Release
```

## Azure Pipeline Configuration

### Trigger Configuration
- Branches: dev/*, test/*, main
- Excludes tags
- Batch mode: enabled

### Build Variables
- `artifactsFrameworkVersion`: net9.0
- `buildConfiguration`: Release
- `buildPlatform`: Any CPU

### Pipeline Stages
1. **Build src**: Build all source projects except Generated*.csproj
2. **Build samples**: Build sample projects
3. **Test**: Run tests excluding SystemTests
4. **Docker**: Build and push SchemaProvider Docker image
5. **Documentation**: Generate API documentation
6. **Artifacts**: Handle CK library and API doc artifacts
7. **NuGet**: Push packages to feed

### Build Arguments
Projects are built with:
- `--configuration $(buildConfiguration)`
- `/p:OctoNugetPrivateServer=$(nugetPrivateServer)`

## Output Configuration

### Output Paths
- Projects: `../../bin/$(Configuration)/`
- Documentation: `../../bin/$(Configuration)/$(AssemblyName).xml`

### Artifact Paths
- CK Libraries: `bin/$(buildConfiguration)/$(artifactsFrameworkVersion)/octo-ck-libraries/`
- API Documentation: `bin/$(buildConfiguration)/$(artifactsFrameworkVersion)/documentation/`

## Target Frameworks
- Primary: net9.0
- Compatibility: netstandard2.0 (where applicable)
- Platform: AnyCPU

## Package Generation
- `GeneratePackageOnBuild`: true (for library projects)
- `PackAsTool`: true (for CLI tools like Compiler)
- Includes: License file, icon, documentation