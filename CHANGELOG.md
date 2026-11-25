# Changelog

Changes of the engine libraries (`Meshmakers.Octo.ConstructionKit.*`, `Meshmakers.Octo.Runtime.*`) that blueprint and model
authors or service owners need to know about. The CK models of this repository keep their own changelogs
(`src/SystemCkModel/ConstructionKit/CHANGELOG.md`, `src/StreamDataCkModel/ConstructionKit/CHANGELOG.md`). Versions are the libs
release tags (`r<X.Y.Z>`); entries without a release are listed under "Unreleased".

## Unreleased

### Added

- **Partial updates can be validated before they are written (AB#9755).** New hook
  `IPreDocumentUpdateValidation<RtEntity>` (`Runtime.Contracts.Repositories`): `BulkRtMutation` hands it the partial
  entities of a batch's updates (with `RtId` and `CkTypeId`) before anything of the batch is written - deletes, inserts
  and replaces included - so an implementation that throws refuses the whole batch. The pre-document modifications run
  on insert and replace only; this closes the gap for rules a stored entity must keep (engine-mongodb uses it for the
  number-range rules). Skipped, like the modifications, when `DisablePreDocumentModifications` is set; without a
  registered implementation nothing changes. Tests: `BulkRtMutationUpdateValidationTests`.
- **One blueprint draft validator for octo-bpm, the asset repository, the install path and MCP (AB#9371).**
  `IBlueprintDraftValidator` (`BlueprintCatalogs/`) checks manifest, seed data, the Secret seed lint and the CK model
  dependencies incl. their version ranges against the CK model catalogs; file sources for a directory, a zip/inline files
  and a catalog blueprint. Findings carry stable catalog codes 149-167 (`BlueprintDraftValidationCodes`); the texts of the
  former octo-bpm messages are unchanged, the Secret lint moves from the ad-hoc numbers 10/11 to 160/161.
  `octo-bpm validate|pack|publish` print each finding with its code and take `--ck-catalogs all|<names>` (default off,
  offline). `IBlueprintService.ValidateBlueprintAsync` delegates to the validator (the empty CK dependency check is gone).
  `ApplyBlueprintAsync` runs it before anything is installed and refuses only on findings that make the install fail
  anyway (149, 150, 151, 153, 155, 162, 164); every other finding is a warning in the install result. Regression: of 557
  catalog blueprint versions (public and private) 554 have no blocking finding; the 3 others (private
  MeshmakersAccounting 3.10.0, 3.15.0, MeshmakersAccounting.Tesla 3.1.15) miss declared seed files and fail today already.

### Fixed

- **A replace no longer looks like a new entity to the pre-document modifications (AB#6209).** `BulkRtMutation` copied
  the target key's `RtId` and `CkTypeId` into the replace payload only after the `IPreDocumentModification<RtEntity>`
  pipeline had run. A replace whose payload carried an empty or another `RtId` (for example an entity from
  `CreateTransientRtEntityAsync` passed to `ReplaceOneRtEntityByIdAsync`) therefore reached the modifications as a new
  entity: the AutoIncrement modifier of engine-mongodb took a new number, overwrote the stored one and advanced the
  counter. The target key now goes into the payload before the modifications run; inserts keep their own `RtId`. Tests:
  `BulkRtMutationReplaceTargetKeyTests`.
- **Blueprint applies of several tenants at the same time no longer share one import state (AB#9803, security).** The
  tenant setup on startup applies blueprints to the child tenants in parallel through one `BlueprintService`, which held
  one `ImportRtModelCommand`. Every import reset the command's state, so a tenant import that started cleared the entity
  and association queues, the duplicate check and the guard entries of the import still running for another tenant:
  seed entities or associations could go missing in one tenant or be flushed into another tenant's session, and every
  overlap logged `Duplicate RtId ... within one import payload` for a seed without duplicates. `BlueprintService` and
  `BlueprintMigrationExecutor` now take a new command for every import from `Func<IImportRtModelCommand>` (registered by
  `AddRuntimeEngine`, transient), and the results read after the import (guard entries, skipped seed edges, archive
  column and formula changes) come from that command. Chosen over serializing the imports: the parallel tenant setup
  stays, and no lock can deadlock. Every log line of a blueprint apply (also the import's) carries `TenantId` and
  `BlueprintId` in a log scope, and the duplicate warning names the tenant. No CK model change; `IImportRtModelCommand`
  is unchanged.

- **The entity rule engine keeps the order of the input for inserts and replaces (AB#9753).** `EntityRuleEngine`
  validates a batch in parallel and collected the inserts in a `ConcurrentBag`, so `RtEntitiesToInsert` (and the
  replaces) came out in an unspecified order and auto-increment numbers of one batch were not handed out in batch
  order. Inserts and replaces are now returned in the order of the `IEntityUpdateInfo` list; the validation still runs
  in parallel. Test: `EntityRuleEngineTests.ValidateAsync_InsertsAndReplaces_KeepTheInputOrder`.

- **Parallel CK model builds no longer fail writing the catalog cache (AB#9305).** Two CK compile tasks of one solution
  (parallel MSBuild nodes) or parallel `octo-ckc` runs that wrote the same catalog cache at the same time failed about
  every third build on Windows with `UnauthorizedAccessException` ("Access to the path is denied") or an `IOException` in
  `CatalogFileIo.MoveIntoPlace`: only `IOException` was retried, for about one second, and a reader holding the cache open
  blocked the rename. Both caches are fixed - the **CK model catalog cache** (`CachedCatalog`) and the **blueprint catalog
  cache** (`CachedBlueprintCatalog`), which wrote a temp file in `%TEMP%` and then deleted
  and moved the cache, so a parallel reader could find no cache at all:
  - writers are serialized by a cross-process lock file next to the cache (`.<cache file>.lock`, kept like the index
    locks); the write stays a temp file next to the cache renamed into place, so a parallel reader sees the old or the new
    cache, never a partial one, and the last writer wins; no temp file is left behind (an older engine writing the
    same cache at the same time can still leave the `~RF*.TMP` backup files of the Windows `File.Replace` behind);
  - a rename that still fails (a writer of an older engine without the lock, a reader or virus scanner holding the file)
    is retried on `IOException` and `UnauthorizedAccessException` with a growing delay for up to 30 seconds; the
    netstandard2.0 build (MSBuild on .NET Framework) no longer checks for the target and then replaces or moves it, a
    lost race is retried instead;
  - readers copy the cache into memory and close it before parsing (a handle open on Windows blocks a writer's rename),
    open it with `FileShare.Delete`, and retry an open that meets a rename;
  - a write that still fails names the cache path and the type of the last error.
  No interface change. Builds pick the fix up with the next `Meshmakers.Octo.ConstructionKit.MsBuildTasks` package.

### Added

- **Catalog numbers for the CK model validation refusals (AB#8502).** The message catalog (`MessageCodes.json`) has four
  new entries, so a tool branches on the number instead of a work item id plus a free-text `reason`:
  145 `CkModelSystemReferenceMismatch` (`modelId`, `referencedModelId`, `installedModelId`), 146
  `CkModelDependencyNotResolvable` (`modelId`, `detail`), 147 `CkModelDraftUnreadable` (`detail`) and 148
  `CkModelInlineDraftTooLarge` (`detail`). The tenant validation of engine-mongodb, the draft validation of the asset
  repository and the MCP service use them instead of 8492 and 9005; the import's `ModelValidationException` texts stay
  unchanged.

- **Seed associations to entities the tenant does not hold yet are kept and written later (AB#9651).** A blueprint seed
  edge whose target (or origin) exists neither in the seed nor in the tenant used to be dropped for good (AB#5005); the
  pipelines of a blueprint lost their `Executes` edge to the Mesh Adapter when the platform enabled Communication only
  after the install (AB#9578). `BlueprintService` now keeps such edges in the tenant (`System/TenantConfiguration`
  `Octo.Blueprints.DeferredSeedAssociations`), reports them as a warning in the install or update result (message 73) and
  writes them with any later blueprint seed import of the tenant once both ends exist (message 74) - for example the
  service-managed `System.Communication` seed that brings the Mesh Adapter. Never over another target of a to-one role
  (fail closed when the multiplicity is unknown), never for identity roles or edges with attributes, forgotten when a
  later version of the blueprint no longer declares them and on uninstall; the store is updated in a transaction with
  retry on a write conflict; details in `docs/blueprints.md`.
  `IImportRtModelCommand` gets the default member `SkippedAssociations` (record `RtImportSkippedAssociation`).
  **Compatibility:** binary and source compatible - a default interface member and a new record; implementations written
  before it report no skipped edges, and nothing is kept then. Services that apply blueprints (asset repository) and those
  that apply service-managed blueprints (Communication Controller) need the new engine for the edges to be written.

- **The applied and updated blueprint notifications carry the CK model dependencies (AB#9578).**
  `BlueprintAppliedNotification` and `BlueprintUpdatedNotification` get the init-only property `CkModelDependencies`
  (distinct names, sorted ordinally). Apply: the CK model dependencies of the root blueprint and of every blueprint the
  resolution installs for it, dependencies the tenant already runs in a newer version included, before the stream-data
  gate (the need of the blueprint, not what the apply installed). Update: the CK model dependencies of the target version.
  Hosts that republish the notifications (octo-asset-repo-services) forward it, so services can switch on what a blueprint
  needs (System.Communication, System.Ai) without reading catalogs. **Compatibility:** binary and source compatible - a
  property, not a positional parameter, so the record constructors and `Deconstruct` are unchanged; a notification created
  by code compiled against an older engine has `null`, which consumers must read as "unknown".

### Changed

- **A sibling-type record is never written through another type, and a delete by abstract type answers a hidden
  and a missing record alike (AB#7212).** In a tenant with data-permission policies, an update, replace or delete
  by a non-system caller (or an association change at its origin) of a record that is stored as a type outside the
  addressed CK type's subtree (a sibling type sharing the collection) answers exactly like the same change of a
  missing rtId and writes nothing; before, such a record was written or deleted through the other type, also when the
  caller could not see it. The data-permission write guard judges a stored record only when it is stored as the
  addressed type or one of its derived types, so a caller with restricted read gets "entity not found" before the
  payload is validated, as for a missing record; a caller with full read gets the validation error of an invalid
  payload and otherwise "entity not found", as for a missing record. `DeleteOneRtEntityByRtIdAsync` with an
  abstract CK type:
  - an rtId that does not exist answers "entity not found" instead of returning silently, like the same call with a
    concrete type;
  - a record the caller may not see answers the same way, under the addressed abstract type and never under the
    concrete type it is stored as;
  - a record stored as a type outside the addressed subtree answers not found and is not deleted;
  - a caller the type write restriction refuses for the addressed type or one of its derived types is refused before
    any read, for an existing and a missing rtId alike.

  Callers without a visibility question (system sessions, tenants without data policies, full read grant) keep their
  answers for a missing record: an invalid payload is a validation error, a valid one is not found. Inserts are
  unchanged.

  Cost: system sessions and tenants without policies pay no extra read. Under policies, a valid change set pays one
  read per addressed CK type whose collection also holds concrete types outside its subtree (the targets the guard has
  already read are not read again); types with a collection of their own pay nothing.
- **A catalog server that never answers no longer hangs the CK build (AB#9388).** A GitHub Pages connection that was
  accepted but never sent response headers kept a CK compile at "Refreshing construction kit model library cache" for
  20 minutes and more: every catalog request waited 3 x 100 s (HttpClient's default timeout, two retries), the refresh
  treated a timed-out root index like an unreachable source and went on with the next request and the next catalog, and
  the 5-minute limit of `CkCompile`/`CkRestore` only stopped waiting while the request ran on. Now each attempt of a
  catalog request has its own timeout of 30 s (two retries), a request that times out in every attempt stops the refresh
  with a `ModelCatalogException` naming the catalog and the URL (`IsRequestTimeout`) - except for a reader that can ask
  the repository (token and API reads, e.g. a CI publish): its refresh flags the cache as unreachable, as for any
  failing mirror, and stops asking the silent mirror (no v3 root after a timed-out v2 root, no further indexes after a
  timed-out library or major-version index) - and the time limit of
  `CkCompile`/`CkRestore` (and an MSBuild cancel) cancels the refresh through a `CancellationToken`; a canceled
  `CkCompile` neither runs the compatibility gate nor publishes. A refresh that read a GitHub catalog only partly (a root
  index was read, another index failed) marks its cache as partial: `ListAsync` and `SearchAsync` of that catalog, and
  `IsExistingAsync(CkModelId)` for a version the partial cache lacks, then fail with `OCTO-CK202` naming the catalog
  and saying the condition is usually transient, instead of answering an incomplete inventory; the catalog manager's
  single-model lookups (`GetAsync`, `TryGetAsync`, `IsExistingAsync(CkModelId)`) go on with the next catalog and fail
  with that error only when no catalog holds the model. A cache of a source that
  was not reachable at all (offline, no internet, 5xx for the root index) answers as before, so offline machines keep
  listing their local and embedded models. A catalog that can ask its repository (token, API reads) keeps answering
  from the partial cache, because its readers that need a complete answer complete it from the repository (AB#6588). API (additive, no existing signature changes): the new
  interfaces `ICancellableCatalogRefresh` (implemented by `CachedCatalog`: the GitHub and local file system catalogs),
  `ICancellableCatalogCacheRefresh` (engine catalog service and catalog manager) and `ICancellableHttpClientWrapper`
  add overloads with a required `CancellationToken`; `ICatalog`, `ICatalogService`, `ICatalogManager` and
  `IHttpClientWrapper` are unchanged, so their implementations and fakes in other repositories keep compiling.

- **Generated C# implements interfaces of another CK model (AB#9096).** A type that implements an interface declared in
  another model (e.g. `System/Named-1` on `Basic/NamedEntity`) generated the short name `IRtNamed`, and the build failed
  with CS0246. The source generator now finds the other model's generated interface in the referenced assemblies and
  emits it fully qualified (`global::<RootNamespace>.Generated.System.v2.IRtNamed`), also in the explicit member
  implementations and in `extends` of interfaces. The consuming model project needs a reference to the C# package of the
  other model (for System: `Meshmakers.Octo.ConstructionKit.Models.System`); without it the generator reports the new
  error **OM1006** naming that package. Models without foreign interfaces generate unchanged code.

- **Uninstall: keeps only what is referenced from outside or edited (AB#8966, follow-up of #371).** On kb-test2 the uninstall
  of System.Ai.Builder erased 0 of 46 entities. The `Referenced` rule spread along the seed's own `LinksTo` links between
  pages, and `Edited` never fired because the comparison left out `TenantOwned` attributes. The rules are now:
  - `Referenced` means a direct reference from an entity outside the blueprint, plus the seeded to-one children of a kept
    entity (its versions) and the to-one parents of a kept entity (a kept version keeps its page), so no kept entity points
    to an erased one. Associations the seed declares between its own entities no longer spread a keep, and they go with
    the erased entities.
  - The uninstall comparison counts `TenantOwned` attributes.
  - An entity outside the blueprint that attaches itself to a seed entity through a to-one association (a tenant-written page
    version) marks it `Edited`.

  `BlueprintEntityComparer.Compare` gains `includeTenantOwned` (default `false`; the update rule is unchanged). No contract
  change. Rules: `docs/blueprints.md`, "Tenant-owned seed entities on uninstall".

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
    and restarts. As soon as the oldest is due, all held obligations of the rollup run together: one job per merged range
    over the union of its series (late deliveries for the same day merge into one job);
  - a write or a merged interval with more than `StreamData:Recompute:MaxScopedRtIds` (**new setting**, default `200`,
    `0` = off) distinct rtIds falls back to the unscoped recompute;
  - chained rollups inherit the set and follow without waiting for another debounce interval (a multi-level chain over
    one or more ticks). Manual recomputes, backfills and manual writes keep their scope and are not held back. An
    unscoped obligation absorbs the set-scoped ones that lie entirely inside its range; a partly covered one stays
    set-scoped. A set refresh also releases parked single-entity obligations of its series;
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

- **Purpose-bound secret envelopes (AB#9127, stage 0).** `ISecretAttributeProtector` gains `Protect(string, SecretPurpose)` and
  `Unprotect(string, SecretPurpose, SecretAccessContext?)`. The purpose is added to the AES-GCM associated data of the `enc:v2`
  envelope, so a value protected for a purpose opens only for exactly that purpose and never as the value of a Secret attribute,
  and a Secret attribute value never opens for a purpose. The stored format is unchanged (`enc:v2:<kid>:...`), key rotation works as
  for every `enc:v2` value, and the purpose overload accepts only `enc:v2` (no clear text, no `enc:v1`). The new members have default
  implementations that throw `NotSupportedException`, so other implementations of the interface keep compiling. First consumer: the
  sealed pipeline-execution values of the communication controller (`ExecutionEnvelope`).

- **Uninstall removes unedited tenant-owned seed entities; dry run (AB#8966).** `IBlueprintService.UninstallAsync` used to keep
  every unlocked seed entity "as user data", so uninstalling `System.Ai.Builder` on kb-test2 erased 0 entities (46 pages and
  versions kept). Now a tenant-owned seed entity (`rtBlueprintLocked: false` in the seed) is erased when it still equals the
  seed of the installed version (the AB#8778 comparison, no stored hash) and it shares no association with an entity that
  stays (any entity of the blueprint's seed that stays; outside that seed, anything but its own seed-declared links); otherwise it is kept and listed in the new
  `BlueprintUninstallResult.TenantOwnedKept` with the reason `Edited`, `Referenced` (with `ReferencedBy`), `NotComparable` or `EraseFailed`.
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
