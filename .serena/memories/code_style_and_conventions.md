# Code Style and Conventions

## C# Language Standards
- **Language Version**: Latest major version (`latestmajor`)
- **Nullable Reference Types**: Enabled - all nullability must be properly annotated
- **Implicit Usings**: Enabled - common namespaces are automatically imported
- **Warnings as Errors**: `TreatWarningsAsErrors` is set to `true` - ALL warnings must be fixed

## Naming Conventions
- **Projects**: Follow pattern `{Namespace}.{Component}` (e.g., `ConstructionKit.Engine`)
- **Assemblies**: Use full namespace `Meshmakers.Octo.{Component}` (e.g., `Meshmakers.Octo.ConstructionKit.Engine`)
- **Root Namespaces**: Match assembly names
- **Construction Kit Identifiers**: Use format `{ModelId}-{Version}` (e.g., "System-1.0.0")

## Code Organization
- **One type per file**: Generally one public class/interface per file
- **DTOs**: Located in `DataTransferObjects/` folders, suffix with `Dto`
- **Interfaces**: Prefix with `I`, typically in same namespace as implementations
- **Services**: Suffix with `Service` (e.g., `CompilerService`, `CkValidationService`)
- **Exceptions**: Suffix with `Exception` (e.g., `CompilerException`, `ModelValidationException`)

## Documentation
- **XML Documentation**: Required for public APIs (DocumentationFile is generated)
- **Resource Files**: Use `.resx` files for user-facing messages and text
- **Message Templates**: Use T4 templates for message code generation

## Project Configuration
- **Output Path**: `../../bin/$(Configuration)/`
- **Package Generation**: `GeneratePackageOnBuild` set for library projects
- **Copy Local Lock Files**: Enabled for proper dependency management

## ReSharper/Rider Settings
- Custom dictionary includes: "Meshmakers", "Octo"
- Settings stored in `.DotSettings` files