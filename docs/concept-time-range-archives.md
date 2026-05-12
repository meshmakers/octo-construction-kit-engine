# Concept: Time-Range-Based Stream Data Archives

Status: **Draft** — concept agreed in chat on 2026-05-12, implementation pending.
See also: [concept-rollup-archives.md](./concept-rollup-archives.md) for the existing
derived-rollup design that this concept extends.

## §1 Overview

OctoMesh currently ingests stream data in two shapes:

| Flavor | Insert path | Time field | Use case |
|---|---|---|---|
| `CkArchive` (raw) | External callers via `IStreamDataRepository.InsertAsync(StreamDataPoint)` | Single `Timestamp` | Instantaneous measurements (temperature reading at 13:42:07.123) |
| `CkRollupArchive` (derived) | System orchestrator, aggregated from a source `CkArchive` | Single `Timestamp = bucketEnd` | Down-sampled views (5-min averages of raw temperature) |

Both flavors carry **one** time coordinate per row. This breaks for external systems
that deliver values **already aggregated over a window the system did not compute**:

- EDA energy reports — 15-min consumption totals from the metering operator.
- Smart-meter daily readings — accumulated kWh between two utility-defined boundaries.
- Tariff prices — hourly prices with explicit validity ranges.
- Weather forecasts — temperature/wind values valid for `[hour, hour+1)`.

For these the time information is the **window itself** — `[from, to)`, not a single
moment. Storing only `timestamp = to` loses the window length and forces every
downstream consumer to reconstruct it from out-of-band knowledge.

### Goals

- A first-class storage shape for time-range-valued data.
- An external insert path through the existing MeshAdapter pipeline.
- Idempotent re-delivery of corrected values (an external source may republish
  `[13:00, 13:15)` with a revised total after a correction).
- Single audit signal for "this value was corrected at some point" (so dashboards can
  surface late corrections without operators having to dig into logs).
- Chained rollups over time-range archives — daily, weekly, monthly aggregations of
  15-min EDA values are a hard requirement, not future work.
- Unified storage schema across rollup-style archives — the system-orchestrated
  `CkRollupArchive` and the externally-ingested `CkTimeRangeArchive` both use the same
  `(window_start, window_end)` primary key. The current single-`timestamp` rollup
  schema is deprecated.

### Non-Goals (MVP)

- Window-alignment enforcement. Arbitrary `[from, to)` pairs are accepted; the EDA
  market has irregular slots, and forcing alignment would reject valid data.
- Overlap detection. Storing both `[13:00, 14:00)` and `[13:00, 13:30)` for the same
  entity is allowed — they are independent rows under the `(start, end, rtid, ckTypeId)`
  natural key. The query layer is responsible for picking a consistent slicing if the
  consumer needs one.
- Full revision history. The `WasUpdated` flag captures "ever updated" but not the
  individual revisions or their timestamps. If audit-grade history is needed later, an
  append-only sidecar table can be added without changing the main archive shape.
- Migrating user-CK `TimeRange` (Basic) callers as part of this concept. The new
  `System/TimeRange` lives alongside `Basic/TimeRange` and existing callers stay
  untouched until a separate cleanup pass.

## §2 Type Hierarchy

```
CkArchive (becomes abstract base — see migration note)
├── CkRawArchive (new name for today's raw shape) — inserts via SaveStreamDataInArchive@1
├── CkTimeRangeArchive (new) — inserts via SaveTimeRangeStreamDataInArchive@1
└── CkRollupArchive (existing, schema change — see §6) — system-orchestrated
```

Three concrete subtypes; `CkArchive` itself flips to `isAbstract: true` so it can no
longer be instantiated directly. Each subtype carries its own storage shape and
insert path:

- **`CkRawArchive`** keeps today's single-`timestamp` schema and direct
  `IStreamDataRepository.InsertAsync(StreamDataPoint)` ingestion. The rename makes
  the type hierarchy honest — once a sibling subtype exists, the abstract base
  shouldn't double as the default raw flavor.
