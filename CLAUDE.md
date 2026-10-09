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

### Construction Kit Model Structure
Construction Kit models are defined in YAML files following a specific schema:
- Models have a unique `modelId` (e.g., "System-1.0.1")
- Models can depend on other models via `dependencies`
- Model definitions are organized in folders: `types/`, `attributes/`, `enums/`, `records/`, `associations/`, and (CK v2, `ckLanguage: 2`) `interfaces/`
- Optional: `migrations/` folder for CK model version migrations

## Build Commands

```bash
# Build the entire solution
dotnet build --configuration Release

# Build in debug mode with local packages
dotnet build --configuration DebugL

# Build specific project
dotnet build src/ConstructionKit.Compiler/ConstructionKit.Compiler.csproj
```

## Test Commands

```bash
# Run all tests except system tests
dotnet test --configuration Release --filter "FullyQualifiedName!~SystemTests"

# Run all tests including system tests
dotnet test --configuration Release

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
/tests                  # Test projects
/samples                # Example implementations
azure-pipelines.yml     # CI/CD pipeline definition (the legacy /devops-build folder is gone)
/scripts                # createDocumentation.ps1 and other CI helper scripts
```

## Blueprints and Migrations

The project supports two types of migrations:

### Blueprints
Blueprints initialize tenants with pre-configured CK models and runtime data. See `docs/blueprints.md` for details.

Key services:
- `IBlueprintService` - Applies blueprints to tenants

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

## CK Model Publishing Lanes (CI)

This repo publishes its CK models (`System`, `System.StreamData`) from **inside the build**, not from a dedicated publish
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

Two pipeline variables are in play and only one of them is the routing decision. Both come
from `templates/steps/update-build-number.yml` in `meshmakers/octo-pipeline-templates`
(pinned by the `ref:` of the `pipelineTemplates` resource in `azure-pipelines.yml`):

| Variable | Meaning |
| -------- | ------- |
| `OctoPublishCatalog` | "where would a GitHub publish go": `PrivateGitHubCatalog` on every branch, `PublicGitHubCatalog` on `r*` tags. The agent exports it as an **environment variable**, so MSBuild picks it up as a global property on any step that does not override it. **Not** the gate. |
| `effectivePublishCatalog` | The gate. `main` / `refs/tags/r*` → `$(OctoPublishCatalog)`; every other branch → `LocalFileSystemCatalog`. |

Resulting truth table:

| Branch / ref | Catalog that receives System, System.StreamData (Test / System.TestIdentity: LocalFileSystemCatalog only, always) |
| ------------ | -------------------------------------------------------------------------- |
| `refs/heads/main` | `PrivateGitHubCatalog` → `meshmakers/construction-kit-libraries-build` |
| `refs/tags/r<X.Y.Z>` | `PublicGitHubCatalog` |
| `refs/heads/test/0.2-dev` | `LocalFileSystemCatalog` (per-run, `Agent.TempDirectory`). The lane catalog `meshmakers/octo-catalog-dev` needs the `publishCkModelsToLaneCatalog` opt-in introduced in `tpl-v0.6.x`; this branch line deliberately does not set it. |
| `refs/heads/dev/*`, feature branches | `LocalFileSystemCatalog` (per-run) |

