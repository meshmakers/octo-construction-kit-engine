# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Octo Construction Kit Engine is a .NET-based framework for building and managing data models with Construction Kits. It's part of the Octo Mesh platform, which transforms raw data into meaningful information with proper context.

## Architecture

The codebase follows a modular architecture with clear separation of concerns:

### Core Components
- **ConstructionKit.Contracts**: Shared contracts, interfaces, and serialization logic for Construction Kit models
- **ConstructionKit.Engine**: Core engine for processing Construction Kit models
- **ConstructionKit.Compiler**: CLI tool for compiling Construction Kit YAML models
- **ConstructionKit.SourceGeneration**: Source code generation from CK models
- **Runtime.Engine**: Runtime execution engine for processing data
- **Runtime.Contracts**: Runtime contracts and interfaces
- **SystemCkModel**: Base system Construction Kit models
- **StreamDataCkModel**: `System.StreamData` model (archives, time-series storage); depends on System
- **SystemFilesCkModel**: `System.Files` model (platform file system: folder roots, folders, files); depends on System

### Construction Kit Model Structure
Construction Kit models are defined in YAML files following a specific schema:
- Models have a unique `modelId` (e.g., "System-1.0.1")
- Models can depend on other models via `dependencies`
- Model definitions are organized in folders: `types/`, `attributes/`, `enums/`, `records/`, `associations/`, and (CK v2, `ckLanguage: 2`) `interfaces/`
- Optional: `migrations/` folder for CK model version migrations

## Build Commands

```bash
# Build in debug mode with local packages - the default for local work
dotnet build --configuration DebugL

# Build in Release mode against a published release (version must be passed explicitly)
dotnet build --configuration Release -p:OctoVersion=3.5.1

# Build specific project
dotnet build src/ConstructionKit.Compiler/ConstructionKit.Compiler.csproj
```

