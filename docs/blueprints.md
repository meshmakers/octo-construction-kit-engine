# Blueprints

Blueprints are versioned, declarative bundles of Construction Kit models and runtime seed data that bootstrap a tenant — and continue to manage it. They are not a one-shot bootstrap mechanism: a blueprint can be updated, rolled back, uninstalled, depend on other blueprints, and ship migration scripts that transform tenant data when its own version moves forward.

## Properties

| Property             | Description                                                                                  |
|----------------------|----------------------------------------------------------------------------------------------|
| **Versioned**        | SemVer (`MyBlueprint-1.2.3`). Version ranges express compatibility, like CK models.          |
| **Dependency-aware** | A blueprint may depend on other blueprints, resolved transitively at install time.           |
| **Owner-tracked**    | Every seed entity is tagged with `rtBlueprintSource` and `rtBlueprintLocked`.                |
| **Updatable**        | Tenants are moved to newer versions via Safe / Merge / Full / Migration modes.               |
| **Multi-install**    | A tenant can host several blueprints concurrently. Refcounted, cascade-uninstall optional.   |

## Blueprint Structure

A blueprint is a directory containing a `blueprint.yaml`, optional seed data, and optional migration scripts:

```
MyBlueprint/
├── blueprint.yaml        # blueprintId: MyBlueprint-1.0.0
├── seed-data/
│   └── entities.yaml
└── migrations/
    └── from-1.0.0.yaml
```

