# Blueprint Update Feature - Implementation Plan

## Overview

Enable updating tenants with new blueprint versions while preserving user modifications.

**Core Principles:**
- Track which blueprint version was applied to a tenant
- Tag entities with their origin (blueprint vs. user-created)
- Support explicit migration scripts for complex updates
- Provide different update modes for different use cases

---

## Phase 1: Data Model Extensions

### 1.1 Tenant Blueprint Tracking

**New file:** `src/Runtime.Contracts/Blueprints/TenantBlueprintInfo.cs`

```csharp
public class TenantBlueprintInfo
{
    public required BlueprintId BlueprintId { get; set; }
    public required DateTime AppliedAt { get; set; }
    public string? SeedDataChecksum { get; set; }
    public BlueprintApplicationMode ApplicationMode { get; set; }
}

public enum BlueprintApplicationMode
{
    Initial,      // First-time application
    Update,       // Update from previous version
    Migration     // Migration script was used
}
```

### 1.2 Entity Source Tracking

**Extend System CK Model:** Add attributes to track entity origin

```yaml
# In System-2.x.x/attributes/
ckAttributeId: RtBlueprintSource
attributeName: rtBlueprintSource
valueType: String
description: "Blueprint ID that created this entity (e.g., 'Infrastructure-1.0.0')"

ckAttributeId: RtBlueprintLocked
attributeName: rtBlueprintLocked
valueType: Boolean
description: "If true, entity is managed by blueprint and will be updated"
defaultValue: false
```

### 1.3 Blueprint History per Tenant

**Storage:** Add to tenant metadata or separate collection

```csharp
public interface ITenantBlueprintHistory
{
    Task<IReadOnlyList<TenantBlueprintInfo>> GetHistoryAsync(string tenantId);
    Task AddEntryAsync(string tenantId, TenantBlueprintInfo info);
    Task<TenantBlueprintInfo?> GetCurrentAsync(string tenantId);
}
```

---

## Phase 2: Migration Script Support

### 2.1 Migration Schema

**New file:** `src/ConstructionKit.Contracts/BlueprintCatalogs/DataTransferObjects/BlueprintMigrationDto.cs`

```csharp
public class BlueprintMigrationDto
{
    public required string FromVersion { get; set; }
    public required string ToVersion { get; set; }
    public List<MigrationOperationDto> Operations { get; set; } = [];
}

public class MigrationOperationDto
{
    public required MigrationOperationType Type { get; set; }
    public EntitySelectorDto? Selector { get; set; }
    public JsonElement? Entity { get; set; }
    public JsonElement? Attributes { get; set; }
}

public enum MigrationOperationType
{
    AddEntity,
    UpdateEntity,
    DeleteEntity,
    AddAttribute,
    UpdateAttribute,
    DeleteAttribute,
    RunScript  // For complex migrations
}

public class EntitySelectorDto
{
    public string? RtId { get; set; }
    public string? RtWellKnownName { get; set; }
    public string? CkTypeId { get; set; }
    public Dictionary<string, object>? AttributeFilters { get; set; }
}
```

### 2.2 Migration JSON Schema

**New file:** `blueprint-migration.schema.json`

```json
{
  "$id": "https://schemas.meshmakers.cloud/blueprint-migration.schema.json",
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "title": "Blueprint Migration",
  "type": "object",
  "properties": {
    "fromVersion": { "type": "string", "pattern": "^[0-9]+\\.[0-9]+\\.[0-9]+$" },
    "toVersion": { "type": "string", "pattern": "^[0-9]+\\.[0-9]+\\.[0-9]+$" },
    "operations": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "type": { "enum": ["addEntity", "updateEntity", "deleteEntity", "addAttribute", "updateAttribute", "deleteAttribute"] },
          "selector": { "$ref": "#/definitions/EntitySelector" },
          "entity": { "type": "object" },
          "attributes": { "type": "array" }
        },
        "required": ["type"]
      }
    }
  },
  "required": ["fromVersion", "toVersion", "operations"]
}
```

### 2.3 Extended Blueprint Meta

**Update:** `BlueprintMetaRootDto.cs`

```csharp
public class BlueprintMetaRootDto
{
    // ... existing properties ...

    /// <summary>
    /// Migration scripts from previous versions
    /// </summary>
    public List<BlueprintMigrationReferenceDto>? Migrations { get; set; }
}

public class BlueprintMigrationReferenceDto
{
    public required string FromVersion { get; set; }
    public required string ScriptPath { get; set; }  // Relative path to migration YAML
}
```

