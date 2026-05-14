# Blueprints

Blueprints initialize a tenant with pre-configured Construction Kit models and runtime data. They enable a quick start without a "cold start" problem.

## Properties

| Property            | Description                                            |
|---------------------|--------------------------------------------------------|
| **One-time**        | Applied only during tenant creation                    |
| **Modifiable**      | All generated models are editable after initialization |
| **Non-destructive** | No dependency on the blueprint after setup             |
| **Composable**      | Blueprints can reference and merge other blueprints    |

## Blueprint Structure

A blueprint consists of a `blueprint.yaml` file and optional seed data:

```
MyBlueprint-1.0.0/
├── blueprint.yaml          # Blueprint metadata
└── seed-data/
    └── initial-entities.yaml   # Optional RT-Model seed data
```

## Blueprint YAML Schema

```yaml
$schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
blueprintId: InfrastructureStarter-1.0.0
description: Infrastructure management starter blueprint

# CK model dependencies (loaded when applying)
ckModelDependencies:
  - System-[2.0,)
  - Commerce-[1.0,2.0)

# Path to seed data (relative to blueprint root)
seedDataPath: seed-data/initial-entities.yaml
```

### Fields

| Field                 | Type     | Description                                        |
|-----------------------|----------|----------------------------------------------------|
| `$schema`             | string   | Schema URI for validation                          |
| `blueprintId`         | string   | Unique ID with version (e.g., `MyBlueprint-1.0.0`) |
| `description`         | string   | Description of the blueprint                       |
| `ckModelDependencies` | string[] | List of required CK models with version range      |
| `seedDataPath`        | string   | Relative path to seed data (optional)              |

## Version Ranges

Blueprints and CK model dependencies support semantic version ranges:

| Format          | Meaning                      |
|-----------------|------------------------------|
| `1.0.0`         | Exact version                |
| `[1.0.0,)`      | Version 1.0.0 or higher      |
| `[1.0.0,2.0.0)` | Version >= 1.0.0 and < 2.0.0 |
| `(1.0.0,2.0.0]` | Version > 1.0.0 and <= 2.0.0 |
| `[1.5.0]`       | Exactly version 1.5.0        |

## Blueprint Application Flow

```
ApplyBlueprintAsync(tenantId, blueprintId)
│
├── 1. Fetch blueprint from catalog
│
├── 2. CreateTenant(tenantId) if not exists
│
├── 3. Load CK models
│       ├── HardResolveDependenciesAsync()
│       └── LoadCkModelGraph(tenantId, graph)
│
└── 4. Apply seed data
        ├── LoadSeedDataAsync(path)
        └── ImportModelAsync(repository, rtModel, Upsert)
```

## API Usage

### Configure Blueprint Catalog

```csharp
// In DI configuration
services.AddBlueprintCatalogs(options =>
{
    options.AddLocalFileSystemCatalog("/path/to/blueprints");
});
```

### Apply Blueprint to Tenant

```csharp
public class TenantSetupService
{
    private readonly IBlueprintService _blueprintService;

    public async Task SetupTenantAsync(string tenantId, string blueprintName)
    {
        var blueprintId = new BlueprintIdVersionRange(blueprintName, "[1.0,)");
        var result = await _blueprintService.ApplyBlueprintAsync(
            tenantId,
            blueprintId,
            cancellationToken);

        if (!result.Success)
        {
            // Error handling
        }
    }
}
```

## Seed Data Format

Seed data are RT-Model YAML files:

```yaml
$schema: https://schemas.meshmakers.cloud/rt-model-root.schema.json
dependencies:
  - System-2.0.0
entities:
  - rtId: "507f1f77bcf86cd799439011"
    ckTypeId: "System-2.0.0/Entity"
    rtWellKnownName: "InitialEntity"
    attributes:
      - name: Name
        value: "My Initial Entity"
      - name: Description
        value: "Created by blueprint"
```

Seed data is applied with **upsert strategy**:
- Existing entities are updated
- New entities are created

## Catalog Types

| Catalog                            | Description                                        |
|------------------------------------|----------------------------------------------------|
| `LocalFileSystemBlueprintCatalog`  | Loads blueprints from the file system              |
| `EmbeddedResourceBlueprintCatalog` | Loads blueprints from assembly resources           |
| `PublicGitHubBlueprintCatalog`     | Loads blueprints from public GitHub Pages          |
| `PrivateGitHubBlueprintCatalog`    | Loads blueprints from private GitHub repositories  |

## GitHub Blueprint Hosting

