# Task Completion Checklist

## When a Development Task is Completed

### 1. Code Quality Checks
- [ ] **Fix All Warnings**: The project has `TreatWarningsAsErrors=true`, so all warnings must be resolved
- [ ] **Nullable Reference Types**: Ensure all nullability is properly handled
- [ ] **Code Style**: Follow established naming conventions and patterns
- [ ] **XML Documentation**: Public APIs should have XML documentation comments

### 2. Build Verification
```bash
# Build the solution to ensure no compilation errors
dotnet build --configuration Release

# Or for local development:
dotnet build --configuration DebugL
```

### 3. Run Tests
```bash
# Run unit tests (excludes system tests for faster feedback)
dotnet test --configuration Release --filter "FullyQualifiedName!~SystemTests"

# If system tests are relevant, run full test suite:
dotnet test --configuration Release
```

### 4. Construction Kit Model Changes
If you modified CK models (YAML files):
- [ ] Ensure `ckModel.yaml` has correct modelId and dependencies
- [ ] Validate YAML syntax
- [ ] Recompile the model if needed:
```bash
dotnet run --project src/ConstructionKit.Compiler/ConstructionKit.Compiler.csproj -- \
  -c Compile \
  -p "path/to/ConstructionKit" \
  -o "output/path"
```
- [ ] Verify generated code compiles

### 5. Git Workflow
```bash
# Check status
git status

# Stage changes
git add <files>

# Commit with meaningful message
git commit -m "AB#<ticket>: <type>: <description>"

# Push (if ready)
git push
```

### 6. Commit Message Format
Follow the pattern seen in recent commits:
- `AB#<ticket-number>: <Type>: <Description>`
- Types: New (feature), Fix (bug fix), Update (enhancement)
- Example: `AB#2706: New: Ensure that numbers are casted to string culture invariant.`

## Before Creating a Pull Request
- [ ] All tests pass
- [ ] Build succeeds without warnings
- [ ] Code follows project conventions
- [ ] Commit messages are clear and follow format
- [ ] Branch is up to date with main

## CI/CD Verification
The Azure Pipeline will automatically:
- Build src projects
- Build samples
- Run tests (excluding SystemTests)
- Build Docker images (for SchemaProvider)
- Generate API documentation
- Create and publish NuGet packages (for main/test branches)

## Additional Considerations

### Performance
- Consider impact on build time
- Optimize CK model compilation if needed

### Documentation
- Update CLAUDE.md if adding new important commands or patterns
- Update inline comments for complex logic

### Dependencies
- Check if new package references are needed
- Ensure version consistency with Directory.Build.props