- **`CkTimeRangeArchive`** stores `(window_start, window_end)` rows written by
  external callers (no orchestration, no source archive).
- **`CkRollupArchive`** keeps its existing rollup attributes (`SourceArchiveRtId`,
  `BucketSize`, `WatermarkLag`, watermark fields) but its **storage shape unifies
  with `CkTimeRangeArchive`** on the `(window_start, window_end)` schema (§6).

### Migration: `CkArchive` → `CkRawArchive`

Today `CkArchive` is `isAbstract: false` and is the de-facto raw archive type. The
rename requires:

1. **CK model change**: introduce `CkRawArchive` in `System.StreamData` (new
   version, e.g. 1.2.0). Mark `CkArchive` as `isAbstract: true`.
2. **Mongo migration script** (`1.1.0-to-1.2.0.yaml` in
   `StreamDataCkModel/ConstructionKit/migrations/`): rewrite every
   `RtEntity_SystemStreamDataCkArchive` document whose `ckTypeId =
   "System.StreamData/CkArchive"` to `ckTypeId = "System.StreamData/CkRawArchive"`.
   The collection name and document shape stay unchanged; only the discriminator
   moves.
3. **CrateDB tables**: unaffected — the per-archive table name is derived from
   `rtid`, not from the CK type. No DDL change for existing raw archives.
4. **Code**: replace remaining references to "the raw `CkArchive`" with
   `CkRawArchive` (mainly in `StreamDataMutation`, the studio archive form, the
   archive lifecycle service's status validation, and the existing
   `ICkArchiveRuntimeStore` which now returns either `RtCkRawArchive` or
   `RtCkTimeRangeArchive` / `RtCkRollupArchive` instances).

The shared lifecycle (`Activate` / `Disable` / `Enable` / …) and the
`ICkArchiveRuntimeStore` continue to operate at the `CkArchive` base level —
subtype-specific behaviour is in dedicated stores (`ICkRawArchiveRuntimeStore`,
`ICkTimeRangeArchiveRuntimeStore`, `ICkRollupArchiveRuntimeStore`).

### `CkTimeRangeArchive` attributes (new)

```yaml
- typeId: CkTimeRangeArchive
  derivedFromCkTypeId: ${this}/CkArchive
  attributes:
    - id: ${this}/Period
      name: Period
      valueType: TimeSpan
      isRequired: false
      description: |
        Descriptive period of the windows this archive holds (e.g. PT15M, PT1H, P1D).
        Optional and advisory only — variable-period archives are allowed, and the
        engine does not enforce that incoming windows match the declared period.
        Used by the UI to label the archive ("EDA 15-min") and by chained-rollup
        recommendations.
```

No `SourceArchiveRtId` — the archive is not derived. No `BucketSize` / `WatermarkLag`
/ `LastAggregatedBucketEnd` / `FrozenUntil` — there is no orchestrator on this path.

### `System/TimeRange` (new, mirrors `Basic/TimeRange`)

```yaml
records:
- recordId: TimeRange
  description: "A time range with an inclusive start and exclusive end date."
  attributes:
    - id: ${this}/From
      name: From
    - id: ${this}/To
      name: To

attributes:
  - id: From
    valueType: DateTime
  - id: To
    valueType: DateTime
  - id: TimeRange
    valueType: Record
    valueCkRecordId: ${this}/TimeRange
```

Shape identical to `Basic/TimeRange`. Lives in `System` (not `System.StreamData`)
because:
- `System.*` cannot depend on user CKs (`Basic`) by architectural convention.
- TimeRange is a universally useful primitive (business validity periods, lease
  durations, …), not StreamData-specific.
- `System.StreamData` only depends on `System`, so making `TimeRange` available to
  `System.StreamData` requires its placement in `System`.

`Basic/TimeRange` is left in place; deprecation handled by a future cleanup pass that
migrates user-CK references (separate concept, out of scope here).

## §3 External Insert Path

