# Archives and Hidden attributes (AB#6247)

CK v2 attributes with `access: Hidden` are never readable outside a method. Stream-data storage (CrateDB) does not
enforce attribute access, so **no archive column may reach a Hidden attribute**. This page describes what happens when a
CK model change makes a column of an existing archive reach Hidden, and how an operator gets the archive back.
It covers the Phase 4 prerequisite N2/N3 of Epic AB#5584 (review G3 re-review, 2026-10-09).

## The rule

`ArchiveHiddenColumnGuard` (Runtime.Engine) decides whether an ingested column path reaches Hidden: a segment is Hidden,
or the path ends at a record (or record array) whose record contains a Hidden sub-attribute, transitively. Segments match
case-insensitively, `[*]` is ignored, computed columns are skipped. For a rollup the aggregation source paths are checked
against its target type: a rollup of a Hidden attribute stores values derived from it. Source paths that name a column of
a source rollup do not resolve on the type and pass.

| When | What happens |
|---|---|
| Activation, `Enable`, `RetryActivation`, re-provisioning | Refused with `HiddenAttributeInArchiveException`, nothing is provisioned. |
| A CK model import makes a column of an **activated** archive reach Hidden | The access re-validation sets the archive to `Failed` (below). |
| A query names a Hidden column | Refused by the asset repository (`ATTRIBUTE_NOT_QUERYABLE`). |

## Re-validation in every service (N2)

`TenantContext.RevalidateArchiveAccessAsync` (engine-mongodb) re-checks every activated archive of a tenant after a CK model
import, a System import on tenant resolve and a model recovery. It needs the stream-data repository, so it does nothing in a
service without stream data. The services that import System.Identity, System.Communication or System.UI (identity,
communication controller, platform services) have none.

A CK model import therefore also triggers the re-validation in the services that **own** stream data. Every import publishes
`PosUpdateTenant` (scope `Full`). The consumer in octo-common-services (`PosCreatePosUpdateTenantConsumer`) runs
`ITenantContext.RevalidateArchiveAccessAsync` after the tenant setup. In the asset repository this re-checks the archives
against the new model; in a service without stream data it returns at once. A `CacheOnly` update does not re-validate,
because it changes no model.

## A Failed archive (N3)

The re-validation writes, in one store call:

- `Archive.Status = Failed`, so the archive refuses every write (`ArchiveNotActivatedException` on insert). A pipeline that
  writes to it fails, so the problem is visible;
- `Archive.StatusReason`: the columns and the Hidden attributes they reach, for example
  `'Reading' (reaches the Hidden attribute 'Reading.Calibration')`;
- `Archive.HiddenColumns`: the offending column paths;
- an audit event (`IArchiveAuditTrail.RecordTransitionAsync`) with the same reason, and a WARN log.

While `HiddenColumns` is not empty the archive's **history stays queryable without those columns**
(`ArchiveSnapshot.IsFailedOnHiddenColumns`, `ArchiveSnapshot.WithoutHiddenColumns`). The query paths of the CrateDB repository
accept such an archive and resolve its columns from the snapshot without the Hidden ones: they are neither projected by a
query without explicit columns nor accepted by name. A later model change that makes more columns Hidden updates the recorded
list without a new transition.

Every other transition clears `StatusReason` and `HiddenColumns`. A failed table provisioning, a failed blueprint
activation and the reconciliation (table missing) record their own reason with an empty `HiddenColumns`, so such an
archive is not readable. A **rollup** whose aggregation reaches a Hidden attribute also goes `Failed` with its reason, but
records no Hidden columns and stays unreadable: its stored values are derived from the Hidden attribute. Change or delete
the rollup; the redaction refuses rollups. Reasons are cut at 2000 characters.

If a later model change makes the columns visible again, the archive stays `Failed` with its recorded columns (hidden in
the queries) until `RetryActivation` or the redaction resumes it.

