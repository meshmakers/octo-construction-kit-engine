# Suggested Commands

## Build Commands

### Build Entire Solution
```bash
# Release build
dotnet build --configuration Release

# Debug build
dotnet build --configuration Debug

# Local development build (uses version 999.0.0 and local nuget packages)
dotnet build --configuration DebugL

# Build specific project
dotnet build src/ConstructionKit.Engine/ConstructionKit.Engine.csproj
```

### Clean Build
```bash
dotnet clean --configuration Release
dotnet build --configuration Release
```

## Test Commands

### Run All Tests (Excluding System Tests)
```bash
dotnet test --configuration Release --filter "FullyQualifiedName!~SystemTests"
```

### Run All Tests (Including System Tests)
```bash
dotnet test --configuration Release
```

### Run Tests for Specific Project
```bash
dotnet test tests/ConstructionKit.Engine.Tests/ConstructionKit.Engine.Tests.csproj
dotnet test tests/Runtime.Engine.Tests/Runtime.Engine.Tests.csproj
```

### Run Specific Test
```bash
dotnet test --filter "FullyQualifiedName~ClassName.TestMethodName"
```

## Construction Kit Compiler

### Compile a CK Model
```bash
dotnet run --project src/ConstructionKit.Compiler/ConstructionKit.Compiler.csproj -- \
  -c Compile \
  -p "path/to/ConstructionKit" \
  -o "output/path"
```

### Generate Documentation from Compiled Model
```bash
dotnet run --project src/ConstructionKit.Compiler/ConstructionKit.Compiler.csproj -- \
  -c generateDocs \
  -f "path/to/compiled-model.yaml" \
  -o "docs/output/path" \
  -l "/docs/technologyGuide/constructionKits/libraries/"
```

## Git Commands (macOS/Darwin)
Standard git commands work normally:
```bash
git status
git add <file>
git commit -m "message"
git push
git pull
git log
```

## File System Commands (macOS/Darwin)
Standard Unix commands:
```bash
ls                  # List files
cd <directory>      # Change directory
find <path>         # Find files
grep <pattern>      # Search content
pwd                 # Print working directory
```

## NuGet Package Management
```bash
# Restore packages
dotnet restore

# List packages
dotnet list package

# Add package
dotnet add package <PackageName>
```

## Docker (for SchemaProvider)
```bash
# Build Docker image
docker build -f src/ConstructionKit.SchemaProvider/Dockerfile -t octo-schema-provider .

# Run Docker container
docker run -p 8080:8080 octo-schema-provider
```