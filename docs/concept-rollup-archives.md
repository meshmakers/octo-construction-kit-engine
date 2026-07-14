# Concept: Rollup Archives (Folgearchive)

## §1 Overview

Rollup archives are derived `CkArchive` instances that store time-bucketed aggregations of another archive's raw data. They allow a tenant to keep long-term summaries (per minute, hour, day, …) cheaply while dropping or cold-storing high-resolution raw data after a configurable retention window.

A rollup archive is a first-class archive: same lifecycle states, same query surface, same per-archive CrateDB table. The only differences are how its rows get produced (system-orchestrated aggregation from a source archive instead of direct `InsertAsync`) and a few additional configuration attributes.

### Goals

- Persist aggregated time-series data (`AVG`, `MIN`, `MAX`, `SUM`, `COUNT`) per CK entity (`rtId`) over fixed time buckets.
- Drop raw data after a configurable retention window without losing analytical history.
- Allow several rollup levels per source archive (e.g. raw → 1 min → 1 h → 1 d).
- Keep rollups queryable via the existing `streamData` GraphQL surface; clients pick the resolution by selecting the right archive.
- Stay DB-neutral at the contract level; CrateDB-specific DDL/DML lives in `Runtime.Engine.MongoDb.StreamData`.

### Non-Goals (MVP)

- Aggregating from multiple source archives into one rollup (N:M).
- Cold-storage / S3 export. Designed for later; not implemented.
- User-defined aggregation functions beyond the canonical five.
- Automatic schema propagation from source to rollup on source change.
- Sub-bucket arithmetic on query (e.g. "give me 15 min from a 1 min rollup"). Re-aggregation across buckets is allowed but is a query-time concern, not part of this concept.

## §2 Relation to the Existing Archive Concept

This concept extends `streamdata-archive-concept`. All sections of that concept apply unchanged to rollup archives unless explicitly overridden here.

| Aspect | Source archive (`CkArchive`) | Rollup archive (`CkRollupArchive`) |
|---|---|---|
| CrateDB table | One per archive | One per archive |
| Lifecycle states | `Created → Activated ↔ Disabled` / `Failed` | Same |
| Data plane writes | External callers via `InsertAsync` | Background orchestrator via `InsertAsync` |
| Columns | User-picked attribute paths on a CK type | Generated from `Aggregations[]` |
| Schema frozen after `Activated` | Yes | Yes |
| Queries | Typed / transient / persisted | Same |
| Per-entity grouping | One row per `(timestamp, rtId)` already | One row per `(bucketEnd, rtId)` |

The lifecycle service treats rollups identically to source archives for state transitions; the additional rules (source must be `Activated`, etc.) are layered on top.

## §3 Model Extension

### `CkRollupArchive` (new type)

Subtype of `CkArchive`. `CkArchive` must lose `isFinal: true` to allow this.

```yaml
- typeId: CkRollupArchive
  derivedFromCkTypeId: ${this}/CkArchive
  description: |
    Derived archive that stores time-bucketed aggregations of another CkArchive.
    Columns are generated from Aggregations[] at activation time and cannot be
    altered afterwards. Inserts happen exclusively through the rollup
    orchestrator; direct InsertAsync calls are rejected.
  isFinal: true
  attributes:
    - ${this}/CkRollupArchive.SourceArchiveRtId
    - ${this}/CkRollupArchive.BucketSize
    - ${this}/CkRollupArchive.WatermarkLag
    - ${this}/CkRollupArchive.LastAggregatedBucketEnd
    - ${this}/CkRollupArchive.Aggregations
    - ${this}/CkRollupArchive.FrozenUntil
```

### Attributes

| Attribute | Type | Notes |
|---|---|---|
| `SourceArchiveRtId` | `OctoObjectId` | Reference to the parent `CkArchive` (raw or itself a rollup). Immutable after `Activated`. |
| `BucketSize` | `Duration` | Bucket width, e.g. `PT1M`, `PT1H`, `P1D`. Must be a positive interval supported by CrateDB's `date_trunc` or `date_bin`. Immutable after `Activated`. |
| `WatermarkLag` | `Duration` | How far behind real-time the orchestrator stays to absorb late inserts. Default `PT5M`. Mutable. |
| `LastAggregatedBucketEnd` | `DateTime?` | The end timestamp (exclusive) of the most recently committed bucket. `null` before the first run. Maintained by the orchestrator. |
| `Aggregations` | `RecordArray<CkRollupAggregation>` | Defines target columns. At least one entry required. Immutable after `Activated`. |
| `FrozenUntil` | `DateTime?` | When set, the rollup is read-only and no new buckets are produced past this point. See §6. |

