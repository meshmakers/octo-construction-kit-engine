# CK Model Migrations

CK Model Migrations enable automatic updates to runtime entities when the Construction Kit model changes. Unlike Blueprint migrations (which migrate tenant data), CK Model Migrations adapt the structure of entities themselves.

## Overview

| Aspect              | Blueprint Migration          | CK Model Migration          |
|---------------------|------------------------------|-----------------------------|
| **Purpose**         | Update tenant data           | Adapt entity structure      |
| **Trigger**         | Blueprint version upgrade    | CK model version upgrade    |
| **Scope**           | Seed data and user entities  | All entities of a CK type   |
| **Typical Actions** | Add, Update, Delete Entities | Transform, Rename, MapValue |

## Migration Script Structure

### Folder Structure

```
MyModel/
└── ConstructionKit/
    ├── types/
    ├── attributes/
    └── migrations/
        ├── migration-meta.yaml     # Index of all migrations
        ├── 1.0.0-to-1.1.0.yaml    # Migration script
        └── 1.1.0-to-2.0.0.yaml    # Migration script
```

### migration-meta.yaml

```yaml
$schema: https://schemas.meshmakers.cloud/ck-migration-meta.schema.json
ckModelName: MyModel
latestVersion: "2.0.0"

migrations:
  - fromVersion: "1.0.0"
    toVersion: "1.1.0"
    scriptPath: "1.0.0-to-1.1.0.yaml"
    description: "Add new attributes"
    breaking: false

  - fromVersion: "1.1.0"
    toVersion: "2.0.0"
    scriptPath: "1.1.0-to-2.0.0.yaml"
    description: "Major restructuring"
    breaking: true
```

### Migration Script

```yaml
$schema: https://schemas.meshmakers.cloud/ck-migration-script.schema.json
sourceVersion: "1.0.0"
targetVersion: "2.0.0"
description: "Migrate from v1 to v2"

steps:
  # Rename type
  - stepId: rename-old-type
    action: Transform
    target:
      ckTypeId: "MyModel/OldTypeName"
    transform:
      type: ChangeCkType
      targetCkTypeId: "MyModel/NewTypeName"

  # Rename attribute
  - stepId: rename-attribute
    action: Transform
    target:
      ckTypeId: "MyModel/Entity"
    transform:
      type: RenameAttribute
      sourceAttribute: oldName
      targetAttribute: newName

  # Set value
  - stepId: set-default
    action: Transform
    target:
      ckTypeId: "MyModel/Entity"
      filter:
        and:
          - attribute: status
            operator: IsNull
    transform:
      type: SetValue
      targetAttribute: status
      value: "active"

  # Map value
  - stepId: map-status
    action: Transform
    target:
      ckTypeId: "MyModel/Entity"
    transform:
      type: MapValue
      targetAttribute: status
      valueMapping:
        "0": "inactive"
        "1": "active"
        "2": "archived"

  # Add new entities
  - stepId: add-config
    action: Add
    target:
      ckTypeId: "MyModel/Configuration"
    entities:
      - rtWellKnownName: DefaultConfig
        attributes:
          name: "Default Configuration"
          version: "2.0"

  # Delete entities
  - stepId: remove-deprecated
    action: Delete
    target:
      ckTypeId: "MyModel/DeprecatedType"
    deleteOptions:
      strategy: Erase
```

## Transform Types

| Type              | Description                      | Parameters                           |
|-------------------|----------------------------------|--------------------------------------|
| `ChangeCkType`    | Changes the CK type of an entity | `targetCkTypeId`                     |
| `SetValue`        | Sets an attribute value          | `targetAttribute`, `value`           |
| `RenameAttribute` | Renames an attribute             | `sourceAttribute`, `targetAttribute` |
| `CopyAttribute`   | Copies an attribute value        | `sourceAttribute`, `targetAttribute` |
| `DeleteAttribute` | Deletes an attribute             | `targetAttribute`                    |
| `MapValue`        | Maps values to new values        | `targetAttribute`, `valueMapping`    |

## Filters

Filters restrict which entities the transformation is applied to:

```yaml
target:
  ckTypeId: "MyModel/Entity"
  filter:
    and:
      - attribute: status
        operator: Equals
        value: "draft"
      - attribute: createdAt
        operator: LessThan
        value: "2024-01-01"
    or:
      - attribute: priority
        operator: Equals
        value: "high"
```

