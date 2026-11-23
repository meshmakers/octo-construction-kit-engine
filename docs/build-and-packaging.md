---
description: Compiler CLI usage, MSBuild properties of the CK build, DebugL/Release configurations and how the dotnet-new template pack stamps its version.
applies_to: Directory.Build.props, Directory.Build.targets, src/ConstructionKit.Templates/**, src/ConstructionKit.MsBuildTasks/**
---

# Compiler usage, build configuration and the ConstructionKit template

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
- `OctoPublishCkModelToRemoteCatalog`: repo-internal opt-out (this repo's `Directory.Build.targets` only). `false` keeps the LocalFileSystemCatalog publish but skips `$(OctoPublishCatalog)`; set by `tests/TestCkModel` so the test-only models never reach the GitHub catalogs (AB#6114)
  - The remote publish runs at most once per compiled content and catalog: a stamp in `obj/.../octo-ck-remote-publish/` (catalog + model + SHA-256 of the compiled yaml) skips repeated pushes of identical content (AB#6119)
- `OctoGenerateCkModelServiceClass`: Generate service classes (default: true)

## `ConstructionKit` dotnet-new template (AB#4418)

`src/ConstructionKit.Templates` packs the public `Meshmakers.Octo.ConstructionKit.Templates` template
(`dotnet new ConstructionKit`). The scaffold csproj
(`Templates/ConstructionKit.CSharp/GeneratedConstructionKitProject.csproj`) references three sibling packages of
this repository (`Runtime.Contracts`, `ConstructionKit.SourceGeneration`, `ConstructionKit.MsBuildTasks`) with the
placeholder `__OCTO_TEMPLATE_PACKAGE_VERSION__` instead of a version. The target `OctoPackTemplateProjectVersion`
of `ConstructionKit.Templates.csproj` replaces it at pack time with the pack version of the template package
itself (`$(PackageVersion)`, falling back to `$(Version)`), writes the result to `obj/.../OctoTemplate/` and packs
that copy instead of the source file. The sibling packages are built and published by the same pipeline run with
the same version, so a scaffold restores from the feed the template came from. DebugL packs `999.0.0` (local
`../nuget`). Never write a version into the scaffold csproj and never pin one in `Directory.Build.props` (versions
come only from the pipeline, AB#6291). The pack fails with `OCTO-TPL001` (empty version) or `OCTO-TPL002`
(placeholder missing); keep the scaffold paths forward-slashed (`$(MSBuildProjectDirectory)/ConstructionKit`), the
copy goes through MSBuild item handling that normalises `\` on Linux/macOS. Tests:
`tests/ConstructionKit.Engine.Tests/Templates/TemplatePackVersionTests.cs` really run `dotnet pack` into a temp
folder and assert the scaffold csproj inside the nupkg (explicit version, prerelease and main-line `0.1.*` build
numbers, DebugL = `999.0.0`).
