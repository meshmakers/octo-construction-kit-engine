# Concept: Recompute Rollup Archives (stable, optimistic)

Status: **Draft** — concept agreed in chat on 2026-06-28, implementation pending.
Work item: **AB#4184** (parent epic AB#4073 — VOEST M1: EDA energy data to data lake).

See also:
- [concept-rollup-archives.md](./concept-rollup-archives.md) — the derived-rollup design (watermark, orchestrator, freeze/rewind) this concept extends.
- [concept-time-range-archives.md](./concept-time-range-archives.md) — the `(window_start, window_end, was_updated)` storage shape shared by rollup and time-range archives.
- [concept-computed-columns.md](./concept-computed-columns.md) — **AB#4189**, which reuses this concept's backfill orchestration + atomic-swap + observability (its active-archive backfill, Phase 7, blocks on AB#4184).
- AB#4188 — multiple aggregations per rollup archive (a recompute updates all declared aggregates of a window atomically).
- AB#4190 — timezone-aware queries (must not break the recompute model).
- AB#4196 — threshold-reset policy (manual vs. automatic); **builds on the dirty-tracking introduced here**.
- AB#4186 / AB#4195 — App and mesh-adapter read paths that must tolerate an in-progress recompute.

## §1 Overview

Raw and time-range archives receive **late values, corrections, and re-ingests** (e.g. a
corrected `DATEN_CRMSG` from EDA arriving after the original). When raw data changes for a
window that a **rollup archive** already aggregated, the rollup is **stale** until it is
recomputed for that window. The existing rollup orchestrator
([concept-rollup-archives.md §5](./concept-rollup-archives.md)) only walks **forward** from a
monotonic watermark; it never revisits a closed bucket unless an operator manually calls
`rewindRollupWatermark`, and that rewind re-aggregates **in place** — a reader hitting the
rollup mid-rewind can see a half-old / half-new mix.

This concept adds **stable, optimistic recompute**: recomputation that can be triggered
**periodically** and **manually**, scoped to a window range (optionally per metering point /
`rtId`), that **never exposes a partially written state** to consumers, propagates through
**multi-level rollup chains**, and is **debuggable** when it fails.

### Goals

- A rollup archive can be **recomputed** for a window range without an operator first having to
  reason about watermarks.
- **Optimistic reads**: while a recompute runs, meshboards / pipelines / SDK readers keep
  working and see a **consistent snapshot** — either the previous values or the new values,
  **never a mix**, never a `500`, never a pipeline abort.
- **Failure-safe**: a recompute that fails mid-run leaves the **previous** values intact.
- **Dirty-tracking on base archives** records, independently, (A) *that* raw data changed
  retroactively and (B) *which* derived archives must be recomputed — across multi-level chains.
- **Periodic + manual** triggers; periodic recompute only touches what is dirty, not the whole
  archive.
- **First-class observability**: a persistent recompute-job history, audit trail, metrics, and
  structured logs sufficient to debug a failed recompute after the fact.

### Non-Goals

- **Automatic event-driven trigger policy** — whether a detected raw change *automatically*
  walks the threshold back is **AB#4196**. This concept produces the dirty signal that 4196
  consumes; it does not decide the auto-reset policy.
