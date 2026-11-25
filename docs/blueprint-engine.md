---
description: BlueprintService internals: tenant CK upgrade guard, replacesGenericCreate preflight, ImportRt and export access rules, identity-owned fields, bulk import.
applies_to: src/Runtime.*/Blueprints/**, src/Runtime.*/Exchange/**
---

# Blueprint engine: upgrade guard, generic-create guard, import and export rules

## Blueprints and Migrations

The project supports two types of migrations:

### Blueprints
Blueprints initialize tenants with pre-configured CK models and runtime data. See `docs/blueprints.md` for details.

Key services:
- `IBlueprintService` - Applies blueprints to tenants
- `IBlueprintCkUpgradeGuard` (host-registered, optional) - tenant upgrade guard for the CK models a blueprint installs or raises (CK v2 F2.5), see below
- `IStreamDataTenantServicesProvider` (host-registered, optional, `Runtime.Contracts/StreamData`; engine-mongodb `TenantStreamDataServicesProvider`) - after the seed import `BlueprintService` activates the archives the seed created (raw first, rollups in source order; pre-existing archives never touched; `archiveActivation.exclude` opt-out; guard: no backfill for TimeWeightedAvg/StateDuration rollups over a raw source) and queues the backfill of new rollups through `BlueprintArchiveActivator`; results in `ArchiveActivations`. Without a provider new archives stay Created. Rules: `docs/blueprints.md`, "Archives and rollups in a blueprint"; tests `BlueprintArchiveActivatorTests`, `BlueprintServiceArchiveActivationTests`

#### One import command per import

`ImportRtModelCommand` holds the state of the one import running on it (entity and association queues, duplicate check,
guard entries, skipped seed edges, archive column and formula changes) and resets it at the start of every import, so it
is not safe for concurrent imports. One `BlueprintService` applies blueprints to several tenants at the same time (the
tenant setup on startup runs the child tenants in parallel), so `BlueprintService` and `BlueprintMigrationExecutor` take
a new command for every import from `Func<IImportRtModelCommand>` (registered by `AddRuntimeEngine`, transient) and read
the results of the import from that command only. A host that imports for several tenants itself does the same. Every
log line of a blueprint apply carries `TenantId` and `BlueprintId` in a log scope, so the lines of parallel applies can be
told apart. Tests: `BlueprintServiceDeferredSeedEdgesTests` (`Install_TakesANewImportCommand_*`,
`ConcurrentInstalls_OnOneService_ImportOnSeparateCommands`, `Install_LogLinesOfTheImport_CarryTenantAndBlueprintId`),
`ImportRtModelCommandFactoryRegistrationTests`; end to end over the parallel tenant setup:
`ConcurrentTenantSeedImportIntegrationTests` in the identity service.

#### Tenant upgrade guard in `BlueprintService` (CK v2 F2.5)

A blueprint installs the floor of each `ckModelDependencies` range as an exact model version, so an apply or update can replace an installed model
exactly like `ImportFromCatalog`. `BlueprintService` therefore asks an optional host service, `IBlueprintCkUpgradeGuard`
(`Runtime.Contracts/Blueprints/IBlueprintCkUpgradeGuard.cs`; the guard lives in engine-mongodb and needs the catalogs, so the host -
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

#### Blueprint apply and `replacesGenericCreate` (F3.7)

A blueprint never runs a CK method, so a seed would skip what the method of a `replacesGenericCreate` type guarantees
(certificate, provisioning). `BlueprintGenericCreateGuard.FindRefusalsAsync` (Runtime.Engine/Blueprints) is the preflight;
the lookup itself is the shared `GenericCreateNewEntityCheck` (Runtime.Engine/Exchange, internal), which the `ImportRt` caller
path uses as well (next section). It is modelled on `ImportBlueprintLockEnforcer.PreflightAsync`: per CK type of the entities it asks
`GenericCreateReplacement.TryGetReplacingMethod` (types without such a method cost no read), reads the stored entities of
the flagged types by rtId in one batched call and refuses every entity whose rtId is empty or does not exist in the tenant.
An entity whose rtId exists is upserted as before. It runs before the import at three places, so a refusal writes nothing:
- `ApplySeedDataForBlueprintAsync` (install, `--force`, dependency install): after the tenant-owned seed entities were
  taken out; the message(s) go to the `OperationResult`, the blueprint is not recorded as installed (blueprints of the
  install order that were applied before it keep their state, as for every other seed error);
- `ApplyDiffAsync` (blueprint update, all modes): over the entities to import (adds, updates, promoted conflicts);
  `ContinueOnError` does not soften it; the errors are `OCTO-CK136: <text>` strings in `BlueprintUpdateResult.Errors`;
- `BlueprintMigrationExecutor.ExecuteAddAsync` (migration `Add` step): the step fails with the same `OCTO-CK136` text
  (not for `DryRun`, which writes nothing).
Out of scope on purpose: `CkModelMigrationService` (a model migration, not a blueprint), `ApplyChanges`, an opt-out
flag (`ImportRt` has its own check, next section). The lookup itself is public: `GenericCreateReplacement.FindReplacingMethod(CkTypeGraph)`
/ `TryGetReplacingMethod(ICkCacheService, tenantId, RtCkId<CkTypeId>, out CkMethodGraph)` and the shared reason token
`GenericCreateReplacement.ReasonToken` live in `ConstructionKit.Contracts` (`DependencyGraph`); asset-repo's
`GenericCreateReplacedByMethodGraphQlError.TryCreate` can call them instead of its own copy of the lookup. The CK message
table of `ConstructionKit.Engine` is `internal`; the assembly makes it visible to `Meshmakers.Octo.Runtime.Engine` for this
message. Tests: `BlueprintGenericCreateRefusalTests` (type graphs are built in the test, no System version involved).

#### `System.Identity/User` is created only through `User.CreateUser-1`

System.Identity 3.0.0 sets `replacesGenericCreate: true` on the static `User.CreateUser-1` in the model (row M16, part of the
major), so `GenericCreateReplacement.FindReplacingMethod` answers it from the flag like for every other type. A user is created
only by the identity service (REST or the method), which runs the user manager (normalised name and e-mail, password policy,
security stamp). Every generic writer asks this one helper, so a **new** user is refused everywhere with
`GENERIC_CREATE_REPLACED_BY_METHOD` and the method id `System.Identity/User.CreateUser-1`: asset-repo generic, typed and
query-row create, MCP create, the mesh-adapter insert pre-check (`GenericCreateGuard`), the caller path of `ImportRt` (137) and
blueprint seed data, update diff and migration `Add` (136). An existing user (rtId stored) is updated as before. Not affected: the
identity service's repository writes, the system import path of `ImportRt` without a caller (no blueprint guard there), a tenant
restore. **History:** System.Identity 2.29.0 - 2.30.0 could not carry the flag (a Major) and the engine stood in for it with a
runtime rule in `GenericCreateReplacement`; that rule was removed. A tenant still on System.Identity 2.x
(until the identity service has imported 3.0.0 into it) keeps the generic create of a user. Tests:
`GenericCreateReplacementIdentityUserTests`, `ImportRtModelCommandGenericCreateTests` (`*IdentityUser*`),
`BlueprintGenericCreateRefusalTests` (`*IdentityUser*`).

#### `ImportRt` and `replacesGenericCreate` (F3.7)

A user-initiated `ImportRt` (`ImportAsCallerAsync`, the `caller != null` path that bot-services uses) cannot create a **new**
entity of a type whose static method declares `replacesGenericCreate` (own or inherited), exactly as the GraphQL generic create
refuses it: only the method may create it. `ImportRtModelCommand.PreflightGenericCreateAsync` runs next to the blueprint-lock
preflight, for Insert and Upsert, before the first write, over the whole file (YAML: the deserialized entities; JSON: a first
pass over the file like `CollectEntityIdsAsync`, made only when the tenant has such a type, so other imports cost nothing extra).
It uses the import session and `GenericCreateNewEntityCheck.FindAsync` (the same lookup as the blueprint guard: types without a
method cost no read, one batched read per flagged type).
- An entity whose rtId is already stored is updated as before (Upsert); an Insert of it fails on the duplicate as before.
- The file fails as a whole with ONE `ExchangeException`: `MessageNumber` 137, `Messages` holds one `OperationMessage` per
  offender (`ImportRtCreateReplacedByMethod`; parameters `rtId`, `ckTypeId`, `methodId` = version-less qualified method id,
  `reason` = `GENERIC_CREATE_REPLACED_BY_METHOD`), and `Message` names the reason token, the type, the method and every rtId, so
  the job result (which carries the exception message) shows the code.
- The system path is unchanged: `ImportModelAsync` and `ImportAsync` (no caller) are not checked by the command (blueprint
  seed data, update diff and migration `Add` run the blueprint guard before they call it); a test pins both.
- A full tenant restore is not an import (it works on MongoDB directly) and is unaffected. Copying method-created entities to a
  tenant that does not hold them is therefore not possible with `ImportRt`; use a restore or the method.
- Tests: `ImportRtModelCommandGenericCreateTests`.

#### RT export and import honour `access` (F4.2, findings H2/H3)

`access: Hidden` / `MethodOnly` used to restrict only asset-repo GraphQL. The RT exchange paths now keep the promise too.
The rule lives in one place on the CK graph: `AttributeAccess.IsReadableOutsideMethod` (not `Hidden`) and
`AttributeAccess.IsWritableOutsideMethod` (`ReadWrite`/`ReadOnly`), exposed as `CkTypeAttributeGraph.IsReadableOutsideMethod` /
`IsWritableOutsideMethod`. Asset-repo `AccessQueryGuard`, the mesh adapter and the MCP service can call these instead of
testing the enum inline.
- **Export** (`RtEntityToTcDtoConverter`, the only export converter): a `Hidden` attribute is never emitted - type attributes and
  record members one by one (the loop is re-entered for records, same granularity as ownership). `MethodOnly` and `ReadOnly`
  stay exported (readable).
- **Import**: a value for a `Hidden` or `MethodOnly` attribute (also a record member, `Profile.Token` / `Credentials[1].Token`;
  an explicit `null` counts) is refused with `OCTO-CK139` before anything is written, on **every** path and for system and user
  imports alike: `ImportAsCallerAsync`, `ImportAsync` (YAML and JSON stream), `ImportTextAsync` and `ImportModelAsync`
  (`ImportRtModelCommand.PreflightAttributeAccess`). The JSON file is checked in the same first pass that `CollectEntityIdsAsync`
  makes for the other preflights; the pass is only made when the tenant has a restricted attribute
  (`RtAttributeAccessCheck.TenantHasRestrictedAttribute`), so other imports cost nothing extra. One `ExchangeException`
  (`MessageNumber` 139, `Messages` one per offender: parameters `rtId`, `ckTypeId`, `attributePath`). `ReadOnly` stays importable.
  Unlike 137 the check is not limited to the caller path: the system path (`ImportModelAsync`, used by blueprint seed) is checked too.
- **Blueprints**: `BlueprintGenericCreateGuard.FindRefusalsAsync` runs the same check first (seed on install, update diff, migration
  `Add`; same three call sites as 136, same error shapes: `OperationResult` message, `OCTO-CK139: <text>` in
  `BlueprintUpdateResult.Errors`, failed `Add` step). The migration **`Update` and `Transform` steps** (they write attributes by name
  through `ApplyAttributeUpdate`) refuse a `Hidden`/`MethodOnly` target or source attribute for the whole step before the first
  entity is replaced (`EnsureWritableOutsideMethod`; `rtId` is `000...` in that message, a step has no single entity).
- **Round trip**: an export that contains `MethodOnly` values cannot be imported again (139). A tenant restore works on MongoDB
  directly and is unaffected. No published model uses `Hidden`/`MethodOnly` yet (first: System.Identity).
- Tests: `RtAttributeAccessCheckTests`, `RtEntityToTcDtoConverterAccessTests`, `ImportRtModelCommandAttributeAccessTests`,
  `BlueprintAttributeAccessRefusalTests` (synthetic graph `Access/AccessTestModel.cs`).

#### Identity-owned User fields and identity associations (F4.2)

**Attributes: the model says it (System.Identity 3.0.0).** The identity-owned attributes of `System.Identity/User` -
`NormalizedUserName`, `NormalizedEmail`, `EmailConfirmed`, `PhoneNumberConfirmed`, `TwoFactorEnabled`, `LockoutEnabled`,
`LockoutEnd`, `AccessFailedCount`, `ResetPasswordOnLogin`, `Claims`, `UserLogins`, `PreferredChannelBindingId` (with `UserName` and
`Email` from 2.28.0 twelve in all) - are `access: MethodOnly` in `ck-user.yaml`. There is no attribute list in the engine any more
(removed `UserAttributeNames` / `IsIdentityOwnedAttribute` / `IsUserType`): the generic access rules enforce it for every
model - asset-repo GraphQL and query rows (`ATTRIBUTE_NOT_WRITABLE`), `ImportRt` and blueprint seeds (rule 139, every path), the
mesh-adapter write nodes (`RtAttributeAccessGuard`). `ReadOnly` would not do (it is importable), which is why `LockoutEnd` and
`AccessFailedCount` went from `ReadOnly` to `MethodOnly`. Differences to the list above: the system path (blueprint seed,
`ImportModelAsync`) refuses a value for these attributes too (139; no blueprint seeds a User), and a GraphQL refusal no longer
carries `reason = identity-owned` but the generic access detail. The identity service writes through its own repository and the
tenant restore works on MongoDB: neither is guarded.
- **Omission (now general):** a caller `Upsert` is a full replace, and the blanking guard alone would let an omitted value
  go under `RtImportBlankingPolicy.Allow` or a confirmation (a lockout reset by leaving `LockoutEnd` out). On the caller path
  `PreserveRuntimeStateAttributesAsync` therefore treats **every attribute that is not writable outside a method** (`access`
  `MethodOnly` or `Hidden`, `!IsWritableOutsideMethod`) like runtime state: the existing value is carried over, whatever the policy
  (flag `_callerImport`, set by `ImportFileAsync` only). The system path keeps the blanking semantics. Tests:
  `ImportRtModelCommandIdentityOwnedTests` (`CallerUpsert_*`, `SystemUpsert_*`).
- **Omission inside records: merge.** The attribute rule alone left a hole: a record attribute `R` whose record has a member `M`
  with `access: MethodOnly` or `Hidden` is replaced as a whole by an upsert, so a file that writes `R` without `M` dropped the stored
  `R.M`. On the caller path `PreserveSecretRecordMembers` (the Secret carry-over, now with the optional `convertProtected`
  converter that only the caller path passes) therefore also carries the stored value of every `!IsWritableOutsideMethod` member into the
  written record: single record by position, record array by the **record key** (same matching as Secrets; a new element has nothing to
  keep; a removed element or an omitted record attribute removes its members, as for Secrets and as the asset-repo GraphQL update does), nested records the same. Chosen over refusing for symmetry with the attribute rule and with GraphQL
  (`RecordMemberPreservation`). Where merging is unsafe - a record array whose record has **no record key**, or whose key member is itself
  not writable outside a method, so the elements cannot be matched - the write is **refused** before anything is written with **message
  141** `RtRecordMemberNotPreservable` (`ExchangeException.RecordMemberNotPreservable`; parameters `rtId`, `ckTypeId`, `path` such as
  `Credentials[0].Token`), but only when the seed holds an element and a stored element holds a value in such a member (nested records
  of the stored element included); nothing stored means nothing to lose. An explicit value for the member is still refused by 139. The
  secret-record selection (`secretRecordAttributes`) takes record attributes with such members on the caller path
  (`RecordHasSecretMembers(..., includeMethodOnlyMembers)`); a record attribute that is itself MethodOnly/Hidden is carried whole by the
  attribute rule. **Derived records (reopen):** a record value may hold a record derived from the declared one, and a protected
  member may exist only on the derived record. The selection therefore also looks at every record that derives from the declared record
  (and from each nested record), taken from the `BaseRecords` chains of the tenant's records (`DerivedRecordsFunc`;
  `CkRecordGraph.DerivedRecords` is never filled by the CK resolver and must not be relied on). The walk itself uses the record type of
  each written element: an element whose record type the caller changes keeps only the members its new record declares (like removing
  the element and adding a new one). The system path is unchanged. Tests: `ImportRtModelCommandRecordMemberAccessTests` (unit, fake
  repository); against a real MongoDB with a compiled CK model: engine-mongodb `ImportRtRecordMemberPreservationIntegrationTests`.

**Associations: still a list.** `IdentityOwnedWrites` (`ConstructionKit.Contracts`, public) is reduced to the association roles
`System.Identity/AssignedRole`, `GroupMember`, `ChildGroup`, `MayActAs`, `GrantsPermission` (both directions; `IsIdentityOwnedAssociation`,
`AssociationRoleIds`, `RefusalReason`). **Why not in the model:** the CK access model has no association-level restriction -
`access` exists only on attributes (including the attributes of an association role), and an association cannot be declared
method-only. Matching is version-less. Consumers: asset-repo GraphQL (generic and typed create/update, query-row update), the
mesh-adapter write nodes and the **caller path** of `ImportRt`: `ImportRtModelCommand.PreflightIdentityOwnedWrites` (YAML) and
the first pass of the JSON stream (`RtIdentityOwnedWriteCheck`, made only for a caller import on a tenant that has System.Identity)
refuse the whole file before the first write with one `ExchangeException`, **message 140** `RtIdentityOwnedWriteRestricted`
(parameters `rtId`, `ckTypeId`, `path` = `association <roleId> -> <targetRtId>`; the association is caught on whichever entity of the
file declares it). The system path (no caller: blueprint seed and update, `ImportModelAsync`, `ImportTextAsync`, CK migrations) is
**not** checked: the System.Identity.Bootstrap seed writes the `TenantOwners` role edges through it. Repository writes
(`ApplyChangesAsync`, insert/replace) are not checked either. The roles `PolicyPermission` and `IdentifiesUser` are deliberately not
in the list. Remove the list when the CK language can express association write restrictions.
- Tests: `ImportRtModelCommandIdentityOwnedTests` (every role on the caller path, YAML and JSON stream, several offenders, the MethodOnly
  attributes through rule 139 on caller and system path, the system path imports the edges as before, tenant without System.Identity
  reads the file once, version-less matching, omission preserved).

#### JSON import writes its bulks inside the awaited call

`ImportAsync` / `ImportAsCallerAsync` with a JSON file read the entities through `IRtDeserializeStream` in bulks of 5000.
Earlier, each bulk was written by an `async void` handler of the `BulkDeserialized` event: the call returned and
committed the transaction before the entities were written (against MongoDB the entity appeared ~3 s later), and a
write error - message 141, the seed or archive guard, strict mandatory validation, a duplicate key - never reached the
caller (bot-services reported the job as done). The handler also set `IsHandled` only after its first await, so the
stream handed every pending entity out again with each further entity of a file above 5000 entities.

- `IRtDeserializeStream.ReadBulksAsync(CancellationToken)` (`IAsyncEnumerable<IReadOnlyList<RtEntityTcDto>>`) is the
  way to process bulks asynchronously: the next bulk is read only when the consumer asks for it, in file order, every
  entity exactly once. `ImportRtModelCommand` writes each bulk with `await foreach`, so the call returns after the last
  write, a failing bulk fails the import with the same exception as the YAML path (`ExchangeException`, inner exception
  of the write) and nothing is committed. The first pass over the file (`CollectEntityIdsAsync`) uses the same method.
  The interface carries a default implementation for implementers written before it (reads the whole stream through the
  event, then hands the bulks out; holds everything in memory) - no implementer exists in the monorepo (checked 2026-11-05).
- The import session is disposed at the end of every import method (`using var session`): a failure before the
  commit aborts the transaction instead of leaving it open until the server times it out.
- `BulkDeserialized` / `ReadAsync` stay for synchronous handlers only (built on `ReadBulksAsync`); never subscribe an
  `async` handler. `RtDeserializeEventArgs.IsHandled` is no longer read.
- The cancellation token of the import now also cancels the read of the JSON file (rollback, nothing committed).
- **Record values in a JSON file**: the stream reads an attribute value of type `object` with
  Newtonsoft, so a record arrived as a `JObject` (a record array as a `JArray`). The import expects `RtRecordTcDto` (the YAML
  reader's shape): a single record was silently dropped, a record array failed with an `InvalidCastException` - both
  unseen while the write ran in the `async void` handler. `RtJsonRecordValueNormalizer` (called by the stream for every
  entity) turns a JSON object with `ckRecordId` (and an array of only such objects) into `RtRecordTcDto`, nested records
  included; every other value is left as the reader produced it. So the record checks of the first pass (139) and the
  carry-over/refusal of the caller upsert (141) now see JSON records too. Not changed: `RtJsonSerializer.SerializeAsync`
  (System.Text.Json) cannot write an `RtRecordTcDto` (the converter on `CkRecordId` does not fit the type) - a separate gap.
- Tests: `ImportRtModelCommandJsonBulkAwaitTests` (repository that completes asynchronously, as MongoDB does: written
  when the call returns, commit after the writes, a failing write fails the import and commits nothing),
  `ImportRtModelCommandRecordMemberAccessTests` (`JsonCallerUpsert_*`: 141 over JSON; `JsonFileThroughTheRealStream_*`: the
  real reader with record values), `RtDeserializeStreamBulkTests` (bulks, order, duplicates, record values).
  The shared test double is `tests/Runtime.Engine.Tests/Exchange/FakeRtDeserializeStream.cs`. Against a real MongoDB:
  engine-mongodb `ImportRtJsonAwaitIntegrationTests`.