Blueprints can be hosted on GitHub Pages for easy distribution.

### Repository Structure

```
blueprints/v1/
├── catalog.json                           # Root catalog index
├── i/
│   └── InfrastructureStarter/
│       ├── catalog.json                   # Blueprint library catalog
│       └── 1/
│           ├── catalog.json               # Version catalog
│           └── InfrastructureStarter-1.0.0/
│               ├── blueprint.yaml
│               └── seed-data/
└── e/
    └── ECommerce/
        └── ...
```

### Catalog Index Format

**Root Catalog (`blueprints/v1/catalog.json`):**
```json
{
  "version": "1.0",
  "updatedAt": "2025-01-15T10:30:00Z",
  "blueprints": [
    {
      "blueprintName": "InfrastructureStarter",
      "catalogPath": "blueprints/v1/i/InfrastructureStarter/catalog.json"
    }
  ]
}
```

**Blueprint Library Catalog:**
```json
{
  "version": "1.0",
  "blueprintId": "InfrastructureStarter",
  "majorVersions": [
    { "majorVersion": 1, "catalogPath": "blueprints/v1/i/InfrastructureStarter/1/catalog.json" }
  ],
  "updatedAt": "2025-01-15T10:30:00Z"
}
```

**Version Catalog:**
```json
{
  "version": "1.0",
  "blueprintId": "InfrastructureStarter",
  "majorVersion": 1,
  "latestVersion": "1.0.0",
  "description": "Infrastructure starter blueprint",
  "versions": [
    {
      "version": "1.0.0",
      "directoryPath": "blueprints/v1/i/InfrastructureStarter/1/InfrastructureStarter-1.0.0",
      "publishedAt": "2025-01-15T10:30:00Z"
    }
  ],
  "updatedAt": "2025-01-15T10:30:00Z"
}
```

### Configuration

```csharp
// Configure public GitHub catalog
services.Configure<PublicGitHubBlueprintCatalogOptions>(options =>
{
    options.GitHubPagesUri = "https://meshmakers.github.io/";
});

// Configure private GitHub catalog (requires API token for write access)
services.Configure<PrivateGitHubBlueprintCatalogOptions>(options =>
{
    options.GitHubPagesUri = "https://meshmakers.github.io/blueprint-libraries-build/";
    options.GitHubApiToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
});
```

## Architecture Components

```
┌─────────────────────────────────────────────────────────────┐
│                     IBlueprintService                       │
│  (Orchestrates blueprint application to tenants)            │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                 IBlueprintCatalogManager                    │
│  (Manages multiple blueprint catalogs)                      │
└─────────────────────────────────────────────────────────────┘
                              │
     ┌────────────┬───────────┴───────────┬────────────┐
     ▼            ▼                       ▼            ▼
┌──────────┐ ┌──────────┐           ┌──────────┐ ┌──────────┐
│LocalFile │ │Embedded  │           │PublicGH  │ │PrivateGH │
│ System   │ │Resource  │           │Blueprint │ │Blueprint │
└──────────┘ └──────────┘           └──────────┘ └──────────┘
```

## Blueprint Updates

Blueprints support updating tenants to newer versions while preserving user modifications.

### Update Modes

| Mode | Description |
|------|-------------|
| `Safe` | Only add new entities, never modify existing ones |
| `Merge` | Add new entities + update blueprint-managed entities (`rtBlueprintLocked=true`) |
| `Full` | Full sync: add, update, delete according to new blueprint (user modifications lost) |
| `Migration` | Use explicit migration script for complex updates |

### Entity Source Tracking

When a blueprint is applied, entities are tagged with metadata:

| Attribute | Type | Description |
|-----------|------|-------------|
| `rtBlueprintSource` | string | Blueprint ID that created the entity (e.g., `Infrastructure-1.0.0`) |
| `rtBlueprintLocked` | boolean | If `true`, entity is managed by blueprint and will be updated |
| `rtBlueprintAppliedAt` | DateTime | When the blueprint was applied |

### Update API