A seed that has grown past what one file can carry may be split across several files and
folders — see [Splitting the seed across several files](#splitting-the-seed-across-several-files):

```
MyBlueprint/
├── blueprint.yaml        # seedDataPaths: [...]
└── seed-data/
    ├── configurations/base.yaml
    ├── data-flows/camt053.yaml
    ├── identity/roles.yaml
    └── master-data/accounts.yaml
```

The folder name carries only the blueprint **Name**; the version lives exclusively
in the manifest's `blueprintId`. Bumping the version is a manifest-only edit — no
folder rename required.

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

# ... or, for a seed split across several files (mutually exclusive in practice; both are
# loaded if both are given)
seedDataPaths:
  - seed-data/configurations/base.yaml
  - seed-data/data-flows/camt053.yaml
  - seed-data/identity/roles.yaml

# Optional preconditions — see "Blueprint Variables" below
requires:
  octo.environment: [staging, production]
  octo.isSystemTenant: "false"

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
| `seedDataPaths`         | string[] | Optional list of seed-data files, merged into one model before import      |
| `requires`              | object   | Optional preconditions evaluated against the tenant variable context       |
| `migrations`            | array    | Optional list of migration scripts keyed by source version                 |

> Composition (`composedBlueprints`) was removed in favour of dependency-only resolution. The field is no longer accepted; the schema rejects manifests that still carry it.

## Blueprint Variables

Every blueprint apply runs against a **variable context** — a string→string dictionary
resolved per tenant by `IBlueprintVariableProvider`. Variables are referenced from seed
data and the manifest's `requires:` block using the same `${name}` syntax used by the CK
compiler and the mesh-adapter `PlaceholderReplaceNode`.

### Built-in variables

The default `IBlueprintVariableProvider` exposes the following `octo.*` keys. Services
can register a custom provider in DI to add their own keys (chart names, feature flags,
secret indirection) — the default registration uses `TryAddTransient`, so a service-side
`AddTransient<IBlueprintVariableProvider, MyProvider>()` before `AddRuntimeEngine()` wins.

| Variable               | Source                                                                              |
|------------------------|-------------------------------------------------------------------------------------|
| `octo.version`         | `OctoBlueprintVariablesOptions.OctoVersion` (helm sets via `.Chart.AppVersion`). Auto-trimmed to 3-segment SemVer — a 4-part input like `3.3.109.0` from `Build.BuildNumber` becomes `3.3.109` so Helm's strict chart-version validation accepts it. Pre-release / build-metadata suffixes are preserved (`3.3.109.0-test1` → `3.3.109-test1`). |
| `octo.environment`     | `OctoBlueprintVariablesOptions.Environment` — `dev` (default), `test`, `staging`, `production` |
| `octo.environmentMode` | Same value mapped to the matching `System/EnvironmentModes` CK-enum name (`Development`, `Testing`, `Staging`, `Production`). Unknown environments fall back to `Development` with a warning log — the blueprint apply still succeeds. |
| `octo.tenantId`        | The tenant currently being initialised                                              |
| `octo.systemTenantId`  | `OctoBlueprintVariablesOptions.SystemTenantId` (defaults to `OctoSystem`)           |
| `octo.isSystemTenant`  | `"true"` when `tenantId == octo.systemTenantId`, otherwise `"false"`                |

Options bind from the `Blueprints` configuration section. Helm / env-var override path:

```bash
OCTO_BLUEPRINTS__OCTOVERSION=3.5.2
OCTO_BLUEPRINTS__ENVIRONMENT=production
OCTO_BLUEPRINTS__SYSTEMTENANTID=OctoSystem
```

### Using variables in seed data

`${name}` placeholders are resolved in two locations:

- Every string-typed attribute `value` in `seed-data/entities.yaml`
- Every entity's `rtWellKnownName`

Non-string attribute values (numbers, booleans, embedded records) are left untouched.
Unknown placeholders log a warning and are kept verbatim — so a typo is loud rather than
quietly writing an empty string to MongoDB.

```yaml
# seed-data/entities.yaml
entities:
  - rtId: '670000000000000000000002'
    ckTypeId: System.Communication/Adapter
    rtWellKnownName: MeshAdapter
    attributes:
      - id: System.Communication/ChartName
        value: octo-adapter-eda
      - id: System.Communication/ChartVersion
        value: "${octo.version}"   # ← rolled forward by helm with every release
```

#### Empty-string sentinel for chart versions

A seeded attribute whose value is the empty string is a valid runtime value, not a
"missing" one. The Communication-controller workflow uses this deliberately for
`System.Communication/ChartVersion`: an empty value tells the Communication
Operator's `HelmRunner` to omit the `--version` argument entirely, so helm picks
the newest chart in the configured repository. Paired with `requires:` this lets
a single blueprint family ship two variants — one with `ChartVersion:
"${octo.version}"` for staging/production (matches the release-channel chart 1:1)
and one with `ChartVersion: ""` for dev/test (tracks the rolling dev-channel
chart, gets overwritten by the CD pipeline on every main-CI run). See
`octo-communication-controller-services/CLAUDE.md` "Service-Managed Blueprints"
and "Empty ChartVersion" for the full contract.

### `requires:` preconditions

`requires:` gates whether the **root** blueprint of an apply call runs. Each key is a
variable name; the value is either a scalar shortcut or a YAML sequence of acceptable
values. The blueprint is applied only when every key resolves to a value present in its
allow-list; otherwise the apply is a successful no-op (no install row, no seed data, no
history entry) and `BlueprintApplicationResult.WasSkipped` is `true`.

```yaml
# Only apply to the system tenant in staging or production
requires:
  octo.isSystemTenant: "true"        # scalar — normalised to ["true"]
  octo.environment: [staging, production]
```

Semantics:

- A variable referenced by `requires:` but missing from the context fails the check
  (fail-closed — better than a silently misapplied blueprint).
- An empty allow-list (`requires: { octo.environment: [] }`) cannot match anything and
  fails — usually a typo, surfaced loudly.
- Matching is case-sensitive (`StringComparer.Ordinal`). Normalise casing in the manifest.
- `requires:` is evaluated only on the explicitly-requested root blueprint, not on
  transitive dependencies — a dep that ships seed data is presumed to be required by
  every consumer. Split blueprints if you need finer-grained gating.

### Pattern: collapse "two-blueprints" into one with `requires:`

Before `requires:`, a service that wanted to seed different data for the system tenant
versus other tenants had to ship two blueprints and branch in code. With `requires:`,
both blueprints are still shipped, both are auto-applied, and each filters itself:

```yaml
# System.UI.SystemCockpit-1.0.0/blueprint.yaml
requires:
  octo.isSystemTenant: "true"

# System.UI.TenantCockpit-1.0.0/blueprint.yaml
requires:
  octo.isSystemTenant: "false"
```

The service-side `DefaultConfigurationCreatorService` then becomes a single loop that
applies every embedded blueprint — the `requires:` block decides which one matches.

## Version Ranges

Both `ckModelDependencies` and `blueprintDependencies` use the same range syntax as CK models:

| Format          | Meaning                      |
|-----------------|------------------------------|
| `1.0.0`         | Exact version                |
| `[1.0.0,)`      | Version 1.0.0 or higher      |
| `[1.0.0,2.0.0)` | Version >= 1.0.0 and < 2.0.0 |
| `(1.0.0,2.0.0]` | Version > 1.0.0 and <= 2.0.0 |
| `[1.5.0]`       | Exactly version 1.5.0        |

A `blueprintDependencies` range resolves to the **highest catalog version that satisfies it across
all readable catalogs** (catalog order only breaks ties). On apply, the range floor is a minimum,
never a target: a dependency the tenant already runs in a newer version that still satisfies every
declared range is kept untouched (no seed re-import, no installation-row change, also with
`--force`, which only applies to the root). A newer installed version outside a declared range fails
the apply instead of downgrading it.

## Application Flow

```
ApplyBlueprintAsync(tenantId, blueprintId, force)
│
├── 1. Resolve transitive blueprint dependency closure (topo-sorted)
│
├── 1c. Keep dependencies already installed in a newer in-range version
│       (newer but out of range → fail, never downgrade)
│
├── 1b. Resolve variable context via IBlueprintVariableProvider
│       Evaluate root blueprint's `requires:` against the context
│       → mismatch → BlueprintApplicationResult.Skipped (success no-op)
│
├── 2. Conflict-check (CK versions, entity ownership, rtId collisions)
│       → BlueprintApplicationResult.Conflicts; abort on hard conflicts
│
├── 3. For each blueprint in topo order:
│       ├── Idempotency: already installed in same version → no-op
│       │                 already installed, --force → ReApply (upsert)
│       │                 already installed, different version → Update path
│       ├── Import CK model dependencies (auto-resolve via ICkModelUpgradeService)
│       ├── Load seed data, interpolate ${variable} placeholders
│       ├── Apply seed data via IImportRtModelCommand (Upsert)
│       ├── Tag entities with rtBlueprintSource / rtBlueprintLocked / rtBlueprintAppliedAt
│       ├── Persist BlueprintInstallation
│       └── Publish BlueprintApplied event
│
└── 4. Append history entry, return result
```

```
ApplyUpdateAsync(tenantId, targetVersion, updateMode, options)
│
├── 1. Resolve the current version *of this blueprint* (by name), preview the update
│
├── 2. Conflict gate → abort unless pre-resolved or ContinueOnError
│       DryRun → return preview counts, write nothing
│
├── 3. Install new CK model dependencies, run CK model migrations
│
├── 4. Apply the diff (Safe / Merge / Full) or execute the migration script
│
├── 5. Upsert the BlueprintInstallation row onto the target version
│       → failure here fails the update; no history entry is written
│
└── 6. Append history entry, publish BlueprintUpdated event, return result
```

## Entity Source Tracking

Every seed entity is stamped with three system attributes when applied:

| Attribute              | Type     | Description                                                                       |
|------------------------|----------|-----------------------------------------------------------------------------------|
| `rtBlueprintSource`    | string   | Owning blueprint, full id (`Infrastructure-1.0.0`). Exactly one owner per entity. |
| `rtBlueprintLocked`    | bool     | `true` = managed by blueprint, updates will overwrite; `false` = user-released.   |
| `rtBlueprintAppliedAt` | DateTime | UTC timestamp of the most recent apply/update touching this entity.               |

A blueprint that ships an entity but wants to leave it user-editable from day one can set `rtBlueprintLocked: false` in its seed data. Such an entity is **tenant-owned**, see the next section.

## Product-owned and tenant-owned seed entities (AB#6383)

The `rtBlueprintLocked` flag **in the seed** says who owns an entity once it exists on the tenant:

| Seed says                        | Owner    | First install            | Update, tenant holds it                               | Update, tenant lacks it                                      |
|----------------------------------|----------|--------------------------|-------------------------------------------------------|--------------------------------------------------------------|
| flag absent or `true` (default)  | product  | created                  | renewed from the seed (Merge/Full)                    | created again (a deleted entity comes back)                  |
| `rtBlueprintLocked: false`       | tenant   | created with seed values | **not touched**, no conflict (only the stamp is moved) | created only if it is new; **stays deleted** if the previous version's seed already had its key |

Rules for a tenant-owned seed entity (`UpdateBlueprint` in `Merge` and `Full` mode, and `Safe` for the creation part):

- **Created once.** On the first install, and when a later version of the seed brings a key the previously installed version did not have, it is created with the seed values.
- **Never updated.** If the tenant holds it, none of its attribute values is written, however the seed changed. The update only moves the blueprint stamp: `rtBlueprintLocked` is set to `false`, `rtBlueprintSource` and `rtBlueprintAppliedAt` are refreshed (not in `Safe` mode, and not for an entity another blueprint or the user owns by `rtBlueprintSource`). No `UserModified` conflict is raised, so the entity does not block the update.
- **Never re-created after a delete.** If the tenant lacks it and the seed of the previously installed version (read from the catalog) contains the same key (`rtWellKnownName`, else `rtId`) with the same CK type, the tenant deleted it and it stays deleted. If that previous seed cannot be read from the catalog, the update does **not** create the absent tenant-owned entities and returns a warning, because it cannot tell a deleted entity from a new one.
- **The seed decides, not the tenant's stamp.** A tenant that still carries the stamp `rtBlueprintLocked: true` from an earlier version is handled as tenant-owned from the first update whose seed declares the entity unlocked; its edits are kept. The key counts as "contained in the previous seed" whatever flag the previous seed gave it, so an entity that was product-owned and is handed over to the tenant stays deleted if the tenant had deleted it.
- **Reported.** `PreviewUpdateAsync` lists them in `TenantOwnedSkipped` and `TenantOwnedStaysDeleted`, the apply result carries the same two lists (and counts them in `EntitiesSkipped`). A forced re-apply or an install over another version reports them as messages of the `OperationResult`.
- **A forced re-apply (`InstallBlueprint --force`) and an install over another version** follow the same rules (the "previous version" is the version recorded on the tenant; for a forced re-apply of the same version that is the version itself, so a deleted tenant-owned entity stays deleted).
- **Not covered: `ImportRt -r`** (and any plain RT import). It is not blueprint-aware, ignores `rtBlueprintLocked` and overwrites by `rtId`; use `UpdateBlueprint` / `InstallBlueprint` to apply a blueprint seed.
- Associations declared on a tenant-owned seed entity that is skipped are not applied either; an association from another seed entity to a tenant-owned entity the tenant deleted is dropped as dangling.
- The reverse flip (seed locks an entity that the tenant copy carries as unlocked) is unchanged: the unlocked tenant copy raises a `UserModified` conflict.

Effect on existing unlocked seed entities: before AB#6383 an update that found such an entity on the tenant raised a `UserModified` conflict, and `ApplyUpdateAsync` refused the whole update unless `ContinueOnError` or a per-entity resolution was given. Such an entity is now **skipped** and does not block the update. Example: the unlocked FamilyOs entities (AB#6317 F10) went from "blocks the update" to "skipped".

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

### Splitting the seed across several files

One `entities.yaml` becomes unreadable long before a rich blueprint is finished — data flows,
pipelines, configurations, identity roles and master data all end up interleaved in a file where
no diff is reviewable and no feature has an owner. `seedDataPaths` replaces the single
`seedDataPath` with a list, so the seed can be organised by feature:

```yaml
seedDataPaths:
  - seed-data/configurations/base.yaml
  - seed-data/identity/roles.yaml
  - seed-data/data-flows/camt053.yaml       # one pipeline + its data flow per file
  - seed-data/data-flows/weclapp-sync.yaml
  - seed-data/master-data/accounts.yaml
```

Each file is a complete runtime-model document with its own `$schema` and `dependencies`; the
engine loads all of them, unions their `dependencies`, concatenates their `entities` and imports
the result as **one** model. What follows from that:

- **Order does not matter.** The merged import writes every entity before any association, so an
  association in the first file may reference an entity declared in the last one. The list order
  only fixes which file is named first when a duplicate `rtId` is reported.
- **Duplicate entities across files are rejected.** Identity here is the CK type *plus* the
  `rtId`, not the `rtId` alone — entities live in a collection per CK type and associations carry
  the target type next to the target id, so the same id under two types is legal (and occurs in
  shipped blueprints). `octo-bpm validate` warns about that reuse because it makes association
  targets easy to get wrong. A duplicate inside a single file keeps its previous behaviour
  (reported by the importer).
- **A missing file is an error**, not a warning, as soon as more than one file is declared:
  importing the rest would leave the tenant partially seeded and still report success. The
  single-file form keeps its historic warning.
- **`octo-bpm validate` warns about YAML files under `seed-data/` that no path references** —
  forgetting to add a newly created file to the list is the one failure mode this form
  introduces, and it would otherwise pass as a silently smaller install.
- **Backwards compatible.** `seedDataPath` keeps working unchanged. If a manifest sets both, the
  single path is loaded first and the list follows (and `octo-bpm validate` warns).

There is no directory or glob form: blueprints are served to the runtime over GitHub Pages, which
offers no directory listing, so the set of files has to be written down in the manifest that is
fetched anyway.

## Update Modes

| Mode        | Behaviour                                                                                                    |
|-------------|--------------------------------------------------------------------------------------------------------------|
| `Safe`      | Add new entities only. Existing entities are left alone, even if locked.                                     |
| `Merge`     | Add new + upsert locked entities. Tenant-owned seed entities (`rtBlueprintLocked: false` in the seed) are skipped without a conflict; an unlocked tenant copy of a product-owned seed entity raises a `UserModified` conflict (default: skip). |
| `Full`      | Like Merge, plus delete entities that exist in the tenant but no longer in the seed. Unlocked → conflict.    |
| `Migration` | Execute the migration script from the installed version to the target. Required for any non-additive change. Fails the update when the target ships no script for the installed version (see below). |

## Seed values never blank tenant values (AB#6313)

Every `Upsert` import (blueprint apply and update, `ImportRt -r`, CK-migration entity writes) is a
full replace of the entity. The engine therefore guards the attributes the blueprint owns
(`SeedOwned`, the default for an attribute without an ownership mark). Attributes owned by the tenant
(`TenantOwned`, `RuntimeState`, `Secret`) are kept as before.

For a `SeedOwned` attribute the decision is made per attribute by `SeedValueGuard.Decide`:

| Existing entity | Seed | Result |
|-----------------|------|--------|
| no value stored | anything | seed value lands |
| empty value | anything | seed value lands |
| non-empty | different non-empty value | **seed wins** (blueprint-owned change, as before) |
| non-empty | empty value (`null`, `""`, empty array, record with only empty members) | **existing kept**, reported |
| non-empty | attribute omitted | **existing kept**, reported |
| non-empty JSON text | JSON text with an empty string where the existing has a non-empty one | **existing kept as a whole**, reported (an "empty skeleton" such as the EDA adapter configuration of AB#6310) |

"Empty" is narrow on purpose: numbers, booleans, enums, dates and time spans are never empty, so an
explicit `0`, `false` or the first enum member in a seed is a value the author chose and replaces
the tenant's value. They are only kept when the seed omits the attribute altogether. Records are
kept or replaced as one unit. For JSON text, properties the seed does not mention and numbers or
booleans are never judged; only a string leaf that the seed empties counts.

Every kept (or, under policy `Allow`, applied) case is logged as a warning and listed in
`IImportRtModelCommand.GuardEntries` (rtId, CK type, attribute, reason `SeedEmpty` / `SeedOmitted`,
applied or kept). The default policy is `RtImportBlankingPolicy.Keep`; `Allow` exists only for an
explicit operator confirmation.

**Consequence for blueprint authors.** A seed can no longer clear a value that a tenant has filled.
If an update must really remove such a value, either mark the attribute's ownership correctly
(an attribute a tenant types in belongs to `TenantOwned`), use a CK migration `Update` action, or
let the operator confirm the blanking explicitly. Blueprint-owned changes to a non-empty value keep
working unchanged. Merge, Safe and Full update modes only decide which entities are imported; the
guard applies to all of them.

### Preview and explicit confirmation (AB#6315)

The same detection (`SeedBlankingDetector`, built on `SeedValueGuard`) feeds the update preview, so
what the preview announces is exactly what the apply keeps.

- `BlueprintUpdatePreview.BlankedAttributes` lists, for every locked entity the update would touch,
  each attribute whose non-empty tenant value the seed would blank: `rtId`, `ckTypeId`,
  `attributeName`, `reason` (`SeedEmpty` / `SeedOmitted`), `currentSummary`, `incomingSummary`
  (kind and size only, e.g. `string (223 chars)`, `empty string`, `omitted`; never the value, which
  may be a credential) and `appliedOnUpdate` (always `false` in a preview).
- `BlueprintUpdateOptions.AllowBlanking` (engine policy `Allow`) confirms every listed blanking;
  `BlueprintUpdateOptions.ConfirmedBlankings` (`[{ rtId, attributeName }]`) confirms exactly those
  entity/attribute pairs. Default: nothing is confirmed, the update proceeds and keeps the tenant
  values.
- `BlueprintUpdateResult.BlankedAttributes` repeats the list with `appliedOnUpdate` telling what
  happened (`false` = kept, `true` = blanked on confirmation), so nothing is silent. A warning
  counts the kept ones.
- Engine level: `IImportRtModelCommand.ImportModelAsync(..., blankingPolicy, confirmedBlankings)`.

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
        new BlueprintUpdateOptions());

    if (result.Success)
        Console.WriteLine("Update applied");
}
```

Updates do not create a tenant backup — take an external snapshot (e.g. the platform's tenant dump/backup jobs) beforehand if you need a safety net.

### What a successful update records

A successful update writes **two** records, in this order:

1. The installation row (`ITenantBlueprintInstallations`) — `BlueprintId` moves to the target
   version and `LastUpdatedAt` is stamped with the apply timestamp. `InstalledAt`,
   `IsDependency`, `ResolvedDependencies` and `SeedDataChecksum` are carried over from the
   existing row. If the row is missing (a tenant that only ever got history entries), it is
   created as a root installation.
2. The history entry (`ITenantBlueprintHistory`) — append-only, sharing the same timestamp.

The installation row goes first on purpose: it is the live view of what is in effect, the
history is the audit log, and there is no transaction spanning both stores. If the installation
upsert fails, the update is reported as failed and **no** history entry is written, so both
records stay on the previous version and a repeated update repairs them idempotently.

Two consequences worth knowing:

- Updates do not re-resolve blueprint dependencies (only CK model dependencies), so
  `ResolvedDependencies` is preserved as-is. A target version that declares new blueprint
  dependencies needs an explicit install to pull them in.
- Conflicts skipped via `ContinueOnError` do not hold the row back: skipped entities are
  unlocked and therefore user data, while the row states which blueprint version is in effect.

## Conflict Resolution

A conflict is raised when an unlocked tenant entity (`rtBlueprintLocked = false`) is in the way of an update, unless the seed itself declares the entity tenant-owned (see above): that entity is skipped without a conflict. Two conflict types exist:

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

`fromVersion` is matched against the version of **this blueprint** currently installed on the
tenant — resolved via `ITenantBlueprintHistory.GetCurrentByBlueprintNameAsync`, so other
blueprints applied to the tenant later do not interfere.

If no `fromVersion` matches, an update requested in `Migration` mode **fails**: the preview
raises a blocking `MissingMigrationScript` conflict (entity id `migration:<blueprintId>`) and
`ApplyUpdateAsync` returns `Success = false` with the installed version and the available
`fromVersion` values in the error. Earlier versions degraded to `Merge` with only a warning and
still reported success, so the deletes and renames the script was meant to perform silently did
not happen (AB#4832). The old behaviour is still reachable for programmatic callers via
`BlueprintUpdateOptions.ContinueOnError`, which downgrades the failure to a warning and applies
the seed data in `Merge` mode; the REST/GraphQL/CLI paths do not expose that flag, so there
`-m Migration` either runs the script or fails.

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
| **Owned entity erase** | All entities with `rtBlueprintSource == <this blueprint full id>` are erased permanently. The match is version-exact and uses the version named by the installation row, i.e. the version currently in effect. |
| **Unlocked entities**  | Entities the user released (`rtBlueprintLocked = false`) are kept — they survive the uninstall. |
| **Cascade**            | `cascade: true` uninstalls dependents first and orphan-cleans dependencies of the target.     |

## Multi-Blueprint Installation

A tenant can host any number of blueprints concurrently. Two services track this state:

| Interface                          | Purpose                                                                                       |
|------------------------------------|-----------------------------------------------------------------------------------------------|
| `ITenantBlueprintInstallations`    | The current set of installed blueprints (one row per blueprint, with `IsDependency` flag). The row mutates to the new version on every update. |
| `ITenantBlueprintHistory`          | Append-only operation log (install, update, rollback, uninstall) with timestamps and counts.  |

```csharp
var installations = await _installations.GetInstalledAsync(tenantId, ct);
foreach (var i in installations)
{
    var role = i.IsDependency ? "(dep)" : "(root)";
    Console.WriteLine($"{i.BlueprintId} {role}  installed {i.InstalledAt:u}");
}
```

Because a tenant carries several blueprints, "the current blueprint" only ever means something
per blueprint name:

| Call                                                    | Answers                                                                 |
|---------------------------------------------------------|-------------------------------------------------------------------------|
| `ITenantBlueprintHistory.GetCurrentAsync(tenantId)`     | The blueprint applied to the tenant **last**, whichever one that is. Only meaningful on a single-blueprint tenant. |
| `GetCurrentByBlueprintNameAsync(tenantId, name)`        | The version of **that** blueprint in effect — what the update, preview and migration paths use. |
| `ITenantBlueprintInstallations.GetByBlueprintNameAsync` | The live installation row of that blueprint (state, not audit log).     |

`IBlueprintService.GetUpdateInfoAsync` has the same split: the two-argument overload describes
the last-applied blueprint, the overload taking a `blueprintName` describes the one asked for.
On the API surface both `blueprints.updateInfo` / `blueprints.current` (GraphQL) and
`GET blueprints/updates` / `GET blueprints/current` (REST) accept an optional `blueprintName`.

## Backup and Rollback

The blueprint-level backup/rollback feature was removed (AB#4317): updates never create tenant snapshots, and there is no `RollbackAsync`. Tenant-level safety nets are the infrastructure layer's job — use the platform's tenant dump/restore jobs (bot services) or database-level snapshots instead.

## Blueprint History

```csharp
var history = await _blueprintService.GetHistoryAsync(tenantId);
foreach (var e in history)
{
    Console.WriteLine($"{e.AppliedAt:u}  {e.BlueprintId}  {e.ApplicationMode}");
    Console.WriteLine($"  +{e.EntitiesCreated} ~{e.EntitiesUpdated} -{e.EntitiesDeleted}");
}
```

`ApplicationMode` values: `Initial`, `Update`, `Migration`, `ReApply`.

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

Both GitHub blueprint catalogs have an `IsEnabled` switch (default `true`, AB#6112). A disabled
catalog is neither read nor written: it does not appear in `ListBlueprints`, is ignored by dependency
resolution and installs, and `RefreshBlueprintCatalogs` reports it as skipped. Production installations
disable the private (main-lane) catalog so that only released blueprints are offered; in the services
this is `OCTO_PrivateOctoGitHubBlueprints__IsEnabled=false` (helm: `assetRepository.blueprintCatalog.privateGitHubEnabled`).
Blueprints already installed from a now-disabled catalog keep working; update checks only offer
versions from the remaining catalogs. Uninstalling such a version fails ("blueprint not found"),
because uninstall re-reads the installed version's seed data from the catalogs to find its entities.

> The private GitHub catalog reads via HTTP against the Pages URI and writes via Octokit. If GitHub Pages is disabled on the source repository, reads will 404 and the catalog is effectively write-only until Pages is enabled.

## Embedding Blueprints in a Service NuGet

Service-managed blueprints typically ship *inside* the owning service's NuGet (so a service version bump automatically rolls every tenant's blueprint forward). The pattern mirrors how CK models are embedded:

1. Lay out blueprint folders next to the CK model in the service's CK-model project:

   ```
   SystemCommunicationCkModel/
   ├── ConstructionKit/         # CK model YAML (existing)
   └── Blueprints/
       └── System.Communication/
           ├── blueprint.yaml   # blueprintId: System.Communication-1.0.0
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

