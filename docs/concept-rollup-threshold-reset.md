# Concept: Rollup Threshold-Reset Policy (manual vs. automatic)

Status: **Decision recorded** — 2026-07-14. Work item: **AB#4196**
(parent epic AB#4073 — VOEST M1: EDA energy data to data lake).

See also:
- [concept-rollup-archives.md](./concept-rollup-archives.md) — the derived-rollup design; defines the
  `LastAggregatedBucketEnd` watermark (the "computed up to" threshold) and the manual
  `rewindRollupWatermark` escape hatch.
- [concept-rollup-recompute.md](./concept-rollup-recompute.md) — **AB#4184** (Closed); built the
  dirty-window ledger, dependency-graph propagation, the optimistic generation-swap recompute, and
  the observability this concept consumes. Its §12 lists *"bounded retro reach"* as the open item
  this concept closes.
- [concept-computed-columns.md](./concept-computed-columns.md) — **AB#4189**; shares the same
  recompute orchestration.
- [concept-timezone-aware-queries.md](./concept-timezone-aware-queries.md) — **AB#4190**; no direct
  interaction (recompute ranges are already resolved on each dependent's own bucket grid + reference
  time zone).

## §1 The question AB#4196 asks

Each rollup archive tracks a **threshold** — `LastAggregatedBucketEnd` — marking how far it has been
computed (a monotone high-water mark on the source time axis). When raw data changes *before* that
threshold (late values, corrections, re-ingests), the rollup beyond that change is stale until it is
recomputed for the affected window. The open question:

> Do we need an operator-driven **manual reset** of that threshold to force re-computation, or can we
> find an **automatic** mechanism that detects the change and walks the threshold backwards itself?

The answer depends on the recompute concept landed in AB#4184.

## §2 Decision

**Both — and, importantly, the automatic path is already implemented and does *not* literally walk
the forward threshold backwards.** AB#4184 shipped a superior mechanism to a threshold rewind; this
concept records that as the answer and adds the one missing guardrail.

| Path | Mechanism | Status |
|---|---|---|
| **Automatic** (the common case) | On every write, `RetroactiveWriteDetector` flags a write whose `timestamp < consumedWatermark` and records a `CkArchiveDirtyWindow`. The `RecomputeOrchestrator` periodic tick propagates that window onto dependents and recomputes **only the affected `[from, to)` range** via the optimistic per-window **generation-swap** — leaving `LastAggregatedBucketEnd` untouched. | ✅ Implemented (AB#4184) |
| **Manual** (escape hatch) | `rewindRollupWatermark(rtId, toBucketEnd)` moves the threshold backwards; the forward orchestrator re-aggregates from there. Reconciled with the genmap via `ClearRecomputeGenerationsAsync`. Plus `recomputeArchive(rtId, from, to, rtId?)` for a reader-safe range recompute. | ✅ Implemented (AB#4184 / rollup MVP) |
| **Guardrail** ("bounded retro reach") | Cap how far back a single very-late write may drag an automatic recompute. | ❌ **Not yet implemented — the scope of this item's code work (b).** |

### Why automatic does not walk the threshold back

A literal threshold rewind (`LastAggregatedBucketEnd := earliestChange`) would force the forward
orchestrator to re-aggregate **in place** from that point, and a reader hitting the rollup mid-rewind
can see a half-old / half-new mix. The generation-swap recompute instead computes the corrected window
into a staging generation `N+1` and commits with a single atomic pointer flip, so readers always see a
consistent old-or-new snapshot. The forward watermark therefore stays monotone and forward-only; the
"reset" is expressed as a **scoped recompute of the dirty window**, not as a threshold move. The manual
`rewindRollupWatermark` remains for the bulk case (a large re-ingest where the operator accepts brief
in-progress reads).

This means the AB#4196 title question — *"do we need to reset the threshold?"* — resolves to: **no
threshold reset is needed for the automatic case; the dirty-window scoped recompute covers it.
Threshold reset stays available as the manual bulk escape hatch.**

## §3 Answers to the design questions

1. **Source of the change signal (Information A).** `RetroactiveWriteDetector.TryBuildDirtyWindow`,
   called from every archive write path in `CrateDbStreamDataRepository` (raw, time-range, and rollup
   sources). It is value-agnostic — a re-ingest of byte-identical data into an already-consumed window
   still marks it dirty (a deliberate safe over-approximation: never misses a real change). The signal
   is persisted as `Archive.DirtyWindows` (`CkArchiveDirtyWindow` = `WindowStart`, `WindowEnd`,
   `ChangeKind`, `Source`, `DetectedAt`).

2. **How far back may automatic reset go?** Today: **unbounded** — this is the gap. A single write with
   a very old timestamp produces a dirty window spanning back to that timestamp, and
   `EnqueueOnDirectDependentsAsync` clamps only the **end** (to the dependent's own watermark, AB#4288),
   never the **start**. §4 specifies the cap.

3. **Per-archive or per-window?** Per-window. Detection records a `[minRetroTs, maxRetroTs + 1 tick)`
   window; propagation snaps it to each dependent's bucket grid and enqueues a bounded
   `CkArchiveRecomputeRange`. The forward threshold is never the unit of reset.

4. **Interaction with the AB#4184 concurrency policy.** Covered. Overlapping triggers **coalesce** into
   one active job per archive; a reset/recompute arriving mid-recompute merges its range into the
   active job (superseded trigger recorded as `Coalesced`). No corruption, no partial reads — the
   generation pointer only flips on a fully-staged range.

5. **Observability / audit.** Automatic and manual paths both surface:
   `Archive.DirtyWindows` / `PendingRecomputeRanges` (queued intent), the `CkArchiveRecomputeJob`
   history (`state, trigger, range, rtIdScope, rowsProcessed, startedAt/finishedAt, errorReason`),
   the `LastRecompute*` health fields, `IArchiveAuditTrail.RecordRecomputeRunAsync` /
   `RecordRecomputeFailureAsync` into the platform event log, and the StreamData Prometheus metrics.
   The dirty-window carries `Source ∈ {Manual, Pipeline, Import}` and `ChangeKind`, so *who / how / when
   / from / to* are captured. **Implemented:** when the §4 cap truncates or drops a retroactive reach,
   the detector sets a `reachCapped` flag and `CrateDbStreamDataRepository` emits both a `WARN` log and a
   typed `IArchiveAuditTrail.RecordRetroReachCappedAsync` event (category `Archive.RetroReachCapped`,
   carrying `consumedWatermark`, `cappedFloor`, `capMs`) into the platform event log — so an operator can
   decide to run an unbounded manual `recomputeArchive` for the dropped tail.

6. **Shared threshold with computed-column backfills (AB#4189) and timezone-aware queries (AB#4190)?**
   Computed-column backfills reuse the same recompute orchestration + generation swap, so they inherit
   the same dirty/recompute machinery and (once built) the same cap — no separate threshold. Timezone
   handling is already per-dependent (`BucketBoundary.ResolveZone(rollup.ReferenceTimeZone)`); no
   interaction.

## §4 The bounded-retro-reach guardrail (the (b) work)

### Config surface

A new **configuration** attribute on the base `Archive` CK type (alongside `RawRetentionMs`), applied
to the **source** archive where detection happens:

| Attribute | Type | Notes |
|---|---|---|
| `Archive.MaxRetroactiveReachMs` | `Int` (ms) | Cap on how far **before** the consumed watermark a single retroactive write may schedule an automatic recompute. `null` = unbounded (current behaviour, backward-compatible). Mutable. **Not** `isRuntimeState` — it is operator configuration. |

Plus an optional global safety ceiling in host config
(`StreamData:Recompute:MaxRetroactiveReachHardLimitMs`, default unset) so a tenant that never sets the
per-archive value still has a fleet-wide backstop. The **effective cap** is
`min(perArchive ?? ∞, globalHardLimit ?? ∞)`.

### Where the cap is applied

Clamp at **detection**, in `RetroactiveWriteDetector.TryBuildDirtyWindow`, so the dirty window itself is
bounded and the cheap over-approximation stays cheap:

```
floor = consumedWatermark - effectiveCap          # (cap = ∞ ⇒ floor = -∞, today's behaviour)
earliest = max(earliest retroactive ts, floor)
# if every retroactive ts < floor ⇒ nothing within reach ⇒ record NOTHING automatically,
#   but emit a capped-reach audit entry so the tail is not silently lost.
```

Belt-and-suspenders: also clamp `start` in `EnqueueOnDirectDependentsAsync`
(`start = max(start, dependentWatermark - effectiveCap)`), so a dirty window that predates the cap
introduction, or a manual enqueue, still cannot drag a decade of recompute.

### What the cap does *not* limit

The **manual** `recomputeArchive(from, to)` and `rewindRollupWatermark` stay **unbounded** — they are
the deliberate operator escape hatch for a genuine deep correction. The cap only bounds the *automatic*
reach so one very-late row can never silently trigger a fleet-crushing recompute.

### Default recommendation

**Decided (2026-07-14): per-archive `null` = unbounded by default.** This preserves current behaviour
and is fully backward-compatible — no existing tenant silently loses a legitimate months-old correction.
The guardrail is opt-in: Studio surfaces an active recommendation to set it (e.g. `P30D`–`P90D` for
high-frequency energy sources), and the global hard limit
(`StreamData:Recompute:MaxRetroactiveReachHardLimitMs`) is set conservatively in production host config
as a fleet-wide backstop. Rationale: silently changing the default to a finite cap could drop a genuine
deep correction on an existing tenant; keeping `null` the default preserves the safe-over-approximation
contract until an operator opts in, while the global ceiling still bounds a pathological runaway.

## §5 Acceptance criteria mapping

- **Decision documented (manual / automatic / both, with rationale).** ✅ This document — §2.
- **Manual: API + admin-UI action to reset the threshold; audit per reset.** ✅ Already shipped —
  `rewindRollupWatermark` + `recomputeArchive` (GraphQL / REST / octo-cli / Studio rollups panel);
  audited via `RecomputeJob` + `IArchiveAuditTrail`.
- **Automatic: change-signal source defined; cap on reach; audit per automatic reset.** ⚠️ Signal +
  audit shipped (AB#4184); **cap is the (b) work** specified in §4.
- **Composes with AB#4184 concurrency (no corruption mid-recompute).** ✅ Coalesce policy.
- **Test coverage.** Existing AB#4184 matrix covers manual reset → reprocess, late value → dirty window
  → exact-range recompute, reset mid-recompute safe. **(b) adds:** a very-late write beyond the cap →
  automatic reach is bounded to the cap (not further) and a capped-reach audit entry is emitted; a
  manual recompute of the same deep range remains unbounded.

## §6 (b) implementation checklist (forward pointer)

1. **CK model** (`System.StreamData`, additive minor bump): add `Archive.MaxRetroactiveReachMs`
   (Int, config, nullable). Additive ⇒ no-migrations bridge.
2. **Engine**: thread the effective cap into `RetroactiveWriteDetector.TryBuildDirtyWindow`
   (`octo-construction-kit-engine-mongodb/Runtime.Engine.CrateDb`) and clamp `start` in
   `RecomputeOrchestrator.EnqueueOnDirectDependentsAsync`
   (`octo-construction-kit-engine/Runtime.Engine/StreamData`).
3. **Host config**: `StreamData:Recompute:MaxRetroactiveReachHardLimitMs` in the recompute options.
4. **Audit** *(done)*: `IArchiveAuditTrail.RecordRetroReachCappedAsync` typed method, forwarded through
   `IAuditEventSink` (`ForwardingArchiveAuditTrail`) — no `IEventRepository` ctor-capture. Injected
   (optional) into `CrateDbStreamDataRepository` via its factory; emitted alongside the `WARN` log on a
   capped reach.
5. **API/Studio** *(done)*: `MaxRetroactiveReachMs` is a base-`Archive` CK attribute, so it is editable
   through the generic CK-entity CRUD exactly like `RawRetentionMs` (no dedicated GraphQL/REST/SDK
   wiring). The Refinery Studio rollups panel shows a passive **recommendation tip** (shown when the
   archive has rollups and is not in single-rollup mode) pointing operators to set the cap.
6. **Tests** *(done)*: `RetroactiveWriteDetector` cap unit tests, orchestrator propagation-clamp tests,
   and Studio rollups-panel tip spec tests.
7. **Docs** *(done)*: `octo-documentation` archives.md / rollupRecompute.md / glossary / studio archives,
   plus the engine + mongodb `CLAUDE.md` recompute sections.
