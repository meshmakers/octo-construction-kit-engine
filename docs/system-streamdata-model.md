---
description: System.StreamData model rules: late-delivery recompute and hidden columns of an archive.
applies_to: src/StreamDataCkModel/**, src/Runtime.Contracts/StreamData/**
---

# System.StreamData on ckLanguage 2

## System.StreamData on ckLanguage 2 (AB#8597, Epic AB#5584 F4.3)

`src/StreamDataCkModel` is **System.StreamData-1.30.0** (archive lifecycle methods 1.24.0, rollup control 1.25.0, late-delivery sets 1.26.0, Hidden-column failures 1.27.0, column methods 1.28.0, confirmed redaction 1.29.0, asynchronous recompute and backfill 1.30.0, below); 1.23.0 was the first ckLanguage 2 release of the model and a **Minor** against
the ckLanguage 1 release 1.22.0 (`ValidateVersion`: only "ckLanguage changed '1' → '2'"). The rules of the System migration above apply:

- **Every type and record declares `derivable: Any`** (`Archive`, `RawArchive`, `RollupArchive`, `TimeRangeArchive`, `RecomputeJob` and the six
  `Ck*` records). Without it the ckLanguage 2 default `Model` is a Major (row T6) and a model deriving from `Archive` fails with message 113.
  A **new** type or record defaults to `Model`; give it `derivable: Any` when other models should derive from it.
- **minEngineVersion 3.5.1, `ck-models/v3/` only** — unchanged against 1.18.0–1.22.0, which were already range-retaining. **Since 1.30.0
  the minEngineVersion is 3.5.67** (`execution.mode` of `Recompute-2` / `BackfillFromSource-2`, `CkEngineVersion.ExecutionModeMinEngineVersion`):
  services on an older engine cannot load 1.30.0.
- **No interface.** No type carries `System/Name`, `System/Description`, `System/Enabled` or a `System/ParentChild` role, so none implements
  `Named-1`, `Describable-1`, `Enableable-1` or `Hierarchical-1`. (Raising the System floor to 2.7 would be allowed as a Minor, row D1:
  1.22.0 resolved System 2.8.0.)
- **No access modifier.** The archive state (`Archive.Status`, `FirstActivatedAt`, the recompute fields, `RollupArchive.LastAggregatedBucketEnd`,
  `FrozenUntil`, `LastRunError*`, `RecomputeJob.*`) is written by the services, but none of it is `securitySensitive`, so `ReadOnly` would be a
  **Major** (row T7; the first-release exception T7a needs the marker in the candidate, and marking state "sensitive" to pass the gate would
  misuse it). Blueprint seeds also write `Archive.Status` through `ImportRt` (e.g. `Samples.Photovoltaics`). Making the state read-only for
  clients is therefore a 2.0 decision or goes through CK methods (the StreamData method stories of F4.3). 1.23.0 used up the T7a exception
  of the model: from 1.24.0 on the baseline is ckLanguage 2 and row T7 applies unchanged.
- **Dependents.** No CK model depends on System.StreamData (`ValidateCascade`: 0 dependents); blueprints and seeds pin `System.StreamData-[1.x,2.0)`.
- **Proof.** The compiled 1.23.0 equals the published 1.22.0 after removing the version, `ckLanguage`, `minEngineVersion`, `derivable` and the
  model description. Tests: `StreamDataCkLanguage2Tests` (Compiler.Tests: version, language, minEngineVersion pinned - 3.5.1, since 1.30.0 3.5.67 -, v3 root, `derivable:
  Any` on every element, nothing tightened or implemented, Minor against a ckLanguage 1 copy of the sources, negative control without
  `derivable: Any` = `OCTO-CK200`, a dependent model derives from `Archive` and `CkArchiveColumn` without message 113). The Mongo round-trip gate
  (`CkMetaModelReflectionGateTests` in engine-mongodb) covers the model through the package.

### Late-delivery recompute (System.StreamData 1.26.0, AB#9016)

1.26.0 adds the optional `StringArray` attributes `CkArchiveDirtyWindow.RtIds`, `CkArchiveRecomputeRange.RtIdSet` and
`RecomputeJob.RtIdSet`. This is a Minor against 1.25.0 with no acknowledgement. They carry the series of a late delivery
(`RtIdSet` value type in `Runtime.Contracts/StreamData`). `RecomputeOrchestrator` uses them as follows:

- it debounces late-delivery obligations (`NextAttemptAt` at attempt zero, `RecomputeLateDataOptions.LateDataDebounce`);
- it merges them into one set-scoped job per merged range and interval, capped by `MaxScopedRtIds`;
- it runs that job through `IArchiveRecomputeExecutor.ExecuteScopedRefreshAsync`, an in-place refresh at the active
  generation in engine-mongodb;
- the chained rollups inherit the set.

Contract rules for a mixed package set:

- the record properties are init-only, so the positional constructors stay;
- the executor member has a default implementation;
- the previous orchestrator constructor stays and means "no debounce".

Pinned by `RtIdSetAndContractCompatibilityTests`; behaviour by `RecomputeLateDeliveryTests`. Concept:
`docs/concept-rollup-recompute.md` §4a.

### Hidden columns of an archive (System.StreamData 1.27.0, AB#6247)

1.27.0 adds `Archive.StatusReason` and `Archive.HiddenColumns` (runtime state) and the destructive method
`Archive.RedactHiddenColumns-1` (parameter `confirmDataLoss`, role `StreamDataAdmin`, result `ArchiveRedactionResult`). A Minor
against 1.26.0 without acknowledgement. Behaviour:

- `ArchiveLifecycleService.RevalidateAccessAsync` sets an activated archive whose columns now reach Hidden to `Failed` and records
  the reason and **every** offending column (`ArchiveHiddenColumnGuard.FindHiddenColumns`) with one store call
  (`IArchiveRuntimeStore.SetStatusAsync(rtId, status, reason, hiddenColumns)`); every other transition uses the plain write, which
  clears both. A failed table provisioning records its reason too.
- `ArchiveSnapshot.IsFailedOnHiddenColumns` / `WithoutHiddenColumns()`: such an archive refuses writes, its history stays queryable
  without the Hidden columns (engine-mongodb query paths).
- `RedactHiddenColumnsAsync` drops the Hidden columns (CrateDB first, then the definition), audits it and resumes a Failed-on-Hidden
  archive; refused for rollups, for an activated archive with Hidden columns, while a computed column (current or pending formula)
  reads one of them (the validator's exception), while a `ConflictPrecedence` key names one (`InvalidArchiveStateTransitionException`,
  like the rollup and activated cases), and while a rollup aggregates one of them (`HiddenColumnRedactionBlockedException`). The audit
  entry is written before the drop, once per attempt.
- A rollup whose aggregation source path reaches Hidden goes `Failed` with its reason but no Hidden columns (unreadable).
- Every new interface member has a default implementation (mixed package sets keep compiling); the implementations live in
  engine-mongodb.

Full description, including the re-validation trigger in services without stream data (octo-common-services): `docs/archive-hidden-columns.md`.
Tests: `ArchiveHiddenColumnGuardTests`, `StreamDataCkLanguage2Tests` (`StreamData_1_27_0_*`, `RedactHiddenColumns_*`).

**Confirmed redaction (1.29.0).** `Archive.RedactHiddenColumns-2` (parameters `confirmDataLoss`, `expectedColumns` StringArray,
required; error `STREAMDATA_REDACTION_COLUMNS_CHANGED`; not idempotent) maps to
`IArchiveLifecycleService.RedactConfirmedHiddenColumnsAsync`: it drops only when the Hidden set at execution time equals
`expectedColumns` (ignoring case, order, duplicates) and otherwise throws `HiddenColumnRedactionColumnsChangedException` before the
state, conflict-key, computed-column and rollup refusals, the audit and the drop. `-1` is deprecated (description only), removed at the next major. A separate engine method
name, not an overload, so the `cref`s to `RedactHiddenColumnsAsync` in consumers stay unambiguous. A new method version, not an
optional parameter on `-1`: an added error code is a Major (row M8). Tests: `ArchiveHiddenColumnGuardTests` (`RedactConfirmed_*`),
`StreamDataCkLanguage2Tests` (`RedactHiddenColumns2_*`, `StreamData_1_29_0_*`).

### Archive lifecycle, rollup and column methods (System.StreamData 1.24.0, AB#8598; 1.25.0, AB#8599; 1.28.0, AB#8600; 1.30.0, AB#9056)

1.24.0 adds the archive lifecycle as CK methods (Minor against 1.23.0, no acknowledgement, `minEngineVersion` unchanged 3.5.1). They
replace the hand-written `streamData` GraphQL mutations of octo-asset-repo-services, which stay and are deprecated there:

| Method | Kind | Result | Mutation it replaces |
|---|---|---|---|
| `Archive.Activate-1` | instance | `ArchiveTransitionResult` | `activateArchive` |
| `Archive.Disable-1` | instance | `ArchiveTransitionResult` | `disableArchive` |
| `Archive.Enable-1` | instance | `ArchiveTransitionResult` | `enableArchive` |
| `Archive.RetryActivation-1` | instance | `ArchiveTransitionResult` | `retryArchiveActivation` |
| `Archive.DeleteArchive-1` | instance | Boolean | `deleteArchive` (`Delete` is a reserved method name, message 102) |
| `TimeRangeArchive.CreateArchive-1` | static, parameter `archive: TimeRangeArchiveCreateInput` | String (rtId) | `createTimeRangeArchive` |
| `RollupArchive.CreateArchive-1` | static, parameter `archive: RollupArchiveCreateInput` | String (rtId) | `createRollupArchive` |
| `RollupArchive.Freeze-1` (1.25.0) | instance, parameter `until: DateTime` | `ArchiveTransitionResult` | `freezeRollupArchive` |
| `RollupArchive.Unfreeze-1` (1.25.0) | instance, optional `acceptGaps: Boolean` | `ArchiveTransitionResult` | `unfreezeRollupArchive` |
| `RollupArchive.RewindWatermark-1` (1.25.0) | instance, parameter `toBucketEnd: DateTime` | `ArchiveTransitionResult` | `rewindRollupWatermark` |
| `RollupArchive.Recompute-1` (1.25.0) | instance, `from`, `to: DateTime`, optional `rtIdScope` | `RecomputeJobInfo` | `recomputeArchive` |
| `RollupArchive.BackfillFromSource-1` (1.25.0) | instance | `RecomputeJobInfo` (none for an empty source) | `backfillRollupFromSource` |
| `RollupArchive.Recompute-2` (1.30.0, async) | instance, as `Recompute-1` | operation handle; the operation ends with `RecomputeJobInfo` | `recomputeArchive` (deprecation names `-2`) |
| `RollupArchive.BackfillFromSource-2` (1.30.0, async) | instance | operation handle; the operation ends with `RecomputeJobInfo` (none for an empty source) | `backfillRollupFromSource` (deprecation names `-2`) |
| `Archive.AddComputedColumn-1` (1.28.0) | instance, `name`, `formula`, `resultType: CkComputedColumnResultType`, optional `indexed` (default true) | `ArchiveTransitionResult` | `addComputedColumn` |
| `Archive.AddColumn-1` (1.28.0) | instance, `path`, optional `indexed` (default false); idempotent | `ArchiveTransitionResult` (transition `AddArchiveColumn`) | `addArchiveColumn` |
| `Archive.RemoveComputedColumn-1` (1.28.0) | instance, `name` | `ArchiveTransitionResult` | `removeComputedColumn` |
| `Archive.UpdateComputedColumnFormula-1` (1.28.0) | instance, `name`, `formula` | `ArchiveTransitionResult` | `updateComputedColumnFormula` |
| `Archive.RetryComputedColumnBackfill-1` (1.28.0) | instance, `name`; idempotent | `RecomputeJobInfo` (with `ComputedColumnName`) | `retryComputedColumnBackfill` |
| `Archive.RepairComputedColumn-1` (1.28.0) | instance, `name`, optional `from`, `to`; idempotent | `RecomputeJobInfo` (with `ComputedColumnName`) | `repairComputedColumn` |

- The instance methods sit on the abstract `Archive`; the concrete archive types inherit them (`CkTypeGraph.AllMethods`).
- Every method: role `StreamDataAdmin`, explicit timeout, the STREAMDATA_* error codes of the mutations plus `STREAMDATA_NOT_ENABLED`.
- The parameter and result records (`records/archiveMethodRecords.yaml`) and their attributes (`attributes/archiveMethodAttributes.yaml`,
  `isRuntimeState: true`) take the ckLanguage 2 default `derivable: Model`. Their shapes equal the mutation input/result types.
- No `replacesGenericCreate`: blueprints seed `TimeRangeArchive` / `RollupArchive` through `ImportRt` (SmartMeterInsights.Base,
  EnergyCommunity.Base, voest-app), which the flag would refuse (message 136).
- **The create-method handlers bind the generated `{Type}{Method}Parameters`** (AB#8707): the method JSON of octo-common-services
  (`CkMethodJson`) binds the record arrays (`IAttributeValueList<T>`) of the generated `Rt{Record}Record`; the plain look-alike
  classes of AB#8598 are gone.
- The column methods (1.28.0, AB#8600) complete the set: every `streamData` mutation of the asset repository now has a method. They
  sit on `Archive` like the lifecycle methods; a rollup refuses them in the engine as the mutations do. The backfills and repairs
  stay background jobs (AB#8002), the methods answer once the job is queued. `RecomputeJobInfo` gained the optional member
  `ComputedColumnName` (row R1, Minor). A path that reaches a Hidden attribute is refused by the engine (AB#6247), so `AddColumn-1`
  answers `STREAMDATA_ERROR` like the mutation.
- `Recompute-1` and `BackfillFromSource-1` (1.25.0, AB#8599) are synchronous like the mutations: they answer the job
  (`RecomputeJobInfo`, state Pending/Running/... or Failed for a refused precondition). Deprecated since 1.30.0 (description only).
- **1.30.0 (AB#9056): `RollupArchive.Recompute-2` and `BackfillFromSource-2`** with `execution.mode: async` (the asynchronous
  versions; a mode change is a Major of the method version, row M19, so they are new versions). Same parameters, role, data access
  and result as `-1`, `timeoutSeconds: 30` (the accept step), plus the error `STREAMDATA_RECOMPUTE_FAILED` (`jobRtId`, `reason`)
  for a job that ended Failed. The mutations `recomputeArchive` / `backfillRollupFromSource` of the asset repository are deprecated
  to the `-2` ids. The mode key raises the compiled `minEngineVersion` of System.StreamData from 3.5.1 to 3.5.67
  (`CkEngineVersion.ExecutionModeMinEngineVersion`): services on an older engine cannot load 1.30.0 (rollout note in the model's
  CHANGELOG). Tests: `RecomputeAndBackfill2_AreAsync_AndDeprecateTheSyncVersions`,
  `StreamData_1_30_0_AgainstThe_1_29_0_Release_IsAMinor_WithoutAcknowledgement`, the pinned `minEngineVersion` in
  `StreamData_Is_1_30_0_OnCkLanguage2_WithMinEngineVersion3567_AndPublishedToTheV3RootOnly`.
- Tests: `StreamDataCkLanguage2Tests` (`ArchiveLifecycleMethods_CarryTheContractOfTheMutations`, `RollupControlMethods_CarryTheContractOfTheMutations`, `ColumnMethods_CarryTheContractOfTheMutations`, `StreamData_1_28_0_AgainstThe_1_27_0_Release_IsAMinor_WithoutAcknowledgement`, `StreamData_1_24_0_AgainstTheMethodlessRelease_1_23_0_IsAMinor_WithoutAcknowledgement`, `StreamData_1_25_0_AgainstThe_1_24_0_Release_IsAMinor_WithoutAcknowledgement`;
  the ckLanguage 1 baseline copy strips the `methods:` blocks and the method-only files).