### Filter Operators

| Operator      | Description          |
|---------------|----------------------|
| `Equals`      | Exact match          |
| `NotEquals`   | No match             |
| `Contains`    | Contains (String)    |
| `StartsWith`  | Starts with (String) |
| `EndsWith`    | Ends with (String)   |
| `GreaterThan` | Greater than         |
| `LessThan`    | Less than            |
| `IsNull`      | Is null              |
| `IsNotNull`   | Is not null          |

## Multi-Hop Migrations

The service automatically finds the shortest path between two versions:

```
1.0.0 → 1.1.0 → 1.2.0 → 2.0.0
```

When a migration from 1.0.0 to 2.0.0 is requested, all intermediate steps are executed.

## API Usage

### Register Service

```csharp
// In DI Configuration
services.AddRuntimeEngine();

// For MongoDB support
services.AddRuntimeEngine()
    .AddMongoDbRuntimeRepository()
    .AddMongoCkModelMigrationSupport();
```

### Execute Migration

```csharp
public class CkModelUpgradeService
{
    private readonly ICkModelMigrationService _migrationService;

    public async Task UpgradeModelAsync(string tenantId)
    {
        var fromModel = new CkModelId("MyModel", "1.0.0");
        var toModel = new CkModelId("MyModel", "2.0.0");

        // 1. Find migration path
        var path = await _migrationService.FindMigrationPathAsync(fromModel, toModel);

        if (path == null)
        {
            Console.WriteLine("No migration path found");
            return;
        }

        Console.WriteLine($"Migration steps: {path.Steps.Count}");
        Console.WriteLine($"Breaking changes: {path.HasBreakingChanges}");

        // 2. Validate
        var validation = await _migrationService.ValidateAsync(tenantId, fromModel, toModel);

        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
            {
                Console.WriteLine($"Error: {error.Message}");
            }
            return;
        }

        // 3. Execute migration
        var options = new CkMigrationOptions
        {
            CreateBackup = true,
            DryRun = false,
            ContinueOnError = false
        };

        var result = await _migrationService.MigrateAsync(
            tenantId, fromModel, toModel, options);

        if (result.Success)
        {
            Console.WriteLine($"Migration successful!");
            Console.WriteLine($"  Added: {result.EntitiesAdded}");
            Console.WriteLine($"  Updated: {result.EntitiesUpdated}");
            Console.WriteLine($"  Deleted: {result.EntitiesDeleted}");
        }
        else
        {
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"Error: {error}");
            }
        }
    }
}
```

### Query Status and History

```csharp
// Get current status
var status = await _migrationService.GetStatusAsync(tenantId, "MyModel");
Console.WriteLine($"Installed: {status.InstalledVersion}");
Console.WriteLine($"Available: {status.LatestAvailableVersion}");
Console.WriteLine($"Update available: {status.UpdateAvailable}");

// Get migration history
var history = await _migrationService.GetHistoryAsync(tenantId, "MyModel");
foreach (var entry in history)
{
    Console.WriteLine($"{entry.FromVersion} → {entry.ToVersion}: {(entry.Success ? "OK" : "FAILED")}");
}
```

## Migration Content Provider

### Embedded Resources (Recommended)

Migration files are automatically embedded as resources when located in the `ConstructionKit/migrations/` folder.

**MSBuild Property (to disable):**
```xml
<OctoEmbedCkMigrations>false</OctoEmbedCkMigrations>
```

**Resource Names:**
```
{RootNamespace}.migrations.migration-meta.yaml
{RootNamespace}.migrations.1.0.0-to-2.0.0.yaml
```

**Register Provider:**
```csharp
var embeddedProvider = serviceProvider
    .GetRequiredService<EmbeddedCkMigrationContentProvider>();

embeddedProvider.RegisterMigrationSource(
    "MyModel",
    typeof(MyModelMarker).Assembly,
    "MyCompany.MyModel");
```

### File System

For development or external migration scripts:

```csharp
var fsProvider = serviceProvider
    .GetRequiredService<FileSystemCkMigrationContentProvider>();

fsProvider.RegisterModelSourcePath(
    "MyModel",
    "/path/to/MyModel/ConstructionKit");
```

### Aggregated Provider

By default, an aggregated provider is used that first checks embedded resources, then the file system:

```csharp
// Automatically configured:
// 1. EmbeddedCkMigrationContentProvider (Priority)
// 2. FileSystemCkMigrationContentProvider (Fallback)
```

## Migration History

Migration history is stored as a `CkMigrationHistory` entity:

| Attribute          | Type     | Description                 |
|--------------------|----------|-----------------------------|
| `ckModelName`      | string   | Name of the CK model        |
| `fromVersion`      | string   | Source version              |
| `toVersion`        | string   | Target version              |
| `executedAt`       | DateTime | Execution timestamp         |
| `success`          | boolean  | Success status              |
| `entitiesAffected` | int      | Number of affected entities |
| `durationMs`       | long     | Duration in milliseconds    |
| `errors`           | string[] | Error messages (on failure) |

## Pre-Conditions and Post-Validations

```yaml
steps:
  - stepId: update-status
    action: Transform
    target:
      ckTypeId: "MyModel/Entity"

    # Check before execution
    preConditions:
      - type: EntityCount
        ckTypeId: "MyModel/Entity"
        operator: GreaterThan
        value: 0

    transform:
      type: SetValue
      targetAttribute: migrated
      value: true

    # Check after execution
    postValidations:
      - type: NoEntitiesMatch
        ckTypeId: "MyModel/Entity"
        filter:
          attribute: migrated
          operator: Equals
          value: false
```

## Best Practices

1. **Incremental Migrations**: Small steps instead of large jumps
2. **Enable Backups**: Always use `CreateBackup = true` in production
3. **Test with Dry-Run**: First validate with `DryRun = true`
4. **Mark Breaking Changes**: Use `breaking: true` for critical changes
5. **Maintain Descriptions**: Use meaningful `description` fields
6. **Use Filters**: Only transform affected entities
7. **Validate Before Execution**: Call `ValidateAsync` before `MigrateAsync`
8. **Check History**: Regularly review migration history

## Error Handling

```csharp
var result = await _migrationService.MigrateAsync(
    tenantId, fromModel, toModel,
    new CkMigrationOptions { ContinueOnError = true });

if (!result.Success)
{
    // Rollback via backup
    if (result.BackupId != null)
    {
        await _backupService.RestoreAsync(tenantId, result.BackupId);
    }

    // Log errors
    foreach (var error in result.Errors)
    {
        _logger.LogError("Migration error: {Error}", error);
    }
}
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                  ICkModelMigrationService                   │
│  (Orchestrates migrations between CK model versions)        │
└─────────────────────────────────────────────────────────────┘
                              │
         ┌────────────────────┼────────────────────┐
         ▼                    ▼                    ▼
┌─────────────────┐  ┌───────────────────┐  ┌─────────────────┐
│ICkMigration-    │  │IRuntimeRepository │  │ITenantBackup-   │
│ContentProvider  │  │Provider           │  │Service          │
└─────────────────┘  └───────────────────┘  └─────────────────┘
         │                    │
         ▼                    ▼
┌─────────────────┐  ┌───────────────────┐
│Aggregate-       │  │Mongo-/InMemory-   │
│ContentProvider  │  │RepositoryProvider │
└─────────────────┘  └───────────────────┘
    │         │
    ▼         ▼
┌────────┐ ┌────────┐
│Embedded│ │File-   │
│Resource│ │System  │
└────────┘ └────────┘
```

## Namespace Organization

CK migration classes live in dedicated namespaces, separate from Blueprints:

| Layer | Namespace | Key Types |
|-------|-----------|-----------|
| **DTOs** | `Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs.DataTransferObjects` | `CkMigrationMetaDto`, `CkMigrationScriptDto`, `CkCompiledMigrationDataDto` |
| **Contracts** | `Meshmakers.Octo.Runtime.Contracts.CkModelMigrations` | `ICkModelMigrationService`, `ICkMigrationContentProvider`, `ICkModelUpgradeService` |
| **Contracts** | `Meshmakers.Octo.Runtime.Contracts` | `IRuntimeRepositoryProvider` |
| **Engine** | `Meshmakers.Octo.Runtime.Engine.CkModelMigrations` | `CkModelMigrationService`, `CkModelUpgradeService`, content providers |
| **Parser** | `Meshmakers.Octo.ConstructionKit.Engine.Serialization` | `CkMigrationParser`, `ICkMigrationParser` |