**Therefore every MSBuild step that can reach `CkCompile` must pass
`/p:OctoPublishCatalog="$(effectivePublishCatalog)"` explicitly** — `Build src`,
`Build samples`, `Build test CK model`, `Build tests` and `Test` all do. Omitting it on any one of them lets MSBuild read
`OctoPublishCatalog` from the environment, which says `PrivateGitHubCatalog` on *every*
branch, so a non-main build force-publishes into the main-lane catalog (AB#5413).

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

**`minEngineVersion` (F1.1-S6).** The compiler writes `CkCompiledModelRoot.MinEngineVersion` =
`CkEngineVersion.CkV2MinEngineVersion` (`3.4.0`, a deterministic constant — not the compiling engine's own
version, so DebugL and release builds produce the same output) for `ckLanguage: 2` and range-retaining models,
**and the highest `minEngineVersion` of its resolved dependencies** (review G3 E-M4: a v1 model on a v2 or
range-retaining dependency gets it too and therefore lands in `ck-models/v3/`, so an older engine never sees a model
it cannot resolve); `null` otherwise (v1 output on v1 dependencies unchanged). 3.4.0 is at or below every engine
the current lib line produces (3.4.x); after the first lib-train release that contains CK v2 it can be raised to that
version to also stop older 3.4.x engines that read `v3/` by hand. `ElementResolver` and both dependency resolvers refuse a model above the
running engine's version with **message 126** (`CkModelRequiresNewerEngine`; a dependency is skipped like a 91).
The running version is the `ConstructionKit.Engine` assembly version; DebugL is `999.0.0` (accepts everything),
and an assembly version below 1.0 (private-feed `0.1.*` builds) skips the check. **Tests of message 126 must not
depend on the ambient assembly version** (DebugL `999.0.0`, CI `0.1.*` = check skipped, r-tag builds `3.x`): pin it
with the internal test seam `using var _ = CkEngineVersion.OverrideCurrentForTests(new Version(3, 4, 149));`
(AsyncLocal, restored on dispose, never set in production; AB#6274), or pass `engineVersion` to `CheckModel`
directly. Raise the constant when a later
engine writes features this engine line cannot read. `CatalogService.PublishAsync` resolves before it publishes, so
it refuses such a model, too.

**Fail fast with the visible versions.** When a dependency range cannot be satisfied,
`CatalogDependencyResolver` lists the versions each readable catalog knows for that model
(`... 'System-[2.5,3.0)' does not match any visible version of System (LocalFileSystemCatalog: 2.4.0)`)
and names the ProjectReference rule (`DependencyResolutionMessageTests`).

Other repositories with sibling CK models must follow the same two rules (checked 2026-10-07:
`octo-construction-kit` — `Basic.Energy` and `EnergyCommunity` depend on `Basic` / `Basic.Energy`
without a ProjectReference; `octo-construction-kit-engine-mongodb` test models depend only on
System).

## CK v2 Range Retention (AB#5664 / AB#5665, Phase 1 F1.1-S2 AB#5905 — behind a flag, default off)

Behind the flag **`OctoCkRangeRetention=true`** (default **off**; MSBuild property, octo-ckc `-rr true`,
or the environment variable of the same name — MSBuild also picks an exported variable up as property).
Flag off ⇒ compiled output is byte-identical to before (verified on System.StreamData).

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

**Known limitation — mixed pins in the catalog (review N4).** The catalog resolver resolves every queued range on
its own (highest catalog version in range). When a range-retaining compile pulls in a classic exact pin
(`System-[2.5.0]` via an exact-pinned dependency) and a range (`System-[2.5,3.0)`) and the catalog already holds
a newer minor, the two resolve to different versions and the compile fails with error 66 (`Multiple versions of
construction kit model 'System'`), even though 2.5.0 satisfies both. Chosen over a per-name solver as the smaller
safe option: the error is deterministic and names the fix (rebuild the exact-pinned dependency with range
retention — the planned one-time re-pin). The repository side is not affected: a tenant has exactly one installed
version, so the exact pin fails on its own (`ResolveFailed`) and the range resolves. Pinned by
`RangeRetentionCompileTests.FlagOn_ClassicExactPinAndRangeOnSameModel_FailWithMultipleVersions`.

The CK SemVer diff does not classify `DependencyRanges` yet (documented exclusion, Phase 2 F2.1). The
element schemas accept `@` in the model part of a reference; the compiled schema accepts
`dependencyRanges`. Tests: `RangeRetentionCompileTests` (flag on/off, YAML round trip with schema
validation, floor violation, resolve against a later minor without recompile, two-level chain).

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
- Message codes for CK v2: 90–113 and 118–128 are in use (table below); **spare: 111, 114–117, 129**. Range
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
| 104 | `CkMethodAuthorizationInvalid` | `InheritanceResolver` | `allowSelf: true` on a `Static` method |
| 105 | `RestrictedAttributeInDerivedRule` | `InheritanceResolver.ValidateRestrictedAttributeUse` | review M9: a `displayNameRule` / `displayDescriptionRule` path or a `Text` index path reaches a `Hidden` attribute (incl. record segments), or `ownerAttributePath` reaches a `Hidden` or `MethodOnly` one — those fields are readable/filterable/searchable and would leak it. Not covered (outside the engine compiler): asset-repo computed columns, association `targetCkAttributeIds` |
| 106 | `HiddenAttributeIndexed` | `InheritanceResolver.ValidateRestrictedAttributeUse` | F1.2-S2 (review N5): a non-text index (`Unique`, `UniqueNotDeleted`, `Ascending`, ...) reaches a Hidden attribute (a unique index reveals values through duplicate-key errors). Text indexes keep reporting 105. Review G3 E-M1: every index path is checked against the type, its derived types **and every other type of the same collection** (defining collection root + all its derived types, i.e. siblings, also from other models) — they share `attributes.<name>`, so a sibling's Hidden assignment would be indexed too |
| 107 | `HiddenAttributeAutoCompleteValues` | `ElementResolver.CheckHiddenAssignments` | F1.2-S2 (N5): a Hidden type/record assignment declares `autoCompleteValues` (published in the model; Secret equivalent: 71) |
| 108 | `HiddenAttributeOnAssociationRole` | `ElementResolver.CheckHiddenAssignments` | F1.2-S2 (N5): association-role attributes cannot be Hidden (no access guard on association attributes). `ReadOnly` / `MethodOnly` stay allowed |
| 109 | `UnknownIndexAttributePath` | `InheritanceResolver.ValidateRestrictedAttributeUse` | F1.2-S2 (review N6): in a `ckLanguage: 2` model every index path segment must name an attribute (matched case-insensitively, like Mongo). Paths starting with `Rt`/`CkTypeId` (entity system fields) are exempt; a path merged into a collection root from a derived type is checked at that type. v1 models: unchanged |
| 110 | `CkElementsInWrongFolder` | `CompilerService` (per element file) | **warning**: an element file declares a root key that is only read from another folder (e.g. `interfaces:` in `types/x.yaml`); those elements are ignored. Applies to every element kind (each folder reads only its own key) |
| 112 | `CkReferenceToInternalElement` | `CkVisibilityValidator` (end of `ReferenceResolver`) | F1.2-S3: a reference from another model to a `visibility: Internal` element — base type/record, attribute assignment (type, record, role, interface member), attribute `valueCkRecordId`/`valueCkEnumId`, `implements`, interface `extends`, type/interface association role and targets, method parameter/result records and enums. Models compared by name |
| 113 | `CkElementNotDerivable` | `CkVisibilityValidator` | F1.2-S3: a type or record derives from a type/record of another model whose effective `derivable` is `Model` (a v2 base without `derivable: Any`) |
| 118 | `CkInterfaceExtendsInvalid` | `ReferenceResolver` (unknown, self) + `InheritanceResolver.ValidateInterfaces` (cycle) | F1.2-S4: an `extends` entry is unknown, the interface itself, or the chain leads back (cycle; reported at every interface on it) |
| 119 | `CkInterfaceMemberConflict` | `InheritanceResolver.ValidateInterfaces` | F1.2-S4: across an interface and its parents one name stands for two attributes, or one attribute appears under two names. Repeating an inherited member identically is allowed |
| 120 | `CkInterfaceAssociationInvalid` | `ReferenceResolver` | F1.2-S4: association member with an unknown role, unknown target, or not exactly one of `targetCkTypeId` / `targetCkInterfaceId` |
| 121 | `CkInterfaceAssociationMissing` | `InheritanceResolver.ValidateAssociationMembers` | F1.2-S4: a type implementing the interface (incl. parents' members) lacks an outbound association (own or inherited) with the role, a compatible target (the type or a subtype; for an interface target an implementor, or an association narrowed to that interface or one extending it) and a multiplicity at least as strict as the member's (One < ZeroOrOne < N). Optional members are skipped |
| 122 | `CkInterfaceMethodConflict` | `InheritanceResolver.InheritInterfaceMethods` | F1.2-S4: a type redeclares an interface method id with another signature (`CkModelDiffService.FormatMethod`). Interface methods are inherited into `CkTypeGraph.AllMethods` (declaring type = highest type of the chain that implements the interface); interface methods follow 100 (duplicate id) and 101-104 |
| 123 | `CkInterfaceHasNoMembers` | `ReferenceResolver` | F1.2-S4: no attribute, association or method member and no `extends` (replaces the former schema `minItems`) |
| 124 | `CkInterfaceDeprecated` | `ReferenceResolver` | F1.2-S4: **warning** for `implements`, `extends`, interface association targets and type association `targetCkInterfaceId` that reference a `deprecated: true` interface |
| 125 | `CkMethodGeneratedNameCollision` | `ElementResolver.CheckGeneratedMethodNames` | F1.4-S2 (review L15): two type methods of a model produce the same generated name `{Type}{Method}` (constant / parameter record) |
| 126 | `CkModelRequiresNewerEngine` | `ElementResolver`, catalog + repository dependency resolvers | F1.1-S6: the compiled model's `minEngineVersion` is above the running engine version (see "minEngineVersion" above) |
| 127 | `CkInterfaceMemberNotUnique` | `ReferenceResolver.CheckCkInterfaces` | review L17: an interface declares the same attribute twice or two members with the same name (case-insensitive); reported at the interface instead of silently dropping the duplicate. Implementation checks (96–99) run on the merged members, so a duplicate produces no follow-up error |
| 128 | `UnknownTargetCkInterfaceOfAssociation` | `ReferenceResolver` | F1.2-S4: a type association's `targetCkInterfaceId` is unknown |

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
SemVer rules: `docs/ck-semver-rules.md` (additions Minor, removals and contract/signature changes Major, `access`
changes Minor with an "access/security" note, `ckLanguage` 1→2 Minor unless it flips the `derivable` default,
`visibility` Public→Internal and `derivable` Any→Model Major, the reverse Minor; interface `extends`, association
and method members added/removed/changed Major, `deprecated` Minor, type association `targetCkInterfaceId` set or
changed Major and cleared Minor).

### Touch-point checklist (keep for every new CK field — contract §2.7)

**Mandatory persistence gate:** every new meta-model field must pass the generic reflection round-trip gate in
`octo-construction-kit-engine-mongodb` — `CkModelReflectionComparer` (F1.3-S3, H-M2 `c3a4911`) compares every
public property of the compiled model after import → Mongo → read-back, so a field that is not written or not read
back (the isRuntimeState class of bug, AB#4589) fails there. Row 10 below is not done until that gate is green.

| # | Touch point | `ckLanguage` | `interfaces` | `implements` | `access` | `methods` | `visibility` / `derivable` | interface completion (S5) |
| - | ----------- | ------------ | ------------ | ------------ | -------- | --------- | -------------------------- | ------------------------- |
| 1 | Source schema | meta (enum 1/2) | interface schema + elements root | `CkType` | `CkTypeAttribute` | method schema + `CkType` | type, record, enum, attribute, association-role, interface, method schemas | interface schema (`extends`, `associations`, `methods`, `deprecated`), type schema `targetCkInterfaceId` |
| 2 | Compiled schema | integer ≥ 1 | compiled root | `CkCompiledType` | shared `$ref` | `CkCompiledType` | `CkCompiledType` (rest via shared `$ref`) | via shared `$ref` |
| 3 | DTO | `CkModelPropertiesDto.CkLanguage` | `CkInterfaceDto`, `CkElementsRootDto`, `CkModelRootBase` | `CkTypeDto.Implements` | `CkTypeAttributeDto.Access` | `CkTypeDto.Methods` + `CkMethodDto` family | `CkVisibilityDto` / `CkDerivableDto` on the 7 element DTOs | `CkInterfaceDto`, `CkInterfaceAssociationDto`, `CkTypeAssociationDto.TargetCkInterfaceId` |
| 4 | Compiler hand-copies | `CompilerService` candidate, `CatalogModelResolver` | same + `interfaces/` loop | `CompilerService` type copy | by reference | `CompilerService` type copy | `CompilerService` type copy (others by reference) | by reference |
| 5 | Graph + `[JsonConstructor]` | `CkCacheRoot.Models` (set in `ElementResolver` / `AppendModel`) | `CkInterfaceGraph`, `CkModelGraph`, `CkCacheRoot`, cache getters | `CkTypeGraph` | `CkTypeAttributeGraph` (init setter) | `CkTypeGraph` + `CkMethodGraph` | settable effective properties, set by `CkModelGraph.ApplyCkV2Modifiers` | `CkInterfaceGraph` (defaulted `[JsonConstructor]` params), `CkInterfaceAssociationGraph`, `CkInterfaceMethodGraph`, `CkTypeAssociationGraph` |
| 6 | Resolvers + codes | 90/91, 126 | 92–94, 118–124, 127 | 95–99, 121, 124 | 90, 99, 105–108 | 100–104, 122, 125 | 90, 112, 113 | 90, 118–124, 128 |
| 7 | SemVer diff/classifier + guard test | yes | yes | yes | yes | yes | yes (`CkModelDiffService.DiffModifiers`) | yes |
| 8 | Source generator | — | yes (ids + `IRt<Name>`) | yes (explicit impls) | — | yes (ids, parameter/result records) | `sealed` (v2; no `abstract`, E-M3) | `IRt<Name>` (extends = interface inheritance) |
| 9 | Docs generator | — | yes | yes | — | yes | yes (non-default only) | yes |
| 10 | Mongo entity + write + read-back (gate: `CkModelReflectionComparer`, mandatory) | engine-mongodb (Persistence agent) | | | | | engine-mongodb (Persistence agent) | engine-mongodb (Persistence agent) |
| 11 | GraphQL CK meta | asset-repo F1.5-S3 (CK meta introspection) | asset-repo F1.5-S2/S3 | asset-repo F1.5-S2 | asset-repo `CkTypeAttributeDtoType.access` | asset-repo F1.5-S3 | asset-repo F1.5-S3 | asset-repo F1.5-S2/S3 |
| 12 | Studio CK browser query (refinery-studio `src/app/graphQL/`, AB#5924) — v2 fields go into the CK v2 follow-up documents run by `CkLanguage2Service`, **never** into the v1 queries (`getCkTypeDetails` etc. must keep working against asset repos without the CK v2 meta API; studio `CLAUDE.md` "CK language 2 in the CK browser") | `getCkModelsLanguage2` | `getCkInterfaces`, `getCkInterfaceDetails`, `getCkTypeLanguage2` | `getCkTypeLanguage2` | `getCkTypeAttributesAccess` | `getCkTypeLanguage2` | `getCkTypeLanguage2`, `getCkElementLanguage2` | `getCkInterfaceDetails` |

Tests: `CkV2SchemaTests`, `CkV2ContractTests`, `CkV2SemVerTests` (`tests/ConstructionKit.Engine.Tests/CkV2`),
`CkV2InterfaceResolverTests` / `CkV2MethodResolverTests` / `CkV2ModifierResolverTests` /
`CkV2InterfaceCompletionResolverTests` (one failing and one passing case per code, on the C#
kitchen sink `sampleData/ckv2KitchenSink/Builder.cs`), `CkV2GraphJsonRoundTripTests` (graph → cache JSON → graph),
`CkIdsCodeGeneratorCkV2Tests`, and in `ConstructionKit.Compiler.Tests` `CkV2CompileTests` (YAML kitchen sink end to
end incl. `interfaces/` folder, gate, docs and range retention of `implements`) and `CkV1CompileOutputUnchangedTests`.

## Important Notes

- The solution uses Azure Pipelines for CI/CD (`azure-pipelines.yml` in the repo root)
- NuGet packages can be published to either public or private feeds depending on configuration
- Local development uses the DebugL configuration with local package sources
- Migration YAML files in `ConstructionKit/migrations/` are automatically embedded as resources