### Wire-Level DTO

```csharp
public class TimeRangeStreamDataPoint
{
    public required OctoObjectId RtId { get; init; }
    public required RtCkId<CkTypeId> CkTypeId { get; init; }
    public required DateTime From { get; init; }      // inclusive
    public required DateTime To   { get; init; }      // exclusive
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = ...;
}
```

Mirrors `StreamDataPoint` with `From`/`To` instead of `Timestamp`. Both must be UTC;
the repository normalises non-UTC inputs the same way the existing `InsertAsync` does.

### Repository

`IStreamDataRepository` gains:

```csharp
Task InsertTimeRangeAsync(
    OctoObjectId archiveRtId,
    IEnumerable<TimeRangeStreamDataPoint> datapoints,
    CancellationToken cancellationToken = default);
```

The CrateDB implementation emits one batched INSERT per call (analogous to the existing
bulk `InsertAsync`), targeting the per-archive table with the two-column window shape
(§4). Conflict resolution: `ON CONFLICT (window_start, window_end, rtid, cktypeid)
DO UPDATE SET <user columns> = EXCLUDED.<user columns>, was_updated = TRUE,
rtchangeddatetime = CURRENT_TIMESTAMP`.

### MeshAdapter Node

```
SaveTimeRangeStreamDataInArchive@1
├── inputs:  archiveRtId, sourceField (rtid), ckTypeIdField,
│            fromField, toField, attributeMappings[]
└── action:  group rows by archiveRtId, build TimeRangeStreamDataPoint[],
             call IStreamDataServiceClient.InsertTimeRangeAsync
```

Pipeline shape for EDA:

```
ExtractFromEdaApi@1            // pulls quarter-hour reports
  → MapToTimeRangePoint@1       // shapes them: { rtid, from, to, attributes }
  → SaveTimeRangeStreamDataInArchive@1
```

The existing `SaveStreamDataInArchive@1` continues to work unchanged for raw archives.

## §4 Storage Layout

Per-tenant schemas remain. Each archive has its own table; the shape depends on the
archive's subtype:

### `CkRawArchive` (unchanged — except for the type rename)

```sql
CREATE TABLE "<tenant>"."archive_<rtid>" (
  "timestamp"       TIMESTAMP WITH TIME ZONE NOT NULL,
  "rtid"            TEXT NOT NULL,
  "cktypeid"        TEXT NOT NULL,
  "rtwellknownname" TEXT,
  "rtcreationdatetime" TIMESTAMP WITH TIME ZONE,
  "rtchangeddatetime"  TIMESTAMP WITH TIME ZONE,
  -- user columns from CkArchive.Columns[]:
  "temperature" DOUBLE PRECISION,
  -- ...
  PRIMARY KEY ("timestamp", "rtid", "cktypeid")
);
```

### `CkTimeRangeArchive` and `CkRollupArchive` (unified — §6)

```sql
CREATE TABLE "<tenant>"."archive_<rtid>" (
  "window_start"    TIMESTAMP WITH TIME ZONE NOT NULL,
  "window_end"      TIMESTAMP WITH TIME ZONE NOT NULL,
  "rtid"            TEXT NOT NULL,
  "cktypeid"        TEXT NOT NULL,
  "rtwellknownname" TEXT,
  "rtcreationdatetime" TIMESTAMP WITH TIME ZONE,
  "rtchangeddatetime"  TIMESTAMP WITH TIME ZONE,
  "was_updated"     BOOLEAN NOT NULL DEFAULT FALSE,
  -- user columns:
  "energyconsumed" DOUBLE PRECISION,
  -- (rollup) "temperature_avg_sum", "temperature_avg_count", ...
  PRIMARY KEY ("window_start", "window_end", "rtid", "cktypeid")
);
```

The primary key `(window_start, window_end, rtid, ckTypeId)` carries the full window
identity. Re-deliveries of the same window for the same entity upsert; different
windows (even overlapping) coexist as independent rows.