`GitHubBlueprintCatalog.GetAsync` caches the parsed `blueprint.yaml` per `BlueprintId` in memory (published versions are immutable), so repeated lookups — e.g. resolving dependencies for a listing — cost one HTTP GET per blueprint and process. Failures are never cached; the entries are dropped on `Publish` / `Unpublish` / `UnpublishAllVersions` of that catalog. Callers receive a copy, not the cached instance.

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
| `UninstallBlueprint`          | Uninstall a blueprint. `-c` to cascade-uninstall dependents.           |

See `octo-cli/CLAUDE.md` § "Blueprints" for the exact argument forms.

## Best Practices

1. **Small, focused blueprints.** One blueprint per domain/feature. Compose via `blueprintDependencies`, not by bundling unrelated entities.
2. **Use version ranges for dependencies.** `[1.0,)` keeps things flexible; pinning exact versions is fine for `blueprintId` but rarely helpful in dependencies.
3. **Sparse seed data.** Ship essential bootstrap data only — no test data, no per-customer specifics.
4. **Lock managed entities.** Default `rtBlueprintLocked` is `true`; only override to `false` when you genuinely intend the user to take ownership immediately. An unlocked seed entity is created once and then belongs to the tenant: no update changes it and none brings it back after a delete. Keep entities the product needs (the rules a feature depends on) locked and unlock only defaults the tenant is meant to edit or remove.
5. **Migration scripts for breaking changes.** Schema renames, deletes, and value transformations need an explicit script. Additive changes work via Merge alone.
6. **Test with `-dr`.** Use `UpdateBlueprint -dr` (dry-run) before applying to production tenants.
7. **Keep backups.** Blueprint updates do not snapshot the tenant — rely on the infrastructure-level tenant dump/restore jobs or database snapshots before risky updates.