`CkArchive.Columns` and `CkArchive.TargetCkTypeId` are inherited but populated automatically at activation (see §4). Direct user editing of `Columns` on a `CkRollupArchive` is rejected.

### `CkRollupAggregation` (new record)

```yaml
- recordId: CkRollupAggregation
  description: |
    One aggregation definition that becomes one or more columns in the rollup
    table. AVG is split into stored SUM + COUNT and is recomputed on read.
  attributes:
    - ${this}/CkRollupAggregation.SourcePath
    - ${this}/CkRollupAggregation.Function
    - ${this}/CkRollupAggregation.TargetColumnName
```

| Attribute | Type | Notes |
|---|---|---|
| `SourcePath` | `String` | An attribute path that exists as a column on the source archive. Validated at activation. |
| `Function` | `Enum<CkRollupFunction>` | `AVG` / `MIN` / `MAX` / `SUM` / `COUNT`. |
| `TargetColumnName` | `String?` | Optional explicit column name. Defaults to `"{sourcePath}_{function}"` lower-cased; for `AVG`, two columns are generated (`_sum` and `_count`). |

### `CkRollupFunction` (new enum)

```yaml
- enumId: CkRollupFunction
  values: [Avg, Min, Max, Sum, Count, TimeWeightedAvg, StateDuration, First, Last]
```

