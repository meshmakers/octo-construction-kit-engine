# Concept: Multi-Source Rollup Archives, Validity Spans and Archive Coverage

**Work item:** AB#5157 · **CK model:** `System.StreamData` 1.8.0 (non-breaking, no migration)

A rollup archive used to aggregate from exactly **one** source archive. Since
`System.StreamData` 1.8.0 it declares a **list of time-disjoint sources**, each with an optional
validity span, so that history imported at a coarser granularity and data ingested natively at a
finer granularity end up in **one continuous rollup ladder**. Nothing changes on the read side:
consumers still query one archive per request.

Related concepts: [rollup archives](concept-rollup-archives.md) ·
[rollup recompute](concept-rollup-recompute.md) ·
[time-range archives](concept-time-range-archives.md) ·
[resolution-aware series queries](concept-resolution-aware-series-queries.md) ·
[time-weighted aggregation](concept-time-weighted-aggregation.md)

---

## §1 Motivation

A tenant that has been metering for years typically holds its history in two different shapes:

- **legacy history** — imported once, already aggregated (quarterly or daily totals from a billing
  system, an EDA export, a spreadsheet), stored in a `CkTimeRangeArchive`;
- **native data** — ingested continuously at full resolution (15-minute values) into a
  `CkRawArchive`, from a cutover date onwards.

Before AB#5157 a rollup could only follow one of the two, so the analytical ladder (daily →
monthly → yearly) either stopped at the cutover or had to be duplicated per source, and every
consumer had to stitch the two series together itself.

With a multi-source rollup the ladder is declared once:

```
legacy quarterly time-range archive  ──[ ValidTo   = 2025-10-01 ]──┐
                                                                   ├──►  CalendarQuarter rollup ──► yearly rollup
15-minute raw archive ──► monthly rollup ──[ ValidFrom = 2025-10-01 ]──┘
```

Every bucket before the cutover is served by the legacy source, every bucket from the cutover
onwards by the native ladder, and the rollup above sees one uninterrupted series. Rollups stacked
on top of it need no knowledge of the split at all.

### History at the *same* granularity needs no rollup change

If the imported history has the **same granularity as the native data** — a replay of 15-minute
values into a 15-minute raw archive, for example — there is nothing to declare here. The history is
written **directly into the raw archive** through the ordinary ingest path, the existing rollups
above it pick it up, and a `rewindRollupWatermark` + backfill recomputes the affected buckets.
Multi-source declarations are only needed when the history arrives at a **coarser** granularity
than the native data and therefore has to live in its own archive.

---

## §2 Model — `Sources` and the deprecated scalar

`CkRollupArchive` gains one attribute in `System.StreamData` 1.8.0:

| Attribute | Type | Notes |
|---|---|---|
| `Sources` | `RecordArray<CkRollupSourceReference>` | The declared source archives with their validity spans. Optional in the model, **required in practice**: a rollup without sources cannot be activated. |
| `SourceArchiveRtId` | `OctoObjectId?` | **DEPRECATED since 1.8.0.** Flipped from required to optional. Kept so existing entities, seeds and blueprints keep importing unchanged. |

`CkRollupSourceReference` (new record):

| Field | Type | Meaning |
|---|---|---|
| `SourceArchiveRtId` | `String` (required) | The source archive — raw, time-range, or another rollup for a chained ladder. Each archive may appear **at most once** per rollup. |
| `ValidFrom` | `DateTime?` | **Inclusive** start of the span. `null` ⇒ open start. |
| `ValidTo` | `DateTime?` | **Exclusive** end of the span. `null` ⇒ open end. |

The spans are **half-open**, `[ValidFrom, ValidTo)`. Two references that abut at the same timestamp
are therefore disjoint, and the bucket that *starts* at the cutover belongs to the **later** source.
A reference with neither bound is *unbounded* and covers every bucket.

The engine works exclusively on the normalised contract types
`RollupArchiveSnapshot.Sources` / `RollupSourceReference`
(`src/Runtime.Contracts/StreamData/RollupArchiveSnapshot.cs`), whose helpers carry the rules:

- `RollupSourceReference.IsUnbounded` — neither bound set.
- `RollupSourceReference.Contains(bucketStart, bucketEnd)` — the **whole** bucket lies inside the
  span; a bucket that only partially overlaps is *not* covered, so no bucket ever mixes two sources.
- `RollupSourceReference.Clip(from, to)` — the intersection of a range with the span, `null` when
  empty. Used by the recompute path (§6).
- `RollupArchiveSnapshot.SourceForBucket(bucketStart, bucketEnd)` — the authoritative source of a
  bucket, or `null` when no span covers it.
- `RollupArchiveSnapshot.HasSource(rtId)` — membership, regardless of span; the test behind the
  dependency graph, the delete guard and `rollupsFor`.
- `RollupArchiveSnapshot.SingleUnboundedSourceRtId` — non-`null` **only** when the rollup declares
  exactly one source and that source is unbounded. This is the value every read surface that still
  exposes the deprecated `sourceArchiveRtId` field reports; it is `null` for every genuinely
  multi-source rollup and for a single source that carries a span.

### The platform never writes the deprecated scalar

New rollups are stored with `Sources` **only**. `IRollupArchiveRuntimeStore.InsertAsync` takes the
source list and writes `Sources`; no code path in the platform writes `SourceArchiveRtId` any more.
The scalar exists solely so that entities written before 1.8.0 — and hand-written imports that
still use it — keep working. `createRollupArchive` still *accepts* it as a deprecated input, but
maps it to a single unbounded source before storing.

---

## §3 Normalisation — one place, on the read side

There is no shared write path across `createRollupArchive`, `ImportRt` and the generic CK mutation,
so both storage forms are reconciled in exactly **one** place: the **snapshot mapping on the read
side** of the runtime store (`MongoRollupArchiveRuntimeStore.NormaliseSources` in
octo-construction-kit-engine-mongodb, called only by `MapToSnapshot` and
`CountActiveRollupsForSourceAsync`).

| Stored `Sources` | Stored `SourceArchiveRtId` | Normalised snapshot |
|---|---|---|
| non-empty | unset | `Sources` as declared |
| empty | set | exactly one **unbounded** reference to that archive |
| non-empty, exactly one unbounded reference with the same id | set, same id | `Sources` as declared — the two agree, no conflict |
| non-empty, anything else | set | `Sources` kept **and** `ConflictingSourceArchiveRtId` = the scalar |
| empty | unset | empty list — rejected at activation |

**Rules that follow from this:**

- Every engine consumer works on the normalised snapshot alone. Nothing outside the normalisation
  point reads the raw entity fields `RtRollupArchive.SourceArchiveRtId` / `RtRollupArchive.Sources`
  — including the delete guard and `rollupsFor`, which is why both behave identically for either
  storage form.
- A **conflict is never thrown during enumeration**. It is carried as
  `RollupArchiveSnapshot.ConflictingSourceArchiveRtId` and rejected the moment it matters —
  at activation, with `RollupSourceDeclarationConflictException` (§4). Listing a tenant's rollups
  therefore never fails because one entity is inconsistent.
- Clients that render the source themselves (Studio) apply **the same precedence** for display:
  non-empty `sources` wins, otherwise the deprecated `sourceArchiveRtId` is shown as one unbounded
  source. "One normalisation point" is a statement about the *engine*; a client that has only the
  DTO in hand mirrors the rule rather than inventing a second one.
- A rollup stored in the old form behaves **exactly** as before: one unbounded source is
  operationally identical to the pre-1.8.0 single-source rollup. No migration and no seed change is
  required.

---

## §4 Validation rules

All rules are pure functions in `RollupValidator`
(`src/Runtime.Engine/StreamData/RollupValidator.cs`) and every exception extends
`StreamDataException`, names the offending source archive rtId in its message and surfaces verbatim
through the GraphQL/REST error mapping.

`ValidateSourcesForSave` runs in the order below at **create**
(`RollupArchiveLifecycleService.CreateAsync`) and again at **activation**
(`ArchiveLifecycleService` → `RollupValidator.ValidateForActivation`, which calls
`ValidateForSave` + `ValidateSourcesForSave` first).

