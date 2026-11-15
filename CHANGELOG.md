# Changelog

Changes of the engine libraries (`Meshmakers.Octo.ConstructionKit.*`, `Meshmakers.Octo.Runtime.*`) that blueprint and model
authors or service owners need to know about. The CK models of this repository keep their own changelogs
(`src/SystemCkModel/ConstructionKit/CHANGELOG.md`, `src/StreamDataCkModel/ConstructionKit/CHANGELOG.md`). Versions are the libs
release tags (`r<X.Y.Z>`); entries without a release are listed under "Unreleased".

## Unreleased

### Changed

- **Late stream-data deliveries recompute rollups only for the affected series, once per debounce interval (AB#9016).**
  Before, every late delivery marked the whole window dirty on the raw archive, and the next tick recomputed the rollup for
  that window over every series, then the whole cascade. Each run used a new generation and cleaned up the old one
  (prod-2 tenant `sbeg`: 57 jobs for one late day, 59-80 k rows each, 556 cleanup DELETEs and 6052 s of CrateDB time in two
  weeks). Now:
  - the dirty window carries the rtIds of the late rows (`ArchiveDirtyWindow.RtIds`), and the recompute range and the job
    carry them as `RtIdSet`. The orchestrator runs such a range as a **set-scoped refresh**: the new
    `IArchiveRecomputeExecutor.ExecuteScopedRefreshAsync` aggregates only those series again and writes them at the
    generation each row is read at today. No new generation, no pointer entry, no generation sweep; a bucket row whose
    source rows are gone is removed, as a full recompute removes it. Readers get the same
    values as after a full recompute of the window;
  - the obligations of late deliveries are held for `StreamData:Recompute:LateDataDebounce` (**new setting**, default
    `00:05:00`, `0` = off), stored as `NextAttemptAt` at attempt zero on the archive entity, so the hold survives replicas
    and restarts. As soon as the oldest is due, all held obligations of the rollup run together in one job over the union
    of their series;
  - a write or a merged interval with more than `StreamData:Recompute:MaxScopedRtIds` (**new setting**, default `200`,
    `0` = off) distinct rtIds falls back to the unscoped recompute;
  - chained rollups inherit the set and run on the same tick without the debounce. Manual recomputes, backfills and manual
    writes stay unscoped and are not held back. An unscoped obligation absorbs the set-scoped ones it overlaps;
  - the active-job guard serialises the refresh with every other recompute of the rollup.
  - **Interface change (agent-operating-rules §2.4a):** `IArchiveRecomputeExecutor.ExecuteScopedRefreshAsync` is a new
    member with a default implementation (one rtId: scoped recompute; several: one unscoped recompute, as before AB#9016), so an implementation built against the
    previous contract still loads. The contract records got init-only properties (`RtIds`, `RtIdSet`) and keep their
    positional constructors. `RecomputeOrchestrator` keeps its previous constructor, which means no debounce; the new one
    takes `RecomputeLateDataOptions`. The engine-mongodb release that records the rtIds and implements the in-place refresh
    merges with or after this one.
  - Needs System.StreamData 1.26.0 (below). Rules: `docs/concept-rollup-recompute.md`, "Late deliveries (AB#9016)".

- **Series resolver prefers a rollup on long windows (AB#8993).** `SeriesResolutionPlanner` (behind `ISeriesResolutionService`
  and the asset repository's `resolveSeriesQuery`) used to return the raw base archive whenever its native resolution fit the
  requested points, so a 7-day chart at 800 points read 672 raw 15-minute windows per series instead of the 168 points of the
  hourly rollup (prod-2 tenant `sbeg`: 129 M-row raw archive, about 400 series per call). Now, when no rollup is fine enough, the
  finest compatible rollup is chosen (signal `ResolutionLimited`) as soon as it delivers at least
  `SeriesResolutionRequest.MinRollupPoints` points over the window (new init property, default 150, `0` = off, negative refused).
  A 1-day window keeps the raw data (hourly gives 24 points < 150). Rules: `docs/concept-resolution-aware-series-queries.md`, §4.2.
  - **Behaviour change:** for a 7-day window over 15-minute data with an hourly rollup, targets of 672 points and more now
    resolve to the hourly rollup (168 points, `ResolutionLimited`) instead of the raw archive (`Ok`). Targets below 672 points
    resolved to the hourly rollup before as well. Callers that need the old rule set `MinRollupPoints = 0`.
  - No interface change: `SeriesResolutionRequest` is a record (new init property with a default), the planner is internal.

### Added

- **Blueprint seeds: escape for runtime placeholders (AB#8995).** A seed value with `$${name}` is stored as the literal
  `${name}`, without a variable lookup and without the "Unknown blueprint variable" warning (message 27). Use it for
  placeholders that are filled at run time, such as notification templates (`${count}`) or KPI formulas. It applies to
  `rtWellKnownName`, string attributes, string lists and records / record arrays at any depth, also when the tenant has no
  blueprint variables. An unescaped unknown `${name}` still warns and stays unchanged. Rules: `docs/blueprints.md`,
  "Keeping a runtime placeholder".
  - **Minimum engine version:** the first libs release after r3.5.75 that contains AB#8995. Older engines do not know the
    escape: when `name` is a known blueprint variable they store `$` followed by its value, an unknown `$${name}` stays the
    literal `$${name}` (with message 27). A blueprint may use the escape only once every cluster that installs it runs that
    release or later.
  - **Behaviour change:** a seed string that already contained `$${` was written as `$` followed by the substituted variable
    (or with a warning). No blueprint in the monorepo or the public blueprint catalog contained `$${` on 2026-11-15.