```csharp
public class BlueprintUpdateService
{
    private readonly IBlueprintService _blueprintService;

    public async Task UpdateTenantAsync(string tenantId)
    {
        // 1. Check for available updates
        var updateInfo = await _blueprintService.GetUpdateInfoAsync(tenantId);

        if (updateInfo?.RecommendedVersion == null)
        {
            Console.WriteLine("No updates available");
            return;
        }

        // 2. Preview the update
        var preview = await _blueprintService.PreviewUpdateAsync(
            tenantId,
            updateInfo.RecommendedVersion,
            BlueprintUpdateMode.Merge);

        Console.WriteLine($"Entities to add: {preview.EntitiesToAdd}");
        Console.WriteLine($"Entities to update: {preview.EntitiesToUpdate}");
        Console.WriteLine($"Conflicts: {preview.Conflicts.Count}");

        // 3. Apply the update
        var options = new BlueprintUpdateOptions
        {
            CreateBackup = true,
            DryRun = false
        };

        var result = await _blueprintService.ApplyUpdateAsync(
            tenantId,
            updateInfo.RecommendedVersion,
            BlueprintUpdateMode.Merge,
            options);

        if (result.Success)
        {
            Console.WriteLine($"Update successful! Backup: {result.BackupId}");
        }
    }
}
```

### Migration Scripts

For complex updates, use explicit migration scripts:

```yaml
# MyBlueprint-2.0.0/migrations/from-1.0.0.yaml
$schema: https://schemas.meshmakers.cloud/blueprint-migration.schema.json
migrationId: MyBlueprint-1.0.0-to-2.0.0
fromVersion: "1.0.0"
toVersion: "2.0.0"
description: "Migration from v1 to v2"

steps:
  # Add new entity
  - stepId: add-dashboard
    action: Add
    target:
      ckTypeId: System/Entity
      rtWellKnownName: NewDashboard
    attributes:
      Name: "Dashboard"
      Description: "Added in v2"

  # Update existing entity
  - stepId: update-config
    action: Update
    target:
      rtWellKnownName: MainConfig
    attributes:
      Version: "2.0"

  # Delete deprecated entity
  - stepId: remove-legacy
    action: Delete
    target:
      rtWellKnownName: LegacyConfig
```

Reference migrations in your blueprint:

```yaml
# MyBlueprint-2.0.0/blueprint.yaml
$schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
blueprintId: MyBlueprint-2.0.0

migrations:
  - fromVersion: "1.0.0"
    scriptPath: "migrations/from-1.0.0.yaml"
  - fromVersion: "1.5.0"
    scriptPath: "migrations/from-1.5.0.yaml"
```

### Backup and Restore

Updates automatically create backups (unless disabled):

```csharp
public class BackupService
{
    private readonly ITenantBackupService _backupService;

    public async Task ManageBackupsAsync(string tenantId)
    {
        // List all backups
        var backups = await _backupService.ListBackupsAsync(tenantId);

        foreach (var backup in backups)
        {
            Console.WriteLine($"{backup.BackupId}: {backup.Reason} ({backup.CreatedAt})");
        }

        // Restore from backup
        var result = await _backupService.RestoreBackupAsync(
            tenantId,
            backups.First().BackupId);

        if (result.Success)
        {
            Console.WriteLine($"Restored {result.EntitiesRestored} entities");
        }
    }
}
```

### Conflict Resolution

When updating, conflicts can occur if users modified blueprint-managed entities:

| Resolution | Description |
|------------|-------------|
| `KeepUser` | Keep user's modifications, skip blueprint update |
| `KeepBlueprint` | Overwrite with blueprint values |
| `Merge` | Attempt to merge both changes |
| `Skip` | Skip entity entirely |

```csharp
var options = new BlueprintUpdateOptions
{
    ConflictResolutions = new Dictionary<string, ConflictResolution>
    {
        ["entity-123"] = ConflictResolution.KeepUser,
        ["entity-456"] = ConflictResolution.KeepBlueprint
    }
};
```

### Blueprint History

Track all blueprint applications per tenant:

```csharp
var history = await _blueprintService.GetHistoryAsync(tenantId);

foreach (var entry in history)
{
    Console.WriteLine($"{entry.BlueprintId} applied at {entry.AppliedAt}");
    Console.WriteLine($"  Mode: {entry.ApplicationMode}");
    Console.WriteLine($"  Changes: +{entry.EntitiesCreated} ~{entry.EntitiesUpdated} -{entry.EntitiesDeleted}");
}
```

## CLI Tool: octo-bpm

The `octo-bpm` (Blueprint Manager) is a dedicated CLI tool for managing blueprints. It provides a streamlined interface for all blueprint operations.

### Installation

```bash
# Install as global tool
dotnet tool install -g Meshmakers.Octo.BlueprintManager
```

### Configuration

Before using GitHub catalogs, configure your API token:

```bash
# Configure private GitHub API token
octo-bpm -c config --privateGitHubApiToken "your-github-token"

# Configure public GitHub API token (if needed for publishing)
octo-bpm -c config --publicGitHubApiToken "your-github-token"

# Configure local catalog path
octo-bpm -c config --localCatalogPath "/path/to/blueprints"
```

