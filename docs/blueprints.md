# Blueprints

Blueprints are versioned, declarative bundles of Construction Kit models and runtime seed data that bootstrap a tenant — and continue to manage it. They are not a one-shot bootstrap mechanism: a blueprint can be updated, rolled back, uninstalled, depend on other blueprints, and ship migration scripts that transform tenant data when its own version moves forward.

## Properties

| Property             | Description                                                                                  |
|----------------------|----------------------------------------------------------------------------------------------|
| **Versioned**        | SemVer (`MyBlueprint-1.2.3`). Version ranges express compatibility, like CK models.          |
| **Dependency-aware** | A blueprint may depend on other blueprints, resolved transitively at install time.           |
| **Owner-tracked**    | Every seed entity is tagged with `rtBlueprintSource` and `rtBlueprintLocked`.                |
| **Updatable**        | Tenants are moved to newer versions via Safe / Merge / Full / Migration modes.               |
| **Rollback-able**    | Destructive operations create a tenant backup; `Rollback` restores the snapshot.             |
| **Multi-install**    | A tenant can host several blueprints concurrently. Refcounted, cascade-uninstall optional.   |

## Blueprint Structure

A blueprint is a directory containing a `blueprint.yaml`, optional seed data, and optional migration scripts:

```
MyBlueprint-1.0.0/
├── blueprint.yaml
├── seed-data/
│   └── entities.yaml
└── migrations/
    └── from-1.0.0.yaml
```

## Blueprint YAML Schema

```yaml
$schema: https://schemas.meshmakers.cloud/blueprint-meta.schema.json
blueprintId: InfrastructureStarter-1.0.0
description: Infrastructure management starter blueprint

# CK models loaded into the tenant when this blueprint is applied
ckModelDependencies:
  - System-[2.0,3.0)
  - Commerce-[1.0,2.0)

# Other blueprints required before this one (resolved transitively, topo-sorted)
blueprintDependencies:
  - BaseEntities-[1.0,)
  - SecurityModel-[2.0,)

# Optional path to seed data (relative to blueprint root)
seedDataPath: seed-data/entities.yaml

# Optional migrations from older versions of this blueprint
migrations:
  - fromVersion: "0.9.0"
    scriptPath: "migrations/from-0.9.0.yaml"
```

### Fields

| Field                   | Type     | Description                                                                |
|-------------------------|----------|----------------------------------------------------------------------------|
| `$schema`               | string   | Schema URI for validation                                                  |
| `blueprintId`           | string   | Unique ID with version (`Name-Major.Minor.Patch`)                          |
| `description`           | string   | Optional description                                                       |
| `ckModelDependencies`   | string[] | CK models with version ranges (auto-imported on apply)                     |
| `blueprintDependencies` | string[] | Other blueprints with version ranges (resolved transitively)               |
| `seedDataPath`          | string   | Optional path to seed-data file (runtime-model format)                     |
| `migrations`            | array    | Optional list of migration scripts keyed by source version                 |

> Composition (`composedBlueprints`) was removed in favour of dependency-only resolution. The field is no longer accepted; the schema rejects manifests that still carry it.

## Version Ranges

Both `ckModelDependencies` and `blueprintDependencies` use the same range syntax as CK models:

| Format          | Meaning                      |
|-----------------|------------------------------|
| `1.0.0`         | Exact version                |
| `[1.0.0,)`      | Version 1.0.0 or higher      |
| `[1.0.0,2.0.0)` | Version >= 1.0.0 and < 2.0.0 |
| `(1.0.0,2.0.0]` | Version > 1.0.0 and <= 2.0.0 |
| `[1.5.0]`       | Exactly version 1.5.0        |

## Application Flow

```
ApplyBlueprintAsync(tenantId, blueprintId, force)
│
├── 1. Resolve transitive blueprint dependency closure (topo-sorted)
│
├── 2. Conflict-check (CK versions, entity ownership, rtId collisions)
│       → BlueprintApplicationResult.Conflicts; abort on hard conflicts
│
├── 3. For each blueprint in topo order:
│       ├── Idempotency: already installed in same version → no-op
│       │                 already installed, --force → ReApply (upsert)
│       │                 already installed, different version → Update path
│       ├── Import CK model dependencies (auto-resolve via ICkModelUpgradeService)
│       ├── Apply seed data via IImportRtModelCommand (Upsert)
│       ├── Tag entities with rtBlueprintSource / rtBlueprintLocked / rtBlueprintAppliedAt
│       ├── Persist BlueprintInstallation
│       └── Publish BlueprintApplied event
│
└── 4. Append history entry, return result
```