Both attributes are runtime state of System.StreamData 1.27.0 and are visible wherever the archive entity is read
(runtime GraphQL query of the `Archive` types; Refinery Studio, AB#9120).

## Resume: `RedactHiddenColumns` (destructive)

`IArchiveLifecycleService.RedactHiddenColumnsAsync`, exposed as the CK method `System.StreamData/Archive.RedactHiddenColumns-1`
(role `StreamDataAdmin`, parameter `confirmDataLoss` must be `true`). It is never called automatically.

1. Determine the columns that reach Hidden under the **current** CK model. A rollup archive is refused, and so is an
   **activated** archive with Hidden columns: ingest and re-provisioning still work from its definition while the columns
   are dropped. The re-validation sets such an archive to `Failed`, or the operator disables it first.
2. Refuse when a computed column reads one of the columns, with its current or its pending formula
   (`IStreamDataRepository.ValidateComputedColumnsAsync` with the remaining columns), and when a `ConflictPrecedence` key
   names one of them (the computed-column validator's exception, or `InvalidArchiveStateTransitionException` for the key);
   remove that computed column or change the precedence first.
3. Refuse with `HiddenColumnRedactionBlockedException` (`STREAMDATA_REDACTION_BLOCKED`) while a rollup archive aggregates one
   of them. Change or delete that rollup first. Nothing is dropped. The check is best-effort: a rollup created between the
   check and the drop is not seen.
4. Record the redaction in the audit trail **before** the drop (a transition event from the current status to the same
   status, reason `RedactHiddenColumns drops the columns …`), so a failure after the drop cannot leave it without an entry.
   Every attempt is recorded; a retry after a failed attempt records a second entry.
5. Drop the CrateDB columns (`IStreamDataRepository.DropIngestedColumnStorageAsync`, `ALTER TABLE … DROP COLUMN`), then remove
   them from the archive definition (`IArchiveRuntimeStore.RemoveIngestedColumnsAsync`). **The values stored in these columns
   are lost**; every other column and row is kept.
6. An archive that was Failed on Hidden columns is activated again, through the same steps as `RetryActivation`. Any other
   archive keeps its status (a Disabled archive with a Hidden column can then be enabled).

Without a Hidden column the call drops nothing; a Failed-on-Hidden archive whose columns are no longer Hidden (the model was
changed back) is still activated again. Alternatives without data loss: change the model back and run `RetryActivation`, or
copy the data elsewhere before the redaction. A retry after a failure between the drop and the definition write converges
(`DROP COLUMN IF EXISTS`). If the resume itself fails (provisioning), the archive is `Failed` with the provisioning reason
and no Hidden columns; `RetryActivation` applies.

## Backups (AB#9141)

A tenant backup leaves out the values of every column known to reach a Hidden attribute, in either format, from the staging
copy and from the final artefact. Known means: found in the archive's CK type, or recorded on the archive. When the type
cannot be read, only the recorded columns are known, so such a backup can hold Hidden values (residual risk, below). The rule
lives in `CrateDbStreamDataRepository` (engine-mongodb); the contracts are unchanged.

What a backup leaves out of an archive:

- every ingested column that reaches a Hidden attribute under the tenant's **current** CK model
  (`ArchiveHiddenColumnGuard.FindHiddenColumns`), whatever the archive's status - also an Activated archive whose
  re-validation has not run yet, and a Disabled archive (the re-validation checks Activated archives only);
- every column the re-validation recorded in `Archive.HiddenColumns`, even when the model was changed back since;
- for a rollup, the aggregate columns of every aggregation whose source reaches a Hidden attribute;
- every computed column whose current or pending formula reads one of these columns, transitively, in every stored
  version (`{base}`, `{base}__v{N}`).

When the archive's CK type cannot be read, only the recorded columns are left out (WARN naming the archive and the reason,
never a value); the dump is not failed, so a tenant with a broken model (ResolveFailed) stays backupable for its recovery.
**Residual risk:** a column that a model made Hidden while that model cannot be read, and that the re-validation has not
recorded yet, can end up in the backup.

| Path | What changes |
|---|---|
| Native copy (format 2) | The plan describes the data table without these columns, so the manifest lists only the columns the dump holds. Each data slice is copied with an explicit column list, `COPY <table> ("a", "b", …) WHERE … TO DIRECTORY … WITH (format = 'json_object', …)`: the staged files written by CrateDB do not contain the left-out values. A generation map holds no attribute values and is copied as before. |
| Row path (format 1) and the archive data export | `ExportRowsAsync` leaves out the same columns (it used to leave out only the recorded ones of an archive Failed on Hidden columns). |

What a restore does with such an archive (octo-bot-services, both formats). Before the archive's table is touched, the
restore runs `IArchiveLifecycleService.RevalidateAccessAsync` against the restored CK model. That call writes: an Activated
archive whose columns reach Hidden goes `Failed` with its reason (and its Hidden columns, for a raw or time-range archive).
Then:

- **A raw or time-range archive that is not Activated** (Failed on Hidden columns, also one that was Activated in the backup
  and fails now, or Disabled): the table is provisioned for the definition without the columns that still reach Hidden, the
  computed columns derived from them and the conflict precedence keys on them, the data is loaded (the backup holds no
  values for them), and the archive **keeps its status** (`Failed` with its reason, or `Disabled`). Its history is
  queryable without them as before; `RedactHiddenColumns` removes the columns from the definition and, for an archive
  Failed on Hidden columns, activates it again (the column drop is `DROP COLUMN IF EXISTS`, so the missing table column is
  no obstacle; a Disabled archive can be enabled after it).
- **A rollup over a Hidden attribute**: skipped with the reason, its table is not touched (the re-validation has set it
  `Failed`). Change the rollup or the model.
- **A rollup whose source was restored this way**: skipped with the reason before its table is touched; it cannot be
  activated while its source is not Activated.
- **A native backup written before AB#9141** that still holds a value column of such an archive: skipped with the reason
  (the column is named) before the table is touched; the load would refuse a column the table does not have. A row-path
  (format 1) backup of that age is restored and the values of these columns are ignored.
- The check itself fails (e.g. the archive's type is unknown): the restore goes on as before.

`CrateDbStreamDataRepository.EnsureArchiveCreatedAsync` and `ImportRowsAsync` leave the columns that still reach Hidden out
for a raw or time-range archive that is not Activated. The lifecycle never provisions such an archive (every activation
refuses a Hidden column first), so only a restore takes this path; a column whose attribute is visible again is provisioned
as before, so `RetryActivation` after a model change back still adds it.

## Contracts for a mixed package set

The new members are default interface members, so implementations built against an older `Runtime.Contracts` keep compiling:

| Member | Default |
|---|---|
| `IArchiveRuntimeStore.SetStatusAsync(rtId, status, reason, hiddenColumns)` | writes the status only |
| `IArchiveRuntimeStore.RemoveIngestedColumnsAsync` | throws `NotSupportedException` |
| `IStreamDataRepository.DropIngestedColumnStorageAsync` | throws `NotSupportedException` |
| `IArchiveLifecycleService.RedactHiddenColumnsAsync` | throws `NotSupportedException` |

`ArchiveSnapshot.StatusReason` and `HiddenColumns` are init-only properties (empty by default). Implementations in the
monorepo: `MongoArchiveRuntimeStore` and `CrateDbStreamDataRepository` (octo-construction-kit-engine-mongodb).

## Not covered

- No compare-and-swap on the status write (review G3 N7): concurrent re-validations in several stream-data replicas can
  write the same `Failed` transition twice (duplicate audit rows), and one that runs during a redaction can set the
  resumed archive to `Failed` again, so the redaction must be repeated.
- A `PosUpdateTenant` that arrives while the same tenant's update is being handled in that service is dropped (existing
  behaviour of the consumer); its re-check runs with the next update or the next import.

Tests: `ArchiveHiddenColumnGuardTests` (Runtime.Engine.Tests), `StreamDataCkLanguage2Tests` (Compiler.Tests);
engine-mongodb `ArchiveHiddenAccessTests`, `ArchiveHiddenColumnBackupIntegrationTests` (real CrateDB: dump, staged files,
manifest, row export, restore, redaction) and `HiddenColumnExclusionTests`; octo-bot-services `RestoreRepositoryNativeTests`
and `RestoreRepositoryArchiveDataTests` (the restore paths).