### Blueprint Management Commands

| Command | Description |
|---------|-------------|
| `new` | Create a new blueprint project |
| `validate` | Validate blueprint structure |
| `pack` | Package blueprint for distribution |
| `list` | List available blueprints in catalogs |
| `version` | Display tool version |

### Catalog Commands

| Command | Description |
|---------|-------------|
| `catalogs` | List available blueprint catalogs |
| `get` | Get a blueprint from a catalog |
| `publish` | Publish a blueprint to a catalog |
| `config` | Configure tool settings (API tokens, paths) |

### Blueprint Update Commands

| Command | Description |
|---------|-------------|
| `status -t <tenant>` | Show current version and available updates |
| `preview -t <tenant> -b <blueprint>` | Preview changes before applying |
| `update -t <tenant> -b <blueprint> [-m <mode>]` | Apply update |
| `history -t <tenant>` | Show application history |

### Usage Examples

```bash
# List available catalogs
octo-bpm -c catalogs

# Create a new blueprint
octo-bpm -c new -p ./blueprints -n MyBlueprint -v 1.0.0

# Validate a blueprint
octo-bpm -c validate -p ./blueprints/MyBlueprint-1.0.0

# List blueprints in catalogs
octo-bpm -c list

# Search for blueprints
octo-bpm -c list -s "Infrastructure"

# Get a blueprint from catalog
octo-bpm -c get -b MyBlueprint-1.0.0

# Get and copy to output directory
octo-bpm -c get -b MyBlueprint-1.0.0 -o ./output

# Publish to local catalog (default)
octo-bpm -c publish -p ./blueprints/MyBlueprint-1.0.0

# Publish to private GitHub catalog
octo-bpm -c publish -p ./blueprints/MyBlueprint-1.0.0 -c PrivateGitHubBlueprintCatalog

# Publish with force (overwrite existing)
octo-bpm -c publish -p ./blueprints/MyBlueprint-1.0.0 -c PrivateGitHubBlueprintCatalog -f

# Check current blueprint status for tenant
octo-bpm -c status -t my-tenant

# Preview update to version 2.0.0
octo-bpm -c preview -t my-tenant -b MyBlueprint-2.0.0

# Apply update with merge mode (default)
octo-bpm -c update -t my-tenant -b MyBlueprint-2.0.0

# Apply update with safe mode (only add, never modify)
octo-bpm -c update -t my-tenant -b MyBlueprint-2.0.0 -m Safe

# Dry-run (preview without applying)
octo-bpm -c update -t my-tenant -b MyBlueprint-2.0.0 --dry-run

# Skip backup creation
octo-bpm -c update -t my-tenant -b MyBlueprint-2.0.0 --no-backup

# Force update despite conflicts
octo-bpm -c update -t my-tenant -b MyBlueprint-2.0.0 --force

# Show blueprint history
octo-bpm -c history -t my-tenant -l 20

# Pack blueprint for distribution
octo-bpm -c pack -p ./blueprints/MyBlueprint-1.0.0 -o ./dist
```

### Publishing to GitHub

Publishing blueprints to GitHub catalogs requires:

1. **GitHub API Token**: Configure via `config` command
2. **Repository Access**: Write access to the target repository

```bash
# 1. Configure API token
octo-bpm -c config --privateGitHubApiToken "ghp_your_token_here"

# 2. Publish blueprint
octo-bpm -c publish -p ./MyBlueprint-1.0.0 -c PrivateGitHubBlueprintCatalog

# 3. Force overwrite existing version
octo-bpm -c publish -p ./MyBlueprint-1.0.0 -c PrivateGitHubBlueprintCatalog -f
```

The publish command will:
- Upload all blueprint files to the GitHub repository
- Update the version catalog (`catalog.json`)
- Update the blueprint library catalog
- Update the root catalog

## Best Practices

1. **Small, focused blueprints**: One blueprint per domain/feature
2. **Use composition**: Extract common base entities into separate blueprints
3. **Version ranges**: Use `[1.0,)` instead of exact versions for flexibility
4. **Sparse seed data**: Only essential startup data, no test data
5. **Documentation**: Always fill in the `description` field
6. **Migration scripts**: Always provide migration scripts for breaking changes
7. **Lock managed entities**: Set `rtBlueprintLocked=true` for entities that should be updated
8. **Test updates**: Use `--dry-run` before applying updates in production
9. **Backup strategy**: Keep backups for rollback capability
