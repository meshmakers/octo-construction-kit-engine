# Concept: Timezone-Aware Rollup Queries (Civil-Time Ranges Across Mixed-Timezone Metering Points)

**Status:** Draft / analysis. Design decisions resolved (§9); write-path primitives already shipped, read-path unimplemented (§3).
**Work item:** AB#4190 (parent epic AB#4073 — voest M1).
**Motivating use case:** An operator asks a MeshBoard "what were the values *yesterday* / *this week* / *last month*?" and gets the answer for the **local civil day/week/month** of a chosen time zone — not for a UTC day shifted against it. Becomes critical the moment one chart compares metering points across time zones (a Linz site in `Europe/Vienna` next to a Portugal site in `Europe/Lisbon`): a single UTC bucket boundary cannot mean "yesterday" for both.
**Related:**
- [`concept-resolution-aware-series-queries.md`](concept-resolution-aware-series-queries.md) (AB#4290) — the resolution ladder + selector this concept extends. **Decision O6 already reserved a reference time zone on the archive/rollup; this concept turns that reservation into an end-to-end read path.**
- [`concept-rollup-archives.md`](concept-rollup-archives.md) — the `CalendarDay/CalendarMonth/CalendarYear/Iso8601Week` bucket alignments this concept aligns to civil boundaries.
- [`concept-simple-downsampling.md`](concept-simple-downsampling.md) (AB#4233) — the UTC `DATE_BIN` / `generate_series` reducer whose civil-time boundaries this concept supplies.
- AB#4184 — rollup recompute; AB#4188 — multi-aggregations; AB#4189 — computed columns. Compatibility required, no changes there (§8).
- AB#4187 — MeshBoard stream-data widgets, the UI consumer (§7).

---

## §1 Goal

Data is stored in **UTC** and stays that way. Queries must nonetheless be answerable in **civil time** for a chosen IANA time zone:

1. A query can ask for a **relative range** (*today / yesterday / this week / last 7 days / this month / …*) **plus a time zone**, and the read path resolves it to the correct UTC window for that zone's local day/week/month boundaries — **including across DST transitions**.
2. Comparing metering points in **different time zones** in one chart produces correct **local-day buckets per the agreed policy** (query-wide vs. per-series).
3. DST transitions produce **no missing or duplicated buckets** (local days are 23 / 24 / 25 h).
4. MeshBoards consume the **same primitives** — no parallel timezone path on the UI side.

The problem separates into two concerns that must stay separate (work-item decision #6):

| Concern | Question | Layer | Status today |
|---|---|---|---|
| **Computation** | Which UTC window / bucket boundaries *are* "yesterday in `Europe/Vienna`"? | query / backend | **partial** — write path aligns, read path does not (§3) |
| **Display** | How is the X axis rendered in local time? | UI | **exists** — `MeshBoardConfig.timeZoneMode` + `meshboard-datetime.ts` |

This concept specifies the **computation** concern and reaffirms the **display** concern is orthogonal and already solved.

## §2 Non-Goals

- Changing storage: rows stay UTC; no double-bucketing (work-item decision #4).
- Fixed UTC offsets: **IANA names only** — offsets are wrong twice a year (work-item decision #7).
- Calendar quirks beyond time zones (fiscal years, custom shift calendars).
- Per-user timezone preferences in the UI — a separate concern.
- Backfilling timezone metadata onto historical rows.
- New aggregation functions or new downsampling SQL — AB#4233 already produces exactly `limit` bins.

---

## §3 Current State (verified 2026-07-03)

### §3.1 Write path (aggregation / bucketing) — timezone-aware, DONE

- **Archive definition carries an IANA zone.** `ReferenceTimeZone` (nullable, e.g. `"Europe/Vienna"`; `null` ⇒ UTC) on `CreateRollupArchiveInputDto.cs:33`. Applies to calendar alignments only.
- **DST-correct bucket boundaries.** `BucketBoundary` (`Runtime.Engine/StreamData/BucketBoundary.cs`) — `ResolveZone`, `AlignDown`, `NextBucketEnd`, `InitialWatermark` snap calendar buckets to local wall-clock boundaries when a zone is supplied. DST forward/backward covered by `BucketBoundaryTests.cs` (Vienna, Mar 29 / Oct 25).
- **Used in aggregation, activation and recompute.** `RollupOrchestrator.cs:178`, `ArchiveLifecycleService.cs:410`, `CrateDbArchiveRecomputeExecutor.cs:106`. Buckets are computed at .NET level and written as UTC literals — CrateDB SQL stays UTC (`RollupAggregationSqlBuilder.cs:80-81`). Consistent with §2 (storage stays UTC).

**Consequence:** a `CalendarDay` rollup provisioned with `ReferenceTimeZone = Europe/Vienna` **already stores DST-correct local-day buckets**. The data to answer "yesterday in Vienna" from pre-aggregated rollups exists today.

### §3.2 Read path (query / selection) — UTC-only, NOT implemented

| Gap | Evidence |
|---|---|
| The resolution ladder never carries the zone | `SeriesResolutionService.cs:83-88` builds each `ResolutionRung` **without** setting `ReferenceTimeZone`; it is always `null` ⇒ UTC. |
| Explicitly deferred | `SeriesResolutionService.cs:127` comment: *"UTC in Phase 1; reference-time-zone alignment lands in Phase 4 / O6"*. The field exists and is documented but unset (`ResolutionRung.cs:44`, "AB#4290 Phase 4 / decision O6"). |
| No query-time zone parameter | `SeriesResolutionRequest.cs:38-59` (`From/To/TargetPoints/RequiredAggregation/SourcePath/RtIds/ObisFilter`), `ResolveSeriesQueryInputDto.cs:12-22`, and `StreamDataArgumentsGraphType.cs:55-88` (`From/To/Interval/Limit/QueryMode/RtIds`) — **none carry a time zone**. All `From/To` are absolute UTC `DateTime`. |
| Relative ranges resolved client-side against browser-local, not an explicit IANA zone | `time-range-picker.models.ts:167-309` (`TimeRangeUtils`) resolves *yesterday/this week/…* with a binary `zone: 'local' \| 'utc'` using `new Date(y,m,d)` (browser-local) or `Date.UTC(...)`. It **cannot** compute "yesterday in `Europe/Lisbon`" while the browser is in Vienna — the exact "off by hours" failure in the acceptance criteria. |

**Verdict:** the DST-correct bucketing exists; what is missing is (a) threading a time zone through the read path so calendar rungs are selected/aligned in that zone, and (b) computing relative ranges in an **explicit IANA zone** rather than the browser's local zone.

---

## §4 Design decision #1 — where the time zone is supplied

Two independent inputs, both needed:

1. **Archive/rollup `ReferenceTimeZone`** *(exists, O6)* — a property of the series' **physical location**, provisioned with the archive, defaulting to the tenant zone. It is what makes the **stored** calendar buckets DST-correct, and it is the natural **per-series** zone for mixed-timezone comparison (§5).
2. **Query time zone** *(new)* — an IANA name supplied **on the query**, used to (a) resolve relative ranges to a UTC window and (b) pick/align the calendar rung for a **query-wide** comparison.

**Decision (T1): combination — query zone drives resolution; archive zone is the per-series default.** The caller always supplies a query zone (explicit input, never inferred from the server clock). Per-row inference from the metering point / operating facility is realised **through the archive's `ReferenceTimeZone`** (already provisioned per physical location) and selected by the comparison policy in §5 — no new per-row lookup path. voest M1 is single-zone (`Europe/Vienna`) but the query zone is an explicit input from day one, so cross-site (Portugal) works without a redesign.

## §5 Design decision #2 — comparison across time zones (policy)

When one query covers metering points in different zones, bucket boundaries can be resolved two ways:

| Policy | "Yesterday" means | Bucket boundary source | Use case |
|---|---|---|---|
| **Query-wide** (default) | one civil day in the query zone, applied to every series | the **query zone** | one operator, one locale, comparing sites "as I experience the day" |
| **Per-series** (override) | each point's *own* local yesterday | each series' archive **`ReferenceTimeZone`** | "each site's local day" — a Vienna site and a Lisbon site each on their own midnight |

**Decision (T2): default query-wide, per-series is an explicit override.** Both are valid; query-wide is the least-surprising default for a single operator and is what M1 needs. Per-series reuses the existing per-rung fan-out (AB#4290 already fans out one downsampling query **per source rtId**), so each series simply aligns to its own rung `ReferenceTimeZone` instead of the query zone. The policy is a single enum on the query (`PerQuery` | `PerSeries`).

**Boundary-consistency invariant.** The `[from, to)` window and the rung's bucket boundaries must be computed against the **same** zone, or the window edges land mid-bucket. Under query-wide both use the query zone. Under per-series, the relative range is resolved **once per series** in that series' zone. This is the load-bearing rule that keeps side-by-side charts from being "off by hours".

## §6 Design decisions #3–#5 — DST, storage, pre-aggregation alignment

### §6.1 DST (#3)

A civil day is 23 / 24 / 25 h across a transition. Boundaries are always computed via `BucketBoundary` (the write-path primitive, already DST-tested), so a civil-day bucket is simply wider/narrower in wall-clock terms — **never split, never duplicated**. The read path inherits this for free by routing civil-day-and-coarser queries to a calendar-aligned rung rather than re-deriving boundaries with fixed arithmetic.

### §6.2 Storage (#4)

Unchanged: UTC only, no double-bucketing. The query zone and archive zone are metadata; neither rewrites a row.

### §6.3 Pre-aggregation alignment (#5) — the crux

How is a civil-day query assembled from pre-bucketed rollups **without re-reading raw**?

- **Civil day and coarser** (`CalendarDay/Month/Year`, `Iso8601Week`): route to the calendar-aligned rung whose `ReferenceTimeZone` matches the resolution zone. Its buckets **already are** DST-correct local buckets (§3.1) — the query reads them directly, `1 bucket = 1 civil unit`. This is why the boundary-consistency invariant (§5) demands query-zone == rung-zone for query-wide.
- **Sub-day, fixed-size** (1 h and finer, `BucketAlignment.FixedSize`): unaffected by time zone. `DATE_BIN` bins are evenly spaced from a UTC epoch and only coincide with civil boundaries when the offset is a whole number of hours — which DST breaks. **Decision (T3): sub-day queries stay UTC-computed; the zone affects only the X-axis labels (display, §7), not the bins.** Civil-boundary semantics are a property of **calendar-aligned rungs**; do not attempt DST-correct sub-day binning in SQL.
- **Consequence for the ladder:** a civil-time relative range should prefer a calendar rung of matching zone. If none exists (e.g. only fixed-size hourly rollups are provisioned), the resolver returns the fixed-size rung with a **truthful signal** (reusing AB#4290's `resolution-limited` / `no-suitable-rollup` signals) rather than silently mis-aligning. Provisioning a `CalendarDay` rollup per reference zone is the clean enablement, exactly as O6 anticipated.

---

## §7 Design decision #6 — display vs. computation; MeshBoard integration

**Computation** (this concept, backend): resolve relative range → UTC window in the query/series zone; select + align the calendar rung. **Display** (UI, already exists): render the X axis in local time via `MeshBoardConfig.timeZoneMode` + `utils/meshboard-datetime.ts`. These stay separate (work-item decision #6).

**One primitive, no parallel UI path (work-item requirement).** The single primitive threaded end-to-end is **an IANA time-zone string**:

1. **Replace the binary `timeZoneMode: 'local' \| 'utc'`** (`MeshBoardConfig`) with an explicit **IANA zone** (defaulting to the browser zone for backward compatibility). This is the same string used for both computation and display.
2. **`TimeRangeUtils` gains true IANA resolution.** Today it uses `new Date(y,m,d)` (browser-local) — it must compute civil boundaries for an *arbitrary* IANA zone (via `Intl`/`Temporal`/`date-fns-tz`), so "yesterday in `Europe/Lisbon`" is correct while the browser is in Vienna. This is the concrete fix for the "off by hours" acceptance criterion.
3. **The widget sends UTC `From/To` + the IANA zone.** `line-chart-widget.component.ts:601/758` already calls `resolveStreamDataTimeArgs(...)` and (resolution-aware path, `:662-686`) `resolveSeriesQuery(...)`. It additionally passes the IANA `timeZone` (and, for cross-site widgets, the `PerSeries` policy). No second timezone code path — the UI just forwards the string it already owns.

## §8 Composition with related work (unchanged)

- **AB#4184 (recompute):** recompute already enumerates buckets via `BucketBoundary` with the archive zone (`CrateDbArchiveRecomputeExecutor.cs:106`). Timezone semantics are a read-path concern and do **not** touch the recompute model.
- **AB#4188 (multi-aggregations):** the zone aligns buckets; it is orthogonal to *how many* aggregations a bucket carries. No interaction.
- **AB#4189 (computed columns):** a timestamp-derived computed column evaluates per row over UTC storage; civil-time bucketing happens above it. Compatible.

---

## §9 Decisions (resolved)

| # | Work-item decision | Resolution |
|---|---|---|
| **T1** | #1 Where the zone is supplied | **Combination.** Query zone (new, explicit, required) drives range resolution + query-wide alignment; archive `ReferenceTimeZone` (exists, O6) is the per-series default. No new per-row lookup. (§4) |
| **T2** | #2 Cross-timezone comparison | **Default query-wide; per-series override** via a policy enum. Per-series reuses the existing per-rtId fan-out and each rung's `ReferenceTimeZone`. (§5) |
| **T3** | #3 DST + #5 pre-aggregation | **Civil-day-and-coarser → calendar rung of matching zone** (buckets already DST-correct, read directly). **Sub-day → UTC bins, zone affects labels only.** No DST-correct sub-day SQL. Missing calendar rung ⇒ truthful signal, never silent mis-alignment. (§6) |
| **T4** | #4 Storage | **UTC only, no double-bucketing.** Zone is query-time metadata. (§6.2) |
| **T5** | #6 Display vs computation | **Separate.** Computation = backend window/bucket alignment; display = existing `timeZoneMode`/`meshboard-datetime.ts`. (§7) |
| **T6** | #7 Vocabulary | **IANA names only**, validated via `TimeZoneInfo.FindSystemTimeZoneById` (backend) / `Intl` (frontend). No fixed offsets. |
| **T7** | Boundary consistency (derived) | **Resolve `[from,to)` and rung buckets against the same zone** — query zone (query-wide) or per-series zone (per-series), computed once. (§5) |

## §10 Implementation outline (the code gap)

Grounded, minimal, mostly wiring — the DST math already exists.

**Backend (`octo-construction-kit-engine` + `octo-asset-repo-services`)**
1. Thread the zone into the ladder: set `ReferenceTimeZone` on each `ResolutionRung` in `SeriesResolutionService.cs:83-88` — from the query zone (query-wide) or the rollup's own zone (per-series). This alone activates the dormant `EffectiveGrainMs` path (`SeriesResolutionService.cs:137`).
2. Add `QueryTimeZone` (IANA) + `ComparisonPolicy` (`PerQuery`|`PerSeries`) to `SeriesResolutionRequest.cs` and `ResolveSeriesQueryInputDto.cs`; add `TimeZone` to `StreamDataArgumentsGraphType.cs`. Validate via `TimeZoneInfo.FindSystemTimeZoneById`; reject unknown IDs with a helpful error.
3. Prefer a matching-zone calendar rung in the selector; emit the existing `resolution-limited`/`no-suitable-rollup` signal when only fixed-size rungs exist for a civil-day request.

**Frontend (`octo-frontend-libraries` / `octo-meshboard`)**
4. Replace `timeZoneMode: 'local'|'utc'` with an IANA zone on `MeshBoardConfig` (default = browser zone; migrate old values `local→resolved browser zone`, `utc→'UTC'`).
5. Give `TimeRangeUtils` true IANA resolution (§7.2) and forward the IANA `timeZone` (+ policy) from the line-chart widget into `resolveSeriesQuery` / `StreamDataArguments`.

**Tests (acceptance criteria)**
6. Civil-day boundaries; DST forward + backward (extend `BucketBoundaryTests` into the read path); mixed-timezone `PerSeries` (Vienna + Lisbon "yesterday" side by side, neither off by hours); alignment with multi-aggregate rollups (AB#4188).

## §11 Relation to existing work

- **AB#4290 O6** reserved `ReferenceTimeZone` on the archive/rollup and on `ResolutionRung` explicitly for this ("Phase 4"). This concept is that Phase 4: it activates the field on the read path and adds the query-time zone + comparison policy on top.
- **AB#4233** supplies the exact-N reducer; this concept only supplies its civil-time window and picks a calendar rung to run it against.
- **AB#4236 / AB#4290** supply the addressing and per-rtId fan-out the per-series policy reuses unchanged.
