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
    ├── ckModel.yaml
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
ckModelId: MyModel-2.0.0

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

You only need to define migration entries for steps that require **data transformations**. The engine auto-bridges any version gaps where no data migration is needed (see [Auto-Bridging Version Gaps](#auto-bridging-version-gaps)).

### Migration Script

```yaml
$schema: https://schemas.meshmakers.cloud/ck-migration.schema.json
sourceVersion: "1.0.0"
targetVersion: "2.0.0"
description: "Migrate from v1 to v2"

preConditions:
  - type: EntityExists
    target:
      ckTypeId: "MyModel/OldTypeName"

steps:
  # Rename type
  - stepId: rename-old-type
    action: Transform
    target:
      ckTypeId: "MyModel/OldTypeName"
    transform:
      type: ChangeCkType
      newCkTypeId: "MyModel/NewTypeName"

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
        attribute: status
        operator: NotExists
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

postValidations:
  - validationId: no-old-types-remain
    description: "Ensure no entities with old type exist"
    type: NoEntitiesOfType
    target:
      ckTypeId: "MyModel/OldTypeName"
    severity: Warning
```

## Transform Types

| Type              | Description                      | Parameters                           |
|-------------------|----------------------------------|--------------------------------------|
| `ChangeCkType`    | Changes the CK type of an entity | `newCkTypeId`                        |
| `SetValue`        | Sets an attribute value           | `targetAttribute`, `value`           |
| `RenameAttribute` | Renames an attribute             | `sourceAttribute`, `targetAttribute` |
| `CopyAttribute`   | Copies an attribute value        | `sourceAttribute`, `targetAttribute` |
| `DeleteAttribute` | Deletes an attribute             | `targetAttribute`                    |
| `MapValue`        | Maps values to new values        | `targetAttribute`, `valueMapping`    |

## Target Specification

The `target` block selects which entities a step operates on:

| Field               | Description                                                    |
|---------------------|----------------------------------------------------------------|
| `ckTypeId`          | CK type ID to target (e.g., `MyModel/Widget`)                 |
| `rtId`              | Specific runtime entity ID                                     |
| `rtWellKnownName`   | Well-known name of the entity                                  |
| `filter`            | Filter expression for fine-grained selection                   |
| `blueprintSourceOnly` | If `true`, only target blueprint-created entities (default: `false`) |

## Filters

Filters restrict which entities the transformation is applied to:

```yaml
target:
  ckTypeId: "MyModel/Entity"
  filter:
    and:
      - attribute: status
        operator: Eq
        value: "draft"
      - attribute: priority
        operator: Ne
        value: "low"
```

### Filter Operators

| Operator     | Description                   |
|--------------|-------------------------------|
| `Eq`         | Exact match                   |
| `Ne`         | Not equals                    |
| `Exists`     | Attribute exists              |
| `NotExists`  | Attribute does not exist      |
| `Contains`   | Contains substring (String)   |
| `StartsWith` | Starts with prefix (String)   |

Filters support boolean combination with `and` / `or` arrays:

```yaml
filter:
  or:
    - attribute: type
      operator: Eq
      value: "legacy"
    - attribute: migrated
      operator: NotExists
```

## Conflict Behavior

Controls what happens when a migration step encounters a conflict:

| Behavior    | Description                        |
|-------------|-------------------------------------|
| `Fail`      | Stop the step on conflict (default) |
| `Skip`      | Skip the conflicting entity         |
| `Overwrite` | Overwrite with new values           |

Set on each step via `onConflict`:

```yaml
steps:
  - stepId: migrate-type
    action: Transform
    target:
      ckTypeId: "MyModel/OldType"
    transform:
      type: ChangeCkType
      newCkTypeId: "MyModel/NewType"
    onConflict: Skip
    continueOnError: true
```

## Migration Path Resolution

### Multi-Hop Migrations

The service automatically finds the shortest path between two versions using breadth-first search:

```
1.0.0 → 1.1.0 → 1.2.0 → 2.0.0
```

When a migration from 1.0.0 to 2.0.0 is requested, all intermediate steps are executed in order.

### Auto-Bridging Version Gaps

The engine automatically bridges version gaps at **both ends** of the migration chain without requiring developers to create empty migration entries:

**Start gap**: When the tenant is at an older version (e.g., `2.2.0`) than the earliest migration entry point (e.g., `3.0.1`), the engine creates a no-op bridge step. No data migration is needed because the tenant's data is compatible with the start of the chain.

**End gap**: When the migration chain doesn't reach the exact target version (e.g., chain ends at `3.1.1` but new model is `3.1.2`), the engine executes all available migrations and treats the remaining version bump as schema-only.

**No-migrations bridge**: When a CK model defines no migration scripts at all but the target version is strictly greater than the installed version, the entire `fromVersion → toVersion` jump is treated as a single schema-only no-op step. This lets a CK model ship a purely additive version bump (e.g., adding a new type) without authoring an empty `migration-meta.yaml` + tombstone script.

**Example**: A tenant at version `2.2.0` receiving model version `3.1.2` with migrations defined from `3.0.1` to `3.1.1`:

```
2.2.0 → 3.0.1  (auto-bridge, no-op — no data migration needed)
3.0.1 → 3.0.2  (migration script executed)
3.0.2 → 3.0.3  (migration script executed)
3.0.3 → 3.1.0  (migration script executed)
3.1.0 → 3.1.1  (migration script executed)
3.1.1 → 3.1.2  (auto-bridge, schema-only — no data migration needed)
```

This eliminates the need to maintain empty migration entries for every version bump. Only create migration scripts for versions that actually transform data.

### Path Resolution Order

The engine resolves migration paths in this order:

1. **Direct migration** — exact `fromVersion → toVersion` match
2. **Multi-hop path** — BFS through the migration chain to the exact target
3. **Auto-bridged path** — bridge version gaps at start and/or end of chain
4. **Partial path** — direct migration to the closest reachable version
5. **No-migrations bridge** — model defines no migration scripts at all; if `toVersion > fromVersion`, returned as a single schema-only no-op step
6. **No path** — only reached for same-version or downgrade cases without scripts; surfaced as "No migration path found"

## Pre-Conditions

Conditions that must be met before the migration runs. Defined at the **script level** (not per step). If a precondition is not met, the migration step is skipped.

| Type                     | Description                               | Parameters              |
|--------------------------|-------------------------------------------|-------------------------|
| `EntityExists`           | Entities matching target must exist       | `target`                |
| `EntityNotExists`        | No entities matching target may exist     | `target`                |
| `CkModelVersionInstalled`| A specific CK model version is installed  | `ckModelId`, `version`  |
| `AttributeEquals`        | An attribute has a specific value         | `target`, `attribute`, `value` |

## Post-Validations

Validations run after all migration steps complete. Defined at the **script level**.

| Type               | Description                            | Parameters              |
|--------------------|----------------------------------------|-------------------------|
| `EntityExists`     | Verify entities of a type exist        | `target`                |
| `NoEntitiesOfType` | Verify no entities of old type remain  | `target`                |
| `EntityCount`      | Verify entity count matches expected   | `target`, `expectedCount` |

Each validation has a `severity`:
- `Error` — fails the migration (default)
- `Warning` — logged but migration continues

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
public class MigrationExample
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
        Console.WriteLine($"Partial path: {path.IsPartialPath}");

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

## Migration Content Providers

### Embedded Resources (Recommended)

Migration files are automatically embedded as resources when located in the `ConstructionKit/migrations/` folder. Controlled by MSBuild property `OctoEmbedCkMigrations` (default: `true`).

**To disable:**
```xml
<OctoEmbedCkMigrations>false</OctoEmbedCkMigrations>
```

**Resource naming convention:**
```
{RootNamespace}.migrations.migration-meta.yaml
{RootNamespace}.migrations.1.0.0-to-2.0.0.yaml
```

**Register provider:**
```csharp
var embeddedProvider = serviceProvider
    .GetRequiredService<EmbeddedCkMigrationContentProvider>();

embeddedProvider.RegisterMigrationSource(
    "MyModel",
    typeof(MyModelMarker).Assembly,
    "MyCompany.MyModel");
```

### Compiled Model

When the CK compiler finds a `migrations/` folder with a `migration-meta.yaml`, it embeds all migration metadata and scripts inline into the compiled `.yaml` model file. This makes compiled models self-contained.

At runtime, the CK model import pipeline must call `SetMigrationData()` on the `CompiledModelCkMigrationContentProvider`:

```csharp
compiledProvider.SetMigrationData(ckModelId, compiledModel.Migrations);
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

By default, an aggregated provider chains three sources in priority order:

```csharp
// Automatically configured:
// 1. CompiledModelCkMigrationContentProvider (auto-populated during import)
// 2. EmbeddedCkMigrationContentProvider (NuGet package references)
// 3. FileSystemCkMigrationContentProvider (local dev fallback)
```

## Migration History

Migration history is stored as `System/MigrationHistory` runtime entities:

| Attribute          | Type     | Description                 |
|--------------------|----------|-----------------------------|
| `CkModelName`      | string   | Name of the CK model        |
| `FromVersion`      | string   | Source version              |
| `ToVersion`        | string   | Target version              |
| `ExecutedAt`       | DateTime | Execution timestamp         |
| `Success`          | boolean  | Success status              |
| `DurationMs`       | long     | Duration in milliseconds    |
| `EntitiesAdded`    | int      | Number of added entities    |
| `EntitiesUpdated`  | int      | Number of updated entities  |
| `EntitiesDeleted`  | int      | Number of deleted entities  |
| `Errors`           | string[] | Error messages (on failure) |

## Migration Options

| Option            | Default | Description                                    |
|-------------------|---------|------------------------------------------------|
| `DryRun`          | `false` | Simulate migration without making changes      |
| `CreateBackup`    | `false` | Create a tenant backup before migrating        |
| `ContinueOnError` | `false` | Continue with next step if a step fails        |

## Best Practices

1. **Incremental migrations**: Small steps instead of large jumps
2. **Enable backups**: Always use `CreateBackup = true` in production
3. **Test with dry-run**: First validate with `DryRun = true`
4. **Mark breaking changes**: Use `breaking: true` for critical changes
5. **No empty migration entries needed**: The engine auto-bridges version gaps — only create migration scripts for steps that actually transform data
6. **Use `continueOnError: true`** for steps that may partially apply (e.g., merging subtypes where some may not exist)
7. **Add post-validations with `severity: Warning`** to verify results without blocking
8. **Keep migration chains linear**: Each `fromVersion` should appear at most once in the chain

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
│                  ICkModelMigrationService                    │
│  (Orchestrates migrations between CK model versions)        │
│                                                             │
│  Path Resolution:                                           │
│    Direct → Multi-Hop → Auto-Bridge → Partial → No Path    │
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
    │    │    │
    ▼    ▼    ▼
┌──────┐┌──────┐┌──────┐
│Compi-││Embed-││File- │
│led   ││ded   ││System│
│Model ││Rsrc  ││      │
└──────┘└──────┘└──────┘
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