- **New rollup aggregation types** — out of scope (AB#4188 owns multi-aggregate).
- **Cross-archive consistency** — each archive is recomputed independently; a chain propagates
  top-down but there is no global transaction across sibling archives.
- **Changing the raw archive's storage layout** beyond the additive dirty/observability fields.

## §2 The two informations on a base archive

Per the requirement, a **source archive** (raw, time-range, **and** rollup — because a rollup is
itself a source for chained rollups) carries **two distinct informations**:

### Information A — "was raw data changed manually / retroactively?"

A **retroactive change signal** with the affected window range. A write is *retroactive* when
it lands **at-or-before** the high-water mark already consumed by at least one dependent —
i.e. a correction / late value, **not** a fresh forward append.

| Storage shape | Source | Notes |
|---|---|---|
| Windowed archives (time-range, rollup) | Existing `was_updated` column (`Constants.WasUpdated`) | Already set `TRUE` on every `ON CONFLICT` upsert and propagated to rollups via `MAX(was_updated)`. Today purely advisory for dashboards — **reused** here as the cheap per-row retro signal. |
| Raw archives (single `timestamp` column) | **New** | Raw tables have no `was_updated`. Detect a retroactive write as `incoming.timestamp < consumedHighWater` at insert time and record it. |

The signal is materialised as a **dirty-window ledger** on the source archive (Mongo,
`RtArchive` / `RtRollupArchive`): a list of entries
`(windowStart, windowEnd, changeKind, source, detectedAt)` where

- `changeKind ∈ { Append, RetroactiveModify }` — distinguishes a normal forward append from a
  correction.
- `source ∈ { Manual, Pipeline, Import }` — *how* the change arrived (manual edit vs. pipeline
  re-ingest vs. bulk import).

`changeKind` and `source` together are the "manual vs. retroactive" distinction the requirement
calls two different informations: *what kind* of change, and *through which path*.

### Information B — "which archives must be recomputed?"

A **dirty-dependents ledger**, derived by **propagating** a dirty window through the
reverse-dependency DAG. Per stale dependent it records the window range to recompute.

- Reverse lookup uses the existing rollup→source link (`RollupArchiveSnapshot.SourceArchiveRtId`,
  `IRollupArchiveRuntimeStore` enumeration / `CountActiveRollupsForSourceAsync`).
- A **new `RollupDependencyGraph` walker** (`Runtime.Engine/StreamData/`) computes the
  **transitive closure**: source → its rollups → their rollups (rollup-of-rollup), with cycle
  protection (the model already forbids cycles via `RollupCycleException`).
- The stale range per dependent is snapped to that dependent's bucket boundaries via the existing
  `BucketBoundary` helper.

A and B are kept separate on purpose: A is an immutable audit fact about the **source data**;
B is the recompute **work list** derived from A and the dependency graph, and is consumed (and
cleared) by the recompute engine.

## §3 Recompute job model

A unit of recompute is a **`RecomputeJob`**:

```
RecomputeJob(
    archiveRtId,            # the dependent archive being recomputed
    range [from, to),       # bucket-boundary-aligned window range  (the atomic swap unit)
    rtIdScope?,             # optional: restrict to one metering point / stream
    trigger,                # Manual | Periodic | ChainPropagation
    state)                  # Pending → Running → Swapping → Completed | Failed | Coalesced
```

- **Swap unit = window range** (per the agreed scope). A job swaps exactly its `[from, to)` range
  (optionally for one `rtId`), never the rest of the archive.
- **Concurrency = coalesce.** If a recompute is triggered for an archive that already has a
  running or pending job, the new range is **merged** into the existing job's range (and any new
  dirty windows join the ledger). There is at most one active job per archive; the superseded
  trigger is recorded as `Coalesced` for traceability. No queue, no reject.
- **Idempotency.** The same input range produces the same staging rows; a crash mid-job is
  recovered from the job state persisted in the Mongo ledger — re-running re-derives identical
  staging output before any swap.

## §4 Atomic swap — CrateDB reality

CrateDB has **no multi-statement transaction** and **no atomic cross-range row swap**. The
compute always happens in a **per-job staging table**; the *commit* differs by scope:

| Recompute scope | Commit mechanism |
|---|---|
| **Full archive** | Compute into a staging table, then **`ALTER CLUSTER SWAP TABLE staging TO live`** — atomic at table level. The old table is dropped afterwards. |
| **Partial range** (the common case — a window range, optionally per `rtId`) | Compute into a staging table, then flip a **per-window `active generation` pointer**. Readers always select exactly one consistent generation per window; the flip is the atomic commit, the superseded generation's rows are GC'd afterwards. |

So the staging table does the heavy lifting (the chosen "shadow / staging" model), and the
**per-window generation pointer** provides the atomic flip that a pure DELETE+INSERT over a
range cannot. This is the one deliberate blend of the staging model with a generation marker,
forced by CrateDB's lack of partial-range atomicity.

