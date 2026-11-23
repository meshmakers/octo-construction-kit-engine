---
description: Run-time semantics: SetValue null, load by rtId, formula errors, enum import, delete as archive, record-array item update, rtVersion, unacknowledged writes.
applies_to: src/Runtime.Contracts/RtPath*.cs, src/Runtime.Contracts/RtVersion*.cs, src/Runtime.Contracts/Unacknowledged*.cs, src/Runtime.Engine.Formulas/**
---

# Runtime write and read semantics

## `RtPathEvaluator.SetValue` and explicit null

A `null` on an **optional** enum attribute sets it to null; on a **required** enum (`isOptional: false`) it is refused
with an `InvalidPathException` that names the path, before anything is changed. A `null` on a sub-path of a record that
does not exist yet (`Amount.Value` without `Amount`) is dropped by default (query-row cells must not create empty
records); the overloads with `RtPathNullHandling.CreateMissingRecords` create the record with that member explicitly null
(used by the mesh adapter's `CreateUpdateInfo@1`, so an explicit null reaches an archive as null). Rules and
table: `docs/runtime-semantics.md`. Tests: `RtPathEvaluatorTests`, region `SetValue explicit null`.

## Load by rtId to use it: archived counts as not found

The default delete archives an entity (`rtState = Archived`, the document stays). List queries filter archived entities,
but `IRuntimeRepository.GetRtEntityByRtIdAsync` returns the document whatever its state, and some callers rely on that
(restore, history, import) - its semantics stay unchanged. A caller that loads an entity by rtId in order to **use** it
(execute a persisted query, run a configuration) calls `RuntimeRepositoryExtensions.GetActiveRtEntityByRtIdAsync`
(`Runtime.Contracts/Repositories`, generic by `OctoObjectId` and by `RtEntityId`) instead: `null` for a missing or an archived
entity; an entity without `rtState` (written before it existed) counts as active. It is an extension method, not an interface
member, so engine-mongodb and other implementers need no change. Users: asset-repo `PersistedQueryLookup`, mesh-adapter
`GetQueryById@1`, the MCP persisted-query tools. Tests: `RuntimeRepositoryExtensionsTests`.

## Formula engine: parse once, never NULL for an exception

`FormulaEngine` (Runtime.Engine.Formulas) caches a parsed mXparser expression per thread and formula (`FormulaShape`, one
`PreparedFormula` per set of identifiers the values supply) and only sets argument values per call; a fresh expression per row
cost ~200 KiB and ran a 2 M-row backfill on prod-1 out of memory. Rules: bind only names that occur in the formula (own
identifier scan, not mXparser's missing-argument list); an exception thrown inside `calculate()` becomes
`FormulaEvaluationException` (never NaN/NULL). **mXparser stays pinned to 4.4.2**: the last Simplified-BSD version;
4.4.3+/5.x/6.x are dual-licensed and Meshmakers holds no commercial licence — never upgrade it, never call a `License.iConfirm*`
method (`MxParser_IsTheLastBsdLicensedVersion` fails on an upgrade). 4.4.2 needs no periodic re-parse (6.1.1 leaked ~400 B per
`if` evaluation) and writes no licence warning. Repair of an
active computed column: `IArchiveLifecycleService.RepairComputedColumnAsync` (job marker `computed-column:repair:<name>`, run by
the backfill drain). Details: `docs/concept-computed-columns.md` §8.3b. Tests: `FormulaEngineReuseTests`, `ComputedColumnRepairJobTests`.

## Extensible Enum Import

Enums marked `isExtensible: true` allow runtime additions via the customize-API. When a new
CK model version is imported, custom extension values are preserved into the new model
revision; if a custom value collides with a CK-defined value on the same numeric key, the
custom value takes precedence and the collision is reported via `ICkModelImportAuditTrail`
so it appears in the platform event log. The preservation/override logic itself lives in the
MongoDB layer (`DatabaseCkModelRepository.PreserveExtensibleEnumValues`); the engine layer
owns the audit-trail contract and a logging default.

## Delete is Archive by default; importing an archived entity again

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

## Atomic update of one RecordArray item

Problem: pipelines that read an entity, change one item of a `RecordArray` in memory and write the whole array back lose
updates under parallel execution (two writers read the same baseline, the later write overwrites the other item).
`$set attributes.<name>` per attribute makes scalar attributes safe, but a RecordArray is always replaced as a whole.

Contract (additive, no CK model or meta-model change): `IEntityUpdateInfo<TEntity>.RecordArrayItemUpdates`
(`IReadOnlyCollection<RtRecordArrayItemUpdate>?`, default `null`) and the factory
`EntityUpdateInfo<TEntity>.CreateRecordArrayItemUpdate(rtEntityId, rtEntity, itemUpdates)`.
`RtRecordArrayItemUpdate` = RecordArray attribute name, match member, match value and member -> value pairs (names PascalCase).
The info serialises the list only when set (Newtonsoft and System.Text.Json), so the wire shape of every existing info is unchanged.

Semantics:
- **One atomic statement, no read, no version check, no retry.** The persistence layer writes the item members with one update
  (MongoDB: `$set attributes.<arr>.$[iN].attributes.<member>` with `arrayFilters` on the match member, see octo-construction-kit-engine-mongodb). Updates of different items never interfere; updates of the same item keep the last write.
- **Use it** when several writers change different items of the same RecordArray of one entity in parallel (e.g. one
  `DataPoint` per state in `Loxone/Control.States`). **Do not use it** to replace or reorder the array, to add or remove items
  (use a whole-value update), or when a single writer owns the entity.
- `RtEntity` of the info carries regular attribute updates that go out in the **same** statement; it may carry none. A whole-value
  update of the SAME RecordArray in the same info is refused as ambiguous.
- **Validation** (`RecordArrayItemUpdateValidator`, run by `EntityRuleEngine`, message 25/26): mod option Update only; the attribute
  must be a `RecordArray`; the match member and members must exist in the record (or a derived record); the match member must be
  String, Integer, Integer64 or Enum and cannot be written by the item update; members must be scalar (String, Integer, Integer64,
  Double, Boolean, DateTime, DateTimeOffset, TimeSpan, Enum); **`Secret` members are refused** (message 26); values are converted to the
  member's CLR type and refused when they do not fit (a String member takes text and renders numbers, booleans and dates
  as text - ISO-8601 for dates - because the pipeline data context turns ISO-8601 text into `DateTime`). Entries for the same item
  are merged, the last value wins.
- **No match** (message 27, a *warning* in the caller's `OperationResult`, no exception, no `rtVersion` bump): when the info carries
  no regular attribute and no item matches - or the entity does not exist. Items that miss while others match are silent no-ops
  (a single statement cannot say which one missed). With regular attributes in the info they are written regardless and a
  missing entity is an error as for any update.
- **Permissions** are those of a regular update of the entity: `DataPermissionWriteGuard` judges the info (write, visibility, owner,
  blueprint lock) by entity id, regardless of how the write is expressed.
- **Not covered**: display-name/description rules that reference the RecordArray are not recomputed by an item update (the stored entity
  is not read); `IBulkRtMutation.ApplyChangesAsync(..., OperationResult)` is the overload that reports the warning (the
  repository's `ApplyChangesAsync` passes its `OperationResult` through).
- Tests: `RecordArrayItemUpdateRuleEngineTests`, `BulkRtMutationRecordArrayItemTests` (this repo); the real-MongoDB concurrency test
  lives in engine-mongodb.

## Expected rtVersion - optimistic concurrency for update and replace

Problem: a service that reads an entity, changes it in memory and writes it back (identity's user store: `AccessFailedCount`,
finding L9) loses updates when two writers run in parallel - both read version n, the later write overwrites the other.
`AttributeNewerThanGuard` does not help: it compares a timestamp the caller writes, and it skips a rejected write silently.

Contract (additive; callers that pass nothing behave exactly as before):
- `EntityUpdateInfo<TEntity>.CreateUpdate(rtEntityId, rtEntity, ulong expectedRtVersion)` and
  `CreateReplace(rtEntityId, rtEntity, ulong expectedRtVersion)`; `IEntityUpdateInfo<TEntity>.ExpectedRtVersion` (`ulong?`, default
  `null`; serialised only when set, Newtonsoft and System.Text.Json).
- Repository shortcuts by id: `ReplaceOneRtEntityByIdAsync(session, [ckTypeId,] rtId, rtEntity, expectedRtVersion)` and
  `UpdateOneRtEntityByIdAsync(session, [ckTypeId,] rtId, rtEntity, expectedRtVersion)` (typed and untyped). Bulk: put several infos with
  an expected version into one `ApplyChangesAsync`.
- `IDataSourceCollection.UpdateOneIfVersionMatchesAsync` / `ReplaceByIdIfVersionMatchesAsync` (`bool`: `true` applied, `false` = the
  document exists with another version; a missing document keeps the backend's not-found error).

Semantics:
- The write applies only if the stored `rtVersion` equals the expected one; the stored version then becomes **expected + 1**
  (update: the usual `$inc`; replace: the engine sets `rtEntity.RtVersion = expected + 1` on the entity that is written - read it back
  from the entity after the call, and keep the version you passed if you retry).
- A mismatch is **`RtVersionConflictException`** (`RtEntityId`, `ExpectedRtVersion`), never a silent skip. Nothing is written for the
  conflicting entity. In a transactional session the exception rolls the transaction back as usual, so an `ApplyChangesAsync` batch is
  all-or-nothing there; without a transaction the entities before the conflicting one stay written.
- The caller's loop: read -> change -> write with the version read -> on `RtVersionConflictException` read again (new session /
  transaction) and repeat. Two writers need at most one retry.
- One mechanism per write: an expected version cannot be combined with `AttributeNewerThanGuard` or atomic RecordArray item
  updates (rule engine throws `RuntimeRepositoryException`), nor with a linked binary in the same update (the conflict would skip the
  entity write after the bytes were stored, same reason as above). A replace checks the stored version once before it touches
  linked binaries, so a replace that is bound to conflict does not delete them; the write itself repeats the check atomically.
- A replace **without** an expected version is unchanged: it writes the entity as given (including its own `RtVersion`) and does not
  increment. A writer that wants to be detected by guarded writers must therefore also use the expected version.
- `LocalDataSourceCollection` (dev-only) checks and applies in-process; the atomic MongoDB statement is in octo-construction-kit-engine-mongodb, "Expected rtVersion".
- Tests: `BulkRtMutationExpectedRtVersionTests` (this repo); the real-MongoDB tests (50 repeated parallel increments) live in
  engine-mongodb.

## Unacknowledged writes: one classification (rule R3)

`UnacknowledgedWriteClassifier.IsOutcomeUnknown(Exception?)` (`Runtime.Contracts`, public, static) answers whether an exception
says that a write may have taken effect without being acknowledged: a MongoDB write-concern error (`MongoWriteConcernException` -
except its subclass `MongoDuplicateKeyException`, a refused write -, `MongoWriteException` / `MongoBulkWriteException` carrying a
`WriteConcernError`, `ClientBulkWriteException` with a non-empty `WriteConcernErrors`) or the error label
`UnknownTransactionCommitResult`, anywhere in the inner-exception chain and in every inner exception of an `AggregateException`
(depth guard 32). A write error without a write-concern error (refused write, e.g. duplicate key) is not unknown. The driver
types are recognised by full name (`MongoDB.Driver*` namespaces) and the public members `WriteConcernError` / `ErrorLabels`, so
`Runtime.Contracts` keeps **no MongoDB reference** (pinned by a test). It is the single implementation for every host:
- `octo-common-services` CK method host: `CkMethodWriteOutcome` delegates to it (`METHOD_OUTCOME_UNKNOWN` + `correlationId`);
- pipeline hosts (mesh adapter `MeshAdapterTriggerContext` / `LeasedPipelineWorkItem`, SDK adapters `AdapterTriggerContext` in
  `octo-communication-sdk`): a failed execution is reported with `PipelineExecutionFailureKind.OutcomeUnknown` (octo-sdk
  `PipelineExecutionEndDto` / `LeaseResultDto`), which the communication controller stores on `PipelineExecution.FailureKind`
  and maps to `METHOD_OUTCOME_UNKNOWN` for pipeline methods.
Tests: `tests/Runtime.Engine.Tests/Persistence/UnacknowledgedWriteClassifierTests` build the real driver exceptions
(`MongoWriteFaults`; `MongoDB.Driver` is a **test-only** package reference of `Runtime.Engine.Tests`), so a rename in a driver
update turns them red.

## `RtPathEvaluator.SetValue`: values, explicit null and missing

`RtPathEvaluator.SetValue` writes a value into an entity or record along an attribute path
(`Designation`, `Amount.Value`, `RecordArrayTests[0].Designation`, `nav->Attr`). This page describes what
a `null` value does.

## Three cases

| Input | Result |
| ----- | ------ |
| a value | the attribute is set; records missing along the path are created |
| `null` | an explicit null, see below |
| nothing (the caller does not call `SetValue`) | the attribute stays missing |

A missing attribute and an attribute that holds `null` are different: an archive write keeps the
stored value of a missing column and clears a column that holds an explicit null.

## Explicit null on an enum attribute

- **Optional enum** (`isOptional: true` on the type or record assignment): `null` sets the attribute to null.
- **Required enum** (`isOptional: false`, the default): `null` is refused with an `InvalidPathException`
  `Path '<path>' cannot be set to null: the enum attribute '<name>' (CkEnumId '<id>') is not optional.`
  It is thrown before anything is changed, also before a missing record would be created.

Earlier every `null` on an enum failed with `Invalid enum value type ... 'null'`.
Other value types are unchanged: a `null` on them is written whatever `isOptional` says; the entity rule
engine checks required attributes when the entity is saved.

## Explicit null on a sub-path of a record that does not exist yet

`Amount.Value = null` while `Amount` is not set depends on the `RtPathNullHandling` argument:

| `RtPathNullHandling` | Result |
| -------------------- | ------ |
| `KeepMissingRecords` (default; the overloads without the argument) | nothing happens: `Amount` stays missing, so the null arrives as *missing* |
| `CreateMissingRecords` | `Amount` is created (as a value would create it) with `Value` explicitly null; its other members stay missing |

Why the default keeps the record missing: query-row creates and updates of the asset repository
(`QueryMapper`) send a cell for every column, and an empty cell must not create an empty record. A
record with only null members would also fail the required-member check of the rule engine on create.

Why `CreateMissingRecords` exists: a pipeline (`CreateUpdateInfo@1`) that sends an explicit null for
`Amount.Value` must reach the archive as null, not as missing. The caller opts in.

Not changed in either mode: a null on an existing record sets the member to null and keeps the other members;
a null whose missing parent is not a record that `SetValue` creates (a navigation without a target, a missing
record array, `RecordArrayTests[0].Member` or `RecordArrayTests.Member`) is dropped, and so is not refused on a
required enum either; a null on the record
attribute itself (`Amount = null`) sets the record to null.

Tests: `tests/Runtime.Engine.Tests/RtPathEvaluatorTests.cs`, region `SetValue explicit null`.
