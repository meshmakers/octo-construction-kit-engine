# Changelog

## 2.10.0 - 2026-11-25

_Required bump level: minor._

### Added

- Record `System/SyncOrigin` with `SourceTenantId` (String), `SourceRtId` (String, the source entity's
  OctoObjectId) and `SyncedAt` (DateTime, UTC) _(minor — new record)_ (AB#9770, Feature AB#8673)
- Optional attribute `RtSyncOrigin` (Record `System/SyncOrigin`) on `System/Entity` _(minor — optional attribute
  added to a public type, row T1)_

### Compatibility

- Additive, no data migration: no attribute is required, no existing element changes, and existing entities carry no
  `RtSyncOrigin` (null). Dependent models need no re-pin (`System-[2.x,3.0)` ranges resolve 2.10.0).
- Meaning: an entity that a cross-tenant master-data sync wrote carries `RtSyncOrigin`; source tenant and source RtId
  are the idempotency key of the sync, and an entity whose `RtSyncOrigin` names another tenant is owned by that tenant.
  The sync never touches entities without it. The write guard and the sync itself are not part of this model.
- The `Rt` prefix follows the other system-managed Entity attributes (`RtBlueprintSource`, ...); a type of a dependent
  model that already declares an attribute named `RtSyncOrigin` would collide (rows H6 / N2 of the SemVer rules).
- Roll out like every System minor (release train, dependency check, `FixAll`/`ClearCache` per tenant).

## 2.9.0 - 2026-11-24

_Required bump level: minor._

### Added

- `System/AutoIncrement` gains six optional, `TenantOwned` attributes for number ranges (AB#8675) and the
  release configuration of released-document locks (AB#8676): `StartValue` (Int), `PeriodKind`
  (Enum `AutoIncrementPeriodKinds`: `None`, `FiscalYear`), `FiscalYearStartMonth` (Int, 1-12),
  `ReferenceTimeZone` (String, IANA), `Periods` (RecordArray of `AutoIncrementPeriod`) and `Release`
  (Record `NumberRangeRelease`) _(minor - optional attributes on an existing type)_
- Record `AutoIncrementPeriod` (`FiscalYear`, `CurrentValue`): the counter of one fiscal year _(minor - new record)_
- Record `NumberRangeRelease` (`StateAttribute`, `ReleasedValues`, optional `ChangeableAttributes`,
  optional `StornoCkTypeId`) _(minor - new record)_
- Enums `AutoIncrementPeriodKinds` and `DocumentAuditActions` (`Release`, `Storno`, `Denied`) _(minor - new enums)_
- Type `DocumentAuditEntry` (final, derived from `Entity`): append-only audit entry of a released document,
  indexed by `(DocumentRtId, Timestamp)` _(minor - new type)_
- Association role `Cancels` (outbound `Cancels`, One; inbound `CancelledBy`, N): a storno references the
  original it cancels _(minor - new association role)_

### Compatibility

- Additive, no data migration and no CK migration script: every new `AutoIncrement` attribute is optional, and
  a counter without them (`End`, `Format`, `CurrentValue` only - the Basic `BillingDocumentDocumentNumber`
  counter, the EnergyCommunity `CustomerNumber` counter, the energy-community seed counters) numbers exactly as
  before. Dependent models need no re-pin (`System-[2.x,3.0)` ranges resolve 2.9.0).
- System 2.9.0 only declares the schema; the behaviour (fiscal-year ranges, start value, numbering on release,
  lock, storno, audit, gap report) comes with the engine and engine-mongodb stories of AB#8675 and AB#8676.
  Until a service runs that code, a range configured with `PeriodKind`, `StartValue` or `Release` numbers like
  a legacy counter: configure them only after the implementation is rolled out.
- `ReferenceTimeZone` is optional in the schema but required by the engine for a range with
  `PeriodKind: FiscalYear`: such a range without a valid IANA zone is refused when it is configured, there is no
  silent UTC default. Ranges without a period reset may leave it empty.
- `Cancels` is only a role: a document model declares it on its storno type (for example on Basic `Document`)
  in its own minor version.
- Design note with the mapping of every acceptance criterion to these elements:
  `docs/concept-number-ranges-and-released-documents.md` of this repository.

## 2.8.0 - 2026-10-30

_Required bump level: minor._

### Added

- Enum value 'AggregationTypes-1/Integral' added _(minor — purely additive enum value)_

### Compatibility

- Additive, no data migration: existing entities (e.g. persisted `System/AggregationSdQuery` with
  aggregation types 0-6) are unchanged; dependent models need no re-pin (`System-[2.x,3.0)` ranges
  resolve 2.8.0).
- A service built before 2.8.0 cannot map the stored value 7, and System 2.8.0 alone does not execute
  `Integral`: do not create a query entity with `Integral` before every service of the installation runs
  a build that contains System 2.8.0 **and** the Integral implementation (engine-mongodb query path,
  asset-repository GraphQL mapping and SDK `AggregationTypesDto.Integral`, AB#7918/AB#7919). Roll out like
  every System minor (release train, dependency check, `FixAll`/`ClearCache` per tenant).
- `Integral` (AB#7918): step integral over the query window in value unit x hours, LOCF, rtIds summed,
  raw event archives; rollup support follows in AB#7920.
