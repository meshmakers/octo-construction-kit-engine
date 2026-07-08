# Concept: Time-Weighted Aggregation for Event-Based Archives

**Status:** Core implemented (see §11) — engine enums + CK model bump (System.StreamData 1.6.5,
System 2.2.1), rollup materialisation, LOCF forward-aggregation SQL, read-path chain mapping, and
the query-surface enum plumbing are in place. Open: direct TWA over raw archives (§6.2, guarded
with a clear error), dirty-window carry extension (D2), CrateDB integration tests, Studio UI,
Docusaurus user docs, energyiq validation.
**Work item:** **AB#4336** (Stream data: time-weighted state-duration aggregation for event-based archives).
**Related:**
- [`concept-rollup-archives.md`](concept-rollup-archives.md) — the rollup orchestrator + SQL-per-bucket model this concept extends; the AVG `_sum`/`_count` two-column materialisation is the precedent for the new integral/duration pair.
- [`concept-rollup-recompute.md`](concept-rollup-recompute.md) (AB#4184, implemented) — staging + per-window generation pointer; the new function must be reproducible under recompute.
- [`concept-computed-columns.md`](concept-computed-columns.md) (AB#4189) — explicitly does **not** cover this: computed columns are row-local, duration semantics needs cross-row context (LAG/LOCF).
- [`concept-resolution-aware-series-queries.md`](concept-resolution-aware-series-queries.md) — the series resolver matches `requiredAggregation` against a rollup's stored functions; since the AB#4336 multi-aggregation matching fix it considers *all* functions of a source path, so a new function participates automatically.
- AB#4327 — the energyiq "Beschattung & Beleuchtung" MeshBoard that motivated this (the *"Wo brennt am längsten Licht?"* widget).
- AB#4188 — multiple aggregations per rollup; a TWA aggregation can sit next to AVG/MAX on the same source path.

---

## §1 Problem

Event-based archives store **one row per state change** (Loxone WebSocket path: a row on every
value change, plus one snapshot row per (re)connect). Duration questions — *how long was the
light on?* — cannot be answered by the canonical five aggregations:

- `AVG` over the 0/100 `DimmingLevel` rows is **sample-weighted**: a light that is ON for 3 hours
  produces exactly 2 rows (on + off) and weighs the same as a light that toggles twice within a
  minute. Rankings are roughly right; operating-hours values are wrong.
- `SUM`/`COUNT`/`MIN`/`MAX` don't encode time at all.

The correct semantics is the **time-weighted average** with *last observation carried forward*
(LOCF): between two events the signal holds its previous value; the average weights each value by
how long it held.

```
TWA(bucket) = Σ value_i × (t_{i+1} − t_i)  /  covered_duration
```

with the last value **before** the bucket carried in as the opening state and the last value of
the bucket held until the bucket end. On a 0/100 (or boolean) signal this directly yields the
**duty cycle** per bucket; `duty × window` = operating hours / burn time.

### Why computed columns (AB#4189) do not solve this

Computed archive columns evaluate a formula **per row, row-locally**. Duration semantics needs the
time delta to the *previous* row of the same `rtId` (LAG) and carry-over across bucket boundaries
(LOCF). Neither is expressible row-locally — this is a new **aggregation function**, not a
computed-column use case.

## §2 Goals / Non-Goals

### Goals

- New aggregation function **`TimeWeightedAvg`** available as
  - a `CkRollupFunction` on `CkRollupAggregation` (rollup materialisation), and
  - an `AggregationType` on `AggregationSdQuery` / `GroupingAggregationSdQuery` (query surface),
  so the existing KPI / bar-chart widgets pick it up without frontend changes (wire cell suffix
  `_twavg`).
- Correct LOCF carry-in across bucket boundaries, derived **deterministically from source data**
  so forward aggregation, rewind, and recompute (AB#4184) all reproduce identical results.
- Chained rollups (TWA rollup of a TWA rollup) stay numerically correct via the stored
  integral/duration pair — same design as AVG's `_sum`/`_count`.
- Keep the orchestrator's *no-rows-in-application-memory* property: one SQL statement per bucket.

### Non-Goals (MVP)

- A separate **`StateDuration`** function. On a 0/1 or 0/100 signal, state duration is derivable:
  `duration = TWA × window ( / 100 )`. A dedicated function (with a comparison value, e.g. "time
  where state == 2") is a documented follow-up, not MVP.
- Gap-fill / interpolation policies other than LOCF (linear interpolation etc.).
- TWA over **string/boolean** columns. MVP is numeric only; booleans are ingested as 0/1 numerics
  where needed.
- Timezone-calendar interaction beyond what fixed/calendar buckets already do — TWA reuses the
  existing `BucketBoundary` machinery unchanged.

## §3 Semantics (normative)

For one `(rtId, bucket [B_start, B_end))` over an event-based (raw) source:

1. **Carry-in (LOCF):** the opening observation is the source row with the largest
   `timestamp < B_start` (within the lookback bound, §5.3). If it exists, it contributes a virtual
   event at `t = B_start` with its value.
2. **Events:** all source rows with `B_start ≤ timestamp < B_end`, in timestamp order.
3. **Interval weighting:** each observation holds from its timestamp until the next observation's
   timestamp, the last one until `B_end`.
4. **Outputs (materialised):**
   - `integral = Σ value_i × Δt_i` (unit: value·milliseconds)
   - `covered = Σ Δt_i` (milliseconds actually covered by observations)
5. **TWA (computed on read):** `integral / covered` (NULL when `covered = 0`).

### Partial coverage (no carry-in available)

When no observation exists at or before `B_start` (archive start, or beyond the lookback bound),
the bucket is only covered from its first in-bucket event onward: `covered < bucketLength`. The
TWA is then the honest average **over the covered part** — not a silently-wrong value that assumes
0 before the first event. Consumers that need duty-of-full-window can compare `covered` against
the bucket length (both are materialised / derivable).

`NULL` values in the source column terminate coverage the same way: a NULL observation contributes
`Δt` to neither integral nor covered duration (the signal is "unknown" until the next non-NULL
observation).

### Windowed sources (time-range archives, chained rollups)

- **Chained TWA (rollup of a TWA rollup):** re-aggregate the stored pair —
  `SUM(integral) / SUM(covered)`. No LOCF is needed; the carry was already applied at the finest
  level. This is exactly the AVG-chaining pattern (`SUM(_sum) / SUM(_count)`).
- **TWA over a time-range source** (each row already carries `window_start`/`window_end`): the
  weight is the row's own window length; no LOCF (the windows are the coverage). This falls out of
  the same integral/covered representation.

## §4 Materialisation

### Two stored columns per aggregation (like AVG)

`RollupAggregationColumns.Resolve` (Runtime.Engine.CrateDb) gains:

```
CkRollupFunction.TimeWeightedAvg =>
    base = TargetColumnName ?? "{sourceColumn}_twavg"
    columns:
        {base}_integral   DOUBLE PRECISION   -- Σ value × Δt(ms)
        {base}_duration   BIGINT             -- Σ Δt(ms) covered
```

- The default base name uses the **short token `twavg`** (not the lower-cased enum name
  `timeweightedavg`) — decision D5. `RollupColumnTypeResolver` maps `_integral` → `DOUBLE
  PRECISION`, `_duration` → `BIGINT`.
- The TWA value itself is **not stored**; it is recomputed on read as
  `integral / NULLIF(duration, 0)` — mirroring AVG. This keeps chained re-aggregation and partial
  re-aggregation exact.

### CK model (`System.StreamData`, additive minor bump)

| Element | Change |
|---|---|
| `ckRollupFunction.yaml` | new value `key: 5, name: TimeWeightedAvg` |
| `aggregationTypes.yaml` (SystemCkModel) | new value `key: 5, name: TimeWeightedAverage` |
| C# `CkRollupFunction` | `TimeWeightedAvg = 5` |
| SDK `AggregationTypesDto` + CrateDb `AggregationFunctionDto` | corresponding values |

Validation (activation-time, on top of rollup concept §10): `TimeWeightedAvg` requires a numeric
source column (same rule as `MIN`/`MAX`/`SUM`/`AVG`).

## §5 Forward aggregation SQL (the hard part)

### §5.1 Constraint

The rollup orchestrator issues **one CrateDB statement per bucket** and never pulls rows into
application memory (rollup concept §5). CrateDB has no native LOCF *gap fill*, but CrateDB 5.10
(the version in use, see `StreamDataFixture` → `crate:5.10.10`) supports **window functions**
(`LEAD`, `ROW_NUMBER` with `PARTITION BY`) and **CTEs** — which is all LOCF-within-a-statement
needs.

### §5.2 Statement shape (raw event source)

`RollupAggregationSqlBuilder` branches for TWA targets; sketch (identifiers simplified):

```sql
INSERT INTO target (window_start, window_end, rtid, cktypeid, rtwellknownname,
                    dimminglevel_twavg_integral, dimminglevel_twavg_duration, generation)
WITH events AS (
    -- carry-in: latest observation before the bucket, per rtId (bounded lookback, §5.3),
    -- surfaced as a virtual event at B_start
    SELECT rtid, rtwellknownname, '<B_start>'::timestamp AS ts, v
    FROM (
        SELECT rtid, rtwellknownname, "dimminglevel" AS v,
               ROW_NUMBER() OVER (PARTITION BY rtid ORDER BY "timestamp" DESC) AS rn
        FROM source
        WHERE "timestamp" <  '<B_start>'::timestamp
          AND "timestamp" >= '<B_start>'::timestamp - <lookback>
    ) c
    WHERE rn = 1
    UNION ALL
    -- in-bucket events
    SELECT rtid, rtwellknownname, "timestamp" AS ts, "dimminglevel" AS v
    FROM source
    WHERE "timestamp" >= '<B_start>'::timestamp AND "timestamp" < '<B_end>'::timestamp
),
weighted AS (
    SELECT rtid, rtwellknownname, v,
           COALESCE(LEAD(ts) OVER (PARTITION BY rtid ORDER BY ts),
                    '<B_end>'::timestamp)::bigint - ts::bigint AS dt_ms
    FROM events
)
SELECT '<B_start>'::timestamp, '<B_end>'::timestamp, rtid, '<rollupCkTypeId>',
       MAX(rtwellknownname),
       SUM(CASE WHEN v IS NOT NULL THEN v * dt_ms END)  AS integral,
       SUM(CASE WHEN v IS NOT NULL THEN dt_ms ELSE 0 END) AS duration,
       0
FROM weighted
GROUP BY rtid
ON CONFLICT (...) DO UPDATE SET ...   -- unchanged upsert pattern
```

Notes:
- Timestamps cast to `bigint` are epoch milliseconds in CrateDB — `Δt` in ms without interval
  arithmetic.
- A bucket whose specs mix TWA with plain aggregations (AVG+TWA on one rollup) needs the CTE only
  for the TWA columns. Simplest correct shape: when any TWA spec is present, the whole SELECT runs
  off the `events`/`weighted` CTE and the plain aggregates are computed over the **in-bucket rows
  only** (the carry-in virtual row must not leak into `MIN`/`MAX`/`COUNT` — guarded by a
  `is_carry` marker column in the CTE). Alternative: two statements per bucket (one plain, one
  TWA) collapsing on the same upsert key. Decision D6 — start with the marker-column single
  statement; fall back to two statements if the SQL gets unwieldy.
- Duplicate carry rows on identical timestamps: `ROW_NUMBER` (not `RANK`) guarantees exactly one
  carry row; ties among in-bucket rows keep stable behaviour because equal timestamps produce
  `Δt = 0` for all but the last.

### §5.3 Bounded carry lookback (decision D1)

The carry-in scan `timestamp < B_start` is potentially unbounded. Two options considered:

| Option | Pro | Con |
|---|---|---|
| **A. Source-derived carry with bounded lookback** (recommended) | Stateless, deterministic — forward, rewind, recompute, and open-bucket refresh all reproduce the identical result from source data alone | Lookback bound is a semantic cutoff: a signal silent for longer than the bound loses its carry (bucket becomes partially covered) |
| B. Stored closing-value chain (each rollup row stores the closing state; the next bucket reads it) | O(1) carry lookup | Stateful: recompute of bucket *n* depends on bucket *n−1*'s row → ordering constraints across recompute jobs, rewind reconciliation, generation-pointer interaction; exactly the class of coupling AB#4184 avoided |

**Decision: Option A.** Lookback default **P35D** (safely above the Loxone reconnect cadence —
the adapter writes a full snapshot row per (re)connect, so real gaps are hours, not weeks),
configurable per rollup (`CarryLookback`, new optional attribute on `CkRollupArchive`, mutable).
Partition pruning on `timestamp` keeps the scan cost bounded to the lookback window.

### §5.4 Recompute / rewind / open-bucket interactions

Because the carry is a pure function of source rows (§5.3 A), no new mechanics are needed:

- **Recompute (AB#4184):** the staging aggregation runs the same statement shape; identical
  inputs ⇒ identical integral/duration. Generation pointer, sweep, per-rtId scope: unchanged.
- **Rewind:** re-aggregation from the rewound watermark reproduces the same rows (upsert
  collapses).
- **Open-bucket refresh (AB#4306):** the open bucket is re-aggregated at generation 0 with
  `B_end` still in the future — the statement uses `min(now_tick, B_end)`? **No** — keep it
  simple and consistent with the existing refresh: the refresh runs the normal statement with the
  bucket's nominal `B_end`; the last observation is extrapolated to `B_end`. For a *partial*
  period this slightly over-weights the current state, exactly like the existing open-bucket
  refresh over-represents nothing (it just shows the period-so-far). Follow-up if this proves
  confusing: cap the last interval at the tick time. Decision D7, revisit after validation.
- **Dirty-window ledger:** a retroactive source write inside bucket *n* can also change the carry
  of bucket *n+1* (and only *n+1…* until the next in-bucket observation). Conservative and simple:
  when marking rollup buckets dirty from a source change at time *t*, extend the dirty range to
  `t + CarryLookback` (or: to the next source observation after *t*, when cheaply determinable).
  MVP: extend by one bucket — a retro write changes the carry of at most the buckets up to the
  next observation, and the common correction case (late event inside a bucket that has later
  events) only affects that bucket plus its immediate successor's carry. Decision D2 — MVP:
  dirty range extends **to the end of the bucket after the one containing the change**; the
  general case (long silent stretch after the correction) is picked up by documenting
  `recomputeArchive` as the manual escape hatch.

## §6 Query surface

### §6.1 Reading a TWA rollup (chain mapping)

`RollupQueryAggregationResolver` gains:

| target function | source spec | SQL |
|---|---|---|
| `TimeWeightedAvg` over `TimeWeightedAvg` | `SUM(_integral) / NULLIF(SUM(_duration), 0)` — alias `{path}_twavg` |
| `Sum` over `TimeWeightedAvg` | not resolvable (integral is value·ms, not a value sum) → null |
| others over `TimeWeightedAvg` | null (information discarded) |

Wire suffix **`_twavg`** — widgets that map cells via `aggregationType` metadata pick the value up
without change; the suffix keeps the alias unique next to `_avg`/`_max` siblings.

### §6.2 Direct queries over raw event archives

`AggregationSdQuery` / `GroupingAggregationSdQuery` (transient + persisted) accept
`AggregationType.TimeWeightedAverage`. The CrateDB query compiler emits the §5.2 CTE shape scoped
to the query's time range (the query window plays the bucket role; grouped variant groups the
outer SELECT by the grouping column in addition to per-rtId LOCF in the CTE). This is what lets
the energyiq bar chart (`GROUP BY Name`) switch from sample-weighted AVG to real burn time even
without provisioning a rollup.

Downsampling queries (AB#4233 bins) with TWA are a follow-up — the bin machine would need the CTE
per bin; not MVP.

### §6.3 Series resolver

No change needed: since the AB#4336 matching fix, `SeriesResolutionService` reports **all** stored
functions of a source path (`ResolutionRung.StoredFunctionsForSeries`), so a widget requesting
`requiredAggregation = TimeWeightedAvg` matches a TWA (or AVG+TWA multi-aggregation) rollup
automatically.

### §6.4 Frontend

- `npm run codegen` regenerates the enum (`CkRollupFunctionDto` / aggregation-type DTOs).
- Optional UI affordances (not required for the wire path): rollup-editor function dropdown +
  line-chart `requiredAggregation` option list gain a "Time-weighted average" entry.

## §7 Acceptance (from AB#4336)

- A rollup/aggregation over an event-based 0/100 signal reports the correct time-weighted duty
  cycle — verified against a **hand-computed interval sum** on the energyiq
  `LuminaireStatusArchive`.
- Grouped variant works (`GROUP BY Name`) so the "wo brennt am längsten Licht" bar chart can
  switch from sample-weighted AVG to real burn time.
- Recompute/backfill reproduces identical results (generation-safe).

## §8 Test matrix

Unit (`Runtime.Engine.Tests` / CrateDb SQL-builder tests, pure string assertions like the existing
`RollupAggregationSqlBuilder` tests):
- column resolution: default naming, explicit `TargetColumnName`, duplicate-column guard
- SQL shape: carry CTE + LEAD weighting; TWA-only; TWA mixed with AVG/MAX (carry row excluded from
  plain aggregates); rtId-scoped variant
- DDL typing: `_integral` double, `_duration` bigint

Integration (`AssetRepositoryServices.IntegrationTests`, CrateDB Testcontainer — reuse
`StreamDataFixture`):
- duty cycle correctness: synthetic 0/100 event series with known hand-computed interval sums,
  incl. carry across bucket boundary, bucket with zero in-bucket events (pure carry), archive
  start (partial coverage), NULL gap
- chained TWA rollup (1 h → 1 d) equals direct 1 d computation
- recompute over a TWA range reproduces forward-aggregated values bit-identically
  (extend `RollupRecomputeGenerationPointerTests`)
- grouped transient query over the raw archive matches the rollup result
- energyiq live validation on `LuminaireStatusArchive` (manual, part of AB#4327 board switch)

## §9 Decisions

| # | Decision | Resolution |
|---|---|---|
| D1 | Carry derivation | **Source-derived per bucket with bounded lookback** (default P35D, per-rollup `CarryLookback` attribute); no stored closing-value chain. (§5.3) |
| D2 | Dirty-window extension for carry | MVP: extend the dirty range by **one bucket** past the change; manual `recomputeArchive` covers pathological silent stretches. (§5.4) |
| D3 | Denominator | **Covered duration** (`_duration`), not bucket length — partial coverage is honest, never assumes a value before the first observation. (§3) |
| D4 | `StateDuration` function | Not MVP — derivable as `TWA × window (/100)`; dedicated comparison-value function is a follow-up. (§2) |
| D5 | Column naming | Short token: base `{source}_twavg`, columns `_integral` + `_duration`; wire alias suffix `_twavg`. (§4) |
| D6 | Mixed-spec statement | Single statement with carry-marker column; two-statement fallback if unwieldy. (§5.2) |
| D7 | Open-bucket refresh extrapolation | Last observation extrapolates to nominal `B_end`; revisit (cap at tick time) after validation. (§5.4) |

## §10 Open items / follow-ups

- `StateDuration(comparisonValue)` for enum-state signals ("time in state 2").
- TWA in downsampling (AB#4233) bins.
- Direct TWA in `AggregationSdQuery` / `GroupingAggregationSdQuery` over **raw** archives (§6.2)
  — currently guarded with a `NotSupportedException` in `QueryVariable`; queries must target a
  TWA rollup.
- Dirty-window carry extension (D2) — a retroactive source write does not yet extend the dirty
  range into the successor bucket's carry.
- `createRollupArchive` GraphQL input + `IRollupArchiveRuntimeStore.InsertAsync` parameter for
  `CarryLookbackMs` (today settable via generic entity update / ImportRt only).
- Cascade function-matching in the series resolver (pre-existing Phase-1 limitation — a TWA
  rollup-of-rollup is not matched; see `SeriesResolutionService` class remarks).
- Linear interpolation as an alternative weighting for continuous (non-state) signals.

## §11 Implementation map (AB#4336, initial increment)

| Piece | Where |
|---|---|
| `CkRollupFunction.TimeWeightedAvg` (=5) | `Runtime.Contracts/StreamData/CkRollupFunction.cs` + `StreamDataCkModel .../enums/ckRollupFunction.yaml` |
| `RollupArchive.CarryLookbackMs` (Int64, optional) | StreamDataCkModel 1.6.4 → **1.6.5** (additive); `RollupArchiveSnapshot.CarryLookback`; read-mapped in `MongoRollupArchiveRuntimeStore.MapToSnapshot` |
| `AggregationTypes.TimeWeightedAverage` (=5) | SystemCkModel 2.2.0 → **2.2.1** (additive); `Runtime.Contracts .../Query/AggregationFunction.cs`; SDK `AggregationTypesDto.TimeWeightedAverage` (=6) |
| Column derivation `{base}_integral` + `{base}_duration`, short token `twavg` | `RollupColumnGenerator` (engine) + `RollupAggregationColumns` (CrateDb, marker tokens `TW_INTEGRAL`/`TW_DURATION`) |
| DDL types (integral DOUBLE, duration BIGINT) | `RollupColumnTypeResolver` |
| LOCF forward-aggregation SQL (carry sub-select + `LEAD`, `is_carry` guard for plain aggs, windowed-source window-length weighting) | `RollupAggregationSqlBuilder` (`BuildWithLocfCarry` / `BuildStandard`); `carryLookback` wired from `CrateDbStreamDataRepository.AggregateBucketAsync` + `CrateDbArchiveRecomputeExecutor` |
| Read-path recombination `SUM(_integral)/NULLIF(SUM(_duration),0)`, alias `_twavg` | `RollupQueryAggregationResolver` (1-level) + `RollupChainAggregationResolver` (cascade via SUM-spec chaining; AVG pair never satisfies a TWA target) |
| GraphQL / query plumbing | `StreamDataGraphQlMapper.MapCkAggregationType` / `MapAggregationFunctionDto` (asset-repo); raw-archive TWA guarded in `QueryVariable` |
| Unit tests | `RollupColumnGeneratorTests` (engine); `RollupAggregationSqlBuilderTests`, `RollupColumnTypeResolverTests`, `RollupQueryAggregationResolverTests`, `RollupChainAggregationResolverTests` (mongodb) |