## Entity Source Tracking

Every seed entity is stamped with three system attributes when applied:

| Attribute              | Type     | Description                                                                       |
|------------------------|----------|-----------------------------------------------------------------------------------|
| `rtBlueprintSource`    | string   | Owning blueprint, full id (`Infrastructure-1.0.0`). Exactly one owner per entity. |
| `rtBlueprintLocked`    | bool     | `true` = managed by blueprint, updates will overwrite; `false` = user-released.   |
| `rtBlueprintAppliedAt` | DateTime | UTC timestamp of the most recent apply/update touching this entity.               |

A blueprint that ships an entity but wants to leave it user-editable from day one can set `rtBlueprintLocked: false` in its seed data.

## Seed Data Format

Seed data is a runtime-model YAML file. The blueprint engine stamps the source attributes during import; you do not write them yourself.

```yaml
$schema: https://schemas.meshmakers.cloud/runtime-model.schema.json
dependencies:
  - System-2.0.0
entities:
  - rtId: 507f1f77bcf86cd799439011
    ckTypeId: System/Entity
    rtWellKnownName: InitialEntity
    attributes:
      - id: System/Name
        value: My Initial Entity
      - id: System/Description
        value: Created by blueprint
```

Seed data is applied with **upsert** strategy: existing entities (matched by `rtId`) are updated; new ones are inserted.

## Update Modes

| Mode        | Behaviour                                                                                                    |
|-------------|--------------------------------------------------------------------------------------------------------------|
| `Safe`      | Add new entities only. Existing entities are left alone, even if locked.                                     |
| `Merge`     | Add new + upsert locked entities. Unlocked entities raise `UserModified` conflicts (default: skip).          |
| `Full`      | Like Merge, plus delete entities that exist in the tenant but no longer in the seed. Unlocked → conflict.    |
| `Migration` | Execute the migration script from the installed version to the target. Required for any non-additive change. |

## Updates

```csharp
public async Task UpdateTenantAsync(string tenantId)
{
    var info = await _blueprintService.GetUpdateInfoAsync(tenantId);
    if (info?.RecommendedVersion == null) return;

    var preview = await _blueprintService.PreviewUpdateAsync(
        tenantId, info.RecommendedVersion, BlueprintUpdateMode.Merge);

    Console.WriteLine($"+{preview.EntitiesToAdd} ~{preview.EntitiesToUpdate} -{preview.EntitiesToDelete}");
    foreach (var c in preview.Conflicts) Console.WriteLine($"!  {c.Description}");

    var result = await _blueprintService.ApplyUpdateAsync(
        tenantId,
        info.RecommendedVersion,
        BlueprintUpdateMode.Merge,
        new BlueprintUpdateOptions { CreateBackup = true });

    if (result.Success)
        Console.WriteLine($"Backup: {result.BackupId}");
}
```

A pre-update backup is created by default (`CreateBackup = true`); disable it with `CreateBackup = false` if you have an external safety net.

## Conflict Resolution

A conflict is raised when an unlocked entity (`rtBlueprintLocked = false`) is in the way of an update. Two conflict types exist:

| Type             | Triggered when                                                                                                |
|------------------|---------------------------------------------------------------------------------------------------------------|
| `UserModified`   | The seed wants to update this entity, but the tenant entity has been unlocked.                                |
| `DeleteModified` | Full mode wants to delete this entity (no longer in seed), but the tenant entity has been unlocked.           |

Default per-entity resolution is `Skip`. The caller can override per-entity:

| Resolution      | Behaviour                                                                                                              |
|-----------------|------------------------------------------------------------------------------------------------------------------------|
| `KeepUser`      | Keep the user's version, skip the blueprint change.                                                                    |
| `KeepBlueprint` | Apply the blueprint's version. **UserModified**: seed is re-applied and the entity is re-locked. **DeleteModified** (Full only): entity is erased. |
| `Merge`         | Currently treated as KeepUser (semantic 3-way merge is out of scope).                                                  |
| `Skip`          | Skip this entity.                                                                                                      |

```csharp
var options = new BlueprintUpdateOptions
{
    ConflictResolutions = new Dictionary<string, ConflictResolution>
    {
        ["507f1f77bcf86cd799439011"] = ConflictResolution.KeepBlueprint,
        ["507f1f77bcf86cd799439012"] = ConflictResolution.KeepUser,
    }
};
```

`KeepBlueprint` overrides take effect in the same call — the apply path treats explicitly-resolved conflicts as non-blocking and routes them through the same import / delete pipeline.

## Migration Scripts

