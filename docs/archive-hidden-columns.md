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

- The native staging copy of a tenant backup (`CopyToStagingAsync`) copies the table as it is, Hidden columns included. The
  row export (`ExportRowsAsync`) leaves them out for an archive Failed on Hidden columns (engine-mongodb).
- No compare-and-swap on the status write (review G3 N7): concurrent re-validations in several stream-data replicas can
  write the same `Failed` transition twice (duplicate audit rows), and one that runs during a redaction can set the
  resumed archive to `Failed` again, so the redaction must be repeated.
- A `PosUpdateTenant` that arrives while the same tenant's update is being handled in that service is dropped (existing
  behaviour of the consumer); its re-check runs with the next update or the next import.

Tests: `ArchiveHiddenColumnGuardTests` (Runtime.Engine.Tests), `StreamDataCkLanguage2Tests` (Compiler.Tests);
engine-mongodb `ArchiveHiddenAccessTests`.