The DDL generator branches on the snapshot type — `CkArchiveSnapshot.RollupAggregations
is not null` or `CkArchiveSnapshot.IsTimeRange` — and emits the appropriate shape. The
existing `ArchivePathTypeResolver` / `RollupColumnTypeResolver` continue to determine
the user-column SQL types; only the time-axis columns differ.

## §5 The `was_updated` Flag

A `BOOLEAN NOT NULL DEFAULT FALSE` column on every time-range row.

- **First insert** of a `(window_start, window_end, rtid, ckTypeId)` tuple: `was_updated = FALSE`.
- **Any conflict upsert** on that key sets `was_updated = TRUE`, regardless of whether
  the values actually changed. The flag means "this row was written more than once at
  some point in its history", which is the cheap-to-detect signal operators need to
  decide whether to re-trust historical aggregations.

The flag is **monotonic** — once `TRUE`, it stays `TRUE`. There is no reset path.

Trade-off: we cannot tell apart "value was actually corrected" from "external source
re-sent the same value". For MVP this is intentional — value-change detection requires
a comparison per column in the upsert, which is more complex and rarely useful (the
external system's `received again` event is usually triggered by a real revision
anyway). A future revision counter or change-log table can refine this.

### Propagation in Chained Rollups

When a `CkRollupArchive` aggregates over a time-range source, the chained row's
`was_updated` is computed as `MAX(source.was_updated)` (boolean MAX = OR over the
contributing source rows). If any source window in the bucket was corrected, the
downstream bucket inherits the flag. This is what dashboards want — a daily total
that contains a corrected quarter-hour should itself be flagged.

The chained-rollup aggregation SQL (§7) emits `MAX(was_updated) AS was_updated`
alongside the user-column aggregates.

## §6 CkRollupArchive Schema Migration (Option A)

The existing `CkRollupArchive` shipped with a single `timestamp = bucketEnd` column. As
part of the unification we move it to the two-column `(window_start, window_end)`
shape (§4). Rationale:

- One storage schema across `CkTimeRangeArchive` and `CkRollupArchive` — chained
  rollups have a uniform source format.
- `window_start` no longer needs to be re-derived from `timestamp - bucketSize` at
  query time.
- `was_updated` propagates cleanly through the chain.

### Backwards Compatibility & Migration

Until this lands, the asset-repo has only a handful of `CkRollupArchive` tables in the
wild (the feature shipped recently). We can rely on a clean recreation rather than
in-place SQL migration:

1. **New activations** use the new schema unconditionally.
2. **Existing activations**: the lifecycle service detects the old single-`timestamp`
   shape on next `ActivateAsync` (or via a fixup script) and `DROP TABLE` + recreate
   with the new shape. Data loss is acceptable for the MVP window (the rollup
   re-aggregates from the source archive on next watermark advance, or by an admin
   `rewindRollupWatermark`).
3. **Future change-friendly DDL**: the activation path emits both columns as
   `NOT NULL`. The orchestrator's INSERT (§7) writes both.

### Query / Read Compatibility

The query layer is updated so the existing `streamDataQuery` / `transientStreamDataQuery`
surfaces return a uniform `timestamp` field regardless of subtype:
- `CkRawArchive`: `timestamp` is the row's `timestamp` column.
- Time-range archives: `timestamp` defaults to `window_end` (= the existing
  `CkRollupArchive` semantics — "this row represents the period ending at `timestamp`").

A future query extension can expose `window_start` / `window_end` explicitly when the
caller needs both, but persisted queries written against the old shape continue to
work.

## §7 Chained Rollups over Time-Range Sources

A `CkRollupArchive` can have either a `CkRawArchive` **or** another time-range archive
(`CkTimeRangeArchive` or `CkRollupArchive`) as its `SourceArchiveRtId`. The
orchestrator's aggregation SQL adapts to the source shape.

### Time Predicate