Versions come only from the pipeline (AB#6291): `Directory.Build.props` has no release fallback, so a
Debug/Release build without `-p:OctoVersion=X.Y.Z` (or `-p:OctoNugetPrivateServer=<feed>` for the main
line `0.1.*`) fails fast with MSBuild error `OCTO0001`. Use `-c DebugL` locally.

## Test Commands

```bash
# Run all tests except system tests
dotnet test --configuration DebugL --filter "FullyQualifiedName!~SystemTests"

# Run all tests including system tests
dotnet test --configuration DebugL

# Run tests for specific project
dotnet test tests/ConstructionKit.Engine.Tests/ConstructionKit.Engine.Tests.csproj

# Run specific test
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"
```

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

## Code Quality

```bash
# The project enforces warnings as errors - fix all warnings before committing
# C# nullable reference types are enabled - handle nullability appropriately
# Latest major C# language version is used
```

## Working with Construction Kit Models

When modifying CK models:
1. YAML files in `ConstructionKit/` folders define the model structure
2. Models must follow the schema at `https://schemas.meshmakers.cloud/construction-kit-meta.schema.json`
3. After changes, recompile the model using the compiler
4. Generated code will be created based on the model definitions

## JSON Serialization & Newtonsoft Parity

The Rt-model serialization rules are split across two canonical options bundles, both in
`Runtime.Contracts/Serialization/`:

- `RtNewtonsoftSerializer.DefaultSerializer` — pre-migration Newtonsoft setup (used by tests and
  legacy callers). `RtNewtonsoftAttributesConverter` preserves source CLR types through the
  in-memory `JObject.FromObject` / `JToken.ToObject` round-trip (e.g. `int 1` stays `Int32` in
  `JValue.Value`).
- `RtSystemTextJsonSerializer.Default` — STJ counterpart. Has `RtAttributesConverter` for the
  attribute dict, the CK / Rt ID converters, and the **`NewtonsoftParityDoubleConverter` /
  `…SingleConverter` / `…DecimalConverter`** that emit `.0` for whole-number reals (mirroring
  `JsonConvert.ToString`). Without these, `double 0.0` would serialize as `0` and re-deserialize
  as `long` via `JsonScalar.ToClr` — observable as `BsonInt64` regressions in MongoDB.
  It also sets **`Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`** so serialized output
  matches Newtonsoft byte-for-byte on non-ASCII (umlauts/ß) and HTML chars (`< > &`); STJ's default
  `JavaScriptEncoder` would `\uXXXX`-escape those and diverge from the legacy wire form (breaking
  hash/HMAC parity over serialized bytes). This encoder is the single source of truth and flows into
  `SystemTextJsonOptions.Default` (octo-sdk) and every wire node. **Any consumer that serializes Rt
  data with a raw `Utf8JsonWriter` (e.g. SDK `IDataContext.WriteJsonTo`) must honour it** — pass
  `new JsonWriterOptions { Encoder = SystemTextJsonOptions.Default.Encoder }`.

`JsonScalar.ToClr` is the single source of scalar boxing (`Runtime.Contracts/Serialization/`).
Rules:
- Integers prefer `Int32` (fits in 32 bits), then `Int64`. Matches Newtonsoft's
  `JObject.FromObject(int)` → `JValue.Value=Int32`.
- Reals box to `double`.
- ISO-8601 strings parse to `DateTime` when `parseDateStrings=true`.

The parity contract is enforced by `Sdk.Common.PipelineParityTests` (in octo-sdk) using
Newtonsoft as the oracle. Irreducible divergences (float vs double, decimal vs double,
DateTimeOffset vs DateTime — JSON has no source-CLR-type marker) are listed by name in
`AttributeValueParityCorpus.IrreducibleDivergences`. Consumers needing exact source-CLR-type
preservation must use typed accessors (`GetAttributeValue<decimal>`, typed properties on DTOs).

**RtCkId virtual-property shim.** On the wire an `RtCkId<T>` serialises to a bare
`SemanticVersionedFullName` string, but legacy pipeline YAML drills `.SemanticVersionedFullName` /
`.FullName` expecting the historical object shape. The rule that those two property names on a JSON
string are virtual self-aliases is centralised in **`RtCkIdJsonShim`**
(`ConstructionKit.Contracts/RtCkIdJsonShim.cs`), next to `RtCkId<T>` where the wire shape is decided.
It replaces magic strings formerly duplicated across the SDK walkers (`JsonPathWalker`, `DataOverlay`,
`LayeredSource`), `RtNewtonsoftAttributesConverter`, and the mesh-adapter `CreateUpdateInfoNode`.

## N:M Association Query Columns

N:M (many-to-many) associations are exposed as query columns with `totalCount` and `exists` meta-properties. These allow listing and filtering entities based on whether associations exist and how many there are.

### Path Syntax
```
navigationPropertyName.targetTypeName::totalCount    → INTEGER_64 (count of associations)
navigationPropertyName.targetTypeName::exists        → BOOLEAN (true if count > 0)
```

The `::` separator distinguishes association meta-properties from regular attribute navigation (`->`). This avoids collisions with actual attributes named `totalCount` or `exists` on the target type.

### Key Components
- **CkTypeQueryColumnCollector** (`ConstructionKit.Engine/DependencyGraph/`): Generates `totalCount` and `exists` columns for both outbound and inbound N:M associations. One column per navigation property grouping (not per derived type).
- **AssociationCountFilter** (`Runtime.Contracts/Repositories/Query/`): Record type that carries count filter operator and comparison value on a `NavigationPair`.
- **NavigationPair.AssociationCountFilter**: When set, the MongoDB layer generates a count-based aggregation pipeline instead of the standard existence check.

### MongoDB Pipeline
When `AssociationCountFilter` is set on a `NavigationPair`, `SingleOriginRtQuery.CreateAssociationCountNavigation` generates:
1. `$lookup` on the associations collection (matching by role ID and target type)
2. `$addFields` to compute `$size` of the lookup result
3. `$match` to filter by the count comparison (e.g., `>= 1` for exists)
4. `$project` to clean up temporary fields

### Usage in GraphQL
```graphql
# Filter by existence
fieldFilter: [{
  attributePath: "documents.bankTransaction::exists"
  operator: EQUALS
  comparisonValue: true
}]

# Filter by count
fieldFilter: [{
  attributePath: "documents.bankTransaction::totalCount"
  operator: GREATER_THAN
  comparisonValue: 3
}]
```

## Query Column Collector Guardrails

`CkTypeQueryColumnCollector` walks the association graph to build navigation columns
(`a.Type->b`). Two guardrails protect against pathological CK models (root cause of a production-class
incident: a single transient stream-data query on a tenant with a 0..1 self-association on
the root `System/Entity` type allocated >60 GB and killed the asset-repo service):

- **`CkTypeQueryColumnOptions.MaxColumns`** (default `50_000`, `null` = unlimited): hard cap
  on the total number of produced columns. The per-path cycle guard (`ignoredNavigations`)
  only prevents revisiting the same `(targetType, role)` tuple *within one path* — sibling
  branches re-explore the same subgraph, and `GetAllDerivedTypes` fans each 0..1/1 navigation
  out over every derived type of the target, so densely connected models expand
  combinatorially. When the cap is exceeded the collector throws a `DependencyGraphException`
  (fail fast) instead of allocating unbounded memory. Callers that only need physical
  attribute columns (e.g. stream-data archives) should pass
  `IgnoreNavigationProperties = true` instead of relying on the cap.
- **Record cycle detection**: record descent (`Record` / `RecordArray` attributes) tracks the
  records on the current descent path and throws a `DependencyGraphException` on a cycle
  (a record containing itself directly or transitively) instead of recursing to stack
  overflow.

## Repository Structure

```
/src                    # Source code
  /ConstructionKit.*    # CK-related components
  /Runtime.*            # Runtime components
  /SystemCkModel        # Base system models
  /StreamDataCkModel    # System.StreamData
  /SystemFilesCkModel   # System.Files (platform file system)
/tests                  # Test projects
/samples                # Example implementations
azure-pipelines.yml     # CI/CD pipeline definition (the legacy /devops-build folder is gone)
/scripts                # createDocumentation.ps1 and other CI helper scripts
```

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

## Blueprints and Migrations

The project supports two types of migrations:

### Blueprints
Blueprints initialize tenants with pre-configured CK models and runtime data. See `docs/blueprints.md` for details.

Key services:
- `IBlueprintService` - Applies blueprints to tenants
- `IBlueprintCkUpgradeGuard` (host-registered, optional) - tenant upgrade guard for the CK models a blueprint installs or raises (CK v2 F2.5, AB#6871), see below

#### Tenant upgrade guard in `BlueprintService` (CK v2 F2.5, AB#6871)

A blueprint installs the floor of each `ckModelDependencies` range as an exact model version, so an apply or update can replace an installed model
exactly like `ImportFromCatalog`. `BlueprintService` therefore asks an optional host service, `IBlueprintCkUpgradeGuard`
(`Runtime.Contracts/Blueprints/IBlueprintCkUpgradeGuard.cs`; the guard of AB#6867 lives in engine-mongodb and needs the catalogs, so the host -
asset-repo - implements the interface), to judge the **CK update set as one set** before anything is installed:

- **Update set** (`EvaluateCkUpgradeGuardAsync`): every dependency whose floor is above the installed version or that is not installed
  (`IRuntimeRepositoryProvider.GetSchemaVersionsAsync`, same rule as `EnsureCkModelInstalledAsync`: a satisfying-or-newer installed version is
  kept and not judged). Judged for the aggregated dependencies of the install order (first-time apply) and for the target blueprint's own
  dependencies (update). The implementation decides what it judges (asset-repo skips `System*`).
- **Opt-in per call**, default off, so platform-internal callers (`TenantContext` applying service-managed blueprints) are unchanged:
  `BlueprintApplyOptions.CkUpgradeGuard` (new overload `ApplyBlueprintAsync(tenantId, id, BlueprintApplyOptions, ct)`; the `bool force` overload is
  `Off`) and `BlueprintUpdateOptions.CkUpgradeGuard`, mode `BlueprintCkUpgradeGuardMode` `Off` | `Enforce` | `ForceBreakingUpdate`.
- **Enforce + blocked**: apply -> `BlueprintApplicationResult.CkUpgradeBlocked(...)` (`IsSuccess` false, `IsCkUpgradeBlocked`, `CkUpdateSet`,
  `CkUpgradeBlockers`), update -> `BlueprintUpdateResult` (`Success` false, `IsCkUpgradeBlocked`, ...) **before the first CK model is installed**:
  no `EnsureCkModelInstalledAsync`, no migrations, no installation row, no history entry. **ForceBreakingUpdate** proceeds and returns the
  forced blockers in `CkUpgradeBlockers` (the host logs them with the caller). A failing guard fails closed in both modes
  (`CkUpgradeGuardFailed`, nothing changed). A host without the registration runs unguarded.
- **Preview** (`PreviewUpdateAsync`) always reports `CkUpdateSet`, `CkUpgradeBlockers` (`IsCkUpgradeBlocked`) and `CkUpgradeGuardError` (a failing
  guard does not fail the preview); `CanProceed` is unchanged (entity conflicts only). **DryRun** of `ApplyUpdateAsync` never refuses: `Success` stays
  true, `IsCkUpgradeBlocked` + blockers are reported, nothing is written.
- Tests: `tests/Runtime.Engine.Tests/Blueprints/BlueprintServiceCkUpgradeGuardTests.cs`.

#### Runtime-State Preservation on Re-Import (Upsert)

Any `ImportStrategy.Upsert` import maps to a full `ReplaceOne` in the
MongoDB layer — every attribute on the existing entity is overwritten by
the import model's value, even attributes the author never meant to manage
(deployment status, last-error fields, sync sequence numbers, stream-data
`Archive.Status`, …). Two observable failure modes: a "Deployed" adapter
flips back to "Undeployed" on the next controller restart that picks up a
blueprint version bump (the seed encodes `DeploymentState: 0` as the
fresh-tenant default, AB#4582's sibling), and an **activated stream-data
archive silently reverts to Disabled** on a blueprint re-apply or a plain
`ImportRt -r` (AB#4582 / AB#4589), breaking SD queries with
`STREAMDATA_ARCHIVE_NOT_ACTIVATED`.

The fix is per-attribute, opt-in on the CK side, and lives at the **single
import choke point** so every Upsert caller gets it automatically:

- **CK schema**: `CkAttribute.ownership: SeedOwned | TenantOwned |
  RuntimeState | Secret` (AB#5187), with `isRuntimeState: bool` kept as a
  deprecated alias. See `Serialization/Schema/construction-kit-elements-attribute.schema.json`,
  `AttributeOwnershipDto`, `CkAttributeDto`, and the propagation through
  `CkAttributeGraph` / `CkTypeAttributeGraph`. Existing CK models that set
  neither, or only the boolean, behave exactly as before — see
  "Attribute Ownership" below.
- **Import path (shared)**:
  `ImportRtModelCommand.PreserveRuntimeStateAttributesAsync` runs at the top
  of the private `ImportEntityAsync` choke point — through which **all three**
  public import methods (`ImportModelAsync`, `ImportTextAsync`, `ImportAsync`
  file/stream) funnel — gated on `importStrategy == Upsert`. It groups the
  incoming entities by CK type for one batch repo query per type (on the
  ambient import session/transaction), looks each existing entity up by
  `RtId`, and for every attribute the CK cache says the tenant owns
  (`SelectPreservedAttributes` → effective ownership ≠ `SeedOwned`)
  it overwrites the **imported value** with the **existing value** before the
  bulk write. The repository call is wrapped — a lookup failure logs a
  warning and falls through to imported values rather than blocking the
  import. `Insert` is untouched (it errors on an existing id, so there is
  nothing to preserve).
- **Per-import state (AB#6652)**: `ImportRtModelCommand` is transient, but
  scoped callers (`DefaultConfigurationCreatorService`, the seed re-import over
  all tenants) reuse one instance for many imports. `ResetImportState()` runs at
  the start of `ImportModelAsync`, `ImportTextAsync` and `ImportFileAsync` and
  clears the duplicate-RtId set, mandatory violations, guard entries, queues,
  association counter and the blueprint-lock summary. Without it every id of the
  second import was logged as "already imported" (ERROR burst at release 3.5.15)
  and violations/guard entries leaked into the next tenant. A duplicate RtId
  inside **one** payload is logged as a Warning (Upsert stores one of the
  entries, Insert is rejected by the database with a duplicate key). Tests:
  `ImportRtModelCommandStateResetTests`. Imports on one instance must stay
  sequential.
- **Who benefits**: because it sits in the import command, all Upsert
  callers are covered with no per-caller code:
  - blueprint seed apply (`BlueprintService.ApplySeedDataForBlueprintAsync`
    and the locked/merge import path) — the explicit pre-import preserve call
    that used to live in `BlueprintService` was removed; it is now automatic.
  - the plain `ImportRt -r` CLI path (`octo-bot-services` `ImportModelJob` →
    `ImportAsync`) — the AB#4589 target (voest-app archive re-import).
  - CK-model migration entity writes (`BlueprintMigrationExecutor`, Upsert).
    Note: an additive migration that introduces a new runtime-state attribute
    on a pre-existing entity still lands its value (existing entity has no
    prior value → nothing to preserve); only a migration that *rewrites an
    already-present* runtime-state attribute would be preserved-over — rare,
    and consistent with "an import must never trample runtime state".
- **Per-entity vs. per-attribute lock**: preservation is orthogonal to the
  blueprint `rtBlueprintLocked` per-entity lock. The lock (blueprint layer)
  decides whether the blueprint touches an entity at all; `isRuntimeState`
  (import layer) decides — assuming the entity is being imported — which
  specific attributes survive the upsert.
- **What it does not do**: preservation only rewrites the incoming model
  in-memory, so the actual import stays a `ReplaceOne`. Fresh tenants /
  brand-new entities (no existing entity) are silent no-ops.

- **Omitted preserved attributes are injected, not cleared (AB#5232)**: because
  the upsert is a full `ReplaceOne`, a preserved attribute the import model
  *omits* would be cleared — and omission is the NORMAL seed shape for
  engine-owned bookkeeping that is null on a fresh tenant. The concrete
  incident: blueprint seeds never declare `RollupArchive.LastAggregatedBucketEnd`
  (null before the first orchestrator run), so every `InstallBlueprint -f`
  nulled the rollup watermark and the RollupOrchestrator skipped the rollup
  forever ("watermark is null … Skipping until set").
  `PreserveAttributesForEntity` therefore now *adds* the existing value to the
  incoming model (through the same `ToTransportValue` conversion as the
  overwrite path) when the model omits a preserved attribute the tenant has a
  value for. Attributes the existing entity has no value for are still left
  alone, and non-preserved (`SeedOwned`) attributes keep the old clear-on-omit
  `ReplaceOne` semantics. System.StreamData 1.10.0 flags
  `RollupArchive.LastAggregatedBucketEnd` and `RollupArchive.FrozenUntil` as
  runtime-state to close the incident.

- **Record-typed values need converting, not copying (AB#4784)**: the value
  read from the repository is in *repository* shape, the import model holds
  *transport* shape. They coincide for every scalar type, which is why a raw
  assignment worked for the overwhelming majority of runtime-state attributes —
  but a `Record` / `RecordArray` value is an `RtRecord` on one side and an
  `RtRecordTcDto` on the other, and handing the former over made the import throw
  `InvalidCastException` in `AssignAttributes`. The import is atomic, so the whole
  seed failed. In practice this hit exactly one attribute,
  `System.Communication/Values` (the Helm `ValueOverride`s), which blocked adopting
  any existing tenant into a blueprint that seeds a workload. `ToTransportValue`
  now converts before assigning.

  It deliberately does **not** reuse `RtEntityToTcDtoConverter`: that one serves
  export, where it *skips* runtime-state attributes and may resolve enum keys to
  names. Both are wrong here — preserving means carrying the existing value over
  verbatim, including nested runtime-state attributes, or the value would be
  silently truncated on the way through. Enum keys pass through unresolved because
  `AssignAttributes` accepts key or name.

The testable seam is the pure
`ImportRtModelCommand.PreserveAttributesForEntity` static helper, exposed
via `InternalsVisibleTo`. It takes the conversion as a `Func<object?, object?>`
so it stays free of the CK cache; production passes `ToTransportValue`. Tests in
`tests/Runtime.Engine.Tests/Exchange/ImportRtModelCommandPreserveAttributesForEntityTests.cs`
cover: flagged+both-sides → preserved; unflagged → imported value wins; mixed;
model-only (additive CK bump) → imported value lands; existing-only (model
omits the attr) → no-op; multi-attribute independence; the stream-data
`Archive.Status` activated-survives-re-import / fresh-import-keeps-Disabled cases;
and (AB#4784) that the preserved value is handed to the converter rather than
copied raw — including the `RecordArray` case the suite was missing, which is why
the bug shipped. The conversion itself is covered by
`ImportRtModelCommandToTransportValueTests` against the real CK cache fixture
(record → DTO with matching record / attribute ids, per-element array conversion,
missing record attribute stays omitted rather than nulled, scalar and string
pass-through).

#### Attribute Ownership (AB#5187)

`isRuntimeState` answered two independent questions with one bit — *who owns
the value on Upsert?* (preservation, `ImportRtModelCommand`) and *is the value
part of the entity's portable definition?* (exclusion from `ExportRt`,
`RtEntityToTcDtoConverter`). They agree for a rotating refresh token and for a
product endpoint, and they collide for tenant master data — a tariff, an IBAN,
a market-partner id, a logo — which must be preserved AND exported. The boolean
forced authors to pick one loss, and two teams picked opposite ones in the same
sprint.

`ownership` replaces it with four one-word answers:

| `ownership` | Blueprint re-apply / Upsert | `ExportRt` | Typical |
| ----------- | --------------------------- | ---------- | ------- |
| `SeedOwned` (default, `== isRuntimeState: false`) | seed value wins | exported | product endpoints, report template names, statutory defaults, well-known-name wiring, taxonomy |
| `TenantOwned` (new) | existing tenant value wins | **exported** | tariffs, IBAN + account holder, market-partner ids, tenant mailbox/host/port, branding, app title, logos, colours |
| `RuntimeState` (`== isRuntimeState: true`) | existing value wins | excluded | deployment/communication status, last-error pairs, sync cursors, execution history, debug toggles, operator-written chart version/hostname |
| `Secret` (new) | existing value wins | excluded | client secrets, API keys, bot tokens, passwords, private keys, refresh tokens |

`Secret` behaves like `RuntimeState` today. It earns its place because it is the
honest name (an API key is not "runtime state"), because a later export opt-in
must be able to re-include `RuntimeState` while never including a secret, and
because a later redaction / vault feature needs something a bool cannot carry.

**No flag day.** `AttributeOwnership.Resolve` maps `ownership` when declared,
otherwise the alias (`true → RuntimeState`, `false`/absent → `SeedOwned`), so
every declaration that exists resolves with zero behaviour change. Declaring
both is an authoring error (`OCTO-CK003`, the lint); the engine still resolves
it deterministically with `ownership` winning. Once `ownership` is declared,
`CkAttributeDto.IsRuntimeState` becomes a computed **mirror** of
`IsPreservedOnUpsert()` — true for `TenantOwned`, `RuntimeState` and `Secret` —
which is serialised into the compiled model and persisted by the CK-model
repository, so an engine that does not know `ownership` yet degrades to
preserve-and-exclude (today's behaviour) instead of regressing to "seed wins"
and resetting credentials.

**Per-assignment override.** Ownership sits on the attribute DEFINITION and is
copied into every assignment, but one definition is shared by many types — a
single `ClientId` is assigned by `FinApiConfiguration`,
`MicrosoftGraphConfiguration` and `ServiceAccountConfiguration`. A type-attribute
or record-attribute assignment may therefore override it
(`CkTypeAttributeDto.Ownership`, nullable, `null` = inherit — so existing models
are untouched). The definition carries the common case; the outlier states its
own answer next to the type it belongs to.

**Two granularities, on purpose.** Preservation inspects only top-level type
attributes (`AllAttributes`, inherited included) and preserves a record-valued
attribute as one unit, verbatim including nested members. Export recurses into
records and evaluates each member individually. Both are right for what they do,
but the consequence is an authoring trap: a `TenantOwned` record whose members
were left `RuntimeState` exports with those members silently stripped. Give
record members the same ownership as the record-valued attribute that contains
them.

**Marked is not frozen.** A CK migration `Update` action (with a target
selector, a condition and `onConflict`) is the sanctioned, versioned, auditable
way to correct a tenant-owned value across tenants. The choice is "the seed can
change it silently" vs "changing it costs a migration", not "changeable" vs
"frozen".

Author guidance — answer the first question that applies and stop:

1. Is it a credential, token, key or password — anything you would not paste
   into a ticket? → `Secret`
2. Is it written by a service, operator, pipeline or job rather than typed by a
   human (status, timestamps, counters, error history, cursors, rotated tokens,
   deployed hostname/chart version, debug toggles)? → `RuntimeState`
3. Would a tenant admin set this in the product and be right to be angry if a
   product update overwrote it? → `TenantOwned`
4. Otherwise it ships with the product and a new version must be able to correct
   it. → `SeedOwned`

Tie-breakers: if you cannot name how a correction would reach every tenant,
choose `SeedOwned` — that is the reversible mistake. Credential tuples travel
together (host, client id, secret, username, sandbox flag): one member left
`SeedOwned` produces half a credential, which fails like no credential but looks
configured in the Studio. Before marking a shared definition, check who else
uses the attribute id and use the per-assignment override rather than making the
strictest consumer win by accident.

The `CkLintRuntimeStateMarkers` MSBuild task (opt-in per project via
`OctoEnforceRuntimeStateMarkers`) accepts either marker and rejects an attribute
that declares neither (`OCTO-CK001`), both (`OCTO-CK003`), or an unknown
ownership value (`OCTO-CK004`).

Armed models (AB#6326 and earlier): System.Communication, System.Identity, System.Ai,
System.Notification, Meshmakers.Accounting(.Tesla) via the package targets, and
**System.StreamData in this repository**. This repository cannot consume its own
MsBuildTasks package, so `Directory.Build.targets` registers the same task from
`bin/<Configuration>/tasks/<tfm>/` with `TaskFactory="TaskHostFactory"` (no DLL lock in
a reused node); a model project that sets `OctoEnforceRuntimeStateMarkers=true` adds a
`ProjectReference` to `ConstructionKit.MsBuildTasks` with `ReferenceOutputAssembly="false"`
for the build order (see `StreamDataCkModel.csproj`). System.StreamData got 56
restating `ownership: SeedOwned` markers. They change the compiled file; since AB#6386 (equivalent-content rule, `CkPublishEquivalence`) the publish
gate treats such a restatement under an already published version as a no-op with a WARNING instead of `OCTO-CK205` (System.StreamData was bumped
to 1.17.1 before that rule existed). Rule: a change of the compiled output that is a real content change (also a flipped ownership other than the
default restated, `TenantOwned`/`Secret`) needs a version bump before it reaches main; a pure restatement of a default no longer does. The F4
the F4
retention attributes (`Archive.RawRetentionMs`, `Archive.MaxRetroactiveReachMs`) carry the
comment "under review in AB#6328". `SystemCkModel` is not armed yet.

**Review question for every new attribute:** could an operator or user type this value in
the product? Yes → `TenantOwned` (or `Secret` for a credential); a service writes it →
`RuntimeState`; the product ships and corrects it → `SeedOwned`.

### CK Model Migrations
CK Model Migrations update runtime entities when CK model versions change. See `docs/ck-model-migrations.md` for details.

CK migration classes live in dedicated namespaces (separate from Blueprints):
- Contracts: `Runtime.Contracts.CkModelMigrations` (`ICkModelMigrationService`, `ICkMigrationContentProvider`, `ICkModelUpgradeService`)
- DTOs: `ConstructionKit.Contracts.ModelCatalogs.DataTransferObjects` (`CkMigrationMetaDto`, `CkMigrationScriptDto`, etc.)
- Engine: `Runtime.Engine.CkModelMigrations` (implementations)
- `IRuntimeRepositoryProvider` is in top-level `Runtime.Contracts`

Key services:
- `ICkModelMigrationService` - Executes migrations between CK model versions
- `ICkMigrationContentProvider` - Provides migration scripts (embedded resources or file system)
- `ICkMigrationParser` - Parses YAML migration scripts

Migration scripts location: `ConstructionKit/migrations/`

MSBuild property to control embedding: `OctoEmbedCkMigrations` (default: true)

Path resolution order: Direct → Multi-Hop → Auto-Bridge → Partial → No-Migrations Bridge → No Path

**Auto-bridging**: The engine automatically bridges version gaps at both ends of the migration chain. When the tenant's installed version is older than the earliest migration entry point, a no-op bridge step is created. When the chain doesn't reach the exact target version, the partial path is executed and the rest is treated as schema-only.

**No-migrations bridge**: When a CK model defines no migration scripts at all but the target version is strictly greater than the installed version, the entire `fromVersion → toVersion` jump is treated as a single schema-only no-op step. This means purely additive CK-model bumps (e.g. adding a new type) need no migration scripts at all — `FindMigrationPathAsync` synthesises the no-op path. Downgrades and same-version calls without scripts still return null. See `docs/ck-model-migrations.md` for the full path-resolution order.

Developers only need to create migration scripts for versions that actually transform data.

**No schema-only bridge across a major (AB#4924, G3)**: the post-chain bridge and the end gap of a bridged/partial path are refused when they would cross a major version (`CkMigrationMajorVersionGuard`). `CkModelUpgradeService` then fails the model and leaves the MigrationHistory at the installed version (no "recorded without data migration"). The start-gap bridge and the no-migrations bridge are unaffected. Root cause it closes: System.Communication 3.40 tenants were lifted to 4.x without the Pool -> DeploymentSite rename because the 4.x migration-meta only listed 3.35/3.36.

## Key Interfaces

| Interface | Namespace | Description |
|-----------|-----------|-------------|
| `ICkModelMigrationService` | `Runtime.Contracts.CkModelMigrations` | Executes CK model migrations |
| `ICkMigrationContentProvider` | `Runtime.Contracts.CkModelMigrations` | Provides migration content (embedded/file system) |
| `ICkModelUpgradeService` | `Runtime.Contracts.CkModelMigrations` | Auto-checks and executes CK model upgrades |
| `ICkModelImportAuditTrail` | `Runtime.Contracts.CkModelMigrations` | Records noteworthy CK-model import events (e.g. extensible-enum overrides). Default impl writes warning logs; host can register an event-repository adapter to surface entries in the platform event log. |
| `IRuntimeRepositoryProvider` | `Runtime.Contracts` | Provides runtime repositories for tenants |
| `IBlueprintService` | `Runtime.Contracts.Blueprints` | Applies blueprints to tenants |
| `ICatalogService` | `ConstructionKit.Contracts.Services` | Manages CK model catalog |
| `ICkBaselineResolver` | `ConstructionKit.Engine.SemVer` | AB#5450: the one read-only baseline lookup of every compatibility gate (`ValidateVersion`, compile gate AB#6294, publish gate AB#6386): newest version of the declared major, a new major against the previous major line, local-catalog entries at or above the declared version never (AB#5434), model names compare case-insensitively (`GetSpellingsAsync`, AB#6386), and `ResolvePublishBaselineAsync` for the publish gate (target catalog only, never the declared version itself). Without `-lcr`, `ValidateVersion` writes nothing into the catalog other builds read (AB#5434): the sibling registration for same-run dependents goes to a per-run working copy of the local catalog (`PerRunLocalCatalogScope` in `ConstructionKit.Compiler`, removed at the end of the run) or to the `-lcr` root, and the report names the write. Rules: `docs/ck-semver-rules.md`, "Baseline" |
| `ICatalog.UnpublishAsync` / `ICatalogService.UnpublishAsync` | `ConstructionKit.Contracts` / `ConstructionKit.Engine.Services` | AB#5433: `octo-ckc -c Unpublish -m <model> -ve <version> [-cn <catalog>] [-f] [-rs <reason>]` withdraws a published version (dry run unless `-f`). `CatalogService` refuses it while a published model of the catalog depends on the version (the catalog is listed from its repository via `ICatalogManager.ReadAuthoritativeVersionsAsync`, AB#6581, never from the lagging Pages mirror; unreadable = `OCTO-CK202`; tests `CkPublishGateMirrorLagTests`) (exact pin, or a range only this version satisfies; `GetResolutionRanges()`), and requires a reason for `PublicGitHubCatalog`. The catalogs prune the index first (`CatalogUnpublishIndex`, shared pure functions), delete the file last and **burn** the number in `<model dir>/withdrawn.json` (a file of its own, so a publish by an older engine cannot drop it); every `PublishAsync` refuses a burned number (`ModelCatalogException.VersionWithdrawn`). Tests: `CatalogUnpublishTests` (local, private and public GitHub catalog on `InMemoryGitHubClientWrapper`), `UnpublishCommandTests`. Rules: `docs/ck-semver-rules.md`, "Withdrawing a published version" |
| `ICkSurfaceSatisfactionChecker` / `ICkCascadeService` | `ConstructionKit.Engine.SemVer` | AB#5437: the surface satisfaction checker (one dependent vs. a prepared `CkSurfaceCandidate`: `Compatible` / `NeedsRepin` / `Breaks` / `NotInRange`, rows H6 / N2 name collisions) and the read-only cascade walk over the catalogs behind `octo-ckc -c ValidateCascade` (`CkCascadeReport` renders console and Markdown). `ValidateCascade -sp <targetCatalog>` (AB#6388, `ICkCascadeService.FindEquivalentPublishedAsync`) skips a candidate whose version is already published there with identical/equivalent content (`CkPublishEquivalence`), so a build only judges versions it would newly publish. Reused by the publish gate (F2.3) and the tenant upgrade guard (F2.5). Rules: `docs/ck-semver-rules.md`, "Cascade dry run" |
| `ICkCatalogValidationService` | `ConstructionKit.Engine.SemVer` | AB#4469: the whole-catalog consistency check behind `octo-ckc -c ValidateCatalog -cn <catalog> [-m <model>...] [-o report.md]` (read-only, standalone, never part of a build). Newest version of every model: exact pins must exist in the catalog **and their model file must load** (AB#6582, a pinned file deleted by hand with its index entry kept is a defect); range-retaining models are checked with the surface checker of the cascade dry run against the newest admitted dependency version (judged against its predecessor, so catalog check and dry run agree); a newer major outside the range is `not in range`, not broken; case-only name variants are reported with both spellings and catalog paths. Severity follows the catalog: error (exit non-zero) in `PrivateGitHubCatalog`/`PublicGitHubCatalog`, warning elsewhere. `CkCatalogValidationReport` renders console and Markdown. Tests: `ValidateCatalogTests` (Compiler tests). Rules: `docs/ck-semver-rules.md`, "Catalog validation". Pipeline wiring (catalogs train, `octo-construction-kit` catalog lane) is Platform Ops (Argos) |
| `CkModelVersionWriter` | `ConstructionKit.Engine.SemVer` | AB#4467: rewrites only the version of the `modelId` line of a `ckModel.yaml` text (used by `ValidateVersion --apply`, never by the build); `false` unless exactly one `modelId: <Name>-<version>` line exists. Rules: `docs/ck-semver-rules.md`, "Applying the minimum version" |
| `ICkCompatibilityVerdictService` | `ConstructionKit.Engine.SemVer` | AB#6294: the one verdict (baseline model, diff, classification, validation of the declared version, uncovered changes) shared by `ValidateVersion` and the compile gate; read-only. `CkCompileGate` (same namespace) is the decision logic of `CkCompile` for ckLanguage 2 models: `OCTO-CK200`/`201`/`202`, `OCTO-CK203`/`204` (AB#6295, `CkAcknowledgementResult`, `CkChangeKey`), baseline source `OctoCkCompatibilityBaseline` (`Local`/`Remote`, CI default `Remote`). Rules: `docs/ck-semver-rules.md`, "Compile gate" **Security exception judged against the major line (AB#6585):** `ICkSemVerClassifier.Classify(…, earlierInMajor)` takes the published versions of the baseline's major below the baseline; an access tightening on a `securitySensitive` attribute is Minor + acknowledge only when the attribute is sensitive in every one of them that has it (otherwise Major, not acknowledgeable - "mark in one Minor, tighten in the next" is a Major). `NeedsMajorLineHistory` tells the caller whether the history is needed, so `CkCompatibilityVerdictService` (compile gate, `ValidateVersion`, publish gate, cascade) and `CkCatalogValidationService` load it lazily, from the baseline's catalog (publish: the authoritative `KnownVersions` snapshot); one classifier, no new message code (`OCTO-CK200`). Widening a method's authorization (row M12: role such as `Everyone`, dropped scope, `allowSelf`, looser block) is Minor + `RequiresAcknowledge` (`OCTO-CK203`/`204`). Tests: `CkPublishGateSecurityTighteningTests`, `PublicSurfaceRowTests` T7, `MethodRowTests` M12 |
| `CkPublishGate` / `IReplaceablePublishCatalog` / `IAuthoritativeVersionCatalog` | `ConstructionKit.Engine.SemVer` / `.ModelCatalogs` | AB#6386: gate 2 of the compatibility concept. **Readers (AB#6588):** dependency resolution (`CatalogManager.IsExistingAsync`/`TryGetAsync`/`GetAsync` when the cache misses), the `ValidateVersion -rf` baseline (`CkBaselineResolver.QueryAsync`) and the cascade listing (`CkCascadeService`) also read the repository when `IsAuthoritativeReadAvailable` (token present), `OCTO-CK202` on failure; tests `CkDependencyMirrorLagTests`. **Fails closed (AB#6580/6581):** the baseline is read from the catalog's repository, not from the Pages-mirror cache - `GitHubCatalog` implements `IAuthoritativeVersionCatalog.ReadVersionsAsync` (Contents API: both root indexes + library/major indexes of the model; `IGitHubClientWrapper.GetRepositoryStateAsync` tells a genuinely new catalog from a missing repository/branch); any read error, `404` of an index that should exist, rate limit or timeout is `OCTO-CK202`, never an empty catalog; `CkBaselineResolver` takes `IEnumerable<ICatalog>` for it; `CatalogManager` named-catalog `GetAsync` asks the catalog itself when the cache misses; the cache refresh flags `SourceUnreachable` for an index the mirror lost. Tests drive the real `GitHubCatalog` through `InMemoryGitHubRepository` fault injection (`PagesBehavior`, `ApiFault`, `State`, `FreezePages`) - never a flag stub; clear the harness cache (`ClearCatalogCache`) to model a fresh process. `CatalogService.PublishAsync` runs it for every writable catalog except the `LocalFileSystemCatalog` (so `octo-ckc -c Publish` and the `CkCompile` MSBuild publish share one implementation; no bypass, `-r` does not skip it): verdict against the **target catalog** (both roots v2/v3, case-insensitive names, baseline strictly below the declared version), same-version rule (identical = no-op; **equivalent after canonicalisation** = no-op with a WARNING naming the difference class, `CkPublishEquivalence`: restated default ownership, element order, compiler-stamped `minEngineVersion`; real content change = `OCTO-CK205` unless the catalog is the dev lane `octo-catalog-dev`), re-read right before the write, v1 and v2 alike. **Race (AB#6584):** verdict and write are one unit - `CkPublishGateResult.State` (`CatalogStateToken`, hashes of the per-major indexes `ReadVersionsAsync` read) goes through `CatalogManager.PublishAsync(expectedState)` to `IGuardedPublishCatalog` (`GitHubCatalog`), whose index update (`UpsertFileWithMergeAsync` merge callback = compare-and-swap, re-run after each SHA conflict) throws `CatalogChangedDuringPublishException` when another publish landed first (model file removed, nothing visible); `CatalogService.PublishAsync` then re-judges (3 attempts, `OCTO-CK202`). After the write `CkPublishGate.VerifyAfterWriteAsync` judges the version against its predecessor as the catalog holds it now and `CatalogService` withdraws it (`OCTO-CK208`, burned number) when a concurrent publish the conditional write cannot see (other name spelling) broke the rules. The local catalog holds its index lock from the existence check to the last index update. Tests: `CkPublishRaceTests` (second harness on the same `InMemoryGitHubRepository`, `BeforeWrite` hook = other process wins the race), `LocalFileSystemCatalogParallelPublishTests`. AB#6387: after the verdict it enforces the acknowledgements of the **compiled** model (`OCTO-CK203`/`204` from `CkAcknowledgementResult`, the same code as the compile gate; structurally invalid entries of a hand-edited file = `OCTO-CK206`, rules in `CkAcknowledgeEntryRules` shared with message 130) and logs "Acknowledged changes" (level, change, reason); an acknowledgement never lowers a level; no output for ckLanguage 1 / models without `compatibility`. `CatalogService` takes the gate as `Lazy<CkPublishGate>` (it reads through `ICatalogService`). Refusal (AB#6389) = `CkPublishRefusedException` (`ModelValidationException`, exit code `-6`): ALL reasons in one refusal (`OCTO-CK205` no longer ends the evaluation; a burned version number is `OCTO-CK207` via `IWithdrawnVersionCatalog`), ordered and deduplicated by `CkPublishRefusal.Normalize`, one self-contained `[compat gate] OCTO-CK2xx (model, catalog): ... Next step: ...` line each in the `OperationResult` (`ckc Publish` and the `CkCompile` MSBuild publish print the same lines); the O1 guard is a pipeline step (`octo-pipeline-templates` `ck-o1-guard.yml`) and never reaches the engine, matrix in `docs/ck-semver-rules.md` "The two gates"; nothing written. Tests: `tests/ConstructionKit.Engine.Tests/PublishGate/` (stub catalog, real `GitHubCatalog` over an in-memory repository, opt-in catalog replay), `PublishCommandGateTests` in the Compiler tests. Rules: `docs/ck-semver-rules.md`, "Publish gate" |

### GitHub catalog API budget (AB#6719)

`GitHubCatalog` reads are split by who needs a fresh answer: the publish-gate decision always asks the repository (indexes, compare-and-swap state);
dependency resolution, the Remote baseline and the cascade ask it only when `GitHubCatalogOptions.ApiReadsEnabled` (property/env `OctoCkCatalogApiReads`
`Auto|Enabled|Disabled`, `Auto` = on CI: `TF_BUILD`/`ContinuousIntegrationBuild`/`CI`; a token alone does not enable it, so local builds need no token). `CkBaselineResolver.ResolveAsync(..., CkBaselineSource.Local)`
never reads the API. `ReadVersionsAsync` memoises index files per path for the life of the process (`ForgetReadMemo()` on every own write and on a lost race); the whole-catalog
read is one request through `IGitHubTreeReader` (recursive tree, versions from file names; a truncated tree walks the indexes). An exhausted limit with a reset more than
2 minutes away throws `GitHubRateLimitExhaustedException` ("GitHub rate limit exhausted, resets at HH:MMZ.") instead of waiting for the 5-minute `CkCompile`/`CkRestore` timeout.
12-model wave measured with `CatalogApiBudgetTests`: 2882 -> 340 requests (docs/ck-semver-rules.md, "The API budget of the catalog reads").

## Extensible Enum Import (WI #3324)

Enums marked `isExtensible: true` allow runtime additions via the customize-API. When a new
CK model version is imported, custom extension values are preserved into the new model
revision; if a custom value collides with a CK-defined value on the same numeric key, the
custom value takes precedence and the collision is reported via `ICkModelImportAuditTrail`
so it appears in the platform event log. The preservation/override logic itself lives in the
MongoDB layer (`DatabaseCkModelRepository.PreserveExtensibleEnumValues`); the engine layer
owns the audit-trail contract and a logging default.

## Audit-Trail Architecture

Engine code surfaces noteworthy events through typed audit-trail interfaces
(`ICkModelImportAuditTrail`, `IArchiveAuditTrail`, …). All of them route through a single
host-side extensibility point: `IAuditEventSink`.

```
Runtime.Contracts/AuditTrails/
  AuditEvent       — discriminated event record (TenantId, Level, Category, Message, Metadata)
  IAuditEventSink  — single host-replaceable surface

Runtime.Engine/AuditTrails/
  LoggingAuditEventSink   — default; writes structured logs via ILogger
  NoOpAuditEventSink      — opt-in silence (tests, hosts that explicitly want events dropped)

Runtime.Engine/{CkModelMigrations,StreamData}/
  ForwardingCkModelImportAuditTrail  — implements ICkModelImportAuditTrail
  ForwardingArchiveAuditTrail        — implements IArchiveAuditTrail
  (both translate typed calls into AuditEvent and call IAuditEventSink.PublishAsync)
```

`AddRuntimeEngine` registers:

1. `IAuditEventSink → LoggingAuditEventSink` (TryAddSingleton — hosts can override)
2. `ICkModelImportAuditTrail → ForwardingCkModelImportAuditTrail` (TryAddTransient)
3. `IArchiveAuditTrail → ForwardingArchiveAuditTrail` (AddTransient)

A host that wants events in the platform event log replaces step 1 only — see
`EventRepositoryAuditEventSink` in `octo-common-services`.

**Why this shape (history matters):** WI #3324 originally landed a per-interface bridge
(`EventRepositoryCkModelImportAuditTrail`) in `octo-common-services` that ctor-captured
`IEventRepository`. That closed a DI bootstrap cycle:
`SystemContext.ctor → IDatabaseCkModelRepository → ICkModelImportAuditTrail (bridge)
→ IEventRepository → ISystemContext → …`. Host startup deadlocked in DI's `StackGuard`
(detected via `dotnet-stack` dump on a stuck integration-test agent). Routing every
audit-trail interface through one sink keeps the bridge surface a single point — the sink
implementation in common-services lazy-resolves `IEventRepository` from
`IServiceProvider`, so the bootstrap cycle cannot re-form, even if more audit-trail
interfaces are added later.

**Adding a new audit-trail interface:**

1. Define a focused typed interface in `Runtime.Contracts` (no dependency on
   `Microsoft.Extensions.Logging` or the notifications stack).
2. Add a `Forwarding{Name}AuditTrail` in `Runtime.Engine` that takes `IAuditEventSink` in
   its ctor and translates typed calls into `AuditEvent`s. Pick a stable
   `Category = "{Domain}.{Event}"` and fill the `Metadata` dictionary so structured-log
   consumers can pivot. Render `Message` in the same way as the existing forwarders.
3. Register the forwarder in `AddRuntimeEngine` next to the existing ones.

**Never** add a host-side bridge class that ctor-captures `IEventRepository` or
`ISystemContext` — that re-introduces the WI #3324 cycle. Use the sink.

`LoggingCkModelImportAuditTrail` and `LoggingArchiveAuditTrail` remain in `Runtime.Engine`
for backwards-compatible direct instantiation (e.g. the manual fallback in Mongo
`TenantContext` when no DI is wired) but are no longer the registered defaults.

## CK Models Owned by This Repository

| Model | Path | Package | Depends on | Owner of the source |
| ----- | ---- | ------- | ---------- | ------------------- |
| `System` | `src/SystemCkModel` | `Meshmakers.Octo.ConstructionKit.Models.System` | - | this repository |
| `System.StreamData` | `src/StreamDataCkModel` | `Meshmakers.Octo.ConstructionKit.Models.StreamData` | `System` | this repository |
| `System.Files` | `src/SystemFilesCkModel` | `Meshmakers.Octo.ConstructionKit.Models.System.Files` | `System-[2.5,3.0)` | this repository (since AB#6479, was octo-asset-repo-services) |

**System.Files (AB#6479).** The model source moved here from `octo-asset-repo-services` so that every consumer
(asset repository, report services, the platform-services installer) gets it as a package of the libs train.
The content is **identical** to the `System.Files-1.0.0` that asset-repo published (the model is now `System.Files-1.1.0`, see "AB#6691" below): same
YAML, same PackageId, same generated namespace
(`Meshmakers.Octo.ConstructionKit.Models.System.Files.Generated.System.Files.v1`: `SystemFilesCkIds`,
`RtFolderRoot`, `RtFolder`, `RtFileSystemItem`, `AddCkModelSystemFilesV1`). The compiled yaml freezes the
dependency range to the newest System it sees (`System-2.5.0`), so a rebuild only matches the published
1.0.0 while the System the project builds against is still 2.5.0. Do not edit the YAML without a version
bump (`octo-ckc ValidateVersion`). `SystemFilesCkModel` builds after `SystemCkModel`
(project reference, same pattern as `StreamDataCkModel`, with `OctoLocalCatalogIsEnabled=true` so that System
resolves from the freshly built local catalog). `OctoEnforceRuntimeStateMarkers` is deliberately not armed
(it would demand ownership markers on an unchanged 1.0.0). `Runtime.Engine.Tests`
(`EmbeddedCkModelResourceTests`) guards the embedded model and the id `System.Files-1.1.0`.

**AB#6691 - one-time MINOR bumps after the range-retention flip (AB#6675).** With range retention as the compiler default, the first
compile of `System.Files` (1.0.0 -> **1.1.0**) and `System.StreamData` (1.17.1 -> **1.18.0**) is classified MINOR by SemVer row D6 ("the one-time
re-pin": exact pin -> declared range), so `ValidateVersion` fails with `OCTO-CK100` until the declared version is raised
(`octo-ckc -c ValidateVersion --apply` wrote exactly these two versions; `System` has no dependencies and needs no bump). **Compat impact:** the
compiled yaml of both models now carries `dependencyRanges`, `minEngineVersion: 3.5.1` and `System@2/...` references and is published under
`ck-models/v3/`; engines below 3.5.1 do not see them (message 126 / catalog path), so every service of an environment must run the CK v2 engine
before these versions are imported. Published versions stay immutable. **Why `main` stayed green:** the `Validate CK model versions` step installs
`octo-ckc` of the build's own version line from the private feed (`Derived octo-ckc toolVersion from version line`), i.e. the compiler of the
previous main build; the flip commit itself was validated with the pre-flip compiler (`Range retention: False`, "no bump required"), a later PR
or main build derives the flip compiler and fails.

**AB#6725 - release tags needed the flip compiler pinned (pin removed 2026-10-21).** The same derivation broke the first release tag after the
flip: an `r*` tag build takes only STABLE compilers of its own line, i.e. the pre-flip 3.5.21, and the compiler of the release being built is
published only by that very build (r3.5.22, build 54764: `OCTO-CK100`, "rangeRetention true -> false" = MINOR bump for the already published,
range-retaining System.Files-1.1.0 / System.StreamData-1.18.0). The `Validate CK model versions` call therefore passed
`toolVersion: '0.1.2610.14032'` (exact main-line build with the flip). Never bump the models to get past such a gate: the old compiler would
publish them with exact pins, immutable. **The pin is removed**: the flip compiler is on the 3.5 line since r3.5.23/r3.5.27 (stable 3.5.36 is on the feed), and
the exact main-line build had been purged from the private feed, which failed every build with "Version 0.1.2610.14032 ... is not found in NuGet
feeds" (PR 298 builds 58123 and 58142). The template derives the compiler of the build's own line again (main/PR: newest 0.1.*, r* tags: newest
stable 3.5.*). Do not pin an exact main-line build again (the feed purges them); a pin is only for a schema feature no released compiler knows yet, and it
is removed in the same release train.

**Single publisher (AB#6481).** This repository's CI is the only publisher of `System.Files` to the private and
public CK catalogs and the only source of its generated model documentation: the `Validate CK model versions`/
`Publish CK models` step lists `src/SystemFilesCkModel/ConstructionKit` (after `System`), and a dedicated `CopyFiles`
step stages the `.md` files of `SystemFilesCkModel/out` for the documentation site. The yaml is deliberately **not**
copied via `handle-artifacts.constructionKitLibraryPaths`: that puts it into `ck-libraries/`, which
`release-catalogs-train` publishes to the public catalog with `-r`, i.e. a second, forcing publisher built against the
newest System of the commit. The CK gate stays the only publisher. Because 1.0.0 is already published with identical
content, the first publish from here is the idempotent "already exists" skip. The asset-repository CI stops
publishing it together with the removal of its model project (AB#6480). A search of all `azure-pipelines.yml`
files for `SystemFilesCkModel` must therefore find only this repository.

## CK Model Publishing Lanes (CI)

This repo publishes its CK models (`System`, `System.Files`, `System.StreamData`) from **inside the build**, not from a dedicated publish
step: `Directory.Build.targets`' `CkCompile` target runs `octo-ckc -c publish -c
$(OctoPublishCatalog) -r` for every project that sets `OctoPublishCkModel=true`. This repo
is special — it cannot consume its own `Meshmakers.Octo.ConstructionKit.MsBuildTasks`
package, so it bootstraps through its own `Exec`-based targets. Every other CK-owning repo
(e.g. octo-identity-services, octo-communication-controller-services) uses the packaged
targets, which do not re-enter the publish path from a test step.

**Test-only models are never published to a GitHub catalog (AB#6114).** `tests/TestCkModel`
(`Test`, `System.TestIdentity`) sets `OctoPublishCkModelToRemoteCatalog=false`, so its
`CkCompile` publishes to the `LocalFileSystemCatalog` only. The tests consume both models
embedded in `TestCkModel.dll`; nothing resolves them from a GitHub catalog. Both models were
removed from `construction-kit-libraries-build` and `meshmakers.github.io` on 2026-10-08. A new
test-only CK model project must set the same property.

Since `tpl-v1.0.0` of `meshmakers/octo-pipeline-templates` (pinned by the `ref:` of the
`pipelineTemplates` resource in `azure-pipelines.yml`, `tpl-v1.3.1` since AB#6394) no MSBuild step
publishes to a shared catalog. `update-build-number.yml` no longer exports `OctoPublishCatalog` /
`effectivePublishCatalog`. The shared catalogs are published only by
`templates/steps/validate-and-publish-ck-versions.yml`, which runs **after build and tests**, validates
System, System.Files and System.StreamData against the last published version, compiles them and publishes them
(never replacing a published version):

| Branch / ref | Catalog that receives System, System.Files, System.StreamData (Test / System.TestIdentity: LocalFileSystemCatalog only, always) |
| ------------ | -------------------------------------------------------------------------- |
| `refs/heads/main` | `PrivateGitHubCatalog` → `meshmakers/construction-kit-libraries-build` |
| `refs/tags/r<X.Y.Z>` | private **and** `PublicGitHubCatalog` |
| every other branch (`test/0.2-dev`, `dev/*`, feature branches) | validate only, nothing published. The lane catalog `meshmakers/octo-catalog-dev` needs the `publishCkModelsToLaneCatalog` opt-in; this repo does not set it. |

**Every MSBuild step that can reach `CkCompile` passes `/p:OctoPublishCatalog=LocalFileSystemCatalog`
explicitly** — `Build src`, `Build samples`, `Build test CK model`, `Build tests` and `Test` all do (the
exception the template documents for solutions whose CK models depend on each other; the local catalog is
run-isolated through `OctoLocalCatalogRootPath`). Omitting it on any one of them lets this repo's own
`Directory.Build.targets` publish with whatever `OctoPublishCatalog` the environment holds (AB#5413).

**CI build/test layout (AB#6093).** The job builds every project exactly once and the
`Test` step runs with `--no-build --no-restore`:

1. `Build src` — each `src/**/*.csproj` (Release); compiles and publishes System and
   System.StreamData. DotNetCoreCLI runs one `dotnet build` per project, so SystemCkModel is
   built twice (via StreamDataCkModel's ProjectReference and by its own invocation). The
   remote-catalog leg of `CkCompile` is therefore stamped in `obj/.../octo-ck-remote-publish/`
   by catalog + SHA-256 of the compiled yaml and skipped when the identical content was already
   published from this output (AB#6119; before: System pushed twice, ~60 s, build 50758). The
   LocalFileSystemCatalog publish still runs on every build. Look for `Skipping publish of
   construction kit library ...` in the log.
2. `Build samples`.
3. `Build test CK model` — `tests/TestCkModel` with `/p:BuildProjectReferences=false`;
   publishes Test and System.TestIdentity to the LocalFileSystemCatalog only (AB#6114). It runs before the test projects because
   `Runtime.Engine.Tests` references `TestCkModel.dll` and the DotNetCoreCLI glob order is
   not guaranteed.
4. `Build tests` — `tests/**/*Tests.csproj` except `*SystemTests`, also with
   `/p:BuildProjectReferences=false`: the src projects are already built into the same
   output folders, so only the test assemblies compile.
5. `Test` — one `dotnet test --no-build --no-restore` per project, sequentially (keep it
   that way: Testcontainers run inside the agent's DinD sidecar with a 14 Gi limit).

Before this, `dotnet test` rebuilt each test project with its full reference graph,
including a second `CkCompile` + publish of System / System.StreamData (CkCompile re-runs
on every build by design, see `Directory.Build.targets`); that was ~6 of the ~8.5 min
`Test` step. If you add a test project that references a new non-src project, add that
project to a build step before `Build tests`, or `--no-build` fails with a missing
assembly.

**Debugging "my model is missing from PrivateGitHubCatalog".** Check the *publish* and the
*read* halves separately; they fail for completely different reasons:

1. Publish: `git log` in `meshmakers/construction-kit-libraries-build` and
   `ck-models/v2/<letter>/<Model>/<major>/catalog.json` (`latestVersion`, `publishedAt`).
   Correlate the timestamp with the CI build. If the commit is there, the pipeline did its
   job and nothing in this repo is at fault.
2. Read: the catalog is served over GitHub Pages, whose deploy workflow serializes a burst
   of publishes and only deploys the final push, so the live tree can lag a cascade by
   many minutes (AB#4872). On top of that, readers keep a cache at
   `~/.octo/ck-catalog/cache/private-github-catalog-cache*.json` (60 s max age;
   repository-scoped file name only on engines carrying the AB#5412 fix).
3. Which repository is the private slot bound to? `octo-cli -c ListCatalogs` prints the
   coordinates in the description (AB#5139) — the main installations read
   `construction-kit-libraries-build`, the 0.2-dev instance reads `octo-catalog-dev`,
   where `System` still tops out at 2.2.2.

## Sibling CK Models and the Local Catalog (AB#5661)

A CK model project that depends on another CK model **built in the same repository / build**
(System.StreamData → System) must follow two rules, or a parallel build can compile the dependent
against a stale or missing version of its sibling:

1. **Ordering — ProjectReference.** The dependent project references the dependency's project
   (`StreamDataCkModel.csproj` → `SystemCkModel.csproj`). `CkCompile` runs as the first dependency of
   `PrepareResources`, i.e. after `ResolveProjectReferences` built (and, with `OctoPublishCkModel=true`,
   published to the `LocalFileSystemCatalog`) the referenced model. Without the reference MSBuild may
   build both in parallel.
2. **Visibility — local catalog enabled.** The dependent sets `OctoLocalCatalogIsEnabled=true`, otherwise
   octo-ckc only sees the remote catalog caches, which know only already-published versions (AB#5532).

**The local catalog answers lookups from disk, not from its cache file.** `LocalFileSystemCatalog`
overrides both `IsExistingAsync` overloads to enumerate the compiled files
(`ck-models/v2/<letter>/<Name>/<major>/ck-<name>-<version>.json`) instead of reading
`<root>/cache/local-catalog-cache.json`. That cache file is shared by every octo-ckc process of a
parallel build, and a concurrent publish could overwrite it with a snapshot taken before a sibling model
was published; the dependent compile then failed with `Dependencies 'System-[2.4,3.0)' are unknown`
although the manifest was on disk (2026-10-06), or resolved a wide range to an older version (the
stale-pin class of AB#5432 / AB#5359). `ListAsync` / `SearchAsync` still use the cache (listing only).
Pinned by `LocalFileSystemCatalogTests.IsExistingAsync_WithVersionRange_StaleCache_*`.

**Publish is atomic (review M10).** Because lookups trust file existence, `PublishAsync` writes the model to a
temp file *in the target directory* (`.ck-<name>-<ver>.json.<guid>.tmp`, never matched by the `ck-*.json`
enumeration) and renames it into place (`File.Move(..., overwrite: true)`; `File.Replace` on netstandard2.0),
deleting the temp file in all cases. The former `File.Copy(temp, target, overwrite)` rewrote the target in place, so
a parallel sibling compile could read a half-written model — and a concurrent reader even made the publish fail
("being used by another process"). Pinned by `PublishAsync_ForcedRepublish_ReaderNeverSeesAPartialFile_*`.

**Index files are atomic and serialized (review N3).** The three `catalog.json` index files (root, model library,
major versions) are read-modify-write. `PublishAsync` now updates them under a cross-process lock
(`ck-models/v2/.catalog-index.lock`, exclusive open = `flock` on Unix, `CatalogFileIo.AcquireLockAsync`) and every
index write — and the shared cache file (`CachedCatalog.WriteCacheAsync`) — goes through
`CatalogFileIo.WriteJsonAtomicallyAsync` (temp file in the same directory, renamed into place, temp always
deleted). Before, parallel publishers of sibling models lost each other's entries (models vanished from
`ListAsync` / `SearchAsync`) and readers could hit a half-written index. Pinned by
`PublishAsync_ParallelPublishersOnOneRoot_KeepEveryIndexEntry` (64 parallel publishers; red 3/3 before).

**The empty `ck-models/v2/.catalog-index.lock` file is expected.** It is created on the first publish and left in
place on purpose: the lock is the exclusive *open* of the file (released when the handle closes, also when a process
dies), not its existence, so a leftover file never blocks anyone. Deleting it after use would race with a waiting
publisher that already opened it. It is safe to delete while no build is running; nothing reads its content. The
leading dot keeps it out of the `ck-*.json` / `catalog.json` lookups. Temp files of an interrupted write
(`.<name>.<guid>.tmp` next to the target) are never read either and can be deleted at any time.

**Two catalog roots: `ck-models/v2/` and `ck-models/v3/` (F1.1-S6, AB#5909).** `CkCatalogLayout` decides where a
model is published: classic models (`ckLanguage` 1, exact pins) go to `ck-models/v2/`, `ckLanguage: 2` and
range-retaining models (and anything carrying `minEngineVersion`) to `ck-models/v3/`. Engines before CK v2 read
only `v2` and tolerate unknown JSON properties, so the separate root is what keeps them from misreading a v2
model. Engines from CK v2 on read both (`ReadRoots` = v3, then v2): `LocalFileSystemCatalog` lookups enumerate
both directories, the refresh merges both index trees (a model may have versions in both), and `GitHubCatalog`
takes the root from the cached `filePath`, otherwise probes v2 then v3 — v3 first only when the cache holds v3 models
(review G3 E-L4: no extra 404 per v1 model). A missing v3 root index is normal and is not retried, so it does not
spend the AB#4872 404-retry budget. Each root has its own `catalog.json` tree and its own `.catalog-index.lock`.
A version lives in one root only: publishing it into the other root needs `force` and deletes the old file — in the
local **and** the GitHub catalog (review G3 E-M5). Known limitation (E-L3): the old root's `catalog.json` keeps a
stale listing entry for a moved version (listing only; lookups use the files). The catalog CI side (Pages deploy of `ck-models/v3/`) is F2.3.

**`minEngineVersion` (F1.1-S6, AB#6390).** The compiler writes `CkCompiledModelRoot.MinEngineVersion` =
`CkEngineVersion.CkV2MinEngineVersion` (**`3.5.1`**, the first libs release with the full CK v2 Phase 1 reader; a
deterministic constant — not the compiling engine's own version, so DebugL and release builds produce the same
output) for `ckLanguage: 2` and range-retaining models, **and the highest `minEngineVersion` of its resolved
dependencies** (review G3 E-M4: a v1 model on a v2 or range-retaining dependency gets it too and therefore lands in
`ck-models/v3/`, so an older engine never sees a model it cannot resolve); `null` otherwise (v1 output on v1
dependencies byte-identical). Models published earlier keep their stored value (published models are immutable; the
older `3.4.0` value stays valid for them). `ElementResolver` and both dependency resolvers refuse a model above the
running engine's version with **message 126** (`CkModelRequiresNewerEngine`, names the running engine as
major.minor.patch; a dependency is skipped like a 91): a 3.4.x or 3.5.0 engine refuses a v2 model, 3.5.1+ reads it.

**The running engine version is patch-exact.** `set-version.yml` stamps the AssemblyVersion as `Major.Minor.0.0`, so
it cannot tell 3.5.0 from 3.5.2. `CkEngineVersion.ReadRunningVersion` therefore reads, in this order, the
`AssemblyFileVersion` (`$(BuildNumberLong)`: `3.5.2.0` on an `r3.5.2` build, `0.1.YYMM.NNNN` on main, `3.5.N.R` on a
`test/3.5-*` build), then the `AssemblyInformationalVersion` (suffixes `-branch` and `+sourcelink` stripped), then
the AssemblyVersion; the first value that parses wins and nothing throws. The FileVersion is preferred because it is
always purely numeric. No pipeline-template change is needed. (`test/X.Y-*` lane builds report
`X.Y.<buildCounter>`, not a release patch.)

**Defined rules for non-release engines.** DebugL is `999.0.0` (accepts every model it can compare, refuses
`1000.0.0`). **Engines below 1.0 (main line, private-feed `0.1.*` builds) skip the check** — an explicit rule in
`CkEngineVersion.IsSatisfiedBy` (and `Current` is `null` for them): the main line carries no number comparable with
the release line, it is covered by the main-line floor `0.1.2610.9010` of
`octo-mesh-deployment/docs/ck-v2-engine-inventory.md` and by rebuilt images. **Tests of message 126 must not depend
on the ambient assembly version** (DebugL `999.0.0`, CI `0.1.*` = check skipped, r-tag builds `3.x`): pin it
with the internal test seam `using var _ = CkEngineVersion.OverrideCurrentForTests(new Version(3, 4, 149));`
(AsyncLocal, restored on dispose, never set in production; AB#6274; visible to Engine.Tests, Compiler.Tests and
the engine-mongodb `Runtime.Engine.MongoDb.IntegrationTests`), or pass `engineVersion` to `CheckModel`
directly. `CkEngineVersionTests` cover the version table (3.4.149/3.5.0 refuse, 3.5.1/3.5.2/3.6.0/999.0.0 accept,
0.x skips), the stamping (a dynamic assembly with AssemblyVersion `3.5.0.0` and FileVersion `3.5.2.0` reads 3.5.2)
and the unparsable fallback. Raise the constant when a later engine writes features this engine line cannot read.
`CatalogService.PublishAsync` resolves before it publishes, so it refuses such a model, too.

**Fail fast with the visible versions.** When a dependency range cannot be satisfied,
`CatalogDependencyResolver` lists the versions each readable catalog knows for that model
(`... 'System-[2.5,3.0)' does not match any visible version of System (LocalFileSystemCatalog: 2.4.0)`)
and names the ProjectReference rule (`DependencyResolutionMessageTests`).

Other repositories with sibling CK models must follow the same two rules (checked 2026-10-07:
`octo-construction-kit` — `Basic.Energy` and `EnergyCommunity` depend on `Basic` / `Basic.Energy`
without a ProjectReference; `octo-construction-kit-engine-mongodb` test models depend only on
System).

## CK v2 Range Retention (AB#5664 / AB#5665, Phase 1 F1.1-S2 AB#5905 — compiler default since AB#6675)

Range retention is the **compiler default**: every compile (MSBuild `CkCompile` and `octo-ckc Compile`, CK
language 1 and 2) writes the retained ranges. The switch is gone: the MSBuild property / environment
variable `OctoCkRangeRetention` and `octo-ckc -rr` are **obsolete and ignored** — a build that still sets
them logs the deprecation warning **`OCTO-CK300`** (`CkCompile` task warning; `ObsoleteOptionFilteringEnvironmentService`
strips `-rr` from the ckc arguments and warns on stderr). `CkCompilerOptions.RangeRetention` (default `true`)
remains only as a code seam for tests and re-pin tooling (`RangeRetention = false` = classic exact pins).
Compiled output of unchanged sources therefore differs (added `dependencyRanges`, `minEngineVersion: 3.5.1`,
`Name@major/` references); the first publish of an existing model needs a MINOR bump (SemVer row D6).
Audit sources before rebuilding: `octo-ckc -c AuditRanges -p <dir>` / `scripts/audit-ck-ranges.sh` (AB#6683).
Merge/release only after the O1 lift (AB#6279).

**Compile side (`CatalogModelResolver.CompileAsync` → `ApplyRangeRetentionAsync`).** The model is still
resolved and validated against the *highest* catalog version (the returned graph stays concrete — the
source-generator cache is unchanged). The **output** is a JSON-round-trip copy
(`CkCompiledModelCloner`) in which:

- `dependencyRanges: [{range, floor}]` lists every *declared* dependency; `floor` = declared range lower
  bound (`System-[2.4,3.0)` → `2.4.0`), never the highest catalog version;
- every reference into a dependency is **major-qualified and model-version-less**: `System@2/Entity-1`
  (`CkModelId.IsMajorQualified`, `CkModelId.MajorQualified()`, `ToMajorQualified()`); own references
  stay concrete (`Basic-2.4.0/TreeNode-1`);
- `dependencies` keeps the exact closure (legacy readers, pre-publish check, SemVer diff).

**One major per range (review H6).** References are stored `Name@<major>` of the floor's major, so with range
retention every declared range must stay inside that major: `System-[2.5,3.0)` is fine, `System-2.5`
(= `>=2.5.0`), `System-[2.0,)`, `System-[2.5,3.0]` and `System-[2.5,4.0)` are rejected at compile time
("… admit more than one major version …"). Chosen over storing a per-major binding because it is the simpler
safe option: a new major is a deliberate cascade anyway (concept §4.3.2). Flag off is unaffected. An exclusive
lower bound (`(2.4,3.0)`) gets the next patch as floor (`2.4.1`, review L13) so the floor is inside the range.

Floor check ("compile against the floor, verify against the highest"): every element the model
references in a dependency must exist in the floor version (or, if the floor itself was never
published, the lowest available version in the range), else `ModelValidationException`
"references elements that do not exist at the floor of its dependency range …".

**Resolve side (both catalog and repository resolvers).** `CkCompiledModelRoot.GetResolutionRanges()`
returns the effective ranges (`CkModelDependencyDto.GetEffectiveRange()`: range with the lower bound
raised to the floor) for range-retaining models and the exact pins for classic models — classic models
keep their exact-match semantics. Dependency resolvers expand children through it, load each resolved
model once (different ranges resolve to the same installed version), and bind major-qualified references
to the resolved version of the same name and major (`CkReferenceRewriter.BindMajorQualified`) before
`AppendModel`. Root models (`HardResolveAsync(CkCompiledModelRoot)` / `SoftResolveAsync`) are bound **on a
copy**, so an import persists the version-less form. A reference whose major is not installed stays
unbound and fails reference resolution.

**Source generation (D1).** The compiled yaml of a range-retaining model holds `System@2/...`, the compile
cache the generator restores (`obj/octo-ck-cache/*.json`) holds the concrete versions the model was resolved
against. `CkSourceGenerator` binds the references to the cache's model ids (`CkGenerationModelBinder`, using the
now public `CkReferenceRewriter`) before any per-element generator looks them up; without it every service model
that assigns a dependency attribute failed with `OM1003 CkAttributeId 'System@2/Enabled-1' not found in CkCache`
(first System.Bot). Pinned by `RangeRetentionSourceGenerationTests` (Compiler.Tests links the dependency-free
generator files). Service repos pick the fix up only with a new `Meshmakers.Octo.ConstructionKit.SourceGeneration`
package.

**Transitive references are floor-checked (review L14).** A model may reference a model it does not declare
(`Industry.Energy` uses `${Basic}` but declares only `Industry.Basic`). Its references are still rewritten to
`Basic@2/...`, and `CatalogModelResolver.CollectTransitiveDependenciesAsync` derives the guarantee for them from the
resolved intermediate models — their range-retaining dependency, or their exact pin as `[v]` with floor `v`; the
highest floor wins. The same floor check as for declared dependencies runs against it; a miss says the dependency is
transitive, names the intermediate and asks to declare the model in `ckModel.yaml`. No `dependencyRanges` entry is
written for transitive models (declared ranges stay verbatim). Pinned by
`FlagOn_TransitiveReference_IsFloorCheckedAgainstTheIntermediateRange`.

**Unbound references in source generation (D1 diagnostic, OM1004).** When a major-qualified reference is still
unbound after `CkGenerationModelBinder.BindToCache` (the compile cache holds no version of that model and major), the
generator reports `OM1004 Unbound major-qualified CK reference` naming the references and the cached model versions,
instead of the opaque `OM1003 … not found in CkCache`. Pinned by `UnboundReference_IsReportedWithReferenceAndCacheContent`.

**Range identity in the resolvers (D2).** `CkModelIdVersionRange.Equals` means *overlaps* (not transitive,
inconsistent with `GetHashCode`). `DependencyRangeMatcher` therefore matches structurally (same name, identical
`CkVersionRange`) whenever range retention is involved — a child range of a range-retaining dependency, or the root
ranges of a range-retaining root (`RangeRetainingDependencyRanges`: compile with the flag on, hard resolve of a
range-retaining compiled model). **Classic children of a classic root keep main's overlap merge** (review G3 E-H1):
with the flag off a v1 compile behaves exactly like main (`V1DependencyMergeTests`, verified against main 0db1c50 —
a library pinned to System-2.5.0 next to System 2.6.0 fails with "unknown base type", as on main, not with
error 66). With range retention `System-[2.5,3.0)` overlaps
both exact pins `System-[2.5.0]` and `System-[2.6.0]`; the overlap lookup threw "Sequence contains more than
one matching element" when a System minor was imported into a tenant holding exact-pinned and
range-retaining models (or silently merged the range into the wrong exact entry). Pinned by
`RangeRetentionRepositoryResolverTests`. `Equals` itself is unchanged (other callers rely on it).

**One version per dependency model (AB#6671, replaces the former limitation N4).** A range-retaining resolve of the
catalog (`CatalogDependencyResolver` with `RangeRetainingDependencyRanges` roots: compile with the flag on, hard
resolve of a range-retaining compiled model) no longer resolves every queued range on its own. `CatalogConstraintSolver`
collects every constraint of one model **name** over the whole closure — ranges and the exact pins of classic
dependencies — and chooses the **highest catalog version that satisfies all of them** (iterated to a fixed point,
because a chosen model contributes its own constraints; no backtracking over versions of dependents). Example:
`System-[2.5,3.0)` next to a classic dependent pinned to `System-[2.5.0]` with System 2.5.0 and 2.6.0 in the catalog
resolves System once, to 2.5.0 (before: error 66). When no version satisfies all constraints the compile fails with
`ModelValidationException.UnsatisfiableDependencyConstraints`, which names every constraint, who requires it and the
versions the catalogs show. In that mode the child ranges are entries of the walk matched structurally (the overlap
merge of `DependencyRangeMatcher` is not used). Classic resolves (flag off, classic roots) keep the old walk and the
overlap merge byte for byte (`V1DependencyMergeTests`). The repository side is unaffected: a tenant has exactly one
installed version per model, so an exact pin fails on its own (`ResolveFailed`) and a range resolves. Pinned by
`RangeRetentionCompileTests` (`…ResolveSystemOnce`, `…ConstraintsNoVersionSatisfies…`, `…ThreeLevelChain…`,
`…HighestWins`).

**Resolution by SemVer, the matrix (AB#4468).** Range `System-[2.5,3.0)` (floor 2.5.0) against the available versions:
`{2.5.0}` → 2.5.0; `{2.6.1}` → 2.6.1; `{2.4.9}` and `{3.0.0}` → rejected with a message naming the range and the visible
versions; `{2.5.0, 2.6.1, 3.0.0}` → 2.6.1 (highest in range). The same table runs through the catalog/compile resolver
(`DependencyResolutionMatrixTests`, Compiler.Tests; with and without range retention), the repository resolver
(`RepositoryResolutionMatrixTests`, Engine.Tests) and the tenant re-validation (engine-mongodb). The repository
side lists the installed versions through the optional `IRepositoryVersionLister` (implemented by
`RepositoryManagementService` → `IModelRepository`, by `DatabaseCkModelRepository` in engine-mongodb); a repository without
it reports the range without versions. **No special treatment for System and Meshmakers-shipped models.** Blueprint floor
semantics are not changed (AB#6672). Rules page: `docs/ck-semver-rules.md`, "Dependency resolution by SemVer".

**Range audit (AB#6683).** The one-major-per-range rule lives in `CkDependencyRangeRule` (`FloorOf`, `IsWithinOneMajor`), called by
`CatalogModelResolver` and by `CkModelRangeAudit` / `octo-ckc -c AuditRanges -p <dir>` (read-only scan of `ckModel.yaml` files, non-zero
exit when a range is rejected) and `scripts/audit-ck-ranges.sh <monorepo-root>` (all repositories with CK sources, list in
`docs/ck-semver-rules.md`, "Range audit"). `RangeAuditTests.Rule_AgreesWithTheRangeRetainingCompile` pins audit = compiler.

The CK SemVer diff does not classify `DependencyRanges` yet (documented exclusion, Phase 2 F2.1). The
element schemas accept `@` in the model part of a reference; the compiled schema accepts
`dependencyRanges`. Tests: `RangeRetentionCompileTests` (flag on/off, YAML round trip with schema
validation, floor violation, resolve against a later minor without recompile, two-level chain).

## Versioned generated CK ids at run time - guard OM1005 (AB#6673)

Rule (also in the monorepo `CLAUDE.md`): runtime code never looks up CK elements by a generated **versioned** id. The generated
`<Model>CkIds` classes carry both forms per element: `RtCk<X>TypeId` / `RtCk<X>RoleId` / ... (version-less `RtCkId<T>`, what the CK
cache is queried with) and `Ck<X>TypeId` / ... (`CkId<T>` bound to the model version the service was compiled against). A tenant may
hold a **newer** System than the service embeds (the embedded-import downgrade guard keeps it), so the versioned id is not in the
tenant's CK cache - an entity insert of an older service failed that way (AutoIncrement, F1.0). `CkModelId` (the embedded model id
for import / `IsCkModelSatisfiedAsync`, which compares by name and minimum version) is not an element id and stays allowed.

Guard: the Roslyn analyzer `CkVersionedIdAnalyzer` (project `src/ConstructionKit.Analyzers`) reports **OM1005** for every
reference to a `…CkIds.Ck<Element>Id` field of type `CkId<T>`; allowed are the `RtCk…` constants, `CkModelId`, and a versioned id that is
converted at once with `.ToRtCkId()`. Not analysed: generated code and assemblies whose name ends with `Tests`. It ships inside the
`Meshmakers.Octo.ConstructionKit.Contracts` package (`analyzers/dotnet/cs`), so every project that uses `CkId` / `RtCkId` gets it.

**Severity and rollout (review of PR #278).** OM1005 is **Info (suggestion) by default**: the package never breaks a consumer build, not
even with `TreatWarningsAsErrors` (a Warning would break it too, an Error certainly). A repository raises it after it has audited itself:
`.editorconfig` `dotnet_diagnostic.OM1005.severity = error` (or `warning`), or `<WarningsAsErrors>$(WarningsAsErrors);OM1005</WarningsAsErrors>`
in `Directory.Build.props` (verified: both turn the Info diagnostic into a build error, with `TreatWarningsAsErrors` on or off);
`= none` opts out. Sequence: (1) audit the consumers (git grep `\w+CkIds\.Ck(?!ModelId\b)\w+`, see `docs/ck-versioned-id-guard.md`), fix
or list the offenders; (2) release the package - consumers see a suggestion only; (3) each repo that is clean sets the severity to `error`
in the same PR as its package update. Tests: `CkVersionedIdAnalyzerTests` (Roslyn `CompilationWithAnalyzers`: reported with the version-less
replacement, every occurrence, allowed forms, test projects and generated code skipped, default severity Info, raised to error and
switched off through the specific diagnostic options). Verified end to end with a real `dotnet build` of a consumer project with
`TreatWarningsAsErrors`: default builds green, `WarningsAsErrors=OM1005` and the `.editorconfig` line fail with OM1005.

## Proof: additive System releases need zero dependent bumps (AB#6676, Epic AB#5584 F2.4)

`scripts/prove-additive-system-releases.sh` (helpers in `scripts/ab6676/`, write-up and evidence in `docs/ck-additive-system-proof.md`) is the
repeatable scenario behind the Phase 2 exit criterion "two consecutive additive System releases require zero dependent bumps, a breaking
change is caught". It is **parameterised** (`--system-base`, compiler paths, tenant, context), **rerunnable** (work directory and tenant are recreated)
and **restores the baseline** (throw-away tenant deleted; the stack's local catalog is only touched with `--guard-test` and is restored byte for byte).

- **Offline phase** (no stack, no network): `octo-ckc` runs with a throw-away `HOME` whose `settings.json` switches both GitHub catalogs off, and every call
  gets `-lcr <private catalog>`. It compiles System plus the dependents (the models of `octo-construction-kit` and the service models) from the
  monorepo sources into a private catalog, derives System n+1 / n+2 (additive: a new optional attribute on `Entity`, later also a new enum) and a breaking
  n+3 (removes `DisplayName`, declared MINOR on purpose) with `scripts/ab6676/proof_lib.py make-release`, and prints per release: `ValidateVersion`,
  the `ValidateCascade` verdicts (forced bumps = `NeedsRepin`) and the number of dependents whose build output changes when they are recompiled
  **without editing their `ckModel.yaml`** (the resolved `dependencies:` closure line is reported separately, SemVer row D8). With
  `--classic-ckc <compiler built before engine commit 8f69cca1>` the same models are compiled with exact pins: that is the "before" row (v1 baseline).
  Also: breaking System n+3 and Basic n+1 (`ValidateVersion` OCTO-CK100, cascade names the dependents) and five adversarial cases.
- **Tenant phases** (need the local stack: `Start-Octo -nonInteractive $true ... -meshAdapterTenantId <tenant> -meshAdapterId 670000000000000000000002`,
  `octo-cli`, a user context that may create/delete tenants): create a tenant, install the dependents (`ImportFromCatalog`), import System n+1 / n+2
  with `ImportCk -f`, and after each: `LibraryStatus` (Available / ResolveFailed counts), login, a pipeline run on the local mesh adapter without redeploy
  (upserts a `Basic/Tree`), a runtime query, a bot job and a scan of the service logs for `not found in CkCache` / `missing dependencies` /
  `marked as ResolveFailed`. `--restart-cmd` restarts the stack after the tenant exists (the mesh adapter must register for it) and after n+2 (service
  restart = embedded import with the downgrade guard: "downgrade prevented" in the log). `--guard-test` publishes the proof releases temporarily into the
  stack's local catalog and runs `CheckUpgrade` / `ImportFromCatalog` (the tenant upgrade guard of F2.5).
- Never use it against a shared environment. The forced `ImportCk -f` of the breaking System release (`--forced-file-import`) leaves the tenant
  unloadable (finding F1 in the doc): throw-away tenant only, and it needs a manual database cleanup afterwards.

## CK v2: interfaces, attribute access, method definitions, visibility/derivable (AB#5667 / AB#5668 / AB#5669; Phase 1 F1.1-S1 AB#5904, F1.2-S1 AB#5910, F1.1-S4 AB#5907, F1.1-S5 AB#5908)

Gated by **`ckLanguage: 2`** in `ckModel.yaml` (`CkModelPropertiesDto.CkLanguage`, `null` = 1). A model
without the key compiles byte-identical to the engine before CK v2 — compiled YAML **and** CK cache JSON
(`CkV1CompileOutputUnchangedTests` against the frozen golden in
`tests/ConstructionKit.Compiler.Tests/sampleData/v1Golden`, produced by octo-ckc built from `main` — 6189ef1d,
re-verified byte-identical against `origin/main` 01fb187 on 2026-10-08; the comparison covers the compiled YAML, the
cache JSON and the catalog JSON of a model without and one with a dependency).

**Methods are definitions only.** Phase 1 ships the method meta-model (schema, DTOs, graphs, cache, compiler rules,
`CkMethodIds`, generated method-id constants). There is no invocation, dispatch or handler runtime in the engine;
that is Phase 3.

| Key | Where | Contracts |
| --- | ----- | --------- |
| `minEngineVersion` (F1.1-S6) | compiled model only (`major.minor.patch`) | `CkCompiledModelRoot.MinEngineVersion`, `CkEngineVersion` (`CkV2MinEngineVersion`, `GetRequiredMinEngineVersion`, `IsSatisfiedBy`, `CheckModel`), `CkCatalogLayout` |
| `ckLanguage` | `ckModel.yaml` (enum 1/2), compiled model (integer ≥ 1, so a higher version reaches message 91 instead of a schema error) | `CkModelPropertiesDto.CkLanguage`, `EffectiveCkLanguage`, `MaxSupportedCkLanguage` |
| `interfaces` | new folder `interfaces/*.yaml` (`CompilerStatics.InterfacesFolder`), schema `construction-kit-elements-interface.schema.json` | `CkInterfaceId` (+ STJ/YAML/Newtonsoft converters, `CkIdInterfaceIdConverter`, `RtCkIdInterfaceIdConverter`), `CkInterfaceDto`, `CkInterfaceAttributeDto`, `CkModelRootBase.Interfaces`, `CkElementsRootDto.Interfaces`, `CkInterfaceGraph`, `ICkModelGraph.Interfaces` / `InterfacesByRtCk` / `GetOrCreateInterface`, `CkCacheRoot.Interfaces`, `ICkCacheService.GetRtCkInterface` / `GetRtCkInterfaces` |
| `implements` | `CkType` / `CkCompiledType` | `CkTypeDto.Implements` (`List<CkId<CkInterfaceId>>`, JSON via `CkIdInterfaceIdListConverter`), `CkTypeGraph.DeclaredImplements` / `AllImplementedInterfaces` |
| `access` | `CkTypeAttribute` (types, records, association roles) | `CkAttributeAccessDto` (`ReadWrite`/`ReadOnly`/`MethodOnly`/`Hidden`), `AttributeAccess` predicates, `CkTypeAttributeDto.Access` (nullable), `CkTypeAttributeGraph.Access` (effective, init setter like `Ownership`) |
| `visibility` | `CkType`/`CkCompiledType`, `CkRecord`, `CkEnum`, `CkAttribute`, `CkAssociationRole`, `CkInterface`, `CkMethod` (enum `Public`/`Internal`) | `CkVisibilityDto`, nullable `Visibility` on `CkTypeDto`, `CkRecordDto`, `CkEnumDto`, `CkAttributeDto`, `CkAssociationRoleDto`, `CkInterfaceDto`, `CkMethodDto`; effective (non-null) `Visibility` on `CkTypeGraph`, `CkRecordGraph`, `CkEnumGraph`, `CkAttributeGraph`, `CkAssociationRoleGraph`, `CkInterfaceGraph`, computed on `CkMethodGraph`; `CkModifiers.ResolveVisibility` (omitted = `Public`) |
| `derivable` | `CkType`/`CkCompiledType`, `CkRecord` (enum `Model`/`Any`) | `CkDerivableDto`, nullable `Derivable` on `CkTypeDto` / `CkRecordDto`, effective `Derivable` on `CkTypeGraph` / `CkRecordGraph`; `CkModifiers.ResolveDerivable` — **omitted = `Any` in a v1 model, `Model` in a `ckLanguage: 2` model**, resolved per declaring model by `CkModelGraph.ApplyCkV2Modifiers` (called by `AppendModel` and `ElementResolver.Resolve`) |
| interface `extends` / `associations` / `methods` / `deprecated` (F1.1-S5) | `CkInterface` (`attributes` no longer required; an interface may consist of `extends` only). Association member `CkInterfaceAssociation` = `{ id, targetCkTypeId \| targetCkInterfaceId, multiplicity?, isOptional }`; methods use the type method schema | `CkInterfaceDto.Extends` / `Associations` / `Methods` / `Deprecated` (nullable, `deprecated` omitted = false), `CkInterfaceAssociationDto`; `CkInterfaceGraph.DeclaredExtends` / `AllExtendedInterfaces` (transitive) / `AllAttributes` (own ∪ extended — implementations are checked against it) / `DefinedAssociations` / `AllAssociations` (`CkInterfaceAssociationGraph` with the declaring interface) / `DefinedMethods` / `AllMethods` (`CkInterfaceMethodGraph`) / `Deprecated` |
| type association `targetCkInterfaceId` (F1.1-S5) | `CkType`/`CkCompiledType` `associations[]` | `CkTypeAssociationDto.TargetCkInterfaceId`, `CkTypeAssociationGraph.TargetCkInterfaceId` (settable, null omitted). **Deviation from the contract table:** `targetCkTypeId` stays required — the interface **narrows** the target (target derives from `targetCkTypeId` and implements the interface; `${System}/Entity` = any implementor). Keeps the non-null `TargetCkTypeId` every runtime/GraphQL consumer relies on |
| `methods` | `CkType` / `CkCompiledType`, schema `construction-kit-elements-method.schema.json` | `CkMethodDto` family (`CkMethodKindDto`, `CkMethodParameterDto`, `CkMethodResultDto`, `CkMethodErrorDto`, `CkMethodAuthorizationDto`, `CkMethodExecutionDto`), `CkTypeDto.Methods`, `CkTypeGraph.DefinedMethods` / `AllMethods` (key = method id), `CkMethodGraph` (`QualifiedMethodId`, `TimeoutSeconds`), `CkMethodIds.Qualify` / `TryParse` (`System.Identity/User.ChangePassword-1`; `TryParse` never throws — an invalid element id wrapped in `TargetInvocationException` by `Activator` returns false, review L1) |

Notes:
- Interface ids always carry their version on the wire (`Named-1`): the YAML converter writes `FullName`,
  unlike type ids, because the version is the contract version and the schema requires it.
- F1.2-S3 visibility/derivable enforcement runs in `ReferenceResolver`, which compile **and** every import
  (`CatalogModelResolver` / `RepositoryModelResolver.HardResolveAsync`) execute, so a forged or old-compiler model
  is refused on import (pinned by `CkV2VisibilityCompileTests.ForgedBase_IsRefusedWhenTheDependentIsResolved`).
  The importing model is refused. **Known limitation (review G3 D-M1):** the re-validation of already installed
  dependents (`RepositoryModelResolver.SoftResolveAsync`) only collects inheritance failures, so a dependent of a
  base that became `internal` / `derivable: Model` stays `Available` until it is itself re-imported.
- F1.2-S2 Hidden parity (review N5/N6/M12): index and derived-rule paths are resolved **case-insensitively**
  (`InheritanceResolver.WalkAttributePath`; `passwordHash` reaches `PasswordHash`, as in Mongo). **Hidden on archived
  types (M12)** cannot be a compile rule: archives are runtime entities (`System.StreamData` `Archive` with
  `TargetCkTypeId` + column paths). The rule — **no archive column may reach a Hidden attribute** — is implemented by
  `ArchiveHiddenColumnGuard` (Runtime.Engine): a path reaches Hidden when a segment is Hidden **or when it ends at a
  record / record array whose record, transitively, contains a Hidden sub-attribute** (a whole-record column stores
  it; review G3 E-M2). Case-insensitive, `[*]` ignored, rollup and computed columns skipped. `ArchiveLifecycleService`
  applies it on activation, retry and re-provisioning (`HiddenAttributeInArchiveException`), and
  `RevalidateAccessAsync(archiveRtId)` sets an **active** archive to `Failed` (stops ingesting) when a model change
  made one of its columns reach Hidden. **Wiring owed by the consumers:** the mongodb `TenantContext` passes the CK
  cache, calls `RevalidateAccessAsync` for the tenant's archives after a CK model import, and the ingest/column
  builder (`ArchivePathTypeResolver.BuildRecordObject`) skips Hidden sub-attributes like Secret ones; the stream-data
  query paths (asset-repo) refuse columns for which `ArchiveHiddenColumnGuard.FindHiddenAttribute` returns a value.
  It needs the optional `ICkCacheService` constructor argument — the mongodb
  `TenantContext` must pass it; without it the check is skipped. **Models imported before F1.2-S2** that violate
  106–109 now fail on repository load (cache rebuild) — only dev tenants that imported pre-release CK v2 models can have such models.
- F1.1-S5 resolution: `InheritanceResolver.ResolveInterfaceHierarchy` computes `AllExtendedInterfaces` depth first
  (unknown entries and cycles are skipped there and reported as 118 by the compiler rules), and implementing an interface implements every
  interface it extends (`CkTypeGraph.AllImplementedInterfaces`, `ImplementingTypes`). The "exactly one target" rule
  of an interface association and "an interface needs at least one member" are compiler rules (120, 123), not schema
  `oneOf`/`anyOf`: the schema validator reports no message for a failed `anyOf` branch set.
- `visibility` / `derivable` values are PascalCase (`Public`/`Internal`, `Model`/`Any`), consistent with `access`;
  lowercase is a schema error. They are enforced across models by `CkVisibilityValidator` (112, 113).
- The CK cache JSON omits the CK v2 members while they hold their default (empty collections, access
  `ReadWrite`, visibility `Public`, derivable `Any`) through a `JsonTypeInfo` modifier in `CkCache` (`OmitCkV2Defaults`), so v1 caches stay
  byte-identical; reading tolerates the missing keys (trailing defaulted `[JsonConstructor]` parameters on
  `CkTypeGraph`, init setter on `CkTypeAttributeGraph.Access`).
- Message codes for CK v2: 90–113 and 118–134 are in use (table below); **spare: 111, 114–117, 135**. Range
  retention (F0.2) has no message codes of its own — its failures are `ModelValidationException`s and the
  unbound-reference diagnostic is the source-generator diagnostic OM1004.
  Warnings (e.g. 110, 124) of a successful compile are printed by `octo-ckc -c compile` (to **stderr**, also with
  `-v none`, so stdout keeps only the `-cr` file path) and by the `CkCompile` MSBuild task (`Log.LogWarning`; not
  promoted by `TreatWarningsAsErrors`, which applies to the C# compiler only). Known v1 sources hitting 110:
  `octo-construction-kit/src/ConstructionKits/Octo.Energy.Demo/ConstructionKit/types/UkDale*.yaml` (top-level
  `attributes:` in `types/` — were silently ignored before); that project is not in `Octo.ConstructionKit.sln` nor in
  any pipeline, so no build breaks.
  `MessageCodes.cs` is generated from `MessageCodes.json` by `MessageCodes.tt` (not part of the build);
  `MessageCodesSyncTests` fails when the two tables differ in key, number, level or text, or a number repeats.

### Compiler rules and message codes

**Unknown type / record hints (AB#6987).** The messages `OCTO-CK018` (unknown association target type), `OCTO-CK019`
(unknown type) and `OCTO-CK034` (unknown record), and the `ModelValidationException`s of the same situations, end in
a `{hint}` computed by `Resolvers/UnknownCkElementHint` from the model graph. Numbers and levels are unchanged. The
hint tells three situations apart: the id's model is **not in the graph** (dependency or alias missing: "This may
happen because a dependency to another construction kit model is missing."), the model **is resolved but holds no
element of that name** ("The type 'Pump' does not exist in model Industry.Basic-2.6.0. Did you mean 'Machine'?",
up to three closest names), and an element of that name **exists in another model** ("... but a type of that name
exists in Industry.Basic-2.6.0. Check the model prefix of the reference."). `OCTO-CK018` also fills its
`{targetCkTypeId}` / `{roleId}` placeholders in the right order now (they were swapped at the call sites). Tests:
`UnknownCkElementHintTests`.

| Code | Key | Where | Rule |
| ---- | --- | ----- | ---- |
| 90 | `CkLanguageFeatureRequiresV2` | `ElementResolver` | `interfaces`, `implements`, `methods`, any `access` (type, record, association-role assignment), any `visibility` / `derivable` (type, method, record, enum, attribute, association role), or a type association `targetCkInterfaceId` without `ckLanguage: 2` |
| 91 | `CkLanguageNotSupported` | `ElementResolver`, catalog + repository dependency resolvers | `ckLanguage` outside 1..`MaxSupportedCkLanguage` (also raised when a compiled model is resolved, e.g. on import, and — review L2 — for every **dependency** model: it is not appended, its dependents are skipped, and a hard resolve throws with the message) |
| 92 | `CkInterfaceIdNotUnique` | `ElementResolver` | same interface id twice (`Named-1` and `Named-2` are different contracts) |
| 93 | `CkInterfaceNameCollidesWithType` | `ElementResolver` | interface name == type name of the same model (I-5, GraphQL type namespace) |
| 94 | `CkInterfaceAttributeUnknown` | `ReferenceResolver` | member references an unknown attribute; known members are merged into `CkInterfaceGraph.Attributes` |
| 95 | `ImplementsUnknownCkInterface` | `ReferenceResolver` | `implements` entry not in the graph |
| 96–99 | `CkInterfaceMemberMissing` / `…MultiplicityMismatch` / `…NameMismatch` / `…Hidden` | `InheritanceResolver` | I-1..I-4, checked at the declaring type against `AllAttributes` (inherited attributes satisfy a member) |
| 100 | `CkMethodIdNotUnique` | `InheritanceResolver` | duplicate method id on a type, or re-declaration of an inherited method id (no overrides) |
| 101 | `CkMethodParameterInvalid` | `ReferenceResolver` + `InheritanceResolver` | unknown record/enum reference; duplicate parameter name; `Record`/`Enum` without (or non-`Record`/`Enum` with) `valueCkRecordId` / `valueCkEnumId` |
| 102 | `CkMethodNameReserved` | `InheritanceResolver` | `Create`, `Update`, `Delete` (case-insensitive, without version) |
| 103 | `CkMethodErrorCodeInvalid` | `InheritanceResolver` | duplicate error code or `METHOD_` prefix |
| 104 | `CkMethodAuthorizationInvalid` | `InheritanceResolver` | `allowSelf: true` on a `Static` method; a role or scope name that is not letters/digits/`_ - . : /` starting with a letter or digit (AB#6338 N4 — names are compared as sets) |
| 105 | `RestrictedAttributeInDerivedRule` | `InheritanceResolver.ValidateRestrictedAttributeUse` | review M9: a `displayNameRule` / `displayDescriptionRule` path or a `Text` index path reaches a `Hidden` attribute (incl. record segments), or `ownerAttributePath` reaches a `Hidden` or `MethodOnly` one — those fields are readable/filterable/searchable and would leak it. Not covered (outside the engine compiler): asset-repo computed columns, association `targetCkAttributeIds` |
| 106 | `HiddenAttributeIndexed` | `InheritanceResolver.ValidateRestrictedAttributeUse` | F1.2-S2 (review N5): a non-text index (`Unique`, `UniqueNotDeleted`, `Ascending`, ...) reaches a Hidden attribute (a unique index reveals values through duplicate-key errors). Text indexes keep reporting 105. Review G3 E-M1: every index path is checked against the type, its derived types **and every other type of the same collection** (defining collection root + all its derived types, i.e. siblings, also from other models) — they share `attributes.<name>`, so a sibling's Hidden assignment would be indexed too |
| 107 | `HiddenAttributeAutoCompleteValues` | `ElementResolver.CheckHiddenAssignments` | F1.2-S2 (N5): a Hidden type/record assignment declares `autoCompleteValues` (published in the model; Secret equivalent: 71) |
| 108 | `HiddenAttributeOnAssociationRole` | `ElementResolver.CheckHiddenAssignments` | F1.2-S2 (N5): association-role attributes cannot be Hidden (no access guard on association attributes). `ReadOnly` / `MethodOnly` stay allowed |
| 109 | `UnknownIndexAttributePath` | `InheritanceResolver.ValidateRestrictedAttributeUse` | F1.2-S2 (review N6): in a `ckLanguage: 2` model every index path segment must name an attribute (matched case-insensitively, like Mongo). Paths starting with `Rt`/`CkTypeId` (entity system fields) and the database key `_id` (the `System/Entity` index `CkTypeId` + `_id`) are exempt; a path merged into a collection root from a derived type is checked at that type. v1 models: unchanged |
| 110 | `CkElementsInWrongFolder` | `CompilerService` (per element file) | **warning**: an element file declares a root key that is only read from another folder (e.g. `interfaces:` in `types/x.yaml`); those elements are ignored. Applies to every element kind (each folder reads only its own key) |
| 112 | `CkReferenceToInternalElement` | `CkVisibilityValidator` (end of `ReferenceResolver`) | F1.2-S3: a reference from another model to a `visibility: Internal` element — base type/record, attribute assignment (type, record, role, interface member), attribute `valueCkRecordId`/`valueCkEnumId`, `implements`, interface `extends`, type/interface association role and targets, method parameter/result records and enums. Models compared by name |
| 113 | `CkElementNotDerivable` | `CkVisibilityValidator` | F1.2-S3: a type or record derives from a type/record of another model whose effective `derivable` is `Model` (a v2 base without `derivable: Any`) |
| 118 | `CkInterfaceExtendsInvalid` | `ReferenceResolver` (unknown, self) + `InheritanceResolver.ValidateInterfaces` (cycle) | F1.2-S4: an `extends` entry is unknown, the interface itself, or the chain leads back (cycle; reported at every interface on it) |
| 119 | `CkInterfaceMemberConflict` | `InheritanceResolver.ValidateInterfaces` | F1.2-S4: across an interface and its parents one name stands for two attributes, or one attribute appears under two names. Repeating an inherited member identically is allowed |
| 120 | `CkInterfaceAssociationInvalid` | `ReferenceResolver` | F1.2-S4: association member with an unknown role, unknown target, or not exactly one of `targetCkTypeId` / `targetCkInterfaceId` |
| 121 | `CkInterfaceAssociationMissing` | `InheritanceResolver.ValidateAssociationMembers` | F1.2-S4: a type implementing the interface (incl. parents' members) lacks an outbound association (own or inherited) with the role, a compatible target (the type or a subtype; for an interface target an implementor, or an association narrowed to that interface or one extending it) and a multiplicity at least as strict as the member's (One < ZeroOrOne < N). Optional members are skipped |
| 122 | `CkInterfaceMethodConflict` | `InheritanceResolver.InheritInterfaceMethods` | F1.2-S4: a type redeclares an interface method id with another **invocation contract** (AB#6336: `CkMethodContract.FirstDifference` — kind, parameters as a set incl. `isOptional`/`sensitive`, result incl. its `sensitive` marker (AB#7006), error-code set; descriptions, authorization, execution and order may differ; the message names the first difference). Row I12 classifies every contract change of an interface method as Major. Interface methods are inherited into `CkTypeGraph.AllMethods` (declaring type = highest type of the chain that implements the interface); interface methods follow 100 (duplicate id) and 101-104 |
| 123 | `CkInterfaceHasNoMembers` | `ReferenceResolver` | F1.2-S4: no attribute, association or method member and no `extends` (replaces the former schema `minItems`) |
| 124 | `CkInterfaceDeprecated` | `ReferenceResolver` | F1.2-S4: **warning** for `implements`, `extends`, interface association targets and type association `targetCkInterfaceId` that reference a `deprecated: true` interface |
| 125 | `CkMethodGeneratedNameCollision` | `ElementResolver.CheckGeneratedMethodNames` | F1.4-S2 (review L15): two type methods of a model produce the same generated name `{Type}{Method}` (constant / parameter record) |
| 126 | `CkModelRequiresNewerEngine` | `ElementResolver`, catalog + repository dependency resolvers | F1.1-S6: the compiled model's `minEngineVersion` is above the running engine version (see "minEngineVersion" above) |
| 127 | `CkInterfaceMemberNotUnique` | `ReferenceResolver.CheckCkInterfaces` | review L17: an interface declares the same attribute twice or two members with the same name (case-insensitive); reported at the interface instead of silently dropping the duplicate. Implementation checks (96–99) run on the merged members, so a duplicate produces no follow-up error |
| 128 | `UnknownTargetCkInterfaceOfAssociation` | `ReferenceResolver` | F1.2-S4: a type association's `targetCkInterfaceId` is unknown |
| 129 | `CkInconsistentVisibility` | `CkVisibilityValidator` (end of `ReferenceResolver`) + `InheritanceResolver.InheritInterfaceMethods` | AB#6334 / AB#6335 (F2.1 gate findings H1/H3): in a `ckLanguage: 2` model a **public** element references an **internal** element of its own model (same reference walk as 112, plus type association `targetCkAttributeIds`), a public interface declares an internal method, or a type redeclares a method of a public interface it implements as internal. One message per offending reference. Coverage of the walk is guarded by `CkVisibilityReferenceCoverageTests`; the classifier mirror is `CkVisibilityIndex.ExposeInternalElementsReachableFromPublicOnes` (defence in depth; it also treats associations of internal types that target a public type, and their roles, as public surface — N1) |
| 130 | `InvalidCompatibilityAcknowledge` | `CkCompatibilityValidator` (from `CompilerService`) | AB#6295: an entry of `compatibility.acknowledge` in `ckModel.yaml` has an empty `change`, a wildcard (`*`, `?`), an empty `reason`, or repeats a key. The JSON schema rejects empty reasons and wildcard keys first; this is the same rule for sources that skip the schema. Using the section in a `ckLanguage` 1 model is message 90 (feature `compatibility.acknowledge`) |
| 131 | `CkMethodHandlerInvalid` | `InheritanceResolver.ValidateMethodHandler` (M-6) | AB#6877: `execution.handler` of a method (type or interface) is not `service` or `pipeline` (exact, lower-case — the schema deliberately has no `enum` so the compiler names the method), `pipeline` without an explicit `timeoutSeconds`, or `pipeline` on an **interface** method (a pipeline binds by the well-known name equal to the qualified method id `Model/Type.Method-1` of one type; an implementing type redeclares the method with `handler: pipeline` itself — execution is not part of the invocation contract, rule 122). Relaxing the interface rule later is non-breaking, tightening it would not be |
| 132 | `CkMethodPipelineSensitiveParameter` | `InheritanceResolver.ValidateMethodHandler` (M-6) | AB#7004 (decision OD-9, option *forbid*): a method with `execution.handler: pipeline` has at least one parameter with `sensitive: true` (the message names the method, the type and the sensitive parameters). A pipeline execution stores its input; a method with a sensitive parameter must use `handler: service`. Reported in addition to 131 when 131 applies too. A sensitive **result** is message 134. The runtime refusal in the pipeline handler (AB#6879) stays as a second gate |
| 133 | `CkMethodReplacesGenericCreateInvalid` | `InheritanceResolver.ValidateMethodReplacesGenericCreate` / `ValidateAtMostOneReplacesGenericCreate` (M-7) | AB#7024: `replacesGenericCreate: true` on a method that is not `kind: Static`, on an **interface** method, on a method of an **abstract** type, or a second flagged method on a concrete type (own or inherited; reported on the type that declares one of them, naming all flagged methods). One message per offending method with a `{reason}`. The flag in a `ckLanguage` 1 model is message 90 (feature `replacesGenericCreate`), an older engine refuses a model that uses it with message 126 (raised `minEngineVersion`). Code taken as the next free number after 132 |
| 134 | `CkMethodPipelineSensitiveResult` | `InheritanceResolver.ValidateMethodHandler` (M-6) | AB#7006 (decision OD-9 follow-up): a method with `execution.handler: pipeline` whose `result.sensitive` is `true` (the message names the method and the type). A pipeline execution stores its output, so a method with a sensitive result must use `handler: service`. Own code next to 132 because the remedy differs (a parameter can be dropped, a result cannot be hidden from the pipeline); both are reported when both apply, and 131 as well when it applies. Interface methods refuse `pipeline` already (131); a sensitive result on a `service` method (type or interface) is fine. The marker in a `ckLanguage` 1 model cannot occur (methods are v2 only, message 90); an older engine refuses a model that uses it with message 126 (raised `minEngineVersion`) |

`InheritanceResolver.ResolveInterfacesAndMethods` also completes `AllImplementedInterfaces` (own ∪ every base
type's declared interfaces), `AllMethods` (nearest declaration wins; `CkMethodGraph.DeclaringCkTypeId` is the
declaring type) and `CkInterfaceGraph.ImplementingTypes`. All new references (`implements`, interface member
ids, method `valueCkRecordId` / `valueCkEnumId`) go through the `VariableResolver`, and `CkReferenceRewriter`
(F0.2 range retention) rewrites and floor-checks them like every other reference (`System@2/Named-1`).

Source generator: the `*CkIds` class gains `RtCk{Name}InterfaceId`, `Ck{Name}InterfaceId`,
`RtCk{Name}InterfaceIdString` and `{Type}{Method}MethodId` constants (e.g.
`SystemIdentityCkIds.UserChangePasswordMethodId = "System.Identity/User.ChangePassword-1"`; a method version > 1 is
appended: `UserChangePassword2MethodId`). Typed parameter/result records: see "v2 class modifiers and method contracts"
below.
**v2 class modifiers and method contracts (F1.4-S2, AB#5919).** For a `ckLanguage: 2` model (the source generator
passes `EffectiveCkLanguage` to `CkTypeCodeGenerator`) the Rt class is `sealed` for `isFinal` or for an effective
`derivable: Model` without a subtype in its own model; v1 output is unchanged. **`isAbstract` does not emit
`abstract`** (platform-owner decision, review G3 E-M3): `IRuntimeRepository` / `IRepositoryDataSource` generics
require `where TEntity : RtEntity, new()`, so an abstract Rt class could not be queried polymorphically. Revisit in
Phase 4 (System → ckLanguage 2) together with a non-`new()` query path.
`CkMethodCodeGenerator` emits per declared type method `{Type}{Method}Parameters` (`required` for required
parameters, nullable for optional ones, camelCase → PascalCase; CLR types: string/bool/DateTime/DateTimeOffset/
TimeSpan/int/long/double, `IReadOnlyList<string|long>`, `Rt{Enum}Enum`, `Rt{Record}Record`) with a `ToString()` that
redacts `sensitive` parameters as `***`, and `{Type}{Method}Result { Value }` when the method has a result
(definitions only, no dispatch). Interface methods get no records. **Review L15:** the generated name
(`CkMethodIds.GeneratedName`, shared with the `{Name}MethodId` constants) is checked per model by the compiler —
**message 125** `CkMethodGeneratedNameCollision` (`User`+`ChangePassword` vs `UserChange`+`Password`); the existing
constant names stay unchanged (no separator). Pinned by `CkV2GeneratedContractsTests` (Roslyn compile + emitted
`ToString`).

**C# interfaces (F1.4-S1, AB#5918).** `CkInterfaceCodeGenerator` emits `public partial interface IRt<Name>` per CK
interface (`Named-1` → `IRtNamed`, `Named-2` → `IRtNamed2`) in the model's generated namespace (the same one as
the Rt classes — deviation from the concept's `.Contracts` sub-namespace, so cross-model references resolve like
base classes do). Attribute members are **get-only** properties with exactly the CLR type of the Rt property (taken
from `AttributeCodeGenerator`; nullable when the member is optional); `extends` becomes C# interface inheritance and
an inherited member is not repeated (CS0108). A generated Rt class lists its declared `implements` and implements
every member of those interfaces and their parents **explicitly** (`string IRtNamed.Name => Name;`; an optional
member the type does not assign returns `default`), so a required assignment of an optional member still conforms
and re-implementing an interface a base class already implements is legal. Association members are not emitted
(the Rt classes have no generated navigations yet); methods neither (definitions only). v1 models: nothing emitted,
type output unchanged. Pinned by `CkInterfaceCodeGeneratorTests` (Roslyn-compiles the generated code against
`Runtime.Contracts` with a consumer that assigns `RtAccount` to `IRtNamed`).
Docs generator: `Interfaces.md` per model (members incl. inherited ones, "Extends" line, association and method
tables, "Deprecated" marker) plus an "Implements" line and a methods table per type (only when present);
"Visibility: `Internal`" / "Derivable: `Model`" lines per element and an "(internal)" method marker, written only
when the value differs from the v1 default, so v1 docs are unchanged.
SemVer rules: `docs/ck-semver-rules.md`. Phase 1 base rules (additions Minor, removals Major, `access` changes Minor
with an "access/security" note, `ckLanguage` 1→2 Minor unless it flips the `derivable` default, `visibility`
Public→Internal and `derivable` Any→Model Major, the reverse Minor, type association `targetCkInterfaceId` set or
changed Major and cleared Minor) plus the F2.1 rule rows with one test each (`tests/ConstructionKit.Engine.Tests/SemVer/Rows/`):
N1–N5 internal elements (AB#6266, `CkVisibilityIndex` caps changes of internal elements and of members of internal
owners at Minor), I1–I12 interfaces (AB#6267, optional members Minor — an optional attribute member only with a new or previously internal definition of this model, AB#6337, association members keyed by role with a `target`
property), M1–M17 methods (AB#6268, one change per method field, element kinds `MethodParameter` / `MethodError`, the
rendered `signature` stays as a level-None summary; AB#6338: M12 under default-deny — roles any-of and case-insensitive (P3-3), scopes all-of, an omitted block or empty roles admit administrators only, block add/remove is one change, looser changes are behavioural "security: method access widened"; AB#6336: I12 lifts every invocation-contract change of an interface method to Major), D1–D8 range retention (AB#6271; AB#5435 D8: a resolved dependency moving up inside its major on a range-retaining model is level None, exact-pinned models keep the Minor rule until F2.6 / AB#5688, the verdict line names the move; AB#6340: a range/floor excluding the previously resolved version, or a resolved version going down, is Major; element kind `DependencyRange`,
model property `rangeRetention`), T1–T7 / E1–E2 / R1–R2 / A1–A3 public types, stable bases, enums, records, attribute
definitions and access (AB#6269; access tightening Major, security exception Minor + `RequiresAcknowledge` for
`securitySensitive` attributes), B1–B4 behavioural changes (AB#6270; AB#6339: v2 indexes are paired by case-insensitive field paths, index-type changes are one modification; AB#6341: removing the default of a required-assigned or public definition is Major in a v2 model; `CkClassifiedModelChange.IsBehavioural` /
`RequiresAcknowledge`, own "Behavioural changes" section in report and changelog, stable base =
`CkSemVerClassifier.IsStableBase`). The guard's `KnownGaps` list is empty.

### Touch-point checklist (keep for every new CK field — contract §2.7)

**Mandatory persistence gate:** every new meta-model field must pass the generic reflection round-trip gate in
`octo-construction-kit-engine-mongodb` — `CkModelReflectionComparer` (F1.3-S3, H-M2 `c3a4911`) compares every
public property of the compiled model after import → Mongo → read-back, so a field that is not written or not read
back (the isRuntimeState class of bug, AB#4589) fails there. Row 10 below is not done until that gate is green.

**Mandatory classification gate (AB#6272):** every new meta-model field also needs a diff **and** a classifier rule.
`tests/ConstructionKit.Engine.Tests/SemVer/CkSemVerClassificationGuardTests.cs` fails, naming the item, when a public
element-DTO property is neither in `CkModelDiffService.ComparedProperties` nor in `ExcludedProperties` (with a written
reason), when a compared property has no probe in the guard's `PropertyProbes` or its probe yields no diff change,
when a change shape is missing in `CkModelDiffService.EmittableChanges`, when any shape reaches the defensive default
of `CkSemVerClassifier` ("no classification rule …"), when a `CkModelElementKind` value has no rule, and when the
rule rows of `docs/ck-semver-rules.md` (N*, I*, M*, T*, E*, R*, A*, B*, D*) and the tests named after them
(`N1_…`) drift apart. To satisfy it: diff the field, list its shape, add a probe, add the classifier rule, add the
docs row and its row test (`docs/ck-semver-rules.md`, "Classification guard"). Open items live only in the guard's
`KnownGaps` list (each with its story); it must be empty when F2.1 closes (AB#6273). Row 7 below is not done until
that gate is green.

| # | Touch point | `ckLanguage` | `interfaces` | `implements` | `access` | `methods` | `visibility` / `derivable` | interface completion (S5) |
| - | ----------- | ------------ | ------------ | ------------ | -------- | --------- | -------------------------- | ------------------------- |
| 1 | Source schema | meta (enum 1/2) | interface schema + elements root | `CkType` | `CkTypeAttribute` | method schema + `CkType` | type, record, enum, attribute, association-role, interface, method schemas | interface schema (`extends`, `associations`, `methods`, `deprecated`), type schema `targetCkInterfaceId` |
| 2 | Compiled schema | integer ≥ 1 | compiled root | `CkCompiledType` | shared `$ref` | `CkCompiledType` | `CkCompiledType` (rest via shared `$ref`) | via shared `$ref` |
| 3 | DTO | `CkModelPropertiesDto.CkLanguage` | `CkInterfaceDto`, `CkElementsRootDto`, `CkModelRootBase` | `CkTypeDto.Implements` | `CkTypeAttributeDto.Access` | `CkTypeDto.Methods` + `CkMethodDto` family | `CkVisibilityDto` / `CkDerivableDto` on the 7 element DTOs | `CkInterfaceDto`, `CkInterfaceAssociationDto`, `CkTypeAssociationDto.TargetCkInterfaceId` |
| 4 | Compiler hand-copies | `CompilerService` candidate, `CatalogModelResolver` | same + `interfaces/` loop | `CompilerService` type copy | by reference | `CompilerService` type copy | `CompilerService` type copy (others by reference) | by reference |
| 5 | Graph + `[JsonConstructor]` | `CkCacheRoot.Models` (set in `ElementResolver` / `AppendModel`) | `CkInterfaceGraph`, `CkModelGraph`, `CkCacheRoot`, cache getters | `CkTypeGraph` | `CkTypeAttributeGraph` (init setter) | `CkTypeGraph` + `CkMethodGraph` | settable effective properties, set by `CkModelGraph.ApplyCkV2Modifiers` | `CkInterfaceGraph` (defaulted `[JsonConstructor]` params), `CkInterfaceAssociationGraph`, `CkInterfaceMethodGraph`, `CkTypeAssociationGraph` |
| 6 | Resolvers + codes | 90/91, 126 | 92–94, 118–124, 127 | 95–99, 121, 124 | 90, 99, 105–108 | 100–104, 122, 125, 131–134 | 90, 112, 113, 129 | 90, 118–124, 128 |
| 7 | SemVer diff + classifier rule + row test (gate: `CkSemVerClassificationGuardTests`, mandatory) | yes | yes | yes | yes | yes | yes (`CkModelDiffService.DiffModifiers`) | yes |
| 8 | Source generator | — | yes (ids + `IRt<Name>`) | yes (explicit impls) | — | yes (ids, parameter/result records) | `sealed` (v2; no `abstract`, E-M3) | `IRt<Name>` (extends = interface inheritance) |
| 9 | Docs generator | — | yes | yes | — | yes | yes (non-default only) | yes |
| 10 | Mongo entity + write + read-back (gate: `CkModelReflectionComparer`, mandatory) | engine-mongodb (Persistence agent) | | | | | engine-mongodb (Persistence agent) | engine-mongodb (Persistence agent) |
| 11 | GraphQL CK meta | asset-repo F1.5-S3 (CK meta introspection) | asset-repo F1.5-S2/S3 | asset-repo F1.5-S2 | asset-repo `CkTypeAttributeDtoType.access` | asset-repo F1.5-S3 | asset-repo F1.5-S3 | asset-repo F1.5-S2/S3 |
| 12 | Studio CK browser query (refinery-studio `src/app/graphQL/`, AB#5924) — v2 fields go into the CK v2 follow-up documents run by `CkLanguage2Service`, **never** into the v1 queries (`getCkTypeDetails` etc. must keep working against asset repos without the CK v2 meta API; studio `CLAUDE.md` "CK language 2 in the CK browser") | `getCkModelsLanguage2` | `getCkInterfaces`, `getCkInterfaceDetails`, `getCkTypeLanguage2` | `getCkTypeLanguage2` | `getCkTypeAttributesAccess` | `getCkTypeLanguage2` | `getCkTypeLanguage2`, `getCkElementLanguage2` | `getCkInterfaceDetails` |

**`usedSurface` (AB#4472) touch points**, same numbering: (2) compiled schema `dependencyRanges[].usedSurface` /
`usedSurfaceHash`; (3) `CkModelDependencyDto.UsedSurface` / `UsedSurfaceHash`; (4) computed by `CkUsedSurfaceCollector`
in `CatalogModelResolver.ApplyRangeRetentionAsync` (range retention only, so v1 output is unchanged); (7) documented
exclusion in `CkModelDiffService.ExcludedProperties` (row D7); (10) engine-mongodb `CkModelDependency.UsedSurface` /
`UsedSurfaceHash` (reflection gate via the C# kitchen sink); consumer: F2.5 (AB#5687). Rules and limits:
`docs/ck-semver-rules.md`, "usedSurface".

**`compatibility.acknowledge` (AB#6295) touch points**, same numbering: (1) meta schema `compatibility.acknowledge[]`
(`change` without wildcards, `reason` non-blank); (2) compiled schema, same shape; (3) `CkMetaRootDto.Compatibility`,
`CkModelCompileCandidate.Compatibility`, `CkCompiledModelRoot.Compatibility` (`CkCompatibilityDto`, `CkAcknowledgeDto`);
(4) `CompilerService` -> `CkCompatibilityValidator` -> candidate -> `CatalogModelResolver.CompileAsync` (omitted when
empty, so existing models serialize as before); (5) not part of the graph; (6) 130, and 90 in a v1 model;
(7) conscious exclusion `CkCompiledModelRoot.Compatibility` in `CkModelDiffService.ExcludedProperties` (the entries are
matched against the diff by `CkAcknowledgementResult`, they are not model structure) and `CkCompatibilityDto` /
`CkAcknowledgeDto` in the guard's `NotDiffedDtoTypes`; (8), (9) -; (10) conscious exclusion in `CkModelReflectionComparer.AllowList`
(`CkCompiledModelRoot.Compatibility`: build and publish-time metadata, read from the catalog JSON by the publish gate, not
needed in tenants); (11), (12) not needed. Rules: `docs/ck-semver-rules.md`, "Acknowledge".

**`execution.handler` (AB#6877, F3.1) touch points**, same numbering (the field lives in `CkMethodExecutionDto`, next to
`timeoutSeconds` / `idempotent`; values `service` (default) and `pipeline`, the string constants of `CkMethodHandlers`;
`null` = omitted = `service`, so existing models serialize byte-identically): (1) method schema
`CkMethodExecution.handler` (plain string, no `enum`: an unknown value is message 131 with the method id, not a schema
error); (2) compiled schema via the shared `$ref`; (3) `CkMethodExecutionDto.Handler` (`string?`, `OmitNull`);
(4) copied by reference with `Methods`; (5) `CkMethodGraph.Handler` (`[JsonIgnore]`, derived from the definition that
the graph already serializes); (6) message 131 (unknown value, `pipeline` without explicit `timeoutSeconds`, `pipeline`
on an interface method) and message 132 (AB#7004: `pipeline` with a `sensitive` parameter); (7) diff property `handler` (effective value, so adding `handler: service` is no change),
classifier row **M15** (service ↔ pipeline = Major), guard probe, `signature` summary shows `, handler pipeline` only for
the non-default; (8) — (parameter/result records and id constants are generated as before); (9) `Types.md` marks a
non-default method `(handler: pipeline)`; (10) **follow-up AB#6878** (engine-mongodb `CkMethodExecution` map +
`CkModelReflectionComparer` gate — until then a tenant document drops the value on import); (11) **follow-up AB#6878**
(asset-repo CK meta); (12) optional, Studio. Runtime routing of `pipeline` methods: AB#6879. Tests:
`CkV2MethodResolverTests` (`Code131_*`, `Code132_*`), `CkV2CompileTests.MethodHandler_*`, `CkV2GraphJsonRoundTripTests`,
`MethodRowTests.M15_*`.

**`replacesGenericCreate` (AB#7024, F3.7) touch points**, same numbering (the field lives on `CkMethodDto` itself, next to
`kind` / `visibility`; `bool`, omitted = `false`; a static method that is the only way to create entities of its type, the
refusal of the generic create is enforced by the asset repository, AB#7025): (1) method schema `CkMethod.replacesGenericCreate`
(boolean; the model-language key needs a **schema release** at schemas.meshmakers.cloud, a catalog model must not use it
before that); (2) compiled schema via the shared `$ref`; (3) `CkMethodDto.ReplacesGenericCreate` (`OmitDefaults` in YAML,
`JsonIgnore(WhenWritingDefault)` in JSON, so models that do not use the key serialize byte-identically); (4) copied by
reference with `Methods`; (5) `CkMethodGraph.ReplacesGenericCreate` (`[JsonIgnore]`, derived from the serialized definition);
(6) message 133 (`InheritanceResolver` rule M-7: static only, no interface method, no abstract type, at most one per
concrete type incl. inherited ones) and message 90 in a v1 model (`ElementResolver.CheckCkLanguage`); (7) diff property
`replacesGenericCreate`, classifier row **M16** (`false` → `true` Major, `true` → `false` Minor, a new flagged method on a type of the previous version is Major too — the diff emits the `false` → `true` change for it —, on a new type Minor),
guard probe, `signature` summary shows `, replaces generic create` only when set; (8) — (no generated code depends on it);
(9) `Types.md` marks the method `(replaces generic create)`; (10) engine-mongodb `CkMethod.ReplacesGenericCreate`
(reflection gate); (11) asset-repo CK meta + refusal: AB#7025; (12) optional, Studio. **minEngineVersion:**
`CkEngineVersion.GetRequiredMinEngineVersion` raises the value to `ReplacesGenericCreateMinEngineVersion` for a model with
a flagged method (models without it keep `CkV2MinEngineVersion`; dependents inherit it through their dependencies), so an
engine that predates the field refuses the model with message 126 instead of silently dropping the flag and serving the
generic create. The constant must equal the number of the release that first ships the field. Tests:
`CkV2MethodResolverTests` (`Code133_*`, `Code90_*`), `CkV2CompileTests.ReplacesGenericCreate_*`,
`CkV2GraphJsonRoundTripTests.ReplacesGenericCreate_*`, `CkEngineVersionTests`, `MethodRowTests.M16_*`.

**Method result `sensitive` (AB#7006, F3.4) touch points**, same numbering (the field lives on `CkMethodResultDto`, next to
`valueType`; `bool`, omitted = `false`; it marks the **whole** result value — scalar, enum or record — as never to be logged,
audited or traced; per-attribute marking inside a record result is out of scope; a `Secret` value type is **not** sensitive
implicitly, the marker is explicit; the redaction itself is enforced at runtime, AB#7007, the GraphQL exposure is AB#7008):
(1) method schema `CkMethodResult.sensitive` (boolean; the model-language key needs a **schema release** at
schemas.meshmakers.cloud, a catalog model must not use it before that; an older compiler refuses the key loudly because the
definition has `additionalProperties: false`); (2) compiled schema via the shared `$ref`; (3) `CkMethodResultDto.Sensitive`
(`OmitDefaults` in YAML, `JsonIgnore(WhenWritingDefault)` in JSON, so models that do not use the key serialize
byte-identically); (4) copied by reference with `Methods`; (5) `CkMethodGraph.HasSensitiveResult` (`[JsonIgnore]`, derived from
the serialized definition; false for a method without a result); (6) message 134 (`InheritanceResolver.ValidateMethodHandler`,
see the table) and rule 122: `CkMethodContract` compares the marker, a type that redeclares an interface method must keep it
(reason `result sensitive`); (7) diff property `resultSensitive` (emitted only when both versions have a result; a result added
or removed is one `result` change, row M6), classifier row **M17** (`false` → `true` Minor; `true` → `false` Minor +
`requiresAcknowledge` + behavioural "security: result redaction removed", change key
`TypeMethod:<Type>/<Method>#Modified:resultSensitive`; interface method either way Major via I12, because `resultSensitive` is
a contract property), guard probe `CkMethodResultDto.Sensitive`, the method `signature` summary appends ` sensitive` to the
result; (8) source generator: the generated `{Type}{Method}Result` gets a `ToString()` override printing `Value = ***` for a
sensitive result (unchanged record rendering otherwise); (9) `Types.md` marks the result column `(sensitive)`; (10) engine-mongodb
`CkMethodResult.Sensitive` (reflection gate, legacy document); (11) asset-repo CK meta: AB#7008; (12) optional, Studio.
**minEngineVersion:** `CkEngineVersion.GetRequiredMinEngineVersion` raises the value to `SensitiveResultMinEngineVersion` for a
model in which any type **or interface** method has a sensitive result (the maximum with the other markers; models without it
keep their minimum), so an engine that predates the field refuses the model with message 126 instead of silently dropping the
marker (fail-open: nothing would be redacted). The constant must equal the number of the release that first ships the field
(the next patch after the last `r3.5.x` tag at merge time). Tests: `CkV2MethodResolverTests` (`Code134_*`, `Code122_*`),
`CkV2CompileTests.SensitiveResult_*`, `CkV2GraphJsonRoundTripTests.SensitiveResult_*`, `CkV2GeneratedContractsTests`,
`CkEngineVersionTests`, `MethodRowTests.M17_*`, `CkV2SchemaTests`.

**`securitySensitive` (AB#6269) touch points**, same numbering: (1) attribute schema `CkAttribute.securitySensitive`;
(2) compiled schema via the shared `$ref`; (3) `CkAttributeDto.SecuritySensitive` (`bool?`, omitted when null); (4) copied
by reference; (5) `CkAttributeGraph.SecuritySensitive`, omitted from the cache JSON while false (`OmitCkV2Defaults`);
(6) message 90 in a v1 model; (7) diff property `securitySensitive`, rows A3 and T7, guard probe; (8) —;
(9) "Security-sensitive: `true`" line in `Attributes.md`; (10) engine-mongodb `CkAttribute.SecuritySensitive`
(reflection gate); (11) asset-repo CK meta API and (12) Studio: follow-up (not needed for classification).

Tests: `CkV2SchemaTests`, `CkV2ContractTests`, `CkV2SemVerTests` (`tests/ConstructionKit.Engine.Tests/CkV2`),
`CkV2InterfaceResolverTests` / `CkV2MethodResolverTests` / `CkV2ModifierResolverTests` /
`CkV2InterfaceCompletionResolverTests` (one failing and one passing case per code, on the C#
kitchen sink `sampleData/ckv2KitchenSink/Builder.cs`), `CkV2GraphJsonRoundTripTests` (graph → cache JSON → graph),
`CkIdsCodeGeneratorCkV2Tests`, and in `ConstructionKit.Compiler.Tests` `CkV2CompileTests` (YAML kitchen sink end to
end incl. `interfaces/` folder, gate, docs and range retention of `implements`) and `CkV1CompileOutputUnchangedTests`.

## Data permissions: one rule set, one public check (AB#4969 / AB#7035 / AB#7126)

Data permissions (policies of `System.Identity`, resolved per tenant by `IDataPermissionResolver`) are enforced on
the repository session by the caller's `RtSecurityContext` (subject, roles, `IsSystem`); a session without one is the
System context and bypasses everything. The rules live in exactly one place:

| Piece | Where | Role |
|-------|-------|------|
| `RtDataAccessEvaluator.Classify` | `Runtime.Contracts/DataPermissions` | pure classification Open / Allowed / OwnedOnly / Denied per type (+ base types) and action; AuditOnly view |
| `DataSecurityFilterFactory` | `Runtime.Engine/Security` | read filter: classifies with the evaluator, the query renders the ownership predicate; denied reads behave as not found |
| `DataPermissionRules` | `Runtime.Engine/Security` (internal) | shared by guard and evaluator: type classification, ownership predicate (`RtCreatedBy` or `CkType.ownerAttributePath`, fail closed), audit events |
| `DataPermissionWriteGuard` | `Runtime.Engine/Security` (internal) | the write/delete funnel in `RuntimeRepositoryBase`: read visibility first (AB#7126), then batch write/delete checks per change set, atomic rejection, blueprint lock |
| `IRtDataPermissionEvaluator` | `Runtime.Contracts/DataPermissions` (impl. `RtDataPermissionEvaluator`, registered singleton) | **the public question** "may this caller read / write / delete this entity (or type)?" |

`IRtDataPermissionEvaluator` is the only public way to ask (a gateway or any caller-bound surface must not copy
the classification):

- `EvaluateAsync(repository, caller, RtEntityId target, RtDataAction action, publishAudit = false, ct)` and
  `EvaluateTypeAsync(repository, caller, ckTypeId, action, publishAudit = false, ct)`.
- Result `RtDataPermissionVerdict { Decision, Reason, AuditOnlyViolation }`, no entity data:
  `Decision` = `Allowed` | `NotVisible` | `NotPermitted`. **`NotVisible` must be answered like a missing entity**
  (not found); `NotPermitted` is a refusal. `Reason` is a language-neutral code (`Open`, `Granted`,
  `GrantedAsOwner`, `AuditOnly`, `NoGrant`, `NotOwner`, `EntityNotFound`, `BlueprintLocked`).
- Entity level: visibility is decided first with the Read classification (what the read filter hides is
  `NotVisible` for every action, also when the entity does not exist and the verdict depends on it); then the
  action's own grant, ownership for owned-only grants and, for Write/Delete, the blueprint lock of opted-in
  types. The stored entity is read (raw, system session) only when the verdict depends on it.
- Type level (no entity, e.g. a static method): Read without any grant is `NotVisible`; Write/Delete without a
  grant is `NotPermitted`; an owned-only grant is allowed like the generic insert.
- System callers and tenants without policies are `Allowed` / `Open`. AuditOnly policies never refuse; the
  verdict carries `AuditOnlyViolation` and `publishAudit: true` publishes the same audit event the repository
  would (`DataPermissions.ReadViolation` / `WriteViolation` / `BlueprintLockViolation`). The default call has
  no side effect. (The evaluator does not publish the guard's AuditOnly visibility event of a write; its
  `AuditOnlyViolation` flag keeps the meaning of the write/read grant audit.)
- **Write requires visibility (AB#7126).** The write guard applies the evaluator's first step before any write/delete
  decision: for an update, replace or delete of an existing entity, and for an association change at its origin, the
  Read classification decides first (`DataPermissionWriteGuard.CheckVisibilityAsync`, same `DataPermissionRules`
  classification and ownership predicate as the evaluator and the read filter). No read grant on the (protected) type,
  or an owned-only read grant and the caller is not the owner, means the entity is invisible and the change is refused
  **as if it did not exist** (`RuntimeRepositoryBase.CreateEntityNotFoundException`, a virtual hook: the persistence
  layer overrides it with the exception its own write path raises for a missing entity, so a hidden and a missing
  entity are indistinguishable; the whole change set is rejected). The check comes before the write decision, so a
  hidden entity never answers "access denied". Inserts have no visibility question and stay allowed, and neither do the associations whose origin is inserted by the same change set (the parent link of a new file or folder: no stored entity exists yet; without this exemption every create-with-association by an owned-only or write-only role was refused as not found, AB#7213). Like the
  evaluator, an id without a stored entity is answered the same way as an invisible one when the caller's read access
  is restricted (so existence is never revealed, and an upsert/replace by a role without full read is refused for
  existing and new ids alike); with unrestricted read it is left to the normal write path. The stored entity is read
  (raw, once per entity) only for an owned-only read grant (or an AuditOnly read policy). AuditOnly policies never refuse: an AuditOnly read policy that would hide an entity publishes
  one `DataPermissions.WriteViolation` event per type (none additionally when the type's write audit already
  published); system callers and tenants without policies are untouched. **Compatibility:** a role that holds Write
  or Delete on a type but no Read (e.g. a custom role with only `files.manage`) can still insert but no longer
  update, replace or delete existing records of that type; grant the matching Read policy (the shipped
  `FileManagement` role has it). Tests: `DataPermissionWriteGuardVisibilityTests`; the matrix test
  `Entity_AgreesWithWriteGuard` now covers every cell, `NotVisible` included.
- Tests: `RtDataPermissionEvaluatorTests` (matrix, agreement with the guard) in `Runtime.Engine.Tests`; the
  differential test against the real repository is `DataPermissionEvaluatorDifferentialTests` in
  engine-mongodb.

## Delete is Archive by default; importing an archived entity again (AB#7018)

`DeleteStrategies.Archive` is the default of `DeleteOptions`, `BulkOperationOptions` and the GraphQL delete: the
runtime entity and its associations only get `RtState = Archived`, the document keeps its `_id` (= rtId). Only
`DeleteStrategies.Erase` removes it. Consequences for imports (`ImportStrategy` / `BulkInsertStrategies`):

- `Insert` (`octo-cli ImportRt` default) replaces an ARCHIVED document with the same rtId (active again); an ACTIVE one
  still fails, with a message naming index and state. `Upsert` (`-w`) replaces in any case.
- Business keys: declare them `UniqueNotDeleted`; a plain `Unique` CK index also blocks the value of an archived
  entity (unchanged CK behaviour).
- The implementation is `MongoDbDataSourceCollection.BulkImportAsync` in `octo-construction-kit-engine-mongodb`
  (details and tests there, section "Re-importing an archived entity"). `LocalDataSourceCollection` (dev-only local
  directory repository) still treats every existing id as taken.

## Important Notes

- The solution uses Azure Pipelines for CI/CD (`azure-pipelines.yml` in the repo root)
- NuGet packages can be published to either public or private feeds depending on configuration
- Local development uses the DebugL configuration with local package sources
- Migration YAML files in `ConstructionKit/migrations/` are automatically embedded as resources