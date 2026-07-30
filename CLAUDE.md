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
- Model definitions are organized in folders: `types/`, `attributes/`, `enums/`, `records/`, `associations/`
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
/devops-build          # CI/CD pipeline definitions
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

- **CK schema**: `CkAttribute.isRuntimeState: bool` (defaults to false).
  See `Serialization/Schema/construction-kit-elements-attribute.schema.json`,
  `CkAttributeDto`, and the propagation through `CkAttributeGraph` /
  `CkTypeAttributeGraph`. Existing CK models that don't set the flag
  behave exactly as before.
- **Import path (shared)**:
  `ImportRtModelCommand.PreserveRuntimeStateAttributesAsync` runs at the top
  of the private `ImportEntityAsync` choke point — through which **all three**
  public import methods (`ImportModelAsync`, `ImportTextAsync`, `ImportAsync`
  file/stream) funnel — gated on `importStrategy == Upsert`. It groups the
  incoming entities by CK type for one batch repo query per type (on the
  ambient import session/transaction), looks each existing entity up by
  `RtId`, and for every attribute the CK cache flags `IsRuntimeState=true`
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
  in-memory, so the actual import stays a `ReplaceOne`. Attributes not in the
  import model are still cleared on the upsert in line with prior behaviour;
  preservation can only *replace* an imported value for an attribute the model
  already declares. Fresh tenants / brand-new entities (no existing entity)
  are silent no-ops.

The testable seam is the pure
`ImportRtModelCommand.PreserveAttributesForEntity` static helper, exposed
via `InternalsVisibleTo`. Tests in
`tests/Runtime.Engine.Tests/Exchange/ImportRtModelCommandPreserveAttributesForEntityTests.cs`
cover: flagged+both-sides → preserved; unflagged → imported value wins; mixed;
model-only (additive CK bump) → imported value lands; existing-only (model
omits the attr) → no-op; multi-attribute independence; and the stream-data
`Archive.Status` activated-survives-re-import / fresh-import-keeps-Disabled cases.

Author guidance: when adding a CK attribute that carries runtime state
the host services / operators / users own at runtime (status enums,
sync counters, error message fields, change timestamps, archive lifecycle
status), mark it `isRuntimeState: true` so a future version bump or
re-import can never trample it. When in doubt: authors describe
**configuration**; everything else is runtime state.

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

## Important Notes

- The solution uses Azure Pipelines for CI/CD (devops-build/azure-pipelines.yml)
- NuGet packages can be published to either public or private feeds depending on configuration
- Local development uses the DebugL configuration with local package sources
- Migration YAML files in `ConstructionKit/migrations/` are automatically embedded as resources