---

## Phase 3: Update Service

### 3.1 Update Modes

```csharp
public enum BlueprintUpdateMode
{
    /// <summary>
    /// Only add new entities, never modify existing
    /// </summary>
    Safe,

    /// <summary>
    /// Add new entities + update blueprint-managed entities (rtBlueprintLocked=true)
    /// </summary>
    Merge,

    /// <summary>
    /// Full update: add, update, delete according to new blueprint
    /// User modifications to blueprint entities are lost
    /// </summary>
    Full,

    /// <summary>
    /// Use explicit migration script
    /// </summary>
    Migration
}
```

### 3.2 Update Service Interface

**New file:** `src/Runtime.Contracts/Blueprints/IBlueprintUpdateService.cs`

```csharp
public interface IBlueprintUpdateService
{
    /// <summary>
    /// Check if an update is available for the tenant's blueprint
    /// </summary>
    Task<BlueprintUpdateInfo?> CheckForUpdateAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Preview what changes would be made by an update
    /// </summary>
    Task<BlueprintUpdatePreview> PreviewUpdateAsync(
        string tenantId,
        BlueprintId targetVersion,
        BlueprintUpdateMode mode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply a blueprint update to a tenant
    /// </summary>
    Task<BlueprintUpdateResult> UpdateAsync(
        string tenantId,
        BlueprintId targetVersion,
        BlueprintUpdateMode mode,
        BlueprintUpdateOptions? options = null,
        CancellationToken cancellationToken = default);
}

public class BlueprintUpdateInfo
{
    public required BlueprintId CurrentVersion { get; set; }
    public required List<BlueprintId> AvailableVersions { get; set; }
    public BlueprintId? RecommendedVersion { get; set; }
    public bool HasMigrationPath { get; set; }
}

public class BlueprintUpdatePreview
{
    public int EntitiesToAdd { get; set; }
    public int EntitiesToUpdate { get; set; }
    public int EntitiesToDelete { get; set; }
    public List<UpdateConflict> Conflicts { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public class UpdateConflict
{
    public required string EntityId { get; set; }
    public required string Description { get; set; }
    public ConflictResolution SuggestedResolution { get; set; }
}

public enum ConflictResolution
{
    KeepUser,
    KeepBlueprint,
    Merge,
    Skip
}

public class BlueprintUpdateOptions
{
    public bool DryRun { get; set; } = false;
    public bool CreateBackup { get; set; } = true;
    public Dictionary<string, ConflictResolution>? ConflictResolutions { get; set; }
}

public class BlueprintUpdateResult
{
    public bool Success { get; set; }
    public int EntitiesAdded { get; set; }
    public int EntitiesUpdated { get; set; }
    public int EntitiesDeleted { get; set; }
    public List<string> Errors { get; set; } = [];
    public string? BackupId { get; set; }
}
```

### 3.3 Update Service Implementation

**New file:** `src/Runtime.Engine/Blueprints/BlueprintUpdateService.cs`

Key methods:
1. `CheckForUpdateAsync` - Query catalog for newer versions
2. `PreviewUpdateAsync` - Compute diff without applying
3. `UpdateAsync` - Apply changes with selected mode
4. `ApplyMigrationAsync` - Execute migration script
5. `DetectConflictsAsync` - Find user-modified blueprint entities

---

## Phase 4: CLI Commands

### 4.1 New Commands

| Command | Description |
|---------|-------------|
| `blueprint-status -t <tenant>` | Show current blueprint version and available updates |
| `blueprint-update -t <tenant> -v <version> -m <mode>` | Apply update |
| `blueprint-preview -t <tenant> -v <version>` | Preview changes |
| `blueprint-history -t <tenant>` | Show blueprint application history |
| `blueprint-rollback -t <tenant> -b <backupId>` | Restore from backup |

### 4.2 Command Implementations

```csharp
// BlueprintStatusCommand.cs
internal class BlueprintStatusCommand : Command<OctoToolOptions>
{
    private readonly IBlueprintUpdateService _updateService;
    private readonly IArgument _tenantArg;

    public override async Task Execute()
    {
        var tenantId = CommandArgumentValue.GetArgumentScalarValue<string>(_tenantArg);
        var updateInfo = await _updateService.CheckForUpdateAsync(tenantId);

        Logger.LogInformation("Current version: {Version}", updateInfo?.CurrentVersion);
        Logger.LogInformation("Available updates: {Count}", updateInfo?.AvailableVersions.Count);
        // ...
    }
}
```

---

## Phase 5: Integration

### 5.1 Modify Initial Application

Update `BlueprintService.ApplyBlueprintAsync` to:
1. Tag all created entities with `rtBlueprintSource`
2. Record application in tenant history
3. Calculate and store seed data checksum

### 5.2 Conflict Detection Algorithm

```
For each entity in new blueprint:
  1. Find matching entity in tenant (by rtWellKnownName or rtId)
  2. If not found → Add (no conflict)
  3. If found:
     a. Check rtBlueprintSource matches current blueprint
     b. Compare current values with original blueprint values
     c. If changed by user → Conflict
     d. If unchanged → Safe to update
```

### 5.3 Backup System

Before updates, create a snapshot:
```csharp
public interface ITenantBackupService
{
    Task<string> CreateBackupAsync(string tenantId, string reason);
    Task RestoreBackupAsync(string tenantId, string backupId);
    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(string tenantId);
}
```

---

## Implementation Order

### Sprint 1: Foundation
- [ ] Add `rtBlueprintSource` and `rtBlueprintLocked` to System CK Model
- [ ] Implement `ITenantBlueprintHistory`
- [ ] Modify `BlueprintService` to tag entities and record history
- [ ] Add `blueprint-migration.schema.json`

### Sprint 2: Update Service Core
- [ ] Implement `IBlueprintUpdateService` interface
- [ ] Implement `BlueprintUpdateService` with Safe mode
- [ ] Add conflict detection algorithm
- [ ] Add preview functionality

### Sprint 3: Migration Support
- [ ] Extend `BlueprintMetaRootDto` with migrations
- [ ] Implement migration script parser
- [ ] Implement migration executor
- [ ] Add Merge and Full update modes

### Sprint 4: CLI & Polish
- [ ] Add CLI commands
- [ ] Implement backup/restore
- [ ] Add comprehensive logging
- [ ] Write documentation

### Sprint 5: Testing
- [ ] Unit tests for update service
- [ ] Integration tests for migrations
- [ ] E2E tests for CLI commands
- [ ] Test conflict resolution scenarios

---

## Example Usage

### Blueprint with Migrations

```yaml
# Infrastructure-2.0.0/blueprint.yaml
$schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
blueprintId: Infrastructure-2.0.0
description: "Infrastructure management with new monitoring features"

ckModelDependencies:
  - System-[2.0,)
  - Monitoring-[1.0,)  # New dependency

migrations:
  - fromVersion: "1.0.0"
    scriptPath: "migrations/from-1.0.0.yaml"
  - fromVersion: "1.1.0"
    scriptPath: "migrations/from-1.1.0.yaml"

seedDataPath: "seed-data/entities.yaml"
```

```yaml
# Infrastructure-2.0.0/migrations/from-1.0.0.yaml
fromVersion: "1.0.0"
toVersion: "2.0.0"
operations:
  # Add new monitoring entity
  - type: addEntity
    entity:
      ckTypeId: Monitoring/Dashboard
      rtWellKnownName: DefaultDashboard
      attributes:
        - name: Title
          value: "System Overview"

  # Update existing location with new field
  - type: updateAttribute
    selector:
      rtWellKnownName: HeadquartersLocation
    attributes:
      - name: MonitoringEnabled
        value: true

  # Remove deprecated entity
  - type: deleteEntity
    selector:
      rtWellKnownName: LegacyConfig
```

### CLI Usage

```bash
# Check for updates
octo-ckc blueprint-status -t my-tenant

# Preview update
octo-ckc blueprint-preview -t my-tenant -v Infrastructure-2.0.0

# Apply update with merge mode
octo-ckc blueprint-update -t my-tenant -v Infrastructure-2.0.0 -m merge

# Rollback if needed
octo-ckc blueprint-rollback -t my-tenant -b backup-2024-01-15
```

---

## Open Questions

1. **Versioning Strategy**: Should we enforce semantic versioning for blueprints?
2. **Multi-Blueprint Tenants**: Can a tenant have multiple blueprints applied?
3. **Dependency Updates**: How to handle CK model dependency changes?
4. **Audit Trail**: Should we log all changes for compliance?
5. **Rollback Scope**: Full rollback or selective undo?