| Source shape | Filter for bucket `[B_start, B_end)` |
|---|---|
| Raw `timestamp` | `timestamp >= B_start AND timestamp < B_end` |
| Time-range `(window_start, window_end)` | `window_start >= B_start AND window_end <= B_end` |

The fully-contained semantic (`window_start >= B_start AND window_end <= B_end`) is the
MVP rule. It implies:

- A daily rollup of 15-min EDA windows aggregates the 96 quarter-hours whose windows
  fit entirely inside the day.
- Windows that straddle a bucket boundary are dropped from that bucket. Operators
  pick bucket sizes that are multiples of (or aligned to) their source windows.
- Misaligned sources surface as missing data in the chained rollup — preferable to
  partial-overlap interpolation, which would invent values.

Future option: pro-rated splitting for overlapping windows (e.g. a 90-min window
contributing to two hourly buckets weighted by overlap). Out of MVP scope.

### Aggregation SQL (chained source)

For target bucket `[B_start, B_end)` over a time-range source:

```sql
INSERT INTO "<tenant>"."archive_<target>" (
  "window_start", "window_end", "rtid", "cktypeid", "rtwellknownname",
  "was_updated",
  <derived columns>
)
SELECT
  '<B_start>'::timestamp AS "window_start",
  '<B_end>'::timestamp   AS "window_end",
  "rtid",
  '<targetCkTypeId>' AS "cktypeid",
  MAX("rtwellknownname") AS "rtwellknownname",
  MAX("was_updated") AS "was_updated",   -- inherit correction flag
  <per-aggregation function over source columns>
FROM "<tenant>"."archive_<source>"
WHERE "window_start" >= '<B_start>'::timestamp
  AND "window_end"   <= '<B_end>'::timestamp
GROUP BY "rtid"
ON CONFLICT ("window_start", "window_end", "rtid", "cktypeid") DO UPDATE SET
  <per-aggregation function = EXCLUDED.<column>>,
  "was_updated" = TRUE,                  -- re-aggregation = correction
  "rtchangeddatetime" = CURRENT_TIMESTAMP;
```

The per-aggregation function for a chained rollup operating on a source that already
materialised `AVG` as `_sum + _count` is:

| Original function | Source col(s) | Target col(s) |
|---|---|---|
| `SUM` | `x` | `SUM(x)` |
| `MIN` | `x` | `MIN(x)` |
| `MAX` | `x` | `MAX(x)` |
| `COUNT` | `x` | `SUM(x)` — yes, sum of counts |
| `AVG` | `x_sum`, `x_count` | `SUM(x_sum)`, `SUM(x_count)` |

`AVG` chains numerically correctly because we already store sum and count separately
(see `RollupAggregationColumns.cs`). The orchestrator inspects the source archive's
columns: if `x` is a single-column source (raw or time-range non-chained), it uses
`SUM(x)`/`COUNT(x)`/`MIN(x)`/`MAX(x)`/`AVG`-as-sum+count over `x`. If `x_sum`/
`x_count` columns exist on the source (the source is itself a chained AVG rollup), it
sums them directly without recomputing.

### Cascade Example (15-min → hour → day → week → month)

```
ExternalEdaArchive  [15-min] ← MeshAdapter inserts
       ↓ chained (BucketSize = 1h)
HourlyRollup        [1h]
       ↓ chained (BucketSize = 1d)
DailyRollup         [1d]
       ↓ chained (BucketSize = 7d, aligned to ISO week)
WeeklyRollup        [7d]
       ↓ chained (BucketSize = …, see "non-uniform buckets" below)
MonthlyRollup       [calendar month]
```

#### Non-uniform buckets (months)

Calendar months are not a fixed duration; the existing `BucketSize: TimeSpan` model
does not express them. Two options:

- **A**: model monthly rollups by setting `BucketSize = 28d` (the lower bound) and
  letting the time predicate align to calendar boundaries via a new `BucketAlignment`
  attribute (`CalendarMonth` / `CalendarWeek` / `Iso8601Week`).
