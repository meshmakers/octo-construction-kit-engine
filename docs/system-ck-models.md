---
description: The CK models this repository owns (System, System.StreamData, System.Files): ckLanguage 2 rules, late-delivery recompute, hidden archive columns.
applies_to: src/SystemCkModel/**, src/SystemFilesCkModel/**, src/StreamDataCkModel/**, src/Runtime.*/StreamData/**
---

# CK models owned by this repository and System on ckLanguage 2

## CK Models Owned by This Repository

| Model | Path | Package | Depends on | Owner of the source |
| ----- | ---- | ------- | ---------- | ------------------- |
| `System` | `src/SystemCkModel` | `Meshmakers.Octo.ConstructionKit.Models.System` | - | this repository |
| `System.StreamData` | `src/StreamDataCkModel` | `Meshmakers.Octo.ConstructionKit.Models.StreamData` | `System` | this repository |
| `System.Files` | `src/SystemFilesCkModel` | `Meshmakers.Octo.ConstructionKit.Models.System.Files` | `System-[2.5,3.0)` | this repository (was octo-asset-repo-services) |

**System.Files.** The model source moved here from `octo-asset-repo-services` so that every consumer
(asset repository, report services, the platform-services installer) gets it as a package of the libs train.
The content is **identical** to the `System.Files-1.0.0` that asset-repo published (the model is now `System.Files-1.1.0`, see "one-time MINOR bumps" below): same
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

**one-time MINOR bumps after the range-retention flip.** With range retention as the compiler default, the first
compile of `System.Files` (1.0.0 -> **1.1.0**) and `System.StreamData` (1.17.1 -> **1.18.0**) is classified MINOR by SemVer row D6 ("the one-time
re-pin": exact pin -> declared range), so `ValidateVersion` fails with `OCTO-CK100` until the declared version is raised
(`octo-ckc -c ValidateVersion --apply` wrote exactly these two versions; `System` has no dependencies and needs no bump). **Compat impact:** the
compiled yaml of both models now carries `dependencyRanges`, `minEngineVersion: 3.5.1` and `System@2/...` references and is published under
`ck-models/v3/`; engines below 3.5.1 do not see them (message 126 / catalog path), so every service of an environment must run the CK v2 engine
before these versions are imported. Published versions stay immutable. **Why `main` stayed green:** the `Validate CK model versions` step installs
`octo-ckc` of the build's own version line from the private feed (`Derived octo-ckc toolVersion from version line`), i.e. the compiler of the
previous main build; the flip commit itself was validated with the pre-flip compiler (`Range retention: False`, "no bump required"), a later PR
or main build derives the flip compiler and fails.

**release tags needed the flip compiler pinned (pin removed 2026-10-21).** The same derivation broke the first release tag after the
flip: an `r*` tag build takes only STABLE compilers of its own line, i.e. the pre-flip 3.5.21, and the compiler of the release being built is
published only by that very build (r3.5.22, build 54764: `OCTO-CK100`, "rangeRetention true -> false" = MINOR bump for the already published,
range-retaining System.Files-1.1.0 / System.StreamData-1.18.0). The `Validate CK model versions` call therefore passed
`toolVersion: '0.1.2610.14032'` (exact main-line build with the flip). Never bump the models to get past such a gate: the old compiler would
publish them with exact pins, immutable. **The pin is removed**: the flip compiler is on the 3.5 line since r3.5.23/r3.5.27 (stable 3.5.36 is on the feed), and
the exact main-line build had been purged from the private feed, which failed every build with "Version 0.1.2610.14032 ... is not found in NuGet
feeds" (PR 298 builds 58123 and 58142). The template derives the compiler of the build's own line again (main/PR: newest 0.1.*, r* tags: newest
stable 3.5.*). Do not pin an exact main-line build again (the feed purges them); a pin is only for a schema feature no released compiler knows yet, and it
is removed in the same release train.

**Single publisher.** This repository's CI is the only publisher of `System.Files` to the private and
public CK catalogs and the only source of its generated model documentation: the `Validate CK model versions`/
`Publish CK models` step lists `src/SystemFilesCkModel/ConstructionKit` (after `System`), and a dedicated `CopyFiles`
step stages the `.md` files of `SystemFilesCkModel/out` for the documentation site. The yaml is deliberately **not**
copied via `handle-artifacts.constructionKitLibraryPaths`: that puts it into `ck-libraries/`, which
`release-catalogs-train` publishes to the public catalog with `-r`, i.e. a second, forcing publisher built against the
newest System of the commit. The CK gate stays the only publisher. Because 1.0.0 is already published with identical
content, the first publish from here is the idempotent "already exists" skip. The asset-repository CI stops
publishing it together with the removal of its model project. A search of all `azure-pipelines.yml`
files for `SystemFilesCkModel` must therefore find only this repository.

## System on ckLanguage 2

`src/SystemCkModel` is **System-2.9.0**, `ckLanguage: 2` (2.5.0 was ckLanguage 1; 2.6.0 switched the language, 2.7.0 adds the interfaces, see
"System interfaces" below; 2.8.0 adds the `AggregationTypes` value `Integral` = 7; 2.9.0 adds number ranges, the release configuration and `DocumentAuditEntry`, see `concept-number-ranges-and-released-documents.md`; unpublished until the explicit System publish).
`src/SystemCkModel/ConstructionKit/CHANGELOG.md` records each version from 2.8.0 on (`ValidateVersion -cl` plus a Compatibility section). Every release of the line is a **Minor** against its predecessor and against 2.5.0 and must stay one:

- **Every type and record declares `derivable: Any`.** Raising the language flips the default from `Any` to `Model`; without the explicit
  keys every model deriving from `System/Entity` would fail with message 113 and the SemVer verdict would be Major (row T6, `OCTO-CK200`).
  `visibility` (omitted = `Public`) and attribute `access` (omitted = `ReadWrite`) do not flip, nothing else is declared. A **new** System type
  without `derivable` is `Model` like in any v2 model; give it `derivable: Any` when other models should derive from it.
  `Entity` and `Configuration` are stable bases by declaration (public, not final, `derivable: Any`): row B4 (unique index = Major +
  acknowledge) is judged through `CkSemVerClassifier.IsStableBase`. The v1 name branch of `IsStableBase` stays for System versions up to 2.5.x
  (their sources and published files are ckLanguage 1 and are still compared) and goes with the last ckLanguage 1 System.
- **minEngineVersion 3.5.1, `ck-models/v3/` only.** The compiler derives it from `ckLanguage: 2`. An engine below 3.5.1 does not see System 2.6
  (message 126 / catalog path); the embedded-import downgrade guard keeps a newer installed System when an older service starts
  (`EmbeddedCkModelImportGuard.Decide`: embedded `System-2.5.0` against installed `System-2.6.0` = `SkipNewerInstalled`, covered by
  `CkModelImportGuardDecisionTests` in engine-mongodb).
- **Message 109 and `_id`.** The `System/Entity` index `(CkTypeId, _id)` names the database key; rule 109 (unknown index path in a v2 model)
  exempts `_id` next to `Rt*` / `CkTypeId` (`InheritanceResolver.IsSystemIndexPath`, test `RestrictedAttributeRuleTests`).
- **Generated C#.** For a v2 model the Rt class of an `isFinal` type is `sealed`: `RtAutoIncrement`, `RtBlueprintBackup`, `RtBlueprintHistory`,
  `RtBlueprintInstallation`, `RtMigrationHistory`, `RtTenant`, `RtTenantConfiguration`, `RtTenantModeConfiguration` (no subclass or mock of them in the monorepo, checked 2026-10-19).
- **Publishing.** A service that embeds System 2.6 or later brings it to every tenant, and a model that is still exact-pinned to 2.5.0
  would go `ResolveFailed`; publishing was meant to wait for the exact-pin scan (F2.6). **System-2.6.0 was published nevertheless**
  by the engine main CI on 2026-10-23 09:02Z (private catalog `ck-models/v3`, also visible in `construction-kit-libraries`) right after PR #294 merged;
  a published version is immutable, hence the interfaces are **2.7.0**, not an extension of 2.6.0. Never publish a ckLanguage 2 System into a shared
  catalog or environment before every service there runs the CK v2 engine.
- **Proof.** `scripts/prove-system-ckl2.sh --monorepo <root>` (private catalogs, throw-away `HOME`, nothing shared is read or written): compiles
  System 2.5.0 (git ref `origin/main`) and the 21 dependent models (committed HEAD of each repository) into a "before" catalog, runs
  `ValidateVersion` (Minor), `ValidateCascade` (0 Breaks, 0 NeedsRepin), shows the SHA-256 list of System before/after, proves the compiled
  surface identical apart from the explicit modifiers, publishes 2.6.0 into a copy of the catalog (`ck-models/v3` only) and recompiles every dependent
  without editing its `ckModel.yaml` (0 output changes; only the resolved closure line moves, row D8). Tests: `SystemCkLanguage2Tests`
  (Compiler.Tests; includes the negative control that the same switch without the explicit modifiers is `OCTO-CK200`).
  `--additive` accepts a candidate that only adds elements (the interfaces): the surface diff may consist of added lines only.

### System interfaces

System-2.7.0 declares four interfaces in `src/SystemCkModel/ConstructionKit/interfaces/`; a model author implements them (`implements:`) instead of copying the attributes:

| Interface | Members | Notes |
| --- | --- | --- |
| `Named-1` | `Name` (required) | **No index is declared** (decision of 2026-10-19): an implementer declares the index it needs for sorted interface queries; no latency promise without one. `DisplayName` is deliberately not a member (a later `Displayable-1`). |
| `Describable-1` | `Description` (optional member) | The existing `PersistentQuery.Description` is optional, so the member is optional too. |
| `Enableable-1` | `Enabled` (required) | No System type has `Enabled`; nobody in System implements it. |
| `Hierarchical-1` | extends `Named-1`; required association member `System/ParentChild` (outbound `Parent`) to an implementor of `Hierarchical-1` | **No new association role**: it reuses the existing `ParentChild` (outbound `Parent` One, inbound `Children` N). An implementer declares `ParentChild` to a `Hierarchical-1` implementor (itself is fine); a root is a node without a Parent instance. Nobody in System implements it (no System type uses `ParentChild`). |

Per existing System type: only `PersistentQuery` (abstract; every query type inherits it: `SimpleRtQuery`, `AggregationRtQuery`, `GroupingAggregationRtQuery`, `StreamDataQuery` and its `*SdQuery` children) has `Name` and the optional `Description` and
implements `Named-1` and `Describable-1` (SemVer row T2, Minor). No other System type has the attributes `Name`, `Description` or `Enabled` under the System attribute ids (`BlueprintName`, `DatabaseName`, `CkModelName` are other
attributes), none uses `ParentChild`, so there are no further implementers; making a type implement an interface it does not satisfy would need a new required member (Major) and does not happen in 2.x.
Generated C# (Octo.Sdk generator): `IRtNamed`, `IRtDescribable`, `IRtEnableable`, `IRtHierarchical`; `RtPersistentQuery` implements the first two.
Tests: `SystemInterfacesTests` (Compiler.Tests: members, per-type table, no index added, Minor against 2.5.0 and 2.6.0, probe model that implements all four, rule 121 and the missing-`Name` error for `Hierarchical-1`),
`EmbeddedCkModelResourceTests.SystemCkModel_GeneratesTheFourInterfaces_AndTheQueryTypesImplementThem` (Runtime.Engine.Tests).

## System.StreamData on ckLanguage 2

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

### Late-delivery recompute (System.StreamData 1.26.0)

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

### Hidden columns of an archive (System.StreamData 1.27.0)

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

### Archive lifecycle, rollup and column methods (System.StreamData 1.24.0, 1.25.0, 1.28.0, 1.30.0)

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
  EnergyCommunity.Base, a customer app), which the flag would refuse (message 136).
- **The create-method handlers bind the generated `{Type}{Method}Parameters`**: the method JSON of octo-common-services
  (`CkMethodJson`) binds the record arrays (`IAttributeValueList<T>`) of the generated `Rt{Record}Record`; the plain look-alike
  classes are gone.
- The column methods (1.28.0) complete the set: every `streamData` mutation of the asset repository now has a method. They
  sit on `Archive` like the lifecycle methods; a rollup refuses them in the engine as the mutations do. The backfills and repairs
  stay background jobs, the methods answer once the job is queued. `RecomputeJobInfo` gained the optional member
  `ComputedColumnName` (row R1, Minor). A path that reaches a Hidden attribute is refused by the engine, so `AddColumn-1`
  answers `STREAMDATA_ERROR` like the mutation.
- `Recompute-1` and `BackfillFromSource-1` (1.25.0) are synchronous like the mutations: they answer the job
  (`RecomputeJobInfo`, state Pending/Running/... or Failed for a refused precondition). Deprecated since 1.30.0 (description only).
- **1.30.0: `RollupArchive.Recompute-2` and `BackfillFromSource-2`** with `execution.mode: async` (the asynchronous
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