For non-additive changes (rename, delete, transform), ship a migration script and reference it from `blueprint.yaml`:

```yaml
# MyBlueprint-2.0.0/migrations/from-1.0.0.yaml
$schema: https://schemas.meshmakers.cloud/blueprint-migration.schema.json
sourceVersion: "1.0.0"
targetVersion: "2.0.0"
description: "Migration from v1 to v2"

preConditions:
  - type: EntityExists
    target:
      ckTypeId: System/Entity
      rtWellKnownName: MainConfig

steps:
  - stepId: rename-config-field
    action: Transform
    target:
      ckTypeId: System/Entity
      blueprintSourceOnly: true
    transform:
      type: Rename
      sourceAttribute: LegacyVersion
      targetAttribute: Version

  - stepId: delete-deprecated
    action: Delete
    target:
      ckTypeId: System/Entity
      rtWellKnownName: LegacyConfig
      blueprintSourceOnly: true

postValidations:
  - validationId: still-have-config
    type: EntityCount
    target:
      ckTypeId: System/Entity
    expectedCount: 5
    severity: Error
```

Reference from the manifest:

```yaml
migrations:
  - fromVersion: "1.0.0"
    scriptPath: "migrations/from-1.0.0.yaml"
```

### Supported step actions

| Action      | Purpose                                                                                          |
|-------------|--------------------------------------------------------------------------------------------------|
| `Add`       | Insert an entity (data carries the full `RtEntityTcDto` payload).                                |
| `Update`    | Update attributes on matching entities (data is a `{ attributeName: value }` dict).              |
| `Delete`    | Erase matching entities (`DeleteOptions.Erase` — permanent).                                     |
| `Rename`    | Rename an attribute on matching entities (shorthand for `Transform` of type `Rename`).           |
| `Transform` | Type-driven: `Rename`, `Copy`, `Delete`, `SetValue`, `MapValue`.                                 |

Primitive attribute updates are coerced via the CK model's declared `AttributeValueTypesDto`. Record / RecordArray attributes are rejected from scalar migration payloads with a clear error.

### Conditions & Validations

| Construct                                                | Type                                                       | Notes                                                                  |
|----------------------------------------------------------|------------------------------------------------------------|------------------------------------------------------------------------|
| `preConditions[]`                                        | `EntityExists` / `EntityNotExists` / `AttributeEquals`     | Block the entire migration before any step runs.                       |
| `step.condition`                                         | same as above                                              | Skip this specific step if the condition is not met.                   |
| `postValidations[]`                                      | `EntityCount` / `EntityExists` / `ReferenceIntegrity`*     | Run after steps; surface as warnings or errors per `severity`.         |

\* `ReferenceIntegrity` is currently a no-op placeholder.

## Uninstall

```csharp
var result = await _blueprintService.UninstallAsync(
    tenantId,
    blueprintName: "InfrastructureStarter",
    cascade: false,
    cancellationToken);

if (!result.Success && result.BlockingDependents.Any())
{
    Console.WriteLine($"Blocked by: {string.Join(", ", result.BlockingDependents)}");
    // re-run with cascade: true to uninstall dependents too
}
```

| Behaviour              | Default                                                                                       |
|------------------------|-----------------------------------------------------------------------------------------------|
| **Refcount check**     | If any other installed blueprint depends on this one, uninstall is blocked.                   |
| **Owned entity erase** | All entities with `rtBlueprintSource == <this blueprint full id>` are erased permanently.     |
| **Unlocked entities**  | Entities the user released (`rtBlueprintLocked = false`) are kept — they survive the uninstall. |
| **Cascade**            | `cascade: true` uninstalls dependents first and orphan-cleans dependencies of the target.     |

## Multi-Blueprint Installation

A tenant can host any number of blueprints concurrently. Two services track this state:

| Interface                          | Purpose                                                                                       |
|------------------------------------|-----------------------------------------------------------------------------------------------|
| `ITenantBlueprintInstallations`    | The current set of installed blueprints (one row per blueprint, with `IsDependency` flag).    |
| `ITenantBlueprintHistory`          | Append-only operation log (install, update, rollback, uninstall) with timestamps and counts.  |

```csharp
var installations = await _installations.GetInstalledAsync(tenantId, ct);
foreach (var i in installations)
{
    var role = i.IsDependency ? "(dep)" : "(root)";
    Console.WriteLine($"{i.BlueprintId} {role}  installed {i.InstalledAt:u}");
}
```

## Backup and Rollback

Every update creates a pre-update backup by default. Rollback restores the entire tenant snapshot:

