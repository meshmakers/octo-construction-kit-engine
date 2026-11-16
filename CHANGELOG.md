# Changelog

Changes of the engine libraries (`Meshmakers.Octo.ConstructionKit.*`, `Meshmakers.Octo.Runtime.*`) that blueprint and model
authors or service owners need to know about. The CK models of this repository keep their own changelogs
(`src/SystemCkModel/ConstructionKit/CHANGELOG.md`, `src/StreamDataCkModel/ConstructionKit/CHANGELOG.md`). Versions are the libs
release tags (`r<X.Y.Z>`); entries without a release are listed under "Unreleased".

## Unreleased

### Changed

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

- **Uninstall removes unedited tenant-owned seed entities; dry run (AB#8966).** `IBlueprintService.UninstallAsync` used to keep
  every unlocked seed entity "as user data", so uninstalling `System.Ai.Builder` on kb-test2 erased 0 entities (46 pages and
  versions kept). Now a tenant-owned seed entity (`rtBlueprintLocked: false` in the seed) is erased when it still equals the
  seed of the installed version (the AB#8778 comparison, no stored hash) and it shares no association with an entity that
  stays (any entity of the blueprint's seed that stays; outside that seed, anything but its own seed-declared links); otherwise it is kept and listed in the new
  `BlueprintUninstallResult.TenantOwnedKept` with the reason `Edited`, `Referenced` (with `ReferencedBy`) or `NotComparable`.
  The new `DeletedEntities` lists what was erased. New `IBlueprintService.PreviewUninstallAsync` is the dry run
  (`DryRun = true`, nothing changed, no notification). Locked entities are erased as before; a reinstall adopts the kept
  entities and creates the erased ones once. Rules: `docs/blueprints.md`, "Uninstall".
  - **Behaviour change:** unedited, unreferenced tenant-owned seed entities are erased by an uninstall instead of kept.
  - Additive contract change: one new interface member (only `BlueprintService` implements `IBlueprintService`), new result
    properties with empty defaults.

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