- **B**: introduce a separate `CkCalendarRollupArchive` subtype with a `Granularity`
  enum (`Day`, `Week`, `Month`, `Year`) instead of `BucketSize`.

Recommendation: **A** for MVP — keep one subtype, add `BucketAlignment` as an optional
attribute, default `FixedSize`. The orchestrator computes the next bucket boundary
from `BucketAlignment` instead of `LastAggregatedBucketEnd + BucketSize`. This is a
small extension to the watermark advance and keeps the storage shape unified.

### Disabling the Orchestrator for `CkTimeRangeArchive`

`CkTimeRangeArchive` has no `BucketSize` / `WatermarkLag` / `LastAggregatedBucketEnd`,
so the existing orchestrator loop in `RollupOrchestratorHostedService` must skip them.
The store enumerator (`ICkRollupArchiveRuntimeStore.EnumerateAsync`) already returns
`CkRollupArchive` instances only; `CkTimeRangeArchive` is **not** a rollup and lives
in a different store (`ICkTimeRangeArchiveRuntimeStore`, new) which the orchestrator
never reads.

## §8 GraphQL Surface

### Mutations (extend `StreamDataMutation`)

| Field | Returns | Notes |
|---|---|---|
| `createTimeRangeArchive(input: CreateTimeRangeArchiveInput!)` | `OctoObjectId` | Server-side create. Input: `rtWellKnownName?`, `targetCkTypeId`, `columns[]`, `period?`. No source archive, no aggregations. Requires `StreamDataAdmin`. |
| `createRollupArchive` *(existing)* | `OctoObjectId` | Already supports `sourceArchiveRtId` pointing at a time-range archive. No API change. |

### Time-Range-Aware Inserts (REST/SignalR)

`MeshAdapter ↔ asset-repo` adds a sibling insert endpoint:

```
POST /{tenantId}/v1/streamdata/{archiveRtId}/insertTimeRange
Body: [ { rtId, ckTypeId, from, to, attributes }, ... ]
```

GraphQL inserts are not exposed (consistent with the existing `CkArchive` design — bulk
inserts are too noisy for GraphQL request shaping).

### Queries

The persisted/transient `streamDataQuery` surfaces gain a `timeRange` sub-shape for
explicit window-aware reads (returns `from`/`to` instead of `timestamp`). Existing
shapes (`simple`, `aggregation`, `groupingAggregation`, `downsampling`) work on
time-range archives via the read-compatibility layer (§6) that surfaces `window_end`
as `timestamp` to consumers that don't ask for both window columns.

## §9 Validation Rules

Save-time, on any state (extends the existing `RollupValidator`-style checks):

| Trigger | Rule | On violation |
|---|---|---|
| Save `CkTimeRangeArchive` | `Columns[].Count >= 1`. | `ArchiveColumnsRequiredException` |
| Save `CkTimeRangeArchive` | No duplicate `Columns[].Path` entries. | `DuplicateArchiveColumnException` |
| Activate `CkTimeRangeArchive` | Each column path resolves against the target CK type's attributes (same as raw archives). | `UnresolvableArchivePathException` |

Insert-time:

| Trigger | Rule | On violation |
|---|---|---|
| Insert | `to > from`. | `InvalidTimeRangeException` |
| Insert | `from`, `to` are valid UTC. | (normalised to UTC; non-UTC inputs converted) |
| Insert | Archive in `Activated` state. | `ArchiveNotActivatedException` |
| Insert | Attribute keys map to existing storage columns. | Unknown columns dropped with WARN log (consistent with raw archive policy). |

## §10 Lifecycle

`CkTimeRangeArchive` inherits the `CkArchive` lifecycle (Created → Activated → Disabled
→ Activated / Failed → … → Deleted). No new transitions. `Activate` provisions the
CrateDB table with the time-range schema (§4); `Disable` rejects inserts; `Delete`
drops the table.