### Generation pointer

- The live rollup table gains a `generation` column (`BIGINT NOT NULL DEFAULT 0`). The orchestrator
  writes recomputed buckets under `generation = N+1`.
- The **active generation per window** is held in the source archive's Mongo metadata (small,
  per-window or per-range, not per-row), written **after** the staging rows are confirmed.
- The read path (§6) injects `WHERE generation = active(window)`; until the pointer flips,
  readers see `generation = N` (previous values).
- After the flip, a background sweep deletes `generation < active(window)` rows for that range.

### Failure mode

A job that fails before the flip leaves the staging table orphaned (GC'd by name) and the live
table / generation pointer **untouched** → readers keep seeing the previous values. No partial
state is ever observable. The failure is recorded in the job history (§7) with its reason.

## §5 Triggers

| Trigger | Surface | Behavior |
|---|---|---|
| **Manual** | GraphQL `recomputeArchive(rtId, from, to, rtId?)` + REST `POST …/archives/{rtId}/recompute` + octo-cli `RecomputeArchive` | Operator forces a recompute, optionally scoped to a range / metering point. Requires `StreamDataAdmin`. Audited with who / when / scope. |
| **Periodic** | `RecomputeOrchestratorHostedService` (per-tenant tick, analogous to `RollupOrchestratorHostedService`) | Reads the **dirty-dependents ledger** (Information B) and recomputes **only dirty windows** — not the whole archive. Schedule configurable per archive. |
| **Chain propagation** | internal | After a rollup's swap succeeds, its own dependents are marked dirty in their ledgers, so a multi-level chain rolls forward top-down. |

Event-driven automatic threshold reset is **out of scope** (AB#4196); the ledger is the prepared
data source for it.

## §6 Read path (optimistic)

- `StreamDataQuery` / `StreamDataVariantExecutor` inject `WHERE generation = active(window)` for
  generation-tracked archives, so a query during recompute returns a single consistent generation
  per window.
- No hard error, no `500`, no abort while a recompute runs (acceptance criterion). The mesh-adapter
  `GetQueryById` node (AB#4195) and the VOEST App (AB#4186) inherit this behavior for free.
- **Optional soft hint**: the existing `was_updated` flag plus the new
  `RecomputeInProgress` / `LastRecomputeSuccessAt` fields let a dashboard show "recompute in
  progress" without being required to.

## §7 Observability & monitoring (for failure-case debugging)

Beyond the acceptance-criteria fields, monitoring is a first-class deliverable so a failed
recompute is debuggable after the fact.

### Per-archive fields (Mongo, exposed via `RollupArchiveInfo`)

`LastRecomputeSuccessAt`, `LastRecomputeStartedAt`, `RecomputeInProgress`,
`LastRecomputeFailureAt`, `LastRecomputeFailureReason`, `DirtyWindowsPending` (count).

### Persistent recompute-job history

A `CkArchiveRecomputeJob` record per run — **not** just a boolean flag — with
`state, trigger, range, rtIdScope, rowsProcessed, windowsProcessed, startedAt, finishedAt,
durationMs, errorReason, stagingTableName`. Queryable via a new GraphQL
`recomputeJobsFor(archiveRtId)`. This is what an operator reads to debug "why did last night's
recompute fail?".

### Audit trail

`IArchiveAuditTrail` is extended with `RecordRecomputeRunAsync(...)` and
`RecordRecomputeFailureAsync(..., reason)`, routed through `IAuditEventSink` into the platform
event log (see `octo-construction-kit-engine/CLAUDE.md` — Audit-Trail Architecture; never
ctor-capture `IEventRepository`).

### Metrics (Prometheus / OTLP)

`recompute_duration` (histogram), `recompute_rows_processed`, `recompute_failures_total`,
`recompute_in_progress` (gauge), `dirty_windows_pending` (gauge). Emitted on the StreamData meter.

### Structured logs

One structured log at each phase boundary — **Compute → Validate → Swap → Propagate** — carrying
`archiveRtId`, `jobId`, `range`, `correlationId`, so a failed run is reconstructible end-to-end.

## §8 CK model extension (`System.StreamData`, additive minor bump)

| Element | Change |
|---|---|
| `CkArchive` / `CkRollupArchive` attributes | `DirtyWindows` (RecordArray — Information A), `PendingRecomputeRanges` (RecordArray — Information B), and the §7 observability fields. All marked `isRuntimeState: true` (see CLAUDE.md — runtime-state preservation, so a blueprint re-apply never tramples them). |
| New record `CkArchiveDirtyWindow` | `WindowStart`, `WindowEnd`, `ChangeKind`, `Source`, `DetectedAt`. |
| New record `CkArchiveRecomputeJob` | the §7 job-history fields. |
| New enums | `CkRecomputeChangeKind { Append, RetroactiveModify }`, `CkRecomputeChangeSource { Manual, Pipeline, Import }`, `CkRecomputeJobState { Pending, Running, Swapping, Completed, Failed, Coalesced }`. |

The bump is additive; existing archives migrate via the no-migrations bridge (CLAUDE.md).

## §9 Service / API surface

| Layer | Addition |
|---|---|
| GraphQL (`StreamDataMutation` / `StreamDataQuery`) | `recomputeArchive(rtId, from, to, rtId?)`, `recomputeJobsFor(archiveRtId)`; extend `RollupArchiveInfo` with the §7 fields. |
| REST (`StreamDataController` / `TimeSeriesServicesClient`) | `POST …/archives/{rtId}/recompute`, `GET …/archives/{rtId}/recompute-jobs`. |
| octo-cli | new `RecomputeArchive` command + `ListRecomputeJobs` (next to `RewindRollupWatermark`). |
| Engine | `RollupDependencyGraph` walker, `RecomputeOrchestrator` + `RecomputeOrchestratorHostedService`, staging-table builder + generation-pointer swap in `CrateDbStreamDataRepository`, Mongo dirty-ledger + job-history store. |
| Studio (`octo-frontend-refinery-studio` + `octo-frontend-libraries`) | Archive detail: recompute action (optional range / rtId scope), `last success / in-progress / last failure (+reason)` panel, dirty-windows badge, job-history table. |

## §10 Test matrix (acceptance criteria)

- read-during-recompute → consistent snapshot (old **or** new, never mixed).
- recompute-during-recompute → coalesce into one job.
- crash mid-recompute → previous values intact, no partial state.
- late raw value → dirty window → follow-up recompute reprocesses exactly that range.
- **multi-level propagation** → raw → rollup → rollup-of-rollup all recompute top-down.
- raw-archive retroactive marker (no `was_updated` column) detected correctly.
- manual vs. retroactive `changeKind` / `source` recorded distinctly.
- pipeline reading during recompute does not abort.
- failed recompute surfaces in the job history with a reason.

Frameworks: xUnit v3 (`Runtime.Engine.Tests`, `StreamData.UnitTests`), integration
(`AssetRepositoryServices.IntegrationTests`), Karma/Jasmine for Studio.

## §11 Sequencing

1. **This concept first.** It provides backfill orchestration + atomic swap + dirty-tracking +
   observability that AB#4189 (Phase 7) and AB#4196 depend on.
2. CK-model bump → engine (dependency graph, staging/generation swap, dirty ledger) → orchestrator
   → GraphQL/REST/SDK → octo-cli → Studio → docs.
3. AB#4196 then layers the **automatic** threshold-reset policy on top of the dirty ledger;
   AB#4189 Phase 7 reuses the staging + generation-swap for computed-column backfills.

## §12 Open items (post-MVP)

- **Generation-pointer GC cadence** — when to delete superseded generations (immediate sweep vs.
  retention window for rollback). Default: immediate sweep after the flip, configurable retention.
- **Per-`rtId` generation granularity** — start per-window; refine to per-`(window, rtId)` only if
  a single late metering point forces recompute of unrelated streams in the same window.
- **Bounded retro reach** — cap how far back a single very-late row may drag a recompute (shared
  with AB#4196's guardrail).