| # | Rule | Runs at | Exception |
|---|---|---|---|
| 1 | The deprecated scalar must not disagree with `Sources` (`ConflictingSourceArchiveRtId` unset). | create, activation | `RollupSourceDeclarationConflictException` |
| 2 | At least one source is declared. | create, activation | `RollupSourcesRequiredException` |
| 3 | Each source archive appears at most once. | create, activation | `DuplicateRollupSourceException` |
| 4 | A bounded span is non-empty (`ValidFrom < ValidTo`). | create, activation | `RollupSourceSpanInvertedException` |
| 5 | At most one source has an open start, at most one an open end. | create, activation | `RollupSourceSpanOpenEndConflictException` |
| 6 | Spans are pairwise disjoint (abutting spans are disjoint — the intervals are half-open). | create, activation | `RollupSourceSpanOverlapException` |
| 7 | Every span boundary lies on a bucket boundary of the rollup, in the rollup's reference time zone. | create, activation | `RollupSourceSpanNotOnBucketBoundaryException` |
| 8 | No cycle in the source graph — following source edges from the rollup must never reach it again (transitive, iterative DFS over the tenant's rollups). | create, activation | `RollupSourceCycleException` |
| 9 | A rollup must not list itself (direct cycle). | create, activation | `RollupCycleException` |
| 10 | Every source archive exists and is not soft-deleted. | activation | `RollupSourceMissingException` |
| 11 | Every source archive is `Activated`. | activation | `RollupSourceNotActivatedException` |
| 12 | Every source targets the **same** CK type as the rollup. | activation | `RollupSourceTargetTypeMismatchException` |
| 13 | Bucket granularity per source (see below). | activation | `RollupBucketIntervalException` |
| 14 | **Every** aggregation spec resolves on **every** source (strict) — two-step per source, see below. | activation | `RollupSourcePathMissingException` |

Rules 1–7 need no repository access and are therefore cheap enough to run on every create; rule 8
needs the tenant's rollups (`ValidateNoTransitiveCycle`) and runs at create and at activation;
rules 10–14 need each source's snapshot and run at activation only.

The aggregation rules that predate AB#5157 — at least one aggregation, no duplicate
`(SourcePath, Function)` pair, a `ComparisonValue` on every `StateDuration` — now run at **create**
as well (`ValidateForSave`). That is a deliberate behaviour change: `createRollupArchive` fails
fast on inputs it used to accept silently and only reject at activation.

### Rule 7 — boundaries on the bucket grid

Every `ValidFrom` / `ValidTo` must satisfy
`BucketBoundary.AlignDown(boundary, alignment, bucketSize, zone) == boundary` for the rollup's own
alignment, bucket size and reference time zone (`FixedSize` ⇒ the grid of `BucketSize` multiples
anchored at `DateTime` tick zero, `0001-01-01T00:00:00Z` — identical to a Unix-epoch grid for any
bucket size that divides 24 h, but not for multi-day fixed buckets).
This is what guarantees the property the whole design rests on: **every bucket lies entirely within
exactly one source's span, or within none.** A boundary aligned in UTC but not in the rollup's
reference zone is rejected, and vice versa.

### Rule 13 — bucket granularity per source (AB#4289 reformulated)

The source's granularity is its window length (`Period` for a time-range archive, `BucketSize` for
a rollup source). A raw source with an undeclared sampling interval is not checked.

| Rollup alignment | Source | Accepted when |
|---|---|---|
| `FixedSize` | any | `BucketSize >= sourceWindow` **and** an integer multiple of it (the original AB#4289 ms rule) |
| calendar / ISO week | calendar-aligned rollup source | the source alignment **nests** inside the rollup alignment: `CalendarDay ⊂ CalendarMonth ⊂ CalendarQuarter ⊂ CalendarYear`; `Iso8601Week` contains only `CalendarDay` and nests inside nothing but itself |
| calendar / ISO week | time-range, fixed-size or rollup source with a fixed bucket | the source window does not exceed the alignment's bucket length: 1 d (`CalendarDay`), 7 d (`Iso8601Week`), 31 d (`CalendarMonth`), 92 d (`CalendarQuarter`), 366 d (`CalendarYear`) |

The last row is what makes the sbeg cutover shape legal: a legacy archive whose quarterly windows
are declared with a ~92 d `Period` is accepted under a `CalendarQuarter` rollup, while anything
genuinely coarser is rejected.

### Rule 14 — per-source resolution of the aggregation specs (two-step)

The rollup's aggregation specs are **logical** (source path + function, e.g. `Amount.Value` /
`Sum`). A base archive (raw / time-range) captures that path verbatim as a column, but a rollup
source declares its *generated physical* column names (`amountvalue_sum`), so a verbatim
containment check can never accept one logical spec over a rung that mixes a time-range base
archive with an hourly rollup of it — exactly the AC1 and sbeg shapes. Each spec is therefore
resolved **per source** by `RollupSourceColumnResolver.TryResolve` in two steps; a spec neither
step resolves is rejected with `RollupSourcePathMissingException` naming that source:

| Step | Applies when | Outcome |
|---|---|---|
| **Rule 1 — declared column** | the source's captured names contain the spec's `SourcePath` **verbatim** (an ingested column by `Path`, a computed column by `Name`, a rollup source's physical column by its physical name) | `DeclaredColumn`: the function is applied directly to that physical column — the pre-AB#5157 behaviour, unchanged |
| **Rule 2 — child aggregation** | the source is a rollup and one of its own specs has the **same function** and a source path that **normalises** to the same physical name (`NormalisePath`: dots removed, lower-cased — the `ColumnNameMapper.PathToColumnName` rule) | `ChildAggregation`: the child's target column(s) are the parent's source column(s), read function-preserving (§5) |

Rule 1 wins when both apply: a verbatim declared column is the more specific match and keeps a
legacy chained spec reading exactly the column it names. A parent function the child does not
store (parent `Avg` over a child that only stores `Sum`) does not resolve — nothing can be
recombined from it. Rule 2 needs the source's rollup snapshot (`RollupActivationSource.Rollup`);
without it only rule 1 applies.

---

## §5 Aggregation — one source per bucket

The rollup's aggregation specs stay **logical** (source path + function) and are resolved into each
source's physical columns per source (§4 rule 14, `RollupSourceColumnResolver`). Which columns the
per-bucket SQL reads therefore depends on the source serving the bucket:

- **Rule 1 — declared column** (base archives, computed columns, and chained specs that name the
  child's physical column): the function is applied to that single physical column exactly as
  before AB#5157. Chained rollups written in the physical-name style (`amountvalue_sum` / `Sum`)
  are unchanged.
- **Rule 2 — child aggregation** (a logical spec over a rollup source): the child's target
  column(s) are read **function-preserving** over the child buckets inside the parent bucket:

  | Parent function | Child column(s) | Read |
  |---|---|---|
  | `Sum`, `Count`, `StateDuration` | `{base}` | summed |
  | `Avg` | `{base}_sum`, `{base}_count` | each summed (pair); the average is recomputed on read as `sum / NULLIF(count, 0)` |
  | `TimeWeightedAvg` | `{base}_integral`, `{base}_duration` | each summed (pair) |
  | `Min` / `Max` | `{base}` | minimum / maximum |
  | `First` / `Last` | `{base}` | the value of the earliest / latest child window (by window order) |

  The parent's function must equal the child's, which is what keeps every row of this table
  function-preserving. The CrateDB layer implements the SQL from the resolver's outcome.

The choice of *which* archive the SQL runs against becomes per-bucket.

`RollupOrchestrator.ProcessRollupSnapshotAsync` per tick:

1. Load every **distinct** declared source snapshot **once** (one `IArchiveRuntimeStore.GetAsync`
   per source id, regardless of how many buckets the tick closes).
2. For each closed bucket `[start, end)`, pick `rollup.SourceForBucket(start, end)`:
   - **a source covers the bucket** → the existing `AggregateBucketAsync(source, …)` runs unchanged
     against it, the watermark advances, the run is audited. Because the source is fixed for the
     whole bucket, the fully-contained-window rule applies exactly as in the single-source case and
     the result is identical to a single-source rollup over the concatenated data for `Sum`,
     `Count`, `Min`, `Max`, `Avg`, `First` and `Last`. For `TimeWeightedAvg` and `StateDuration` the
     LOCF carry-in never crosses a source boundary: the aggregation reads its carry-in only from the
     rows of the source serving that bucket, so the first bucket served by a later source opens
     without a carry from the earlier source and its covered duration can be shorter than a
     concatenated single-source rollup would report. That is the tolerance the catalogue's
     "identical" assertions (TC-X-AGG-05, and through it TC-AGG-06 / TC-E2E-02) are executed
     against; every other function agrees exactly.
   - **no span covers the bucket** (a *gap bucket*) → **no row is written**, the watermark advances
     past it, and a `Debug` line records the skip. Gap buckets are a normal, declared state, not an
     error.
   - **the covering source is missing or not `Activated`** → the tick **stops** with a `WARNING`
     naming both rollup and source, and the watermark stays where it is. Buckets already committed
     in this tick remain committed. The orchestrator never skips past data a source will deliver
     once it is available again (§8).
3. The AB#4306 open-bucket refresh resolves its source the same way and is skipped at `Debug` when
   nothing covers the open bucket or its source is unavailable — a warning per tick while a bucket
   is still open would be noise; the closed-bucket loop emits it once the bucket closes.

A rollup whose `Sources` list is empty is skipped with a `WARNING` and produces nothing.

The initial watermark of a newly activated rollup is unchanged: it is derived from the clock alone
(`truncate(now − BucketSize)`), never from a source, so a two-source rollup starts exactly where a
single-source one would.

### `CalendarQuarter`

`BucketAlignment` gains `CalendarQuarter` (value **5**; the ordinal is load-bearing because the CK
enum key and the C# value are cast into each other). Quarters start on 1 January, 1 April, 1 July
and 1 October **in the rollup's reference time zone**, following the `CalendarMonth` shape exactly:
alignment happens in local wall-clock time and is converted back to UTC, so DST is handled for
free — in `Europe/Vienna` the quarter holding the spring-forward switch (Q1) is 90 days minus one
hour of UTC duration, the quarter holding the fall-back switch (Q4) is 92 days plus one hour, and
Q2 / Q3 are exactly 91 / 92 days. `null` reference time zone ⇒ UTC quarters.

---

## §6 Recompute — a DAG with multiple parents

A rollup is a **direct dependent of each of its sources**, so the reverse dependency graph
(`RollupDependencyGraph`) has one edge per `(source, rollup)` pair and is a DAG, not a tree. Its BFS
visited-set already deduplicates multi-parent reachability, so a rollup with two sources appears
exactly **once** in the transitive dependents of either.

**Propagation of a retroactive write** (`RecomputeOrchestrator.EnqueueOnDirectDependentsAsync`):
membership via `HasSource(sourceRtId)`, then the dirty range is **clipped to that source's span on
both ends** via `Clip(from, to)`. An empty clip enqueues nothing — a retroactive write into the
legacy archive after the cutover, or into the native archive before it, is correctly ignored by the
rollup above. For `TimeWeightedAvg` the range is extended by one bucket as before, but the
extension is capped at the source's `ValidTo`, because the successor bucket is served by a
different source — its carry-in comes from that source's own rows and cannot be affected by a write
into this one. This is the same source-local LOCF carry rule that §5 states as the tolerance for
`TimeWeightedAvg` / `StateDuration` across a source boundary.

**Backfill** (`EnqueueBackfillFromSourceAsync`, driven by `rewindRollupWatermark` /
`backfill_rollup_archive`; backfill stays **manual**): the start is the **minimum over all sources**
of `max(earliest stored timestamp of the source, ValidFrom)`. A source that holds no data, or whose
data lies entirely at or after its `ValidTo`, contributes nothing (`Debug` per source). If no source
contributes, the backfill is a logged no-op — no job, no range.

**Recompute of a range** (`RecomputeArchiveAsync`): the requested range is first split into
**per-source segments** (`Clip` per source, ordered by start) and only then into chunks. Each
segment is executed with its own source snapshot, sequentially in time order, and the job's row and
window totals accumulate across segments — so a recompute spanning the cutover runs the legacy
source for `[from, cutover)` and the native one for `[cutover, to)` in one job. A source that
cannot be loaded fails the request **before** the job is created. A range that no span covers
completes with zero totals and an `Information` line. When the job completes, the archive coverage
cache is invalidated for the recomputed rollup (§7) before dependents are enqueued on the full
range.

Segment boundaries are span boundaries and therefore on the rollup's bucket grid (rule 7), which is
what keeps chunk planning aligned.

---

## §7 Archive coverage

Coverage answers "which resolution is actually available for which time range?".

### §7.1 Measurement and timestamp convention

Every archive — base **and** rollup — exposes its measured coverage, read from CrateDB on request:

| Archive shape | `AvailableFrom` | `AvailableTo` |
|---|---|---|
| windowed (rollup, time-range) | `MIN(window_start)` — the **start** of the earliest stored window | `MAX(window_end)` — the **end** of the latest stored window (exclusive end of the last bucket, *not* its start) |
| raw | `MIN(timestamp)` | `MAX(timestamp)` |

Coverage is **archive-wide**, not per entity `rtId`, and is a single from/to pair.

**Null semantics.** An archive without rows, or without a backing table, has **no coverage**. That
is expressed as a `null` `ArchiveCoverage` — never a sentinel range, never an error. Every other
storage error propagates.

**Coverage does not show gaps.** It is one `AvailableFrom` / `AvailableTo` pair; holes inside that
range are deliberately **not** represented. A family whose native ingestion started long after the
legacy history ended still reports one contiguous-looking range. Deriving gaps from the graph and
the validity spans is explicitly out of scope.

### §7.2 Caching and invalidation

Answers are memoised by `ArchiveCoverageCache`, a **process-wide** cache keyed
`(tenantId, archiveRtId)` — the per-tenant `TenantContext` is created fresh for every resolution, so
a cache scoped to it would never hit.

- **Default time-to-live: 60 seconds**, configurable per host via
  `StreamData:Coverage:CacheTtlSeconds` (`ArchiveCoverageOptions`). A TTL of zero disables
  memoisation; a negative TTL is rejected.
- A `null` (no coverage) answer is cached like any other. An exception from the measurement is
  **never** cached — it propagates and the next request measures again.
- Beyond the TTL, entries are dropped through `IArchiveCoverageInvalidator.Invalidate(tenantId, archiveRtId?)`
  on the events that would otherwise be hidden for too long. The engine owns two of them: an
  **archive delete** (that archive, `ArchiveLifecycleService.DeleteAsync`) and the **completion of a
  recompute or backfill job** (the recomputed rollup). The tenant-wide events — **dropping a
  tenant's stream data** and **disabling the stream data feature** — live in the persistence layer
  and call `Invalidate(tenantId)` from there. Ordinary ingest is deliberately not invalidated — it
  only extends the range, and the TTL absorbs it.
- Invalidation is **process-local**: it clears the cache of the process that observed the event, not
  of every host in the cluster.

**Accepted consequence:** within the TTL, `coverageFor` and the resolver keep reporting the
pre-ingest coverage. A query issued seconds after data arrived can therefore still route to a
coarser rung (§7.4). This is accepted; lower `StreamData:Coverage:CacheTtlSeconds` if a deployment
needs it fresher.

### §7.3 The family query

`IArchiveFamilyCoverageService.GetFamilyCoverageAsync(archiveRtId)` returns, for **any** archive
rtId of a family, the queried archive plus every rollup transitively reachable from it, in BFS order
with the queried archive first. Each rung is an `ArchiveCoverageRung`:

| Field | Meaning |
|---|---|
| `ArchiveRtId`, `RtWellKnownName` | Identity of the rung. |
| `IsBase` | The rung **is not a rollup** — the raw or time-range archive the family is keyed by. |
| `Status` | The rung's archive lifecycle status, so a client can tell a *disabled* rung from an *empty* one. |
| `BucketSizeMs` | Rollup: `BucketSize`. Time-range base: `Period`. Raw base: `null` (no declared grain). |
| `Alignment` | The rung's `BucketAlignment`; `FixedSize` for a base archive. |
| `StoredFunctions` | The distinct aggregation functions the rung **declares**; empty for a base archive. |
| `AvailableFrom` / `AvailableTo` | The measured coverage, or `null` / `null` when the rung has none. |

The family stays the set of rollups transitively reachable from a base archive. A multi-source
rollup therefore belongs to the family of **each** of its sources and reports the same measured
coverage in each — listed exactly once per family. An unknown rtId, or a tenant without stream data,
yields an **empty list**, not an error.

Exposed as GraphQL `streamData.coverageFor(rtId)`, the REST endpoint
`GET streamdata/archives/{archiveRtId}/coverage`, and the MCP tool `get_archive_coverage`.

### §7.4 The resolver's coverage filter

`resolveSeriesQuery` applies coverage as a filter over the candidate rungs **before** the existing
selection rule of [concept-resolution-aware-series-queries §4.2](concept-resolution-aware-series-queries.md):

1. A rung **covers** the request when its `AvailableFrom` is at or before the requested start
   (`AvailableFrom == from` counts as covering; the requested **end is not considered**). A rung
   without coverage never covers.
2. The **base rung takes part in the filter** like every other rung.
3. If **no rung reports coverage at all**, the filter is **inert**: the plan is byte-for-byte the
   pre-AB#5157 result, including its signal. This is what keeps hosts that do not wire a coverage
   provider on the old behaviour.
4. Candidates = the covering rungs. If none covers, the candidates are the rungs with the
   **earliest** `AvailableFrom`, ties kept.
5. The selection rules run unchanged over the candidates.

When the filter changes the chosen archive, the result carries:

- `Signal = CoverageLimited` — it **overrides `Ok` and `ResolutionLimited`**;
- `ActualPoints` = the delivered point count, with the same meaning as for `ResolutionLimited`;
- `Diagnostic` naming the excluded finer rung and its available-from;
- `FinerRungAvailableFrom` — a first-class result field carrying that rung's `AvailableFrom`
  (`null` when the excluded rung reports no coverage at all).

Two deliberate edge cases: if the filtered plan lands on a **refuse** path
(`NoSuitableRollup` / `UnknownBaseGrain`) that signal is kept and the coverage exclusion is
prepended to the diagnostic, and if the covering candidates yield an `EmptyLadder` the unfiltered
result is returned — so a `CoverageLimited` answer **always** names a covering fallback rung.

`GetQueryById@1` routes through the resolver and, as before, keeps the persisted archive with a
warning for any signal other than `Ok`.

---

## §8 Operational rules

**A referenced source archive cannot be deleted.** `ArchiveLifecycleService.DeleteAsync` refuses to
delete an archive while a non-soft-deleted rollup lists it among its sources
(`RollupSourceInUseException`, count from `CountActiveRollupsForSourceAsync`), in **either** storage
form and whatever validity span the reference carries. Remove or replace the rollup first.

**Sources are immutable after activation — documented, not enforced.** `createRollupArchive` is the
supported way to declare sources, Studio's edit mode is rename-only and shows the sources read-only,
and the concept treats an activated rollup's source list as fixed. The generic CK mutation can
nevertheless change any attribute the model allows, so nothing *technically* prevents editing
`Sources` on an activated rollup. A guard is **out of scope** (decided, no follow-up item).
Editing sources on an activated rollup is unsupported: it can put an already-populated rollup out of
sync with its declaration and, if a boundary ends up off the bucket grid, make a recompute segment
extend past it.

**Disabling a source of an activated rollup stays allowed.** It is not blocked, and it does not fail
the rollup. The orchestrator simply **stalls**: it stops the tick with a `WARNING` naming the rollup
and the source and leaves the watermark untouched, so aggregation resumes exactly where it stopped
once the source is activated again (§5). Nothing is silently skipped.

**A disabled finer rung can make a coverage end misleading — accepted.** Coverage is *measured* per
archive, not derived from the graph. If the finest rung beneath a multi-source rollup is disabled,
it keeps reporting the range of the rows it still holds while no new data arrives, so its
`AvailableTo` can look current although the rung has stopped advancing. Read `Status` from the
family query (§7.3) alongside the coverage to tell a disabled rung from a live one. This is an
**accepted** limitation of measured coverage, not a defect.

**Existing rollups and seeds are unaffected.** Rollups stored in the deprecated single-id form
behave identically to before, no CK model migration is involved, and existing seeds and blueprints
import unchanged on `System.StreamData` 1.8.0.