No source-archive coupling — `CkTimeRangeArchive` cannot be the source of another
`CkTimeRangeArchive` (no orchestration), but **can be the source of a `CkRollupArchive`**
(§7).

## §11 Audit & Events

`IArchiveAuditTrail` extends:

```csharp
Task RecordTimeRangeInsertAsync(
    string tenantId,
    OctoObjectId archiveRtId,
    int rowsWritten,
    int rowsUpdated,        // count of upserts that hit ON CONFLICT
    TimeSpan elapsed);
```

`rowsUpdated > 0` is the operator's signal that a re-delivery happened. The default
`LoggingArchiveAuditTrail` logs at INFO when `rowsUpdated > 0`, at DEBUG otherwise.

## §12 Implementation Phases

Suggested rollout order — each phase ships a coherent slice.

1. **TimeRange in `System` CK** — add `System/TimeRange-1` (same shape as
   `Basic/TimeRange`). Bump `System` minor version. No code consumers yet.
2. **`CkArchive` → `CkRawArchive` rename** — introduce `CkRawArchive` in
   `System.StreamData`, flip `CkArchive` to `isAbstract: true`, ship the migration
   script that rewrites existing entities' `ckTypeId`. Update code call sites that
   instantiate raw archives. No storage change.
3. **`CkTimeRangeArchive` CK type** — yaml + record/enum stubs in `System.StreamData`.
   `IStreamDataCkModelDescriptor` bumps to the new version.
4. **Storage + insert path** — `ICkTimeRangeArchiveRuntimeStore` (Mongo impl),
   `IStreamDataRepository.InsertTimeRangeAsync` (CrateDB impl), DDL generation,
   `was_updated` upsert handling. Unit + integration tests.
5. **GraphQL create mutation + REST insert endpoint** — `createTimeRangeArchive` and
   `POST /v1/streamdata/{rtId}/insertTimeRange`.
6. **MeshAdapter node** — `SaveTimeRangeStreamDataInArchive@1`. EDA pipeline can now
   land data.
7. **Unify `CkRollupArchive` storage to `(window_start, window_end)`** ✅ — §6 migration.
   Drop-and-recreate path for existing rollups (`EnsureWindowedTableShapeAsync` detects
   the pre-Phase-7 single-`timestamp` shape and drops); new schema for new ones via
   `ArchiveDdlGenerator.GenerateCreateWindowedTable`.
8. **Chained rollup aggregation over time-range sources** ✅ — orchestrator's SQL builder
   branches on source shape (`RollupAggregationSqlBuilder.Build` takes
   `sourceUsesWindowedStorage`), windowed source ⇒ fully-contained
   `window_start >= B_start AND window_end <= B_end` predicate and `MAX(was_updated)`
   propagation. Raw source path unchanged. §7 cascade examples become testable.
9. **Calendar alignment** — `BucketAlignment` attribute on `CkRollupArchive`, calendar-
   month / week / day boundary computation in the orchestrator. Enables daily /
   weekly / monthly EDA rollups.
10. **Studio UI** — list time-range archives in the archives list, create form, insert
    preview, rollup cascade visualisation.
11. **Cleanup pass** — deprecate `Basic/TimeRange`, migrate consumers to
    `System/TimeRange`. Separate concept; out of scope here.

## §13 Open Questions (post-MVP)

- **Pro-rated overlap aggregation** for windows that straddle target bucket
  boundaries. The MVP "fully contained" rule produces gaps when source and target
  windows are misaligned.
- **Revision history** beyond the `was_updated` flag — an append-only sidecar table
  if audit-grade history becomes a requirement.
- **Per-row time zone** — currently everything is stored UTC. EDA windows sometimes
  carry an explicit local time (DST transitions affect window boundaries). Whether
  to surface this in the data plane or normalise at ingest is undecided.
- **Schema evolution for the `Period` advisory** — if an archive's `Period` changes
  (15-min → 5-min), do we keep two periods coexisting or split into two archives?
  MVP: split (the `Period` is descriptive, the archive identity is the rtId).