```csharp
var backups = await _blueprintService.ListBackupsAsync(tenantId);
var latest = backups.First();

var result = await _blueprintService.RollbackAsync(tenantId, latest.BackupId, ct);
Console.WriteLine($"Restored {result.EntitiesRestored} entities from {latest.CreatedAt:u}");
```

Rollback is a full tenant restore, not a semantic undo of migration steps. After rollback, the `BlueprintInstallations` rows match the snapshot — partial undo is not supported (see concept-v2 §2.5).

## Blueprint History

```csharp
var history = await _blueprintService.GetHistoryAsync(tenantId);
foreach (var e in history)
{
    Console.WriteLine($"{e.AppliedAt:u}  {e.BlueprintId}  {e.ApplicationMode}");
    Console.WriteLine($"  +{e.EntitiesCreated} ~{e.EntitiesUpdated} -{e.EntitiesDeleted}");
}
```

`ApplicationMode` values: `Initial`, `ReApply`, `Update`, `Rollback`, `Uninstall`.

## DI Configuration

```csharp
services.AddBlueprintCatalogs(options =>
{
    options.AddLocalFileSystemCatalog("/path/to/blueprints");
});

services.AddRuntimeEngine();             // pulls in IBlueprintService et al.
services.AddMongoBlueprintSupport();     // wires MongoDB-backed history + installations + backups
```

The blueprint service ships in `Runtime.Engine`. The MongoDB-backed history / installations / backup persistence ships in `Runtime.Engine.MongoDb` and is registered with `AddMongoBlueprintSupport()`. For in-memory-only setups (unit tests), `AddRuntimeEngine()` registers in-memory defaults.

## Service-Managed Blueprints (`System.*`)

OctoMesh draws a convention line through the blueprint *name*:

| Name prefix    | Lifecycle                                                                                     |
|----------------|-----------------------------------------------------------------------------------------------|
| `System.…`     | **Service-managed.** Applied and updated automatically by the owning OctoMesh service.        |
| anything else  | **Admin-installable.** Picked up from a regular catalog; admin clicks Install in Studio.      |

Use `BlueprintIdExtensions.IsServiceManaged(blueprintId)` to check at runtime. The matching constant is `BlueprintIdExtensions.ServiceManagedNamePrefix` (`"System."`). Studio mirrors the same check in TypeScript (`blueprint-management.ts`) to hide Install / Re-apply controls for service-managed blueprints — manual install on a system blueprint would race the service for ownership.

The convention is on the name, not the catalog. A service-managed blueprint discovered through a `LocalFileSystemBlueprintCatalog` is still service-managed.

## Catalog Types

| Catalog                            | Description                                                                                       |
|------------------------------------|---------------------------------------------------------------------------------------------------|
| `LocalFileSystemBlueprintCatalog`  | Loads blueprints from the file system.                                                            |
| `EmbeddedResourceBlueprintCatalog` | Read-only catalog that aggregates every DI-registered `IBlueprintEmbeddedSource` (see below).     |
| `PublicGitHubBlueprintCatalog`     | Reads blueprints from a public GitHub Pages site (default: `meshmakers.github.io`).               |
| `PrivateGitHubBlueprintCatalog`    | Reads blueprints from a private/internal GitHub repository (writes via Octokit).                  |

```csharp
services.Configure<PublicGitHubBlueprintCatalogOptions>(o =>
{
    o.GitHubPagesUri = "https://meshmakers.github.io/";
});

services.Configure<PrivateGitHubBlueprintCatalogOptions>(o =>
{
    o.GitHubPagesUri = "https://meshmakers.github.io/blueprint-libraries-build/";
    o.GitHubApiToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
});
```

> The private GitHub catalog reads via HTTP against the Pages URI and writes via Octokit. If GitHub Pages is disabled on the source repository, reads will 404 and the catalog is effectively write-only until Pages is enabled.

## Embedding Blueprints in a Service NuGet

Service-managed blueprints typically ship *inside* the owning service's NuGet (so a service version bump automatically rolls every tenant's blueprint forward). The pattern mirrors how CK models are embedded:

1. Lay out blueprint folders next to the CK model in the service's CK-model project:

   ```
   SystemCommunicationCkModel/
   ├── ConstructionKit/         # CK model YAML (existing)
   └── Blueprints/
       └── System.Communication-1.0.0/
           ├── blueprint.yaml
           └── seed-data/entities.yaml
   ```