`TimeWeightedAvg` / `StateDuration` are the AB#4336 event-based additions (see
`concept-time-weighted-aggregation.md`). `First` / `Last` (AB#4188) store the value at the
earliest / latest observation in the bucket — an `arg_min` / `arg_max` over time. CrateDB has no
native `arg_min` / `arg_max`, so the orchestrator ranks the source rows with `ROW_NUMBER()` in a
wrapping sub-select and the outer aggregate picks the value of the row ranked 1 (numeric source
columns, the same envelope as `Min`/`Max`/`Sum`). The ranking key is the
raw event `timestamp` for a raw source and the child `window_end` for a windowed / cascade source,
so a rollup-of-rollup re-picks the earliest / latest child bucket. Each materialises a single
`DOUBLE PRECISION` column and chains via itself (`First`→`First`, `Last`→`Last`). Exposing `First` /
`Last` as an *ad-hoc query-time* aggregation (`StreamDataAggregationQuery`) is out of scope; the
stored columns are readable as their own series like any other rollup column.

### Extension on `CkArchive`

Optional, for retention:

| Attribute | Type | Notes |
|---|---|---|
| `RawRetention` | `Duration?` | When set, partitions older than `now - RawRetention` may be dropped from the source. Only effective once at least one rollup has aggregated past that point. `null` = keep forever. Mutable. |

## §4 Activation Semantics

When a `CkRollupArchive` is activated:

1. **Pre-validation** (concept §10):
   - Source archive exists, is not soft-deleted, and is itself a `CkArchive` (any subtype).
   - Source archive is in `Activated` state. Activation while source is `Created`/`Disabled`/`Failed` is rejected with `RollupSourceNotActivatedException`.
   - All `Aggregations[].SourcePath` entries resolve against the source archive's `Columns` list (not the CK type — the path must already be captured by the source).
   - `MIN`, `MAX`, `SUM`, `AVG` require a numeric source column; `COUNT` accepts any non-null column. Validated via the resolved `ArchiveColumnSpec.PrimitiveType`.
   - `BucketSize` is a positive interval.
2. **Column generation** (`RollupColumnGenerator.Generate` in `Runtime.Contracts.StreamData`):
   - For each `Aggregations[]` entry, derive target columns:
     - `MIN`/`MAX`/`SUM`/`COUNT`/`First`/`Last` → one column.
     - `AVG` → two columns (`{name}_sum`, `{name}_count`). The `AVG` is computed on read as `sum / NULLIF(count, 0)`.
     - `First`/`Last` → one column holding the value at the earliest/latest observation
       (AB#4188); the ordering key (raw `timestamp` or child `window_end`) is not stored.
   - The derived columns are written to the inherited `CkArchive.Columns` slot at **create** time (by `RollupArchiveLifecycleService.CreateAsync`), so the generic CkEntity mandatory-attribute validation passes and the read-side mapping (`MongoCkArchiveRuntimeStore.MapToSnapshot`) re-derives them from `Aggregations[]` on every load.
   - `TargetCkTypeId` is **inherited from the source archive** — a rollup of an archive about `Industry.Energy/Meter` rows is itself about `Industry.Energy/Meter` rows (just grouped by bucket). This lets the existing `RtCkTypeId`-keyed query / DDL plumbing work transparently. The rollup rows are still identifiable as rollups via the `ckTypeId` SQL column (= the rollup's own `RtCkRollupArchive` CK type), written by the orchestrator's INSERT.
3. **DDL** (concept §11):
   - Crate first, Mongo last. `CREATE TABLE IF NOT EXISTS` with the generated columns plus the standard `(timestamp, rtId, ckTypeId, rtWellKnownName)` columns.
   - For rollup snapshots (`CkArchiveSnapshot.RollupAggregations is not null`), `EnsureArchiveCreatedAsync` routes through `RollupColumnTypeResolver` instead of `ArchivePathTypeResolver`: the derived storage names are not paths into the CK type, so the column SQL type is determined by the aggregation function (COUNT and AVG's count half → `BIGINT`; SUM/MIN/MAX/First/Last and AVG's sum half → `DOUBLE PRECISION`).
   - `timestamp` on a rollup row is the bucket **end** (exclusive), giving `[bucketEnd - BucketSize, bucketEnd)` semantics. This matches how `ExecuteDownsamplingQueryAsync` already emits its `date_trunc` boundary and keeps "give me the last hour" filters intuitive.
   - The orchestrator's batched upserts use the natural key `(timestamp, rtId, ckTypeId)`.
4. **Initial watermark** (`ArchiveLifecycleService.EnsureRollupWatermarkInitialisedAsync`):
   - MVP policy: `LastAggregatedBucketEnd = truncate(now - BucketSize)` down to the bucket boundary. Historical source rows that predate activation are deliberately not back-filled — use `rewindRollupWatermark` to opt in.
   - Re-activation after Disabled/Failed preserves the existing watermark; it is only seeded when null.
   - The "scan the source for the smallest timestamp" variant remains documented as a future option but requires a dedicated repository method that doesn't exist yet.

Deactivation (`Activated → Disabled`) and reactivation behave identically to `CkArchive` for the table and status; the orchestrator skips disabled rollups. Reactivation re-validates source paths but does not re-emit DDL.

## §5 Aggregation Orchestration

### Trigger

The orchestrator is a background worker registered per tenant, similar to the reconciliation job (concept §11, T23). For each `Activated`, non-`Frozen` rollup it runs on a tick (configurable, default 30 s).

DataFlow-based aggregation is **not** part of MVP. The orchestrator is system-internal; users only configure the rollup definition. A `LoadAggregatedFromArchive@1` / `SaveStreamDataInArchive@1` DataFlow pair remains available for ad-hoc cases but is not how rollups are populated.

### Bucket Loop

```
while watermark + BucketSize ≤ now - WatermarkLag:
    bucketStart = watermark
    bucketEnd   = watermark + BucketSize
    rows = aggregateSource(source, bucketStart, bucketEnd)  # GROUP BY rtId
    if rows is empty:
        watermark = bucketEnd
        persistWatermark(watermark)
        continue
    upsert rows into rollup table with (timestamp=bucketEnd, rtId=...)
    persistWatermark(bucketEnd)
```

Multiple buckets per tick are processed sequentially; the loop yields after a configurable max number of buckets per tick to keep tail latency bounded (default 60).

### Aggregation SQL

For a single bucket the orchestrator issues one `INSERT INTO target (...) SELECT ... FROM source WHERE timestamp >= $1 AND timestamp < $2 GROUP BY rtId` against CrateDB. The orchestrator does not pull rows into application memory.

`SUM`/`MIN`/`MAX`/`COUNT` map 1:1 to CrateDB aggregation functions on the source column. `AVG` expands at SQL level to `SUM(...) AS x_sum, COUNT(...) AS x_count`. Chained rollups (a rollup-of-a-rollup) read from the *stored* `_sum`/`_count` columns and re-aggregate them with `SUM` — never with `AVG` on a previously averaged column. This is what makes chained AVG correct (§3-rationale).

`First`/`Last` (AB#4188) have no native CrateDB aggregate (CrateDB has no `arg_min`/`arg_max`, and
`MIN`/`MAX` do not accept an array). Instead the source rows are wrapped in a sub-select that stamps
each row with a `ROW_NUMBER()` rank per `rtId` — ascending time for `First`, descending for `Last` —
and the outer `GROUP BY` picks the value of the row ranked 1 via `MAX(CASE WHEN rn = 1 THEN value END)`
(exactly one row per group is rank 1, so the `MAX` collapses to that value). The ranking key is the
raw `timestamp` for a raw source and the child `window_end` for a windowed / cascade source, so
`Last` of a day is the stored `Last` of its latest non-empty child bucket. When a `First`/`Last`
co-occurs with a `TimeWeightedAvg` (which forces the LOCF sub-select), the rank orders the carry-in
virtual row last and the pick is guarded with `AND NOT is_carry`, so a bucket with only a carry (no
in-bucket events) yields `NULL`.

### Idempotency / Upsert

CrateDB target rows use the natural key `(timestamp, rtId)`. The orchestrator issues `INSERT INTO ... ON CONFLICT (timestamp, rtId) DO UPDATE SET ...` (CrateDB supports this). If the orchestrator crashes between `INSERT` and `persistWatermark`, the next run re-aggregates the same bucket; the upsert collapses duplicates.

### Manual Backfill

An admin GraphQL mutation `rewindRollupWatermark(archiveRtId, toTimestamp)` moves `LastAggregatedBucketEnd` backward. The next orchestrator tick re-aggregates from there. The mutation is destructive to the rollup's data in the rewound range but the re-aggregation restores it; useful when source data was backfilled or when `Aggregations[]` definitions change (the latter requires recreating the rollup; see §7).

## §6 Source Lifecycle Effects on Rollups

| Source action | Effect on rollup |
|---|---|
| Source `Activated → Disabled` | Rollup keeps producing buckets from already-ingested data up to its watermark, then naturally stalls because no new source data arrives. Status stays `Activated`. |
| Source `Disabled → Activated` | Orchestrator resumes; gap is backfilled automatically. |
| **Empty source data** (admin truncates source partitions or runs explicit "empty archive" op) | Rollup is **frozen** for the time range covered by the truncation: `FrozenUntil` is set to the largest `timestamp` that was removed from source. Rollup keeps the aggregated rows it already produced for that range — those are intentionally preserved as the long-term summary. The orchestrator only advances past `FrozenUntil`; it will not re-aggregate (and therefore not lose) frozen ranges. |
| Source soft-delete | Rejected if any non-soft-deleted rollup references the source. Admin must delete or freeze the rollups first. This prevents accidental destruction of analytical history. |
| Source schema change | Source columns are immutable (concept §3). The case does not arise unless the schema-evolution feature is added later — at which point rollups must be re-validated explicitly (§7). |

`FrozenUntil` is monotonic: it can only move forward, never back, and it survives subsequent source state changes. A frozen rollup is still queryable like any other archive.

### Retention Coupling

When `CkArchive.RawRetention` is set, partition dropping on the source table is gated by `min(LastAggregatedBucketEnd)` across all rollups attached to that source. The system never drops raw data ahead of the slowest rollup. The rollup orchestrator publishes its watermark; the retention job reads watermarks before deciding which partition is safe to drop. A rollup that is `Disabled` or `Failed` blocks retention on its source — this is intentional, the operator notices.

## §7 Schema Evolution

Rollup column definitions (`Aggregations[]`) are **immutable** after `Activated`, mirroring `CkArchive.Columns`. To change them, the admin:

1. Creates a new `CkRollupArchive` next to the old one (different `rtWellKnownName`).
2. Activates it; it starts producing from the earliest source data still present.
3. Optionally backfills via `rewindRollupWatermark` to cover historical buckets.
4. Switches downstream consumers to the new rollup.
5. Deletes the old rollup once nothing reads from it.

This avoids the question "what happens to existing rows when a column is added/removed/retyped?" — there is no in-place edit.

> **Adding a new aggregation declaration to an existing archive (AB#4188 AC).** This is the
> sanctioned answer to "add `max(B)` to an archive that already produces `sum(A)`": create a new
> rollup carrying the full aggregation set (`sum(A)` + `max(B)`), activate it, and backfill it over
> the historical range with `rewindRollupWatermark`. The old rollup's already-stored values are
> never invalidated because they live in a separate table. In-place mutation of `Aggregations[]` on
> an activated rollup — adding a column to the live table and backfilling only the new column — is
> deliberately **not** supported: it would reopen the atomic-swap / partial-generation questions
> that §3 (immutability) and AB#4184 (recompute) close by construction. Multiple aggregations across
> different attributes *within one archive from the start* are fully supported and are the common
> case; only after-the-fact addition to a live archive routes through the create-new-rollup flow.

If a source archive ever gains schema evolution (out of scope for this concept), rollups remain decoupled: a rollup only references columns it captures via `Aggregations[].SourcePath`. New source columns are not auto-propagated; the admin must create a new rollup definition.

## §8 Status & Failure Model

Inherited from `CkArchive` plus rollup-specific failure modes that produce `Failed`:

| Failure | Detection | Recovery |
|---|---|---|
| Activation: source not activated | Validation in `ActivateAsync` | Activate source, retry. |
| Activation: source path missing | Validation | Edit `Aggregations[]` (only while `Created`). |
| DDL failure | CrateDB error from `EnsureArchiveCreatedAsync` | Inherited from concept §11. |
| Bucket aggregation failure | Orchestrator catches exception | Rollup stays `Activated`; watermark **not** advanced; per-archive retry counter bumped; after N consecutive failures (default 5) status → `Failed`, alert raised via `IArchiveAuditTrail.RecordTransitionAsync(..., reason: <sql error>)`. Admin uses `retryArchiveActivation` after fix. |
| Upsert conflict on schema mismatch | CrateDB error | Should not occur — table schema is frozen. If it does, treat as DDL failure. |

`Failed` rollups do not block source retention: once a rollup is `Failed` for longer than a configurable grace period (default 24 h) it is treated as if its watermark were at `now` — retention proceeds. The operator notices via the alert; the rollup data up to the failure is preserved but no new buckets are added until they re-activate.

## §9 GraphQL Surface

### Mutations (extend `StreamDataMutation`, concept §16)

| Field | Returns | Notes |
|---|---|---|
| `createRollupArchive(input: CreateRollupArchiveInput!)` | `OctoObjectId` | Server-side rollup creation. Input carries only the rollup-specific fields (`sourceArchiveRtId`, `bucketSizeMs`, `watermarkLagMs`, `aggregations[]` + optional name); `TargetCkTypeId` is inherited from the source archive and `Columns` is derived from the aggregations via `RollupColumnGenerator`. Requires `StreamDataAdmin`. |
| `freezeRollupArchive(archiveRtId, until)` | `ArchiveTransitionResult` | Sets `FrozenUntil`. Idempotent if `until` ≥ current. |
| `unfreezeRollupArchive(archiveRtId, acceptGaps)` | `ArchiveTransitionResult` | Clears `FrozenUntil`. The gap-detection guard is deferred to a follow-up; for the MVP `FrozenUntil` is cleared unconditionally and the `acceptGaps` flag is logged for traceability. |
| `rewindRollupWatermark(archiveRtId, toBucketEnd)` | `ArchiveTransitionResult` | See §5. The target timestamp is truncated down to the bucket boundary. Requires `StreamDataAdmin`. |

`activateArchive` / `disableArchive` / `enableArchive` / `retryArchiveActivation` / `deleteArchive` work transparently on `CkRollupArchive` (the mutations are typed on `CkArchive`, dispatch is polymorphic).

The generic `systemStreamDataCkRollupArchives.create` mutation (auto-generated from the CK model) is technically still callable, but should not be used: it requires the caller to pre-fill `TargetCkTypeId` and `Columns`, and bypasses the source-archive cross-check that the dedicated `createRollupArchive` enforces.

### Queries

No new query types. Rollups are queryable via the existing typed / transient / persisted entry points. `AVG` columns are exposed as a single virtual field that the resolver computes from `_sum`/`_count`; the underlying columns remain available for clients that want raw building blocks (e.g. for further client-side re-aggregation).

A new query `rollupsFor(archiveRtId): [RollupArchiveInfo]` returns the rollups attached to a source archive plus their watermark and status, for studio display.

## §10 Validation Rules (rollup-specific, on top of concept §10)

| Trigger | Rule | On violation |
|---|---|---|
| Save `CkRollupArchive` (any state) | `SourceArchiveRtId` references an existing, non-soft-deleted `CkArchive`. | `RollupSourceMissingException` |
| Save `CkRollupArchive` (any state) | `Aggregations.Count >= 1`. | `RollupAggregationsRequiredException` |
| Save `CkRollupArchive` (any state) | No duplicate `(SourcePath, Function)` pairs. | `DuplicateRollupAggregationException` |
| Save `CkRollupArchive` (`Activated`+) | `SourceArchiveRtId`, `BucketSize`, `Aggregations` unchanged. | `RollupSchemaImmutableException` |
| Activate | Source archive in `Activated`. | `RollupSourceNotActivatedException` |
| Activate | Each `SourcePath` exists in source `Columns` and has compatible primitive type. | `RollupSourcePathInvalidException` |
| `SourceArchiveRtId` cycle (rollup-of-self) | Graph check at save. | `RollupCycleException` |
| Source archive soft-delete | No active rollups reference this source. | `RollupSourceInUseException` |

All exceptions extend `StreamDataException` (concept §12) and surface as stable GraphQL error codes.

## §11 Audit & Events

`IArchiveAuditTrail` is extended with:

```csharp
Task RecordRollupRunAsync(
    string tenantId,
    OctoObjectId rollupRtId,
    DateTime bucketStart,
    DateTime bucketEnd,
    int rowsWritten,
    TimeSpan elapsed);

Task RecordFreezeAsync(
    string tenantId,
    OctoObjectId rollupRtId,
    DateTime frozenUntil,
    string? reason);
```

Status transitions (`Activated`/`Disabled`/`Failed`) already flow through `RecordTransitionAsync` and require no change.

## §12 Performance & Sizing Notes

- One CrateDB SQL statement per bucket per tick. For a tenant with 100 rollups and 30 s tick that is ≤ 200 statements/min — well below CrateDB's per-tenant load expectations.
- The orchestrator must spread ticks across tenants; a single global timer that fires per-tenant in round-robin is sufficient at expected scale.
- Bucket aggregation cost on the source is `O(rows in bucket)`. For 1 min buckets on a high-frequency source this can be material; if needed, CrateDB partition pruning + an index on `timestamp` (already present on raw archives) keeps cost linear in the bucket size, not the table size.
- Rollups should themselves be partitioned by a coarser interval (e.g. 1 d for a 1 min rollup, 30 d for a 1 h rollup) so `DROP PARTITION`-based retention on rollups remains an option later.

## §13 Open Items (post-MVP)

- **Cold storage**: `COPY (SELECT ... WHERE timestamp < threshold) TO 's3://...'` before partition drop. Restore is admin-driven, no auto-rehydrate.
- **N:M aggregations**: aggregate from multiple source archives into one rollup. Likely needs a richer `Aggregations` model (`source` per aggregation, not per rollup).
- **DataFlow-based custom rollups**: expose `LoadAggregatedFromArchive@1` for users who want non-canonical aggregations (percentiles, stddev, downsampling with gap-fill). Out of MVP because it duplicates the orchestrator semantics with weaker correctness guarantees.
- **Sub-bucket query rewrite**: when a query asks for 15 min and only a 1 min rollup exists, automatically re-aggregate on read.
- **Source schema evolution**: requires explicit rollup re-validation flow and a "rebuild rollup" mutation.
