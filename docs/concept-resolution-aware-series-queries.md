# Concept: Resolution-Aware Series Queries (Archive Auto-Selection)

**Status:** Draft / analysis. Core decisions resolved (§6); no implementation yet.
**Motivating use case:** "For metering point *X*, OBIS code *Y*, give me all energy data of the last year — I need ~600 points to render a chart."
**Related:**
- [`concept-simple-downsampling.md`](concept-simple-downsampling.md) (AB#4233) — reduce a query to exactly `limit` buckets sized to the chart. This concept is the layer that decides *which archive* the `limit` is applied to.
- [`concept-rollup-archives.md`](concept-rollup-archives.md) — the raw→1 min→1 h→1 d rollup ladder this concept selects from.
- [`concept-time-range-archives.md`](concept-time-range-archives.md) §7 — windowed full-containment semantics that constrain re-aggregation across the ladder.
- AB#4236 — MeshBoard stream-data widget scoping to a MeteringPoint via `StreamDataArguments.rtIds` (one-hop `ParentChild.inbound` from EnergyMeasurement).
- AB#4289 — reject rollup activation when the target bucket is finer than the source granularity (protects the ladder from nonsense rungs).

---

## §1 Goal

A caller (MeshBoard widget or a direct GraphQL/API consumer) wants a **logical time series** — identified by a business key such as *(metering point, OBIS code)* — over a **time range**, reduced to a **target number of points** suitable for display, **without knowing which physical archive** (raw, 15-min time-range, 1 h / 1 d rollup) actually holds the data at a usable resolution.

The problem splits cleanly into two layers, which today are at very different maturity:

| Layer | Question | Status today |
|---|---|---|
| **A — Addressing** | Which rows are "this series"? (rtIds + OBIS + which archive) | **Works** — AB#4236 + field filters |
| **B — Resolution** | From which archive / at which granularity, so I get ~N points at minimum scan cost? | **Missing** — no auto-selection exists |

This concept specifies layer B and formalises how it composes with layer A.

## §2 Non-Goals

- Defining new aggregation functions (the canonical five from `concept-rollup-archives.md` are sufficient).
- Cross-archive N:M merging into one virtual series (a metering point's registers stay separate series).
- Changing the downsampling SQL itself — AB#4233 already produces exactly `limit` bins via `generate_series` + `DATE_BIN`.
- Building rollups automatically. Which rungs exist is a provisioning/blueprint decision; this concept only *selects among existing rungs* and degrades gracefully when a rung is absent.

---

## §3 Current State (verified)

### §3.1 Addressing works end-to-end

The series key in a CrateDB archive table is **`rtid` (the EnergyMeasurement entity's rtId) + the `obis_code` column**. A metering point resolves to its series as:

```
MeteringPoint (rtId)
  └─ ParentChild.inbound → EnergyMeasurement (children)   ← one entity per register/series
        └─ attribute ObisCode == Y                        → the series rtId(s)
              └─ archive rows WHERE rtid IN (...) [AND obis_code = Y]
```

CK types (`octo-construction-kit/src/ConstructionKits/Octo.Sdk.Packages.Basic.Energy/ConstructionKit/types/`):
- `meteringPoint.yaml` — `MeteringPointNumber` (unique), `State`, `CarrierType`; `ParentChild` to `OperatingFacility`.
- `energyMeasurement.yaml` — `TimeRange{From,To}`, `Amount{Value,Unit}`, `ObisCode` (optional), `DataQuality`; `ParentChild` inbound to `MeteringPoint`; compound index on `(TimeRange.From, TimeRange.To, ObisCode)`.

Scoping mechanism (already shipped):
- `StreamDataArguments.RtIds` (`octo-asset-repo-services` `GraphQL/Types/Inputs/StreamDataArgumentsGraphType.cs`) — runtime override of the source rtIds; the AB#4236 hop populates it from a selected MeteringPoint.
- `fieldFilter { attributePath: "obisCode", EQUALS, Y }` — merged AND with persisted filters. Redundant when there is exactly one EnergyMeasurement entity per OBIS (then `RtIds` already isolates the series), but useful for discovery.
- Archive discovery: the energy archive is the one whose `TargetCkTypeId = Basic.Energy/EnergyMeasurement`, enumerable via `get_available_archives` / `get_available_archive_paths`.

### §3.2 The point-count primitive exists (AB#4233)

`query_stream_data_downsampling` / `QueryMode = DOWNSAMPLING` takes `from`, `to`, `limit`; the server computes `bucketSize = (to - from) / limit` and returns exactly `limit` bins (empty bins → `NULL`). The bin machine (`Runtime.Engine.CrateDb/QueryBuilder/CrateQueryCompiler.cs`, `CompileDownsamplingQuery`) uses `generate_series` + `DATE_BIN` and groups by `bins.ts` (per AB#4233 D4: additionally by source `rtId`). **So "~600 points" is already solvable on any single archive** — the open question is only *which* archive.

### §3.3 What is missing

| Gap | Evidence |
|---|---|
| No resolver `(family, from, to, targetPoints) → archiveRtId` | No `select_best_rollup*` tool; callers pass an explicit `archiveRtId`. |
| No virtual routing | One CrateDB table per archive; the caller must name the raw/rollup archive rtId directly. |
| Rollup bucket sizes not available in one call | `list_rollups_for_archive` lists rollups but not their `BucketSizeMs`; that needs a per-rollup `get_rollup_query_metadata`. |
| Widgets never send `limit`/`queryMode` | MeshBoard chart widgets emit only `{from, to, rtIds}` (`octo-meshboard` `query-executor.service.ts`); no `targetPointCount`/`preferredRollup` config field exists. |
| No source-granularity metadata to reason about | `TimeRangeArchive.Period` is advisory-only and unenforced (see AB#4289). |

---

## §4 The core idea: a resolution ladder + a selector

### §4.1 Archive family / resolution ladder

A raw (or 15-min time-range) archive **plus its rollups** form a **resolution ladder** for the *same logical series*: `raw(15 min) → rollup(1 h) → rollup(1 d)`. The rungs are discoverable — `list_rollups_for_archive(sourceArchiveRtId)` gives the direct children, and cascade chains are walked by the existing `RollupLogicalPathResolver` / `RollupDependencyGraph`. The series key (`rtid` + `obis_code`) is preserved on every rung.

The **native grain** of each rung is derivable without new metadata:
- Rollup rung → its `BucketSizeMs` (`RollupArchiveSnapshot.BucketSize`).
- Time-range base rung → its `Period` (advisory today; AB#4289 proposes making it authoritative).
- Raw base rung → undefined (irregular event data — treated as "finest available").

### §4.2 Selection rule

Given the bound time range `[from, to)` (`timespan = to − from`) and a `targetPoints` (default ~600):

```
reducer     = requiredAggregation                     (O2, caller-supplied; never guessed)
eligible    = compatible rollup rungs                 (stored functions of the path CONTAIN the
                                                       reducer, grain known — a rollup may carry
                                                       several aggregations on one path, AB#4188)
ideal       = timespan / targetPoints
baseNative  = timespan / basePeriod                   (null if the base grain is undeclared)

1. fineEnough = eligible rungs with grain <= ideal
   if fineEnough → the COARSEST fineEnough rung, downsample to targetPoints        (Ok)
2. else if baseNative <= targetPoints → the BASE rung unreduced (raw fits)          (Ok)
       # checked BEFORE ResolutionLimited: raw is finer and delivers more points
       # than any coarser rollup when it already fits (e.g. 1 day of 15-min = 96
       # points, not the hourly rollup's 24).
3. else if eligible non-empty → the FINEST eligible rung's native buckets           (ResolutionLimited, O4)
4. else                       # no compatible rollup and the base does not fit
       base grain unknown → return base                                            (UnknownBaseGrain)
       else               → return base raw, refuse to reduce                       (NoSuitableRollup, O2-followup)
   # no base rung at all → EmptyLadder
then = downsample the chosen rung with limit = targetPoints   (AB#4233 path)
```

Rationale: the coarsest sufficient rung minimises the CrateDB scan while the `limit`-downsampling on top lands the exact point count. Picking a rung *finer* than necessary only inflates the scan for an identical picture — **except** when no rollup is fine enough yet the raw base already fits within the target: then the base (finest, most points) is preferred over a coarser ResolutionLimited rollup. The resolver **never silently produces a wrong or degraded result** — it either reduces correctly, returns raw when it fits, or returns a truthful signal (`no-suitable-rollup` / `resolution-limited: actual/target`) the caller can surface.

### §4.3 Worked example (1 year, targetPoints = 600)

`timespan = 365 d`, ideal bucket ≈ `365 d / 600 ≈ 14.6 h`.

| Rung | grain | nativePoints | Decision |
|---|---|---|---|
| raw | 15 min | 35 040 | too fine — 35 k-row scan for no visual gain |
| rollup | 1 h | 8 760 | **chosen** — ≥ 600; downsample → 600 (scans 8 760/series) |
| rollup | 1 d | 365 | too coarse (< 600) — would cap at 365 points |

If only the daily rung existed, the selector would fall back to it and return 365 points (honest under-delivery), and a consumer wanting more would have to fall through to the 1 h/raw rung.

### §4.4 Aggregation semantics per series (correctness constraint)

Re-aggregating across the ladder is **not** "AVG of AVGs". The reducer must match the physical quantity:
- **Energy (kWh, meter register)** is additive → **SUM** across sub-buckets.
- **Power / instantaneous** values → **AVG via `SUM(x_sum)/SUM(x_count)`**, never `AVG(x_avg)`.

`concept-rollup-archives.md` already carries `sum`+`count` for exactly this reason (chained rollups re-aggregate `SUM(x_sum), SUM(x_count)`). The selector/query must therefore know the intended reducer **per series/OBIS**.

**Decision (O2): the caller supplies the intended aggregation function (`requiredAggregation`), and it is never guessed.** The reducer semantics come from the caller — the widget/consumer knows the series is energy (`SUM`), demand (`MAX`), etc. (derived UI-side from the OBIS code / field). The resolver then uses that function two ways:
- **On a rollup rung:** it must match the function the rollup declared for that column in `Aggregations[]`; a rung whose stored function differs is not a valid reduction source (see O2-followup).
- **On the raw/base rung:** the raw archive carries no stored function, so `requiredAggregation` is the *only* place the semantics can come from. Its role here is to let the resolver **know** that AB#4233's numeric default (MIN/MAX/**AVG** envelope) would be *wrong* for this series (it would average kWh windows instead of summing them) — which is precisely why the resolver **refuses** rather than silently mis-reducing (O2-followup).

No new model attribute, no OBIS→function registry, no unit heuristic (which would conflate max-demand and average power, both `kW`). An OBIS→function registry, or inferring the hint from an existing rollup's stored function, remain optional future conveniences — but the *contract* is: `requiredAggregation` is a **required** input for any reducing query.

**Load-bearing consequence:** a reduced-resolution view of an **additive** series is only served **through a rollup** whose stored function matches `requiredAggregation`. When such a `SUM` rollup exists the resolver picks it (far less scan). When none exists, the resolver does **not** reduce the raw archive itself (we deliberately did not extend AB#4233's raw path to accept an arbitrary reducer, O2-followup) — it returns the raw rung plus a `no-suitable-rollup` signal so the caller sees exactly why. `requiredAggregation` is what makes that refusal *informed* instead of a silent AVG-of-kWh curve. Auto-provisioning a `SUM` rollup for additive series is the clean follow-up so the refuse path rarely triggers in practice.

Grounding: `EnergyMeasurement` (`octo-construction-kit/.../Basic.Energy/.../types/energyMeasurement.yaml`) carries `Amount{Value,Unit}` **and its own `TimeRange{From,To}`** — every row is already a windowed, additive quantity. That is why the base rung is naturally a `TimeRangeArchive` whose `Period` equals the measurement window (§4.1 / O5), and why SUM is the correct cross-rung reducer for energy.

---

## §5 Proposed design

### §5.1 Where the selector lives — server-side (recommended)

| Option | Pro | Con |
|---|---|---|
| **A. Server-side resolver** (endpoint/tool: `(baseArchiveRtId \| targetCkTypeId, from, to, targetPoints, rtIds?, obis?) → {archiveRtId, effectiveBucket, points}`) | Single source of truth; MeshBoard **and** direct GraphQL/API consumers benefit; ladder math + agg policy centralised | New surface to build and version |
| **B. Client-side resolver** (each frontend walks `list_rollups_for_archive` + metadata and picks) | No backend change | Re-implemented per consumer (MeshBoard, Power BI, API users); agg policy duplicated and drifts |

**Decision (O1): server-side, shape A1.** The resolver is a backend endpoint/tool that returns the chosen `{archiveRtId, effectiveBucket, points}`; the caller then issues the existing AB#4233 downsampling query against that rtId. Minimal, explicit, easy to cache, and a single source of truth for MeshBoard **and** direct GraphQL/API/Power-BI consumers. The whole value proposition — "ask for a metering point + OBIS + point count and don't think about archives" — collapses if every consumer reimplements the ladder walk. The virtual-archive variant (A2 — caller queries a logical archive at a requested resolution, backend routes to the rung) is deferred: nicer API, but needs a routing layer the query engine does not have today.

### §5.2 Supporting metadata

- Extend `list_rollups_for_archive` (or a new `get_archive_family_metadata`) to return, in **one** call, each rung's `archiveRtId`, `grain` (`BucketSizeMs` / `Period`), its declared aggregation function per column (for O2 propagation), and its logical source paths — so the resolver does not fan out one `get_rollup_query_metadata` per rung.
- Depends on AB#4289 making base-archive grain authoritative; until then the resolver can only reason about rollup rungs (which always carry `BucketSize`) and treats the base rung as "finest, unknown grain".

### §5.2a Reference time zone (Decision O6)

A **reference time zone** is added to the archive/rollup definition so that calendar-aligned coarse rungs (`BucketAlignment.CalendarDay` / `CalendarMonth`, see `Runtime.Engine/StreamData/BucketBoundary.cs`) fall on DST-correct local-day / local-month boundaries rather than UTC. This is required as soon as a tenant spans time zones (e.g. 15-min electricity in one country, 1-h gas in another). Fixed-size sub-day rungs (1 h and finer) are unaffected and need no TZ. The TZ is a property of the series' physical location — provisioned alongside the archive, defaulting to the tenant's configured zone.

### §5.3 MeshBoard integration (extends AB#4236 / AB#4233)

- Add optional widget config: `targetPointCount` (default from pixel width, per AB#4233 D9) and optionally `preferredResolution` / `pinnedArchiveRtId` for power users.
- The widget already resolves `{from, to}` (`resolveStreamDataTimeArgs`) and `rtIds` (`resolveStreamDataRtIds`); it additionally calls the resolver (§5.1) and then issues the AB#4233 downsampling query with `queryMode = DOWNSAMPLING`, `limit = targetPointCount` against the resolved `archiveRtId`.

### §5.4 End-to-end query flow

```
1. Addressing (layer A, exists):
   MeteringPoint X → ParentChild.inbound EnergyMeasurement → filter ObisCode == Y → rtIds[]
2. Resolution (layer B, new):
   resolver(baseArchiveRtId, from, to, targetPoints=600, rtIds, obis=Y)
     → { archiveRtId: <1h-rollup>, effectiveBucket: ~14.6h, points: 600 }
3. Execution (exists, AB#4233):
   downsampling query on archiveRtId, rtIds, obis filter, from/to, limit=600,
   reducer per §4.4 (SUM for energy) → 600 bins per series
```

---

## §6 Decisions (all resolved)

| # | Decision | Resolution |
|---|---|---|
| **O1** | Selector location | **Server-side, shape A1** — resolver returns `{archiveRtId, effectiveBucket, points}`; caller runs the AB#4233 query. Virtual archive (A2) deferred. (§5.1) |
| **O2** | Per-series aggregation function | **Caller supplies `requiredAggregation`** (required input; UI derives it from OBIS/field — energy=`SUM`, demand=`MAX`). Never guessed. On a rollup rung it must match the rollup's stored `Aggregations[]` function; on the raw rung it is the only semantic source. No new attribute, no OBIS registry, no unit heuristic. (§4.4) |
| **O2-followup** | Additive series without a rollup | **Refuse + signal** — when no compatible (`SUM`) rollup exists, the resolver does not reduce; it returns the finest raw rung plus a `no-suitable-rollup` signal. No silently-wrong (AVG-of-kWh) curves. Auto-provisioning a SUM rollup (runner-up) stays a future enhancement. (§4.2) |
| **O3** | `targetPoints` default | **Pixel-driven** per AB#4233 D9 `clamp(width/pxPerBin, 50, 4000)`, business fallback ~600. |
| **O4** | Under-delivery contract | **Fewer points + signal** — deliver the available points (e.g. 365) and a `resolution-limited: actual/target` field so the frontend can flag the coarser resolution. Does **not** silently fall through to a finer, costlier rung. (§4.2) |
| **O5** | Grain of the base rung | **`Period` = the `EnergyMeasurement` window**, made authoritative via AB#4289; base rung then participates in the ≥-target comparison. |
| **O6** | Time zone for coarse rungs | **Add a reference-TZ field now** to archive/rollup for DST-correct `CalendarDay/Month` buckets. (§5.2a) |
| **O7** | Caching | Cache resolver output per `(family, bucketed timespan, targetPoints)` to avoid re-walking the ladder per widget refresh. |

Remaining choices (signal field names, exact resolver endpoint shape, TZ field name/default) are implementation details for the design/work-item stage.

---

## §7 Relation to existing work

- **AB#4233** provides the exact-N reduction; this concept only chooses the archive it runs against.
- **AB#4236** provides the metering-point → rtIds addressing (layer A).
- **AB#4289** is the guardrail *beneath* the ladder: it stops finer-than-source rollups that would corrupt rung selection, and its authoritative-`Period` proposal (O5) is what lets the base rung participate in selection.
- **`concept-rollup-archives.md`** provides the rungs and the `sum`+`count` columns that make correct cross-rung re-aggregation (§4.4) possible.