2. Add the folder to the project's MSBuild item set so the `BlueprintEmbed` task (shipped in the `Meshmakers.Octo.ConstructionKit.MsBuildTasks` NuGet) discovers it:

   ```xml
   <ItemGroup>
       <BlueprintFolder Visible="false" Include="$(MSBuildProjectDirectory)\Blueprints" />
   </ItemGroup>
   ```

   At build time the task validates each `blueprint.yaml` against `blueprint-meta.schema.json`, registers every file as an `EmbeddedResource` with a deterministic `LogicalName`, and emits an `obj/<Config>/<TFM>/octo-blueprints/blueprints-cache.json` inventory.

3. The `BlueprintSourceGenerator` (shipped in the `Meshmakers.Octo.ConstructionKit.SourceGeneration` NuGet) consumes the cache via `AdditionalFiles` and emits, per blueprint version:
   - an `IBlueprintEmbeddedSource` implementation under `{RootNamespace}.Generated.Blueprints.{Name}.v{Major}`
   - a DI extension method `AddBlueprint{Name}V{Major}(this IServiceCollection)` under `Microsoft.Extensions.DependencyInjection`.

4. The consuming service registers the embedded source with one line per blueprint and lets the engine discover them through the always-registered `EmbeddedResourceBlueprintCatalog`:

   ```csharp
   services.AddRuntimeEngine();                       // registers IBlueprintService + the catalog
   services.AddBlueprintSystemCommunicationV1();      // generated extension
   ```

5. To apply (or re-apply) a service-managed blueprint per tenant, call `IBlueprintService.ApplyBlueprintAsync(tenantId, new BlueprintId("System.Communication-1.0.0"))` — typically on tenant Enable and again on tenant startup. `ApplyBlueprintAsync` is idempotent at the same version; bumping the embedded version is enough to roll every tenant forward on the next startup.

The convention for picking what to embed:
- `System.*` blueprints (production base, service-managed) → embed in the service's CK-model NuGet.
- Demo / sample / opt-in blueprints → ship through a regular catalog (LocalFileSystem, GitHub) so admins decide per tenant.

## GitHub Catalog Layout

```
blueprints/v1/
├── catalog.json                                 # Root catalog index
└── m/                                           # First letter of blueprint name (lowercase)
    └── MyBlueprint/
        ├── catalog.json                         # Library catalog (one entry per major version)
        └── 1/
            ├── catalog.json                     # Version catalog (list of versions)
            └── MyBlueprint-1.0.0/
                ├── blueprint.yaml
                ├── seed-data/
                └── migrations/
```

The three `catalog.json` levels are generated by the `Publish` flow on the engine side — application code talks to `IBlueprintCatalogManager`, not to the catalog files directly.

## CLI (octo-cli)

Runtime blueprint operations against a tenant service are handled by `octo-cli`. The relevant commands live under `Asset/Blueprints/`:

| Command                       | Purpose                                                                |
|-------------------------------|------------------------------------------------------------------------|
| `ListBlueprints`              | List blueprints available across configured catalogs.                  |
| `InstallBlueprint`            | Apply a blueprint to the active tenant. `-f` re-applies (upsert).      |
| `GetBlueprintHistory`         | Show the application history for the active tenant.                    |
| `ListBlueprintInstallations`  | List blueprints currently installed on the tenant.                     |
| `PreviewBlueprintUpdate`      | Preview the diff for a target version + mode (Safe/Merge/Full).        |
| `UpdateBlueprint`             | Apply an update. `-m <mode>`, `-dr` (dry-run), `-nb` (no backup).      |
| `ListBlueprintBackups`        | List backups created before updates.                                   |
| `RollbackBlueprint`           | Restore a tenant from a backup (`-bid <backupId>`).                    |
| `UninstallBlueprint`          | Uninstall a blueprint. `-c` to cascade-uninstall dependents.           |

See `octo-cli/CLAUDE.md` § "Blueprints" for the exact argument forms.

## Best Practices

1. **Small, focused blueprints.** One blueprint per domain/feature. Compose via `blueprintDependencies`, not by bundling unrelated entities.
2. **Use version ranges for dependencies.** `[1.0,)` keeps things flexible; pinning exact versions is fine for `blueprintId` but rarely helpful in dependencies.
3. **Sparse seed data.** Ship essential bootstrap data only — no test data, no per-customer specifics.
4. **Lock managed entities.** Default `rtBlueprintLocked` is `true`; only override to `false` when you genuinely intend the user to take ownership immediately.
5. **Migration scripts for breaking changes.** Schema renames, deletes, and value transformations need an explicit script. Additive changes work via Merge alone.
6. **Test with `-dr`.** Use `UpdateBlueprint -dr` (dry-run) before applying to production tenants.
7. **Keep backups.** Default backup creation is on; only disable it when you have an alternate snapshot mechanism.
