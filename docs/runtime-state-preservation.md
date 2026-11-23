---
description: How blueprint and CK re-imports preserve runtime state and the attribute ownership markers (Secret, RuntimeState, TenantOwned, SeedOwned).
applies_to: src/Runtime.*/Blueprints/**, src/Runtime.*/Exchange/**, src/ConstructionKit.MsBuildTasks/**
---

# Runtime-state preservation on re-import and attribute ownership

#### Runtime-State Preservation on Re-Import (Upsert)

Any `ImportStrategy.Upsert` import maps to a full `ReplaceOne` in the
MongoDB layer — every attribute on the existing entity is overwritten by
the import model's value, even attributes the author never meant to manage
(deployment status, last-error fields, sync sequence numbers, stream-data
`Archive.Status`, …). Two observable failure modes: a "Deployed" adapter
flips back to "Undeployed" on the next controller restart that picks up a
blueprint version bump (the seed encodes `DeploymentState: 0` as the
fresh-tenant default, a sibling case), and an **activated stream-data
archive silently reverts to Disabled** on a blueprint re-apply or a plain
`ImportRt -r`, breaking SD queries with
`STREAMDATA_ARCHIVE_NOT_ACTIVATED`.

The fix is per-attribute, opt-in on the CK side, and lives at the **single
import choke point** so every Upsert caller gets it automatically:

- **CK schema**: `CkAttribute.ownership: SeedOwned | TenantOwned |
  RuntimeState | Secret`, with `isRuntimeState: bool` kept as a
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
- **Per-import state**: `ImportRtModelCommand` is transient, but
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
- **Mandatory-attribute validation**: the bulk path bypasses the
  entity rule engine, so `ImportRtModelCommand.FindMissingMandatoryMembers` applies
  the rule engine's insert predicate (not optional, no value, no default values, no
  auto-increment reference; empty Secret = not set) to every imported entity. Since then it also recurses into every `Record` value and every item of a `RecordArray`
  value, nested records included (same recursion as `EntityRuleEngine.SetDefaultValuesOnInsert`);
  an absent or null record-valued attribute is not inspected, unresolvable records are
  skipped. A top-level violation is reported as the CK attribute id, a record member as
  `<ckAttributeId> at <path>` (e.g. `System.Communication-1/ExternalId at States[3]`,
  nested `Groups[1].Inner`). Reporting: one warning log + one `IRtImportAuditTrail.RecordMissingMandatoryAttributesAsync`
  audit event per entity (all its violations); with `Import:StrictMandatoryValidation`
  (`RtImportOptions`, default off) the import is rejected before anything is written
  (`ExchangeException.MandatoryAttributesMissing`, summary capped at 20 entities).
  Default mode still imports, so existing tenant imports, blueprint applies and
  restores are unchanged. Tests: `ImportRtModelCommandMandatoryValidationTests`.
- **Who benefits**: because it sits in the import command, all Upsert
  callers are covered with no per-caller code:
  - blueprint seed apply (`BlueprintService.ApplySeedDataForBlueprintAsync`
    and the locked/merge import path) — the explicit pre-import preserve call
    that used to live in `BlueprintService` was removed; it is now automatic.
  - the plain `ImportRt -r` CLI path (`octo-bot-services` `ImportModelJob` →
    `ImportAsync`) — the target (archive re-import).
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

- **Omitted preserved attributes are injected, not cleared**: because
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

- **Record-typed values need converting, not copying**: the value
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
and that the preserved value is handed to the converter rather than
copied raw — including the `RecordArray` case the suite was missing, which is why
the bug shipped. The conversion itself is covered by
`ImportRtModelCommandToTransportValueTests` against the real CK cache fixture
(record → DTO with matching record / attribute ids, per-element array conversion,
missing record attribute stays omitted rather than nulled, scalar and string
pass-through).

#### Attribute Ownership

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

Armed models (and earlier): System.Communication, System.Identity, System.Ai,
System.Notification, Meshmakers.Accounting(.Tesla) via the package targets, and
**System.StreamData in this repository**. This repository cannot consume its own
MsBuildTasks package, so `Directory.Build.targets` registers the same task from
`bin/<Configuration>/tasks/<tfm>/` with `TaskFactory="TaskHostFactory"` (no DLL lock in
a reused node); a model project that sets `OctoEnforceRuntimeStateMarkers=true` adds a
`ProjectReference` to `ConstructionKit.MsBuildTasks` with `ReferenceOutputAssembly="false"`
for the build order (see `StreamDataCkModel.csproj`). System.StreamData got 56
restating `ownership: SeedOwned` markers. They change the compiled file; (equivalent-content rule, `CkPublishEquivalence`) the publish
gate treats such a restatement under an already published version as a no-op with a WARNING instead of `OCTO-CK205` (System.StreamData was bumped
to 1.17.1 before that rule existed). Rule: a change of the compiled output that is a real content change (also a flipped ownership other than the
default restated, `TenantOwned`/`Secret`) needs a version bump before it reaches main; a pure restatement of a default no longer does. The F4
the F4
retention attributes (`Archive.RawRetentionMs`, `Archive.MaxRetroactiveReachMs`) carry the
comment "under review". `SystemCkModel` is not armed yet.

**Review question for every new attribute:** could an operator or user type this value in
the product? Yes → `TenantOwned` (or `Secret` for a credential); a service writes it →
`RuntimeState`; the product ships and corrects it → `SeedOwned`.
