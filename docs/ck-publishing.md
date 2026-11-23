---
description: How CK models are published (CI lanes) and how sibling models resolve through the local catalog.
applies_to: azure-pipelines.yml, src/ConstructionKit.Engine/ModelCatalogs/**, src/ConstructionKit.Engine/ModelRepositories/**
---

# CK model publishing lanes and sibling models in the local catalog

## CK Model Publishing Lanes (CI)

This repo publishes its CK models (`System`, `System.Files`, `System.StreamData`) from **inside the build**, not from a dedicated publish
step: `Directory.Build.targets`' `CkCompile` target runs `octo-ckc -c publish -c
$(OctoPublishCatalog) -r` for every project that sets `OctoPublishCkModel=true`. This repo
is special — it cannot consume its own `Meshmakers.Octo.ConstructionKit.MsBuildTasks`
package, so it bootstraps through its own `Exec`-based targets. Every other CK-owning repo
(e.g. octo-identity-services, octo-communication-controller-services) uses the packaged
targets, which do not re-enter the publish path from a test step.

**System models are published automatically from main (default flipped).** The shared catalogs are published by the
`validate-and-publish-ck-versions.yml` step of `azure-pipelines.yml`, not by the dotnet build. The pipeline parameter
`publishSystemCkModels` (boolean, **default true** since 2026-11-10) feeds the template parameter `publishOnMain` (`tpl-v1.8.0` and later; pinned: `tpl-v1.11.0`):
a main build runs ValidateVersion, the schema compile and the cascade check, which still refuse a breaking change declared as
minor, and publishes the System models; an already-published version is a no-op. The gate was introduced as default false after
System-2.6.0 reached `construction-kit-libraries-build` on 2026-10-23 before the re-pin; CK v2 range retention
 and the completed re-pin removed that exact-pin breakage. **To build main without publishing:**
*Run pipeline* on `octo-construction-kit-engine-CI` -> branch `main` -> untick *Publish System CK models to the CK catalogs (main)*
(CLI: `az pipelines run --id 108 --branch main --parameters publishSystemCkModels=false`). `r*` tags (release train) publish as
before. All three models are System models and are gated together.

**Test-only models are never published to a GitHub catalog.** `tests/TestCkModel`
(`Test`, `System.TestIdentity`) sets `OctoPublishCkModelToRemoteCatalog=false`, so its
`CkCompile` publishes to the `LocalFileSystemCatalog` only. The tests consume both models
embedded in `TestCkModel.dll`; nothing resolves them from a GitHub catalog. Both models were
removed from `construction-kit-libraries-build` and `meshmakers.github.io` on 2026-10-08. A new
test-only CK model project must set the same property.

Since `tpl-v1.0.0` of `meshmakers/octo-pipeline-templates` (pinned by the `ref:` of the
`pipelineTemplates` resource in `azure-pipelines.yml`, `tpl-v1.11.0`) no MSBuild step
publishes to a shared catalog. `update-build-number.yml` no longer exports `OctoPublishCatalog` /
`effectivePublishCatalog`. The shared catalogs are published only by
`templates/steps/validate-and-publish-ck-versions.yml`, which runs **after build and tests**, validates
System, System.Files and System.StreamData against the last published version, compiles them and publishes them
(never replacing a published version):

| Branch / ref | Catalog that receives System, System.Files, System.StreamData (Test / System.TestIdentity: LocalFileSystemCatalog only, always) |
| ------------ | -------------------------------------------------------------------------- |
| `refs/heads/main` | `PrivateGitHubCatalog` → `meshmakers/construction-kit-libraries-build` |
| `refs/tags/r<X.Y.Z>` | private **and** `PublicGitHubCatalog` |
| every other branch (`test/0.2-dev`, `dev/*`, feature branches) | validate only, nothing published. The lane catalog `meshmakers/octo-catalog-dev` needs the `publishCkModelsToLaneCatalog` opt-in; this repo does not set it. |

**Every MSBuild step that can reach `CkCompile` passes `/p:OctoPublishCatalog=LocalFileSystemCatalog`
explicitly** — `Build src`, `Build samples`, `Build test CK model`, `Build tests` and `Test` all do (the
exception the template documents for solutions whose CK models depend on each other; the local catalog is
run-isolated through `OctoLocalCatalogRootPath`). Omitting it on any one of them lets this repo's own
`Directory.Build.targets` publish with whatever `OctoPublishCatalog` the environment holds.

**CI build/test layout.** The job builds every project exactly once and the
`Test` step runs with `--no-build --no-restore`:

1. `Build src` — each `src/**/*.csproj` (Release); compiles and publishes System and
   System.StreamData. DotNetCoreCLI runs one `dotnet build` per project, so SystemCkModel is
   built twice (via StreamDataCkModel's ProjectReference and by its own invocation). The
   remote-catalog leg of `CkCompile` is therefore stamped in `obj/.../octo-ck-remote-publish/`
   by catalog + SHA-256 of the compiled yaml and skipped when the identical content was already
   published from this output (before: System pushed twice, ~60 s, build 50758). The
   LocalFileSystemCatalog publish still runs on every build. Look for `Skipping publish of
   construction kit library ...` in the log.
2. `Build samples`.
3. `Build test CK model` — `tests/TestCkModel` with `/p:BuildProjectReferences=false`;
   publishes Test and System.TestIdentity to the LocalFileSystemCatalog only. It runs before the test projects because
   `Runtime.Engine.Tests` references `TestCkModel.dll` and the DotNetCoreCLI glob order is
   not guaranteed.
4. `Build tests` — `tests/**/*Tests.csproj`, the `*SystemTests` projects included (they need no running services; the GitHub write test skips itself without
   `CK_GITHUB_WRITE_TEST_REPO`/`CK_GITHUB_WRITE_TEST_TOKEN`), also with
   `/p:BuildProjectReferences=false`: the src projects are already built into the same
   output folders, so only the test assemblies compile.
5. `Test` — one `dotnet test --no-build --no-restore` per project, sequentially (keep it
   that way: Testcontainers run inside the agent's DinD sidecar with a 14 Gi limit).

Before this, `dotnet test` rebuilt each test project with its full reference graph,
including a second `CkCompile` + publish of System / System.StreamData (CkCompile re-runs
on every build by design, see `Directory.Build.targets`); that was ~6 of the ~8.5 min
`Test` step. If you add a test project that references a new non-src project, add that
project to a build step before `Build tests`, or `--no-build` fails with a missing
assembly.

**Debugging "my model is missing from PrivateGitHubCatalog".** Check the *publish* and the
*read* halves separately; they fail for completely different reasons:

1. Publish: `git log` in `meshmakers/construction-kit-libraries-build` and
   `ck-models/v2/<letter>/<Model>/<major>/catalog.json` (`latestVersion`, `publishedAt`).
   Correlate the timestamp with the CI build. If the commit is there, the pipeline did its
   job and nothing in this repo is at fault.
2. Read: the catalog is served over GitHub Pages, whose deploy workflow serializes a burst
   of publishes and only deploys the final push, so the live tree can lag a cascade by
   many minutes. On top of that, readers keep a cache at
   `~/.octo/ck-catalog/cache/private-github-catalog-cache*.json` (60 s max age;
   repository-scoped file name only on engines carrying that fix).
3. Which repository is the private slot bound to? `octo-cli -c ListCatalogs` prints the
   coordinates in the description — the main installations read
   `construction-kit-libraries-build`, the 0.2-dev instance reads `octo-catalog-dev`,
   where `System` still tops out at 2.2.2.

## Sibling CK Models and the Local Catalog

A CK model project that depends on another CK model **built in the same repository / build**
(System.StreamData → System) must follow two rules, or a parallel build can compile the dependent
against a stale or missing version of its sibling:

1. **Ordering — ProjectReference.** The dependent project references the dependency's project
   (`StreamDataCkModel.csproj` → `SystemCkModel.csproj`). `CkCompile` runs as the first dependency of
   `PrepareResources`, i.e. after `ResolveProjectReferences` built (and, with `OctoPublishCkModel=true`,
   published to the `LocalFileSystemCatalog`) the referenced model. Without the reference MSBuild may
   build both in parallel.
2. **Visibility — local catalog enabled.** The dependent sets `OctoLocalCatalogIsEnabled=true`, otherwise
   octo-ckc only sees the remote catalog caches, which know only already-published versions.

**The local catalog answers lookups from disk, not from its cache file.** `LocalFileSystemCatalog`
overrides both `IsExistingAsync` overloads to enumerate the compiled files
(`ck-models/v2/<letter>/<Name>/<major>/ck-<name>-<version>.json`) instead of reading
`<root>/cache/local-catalog-cache.json`. That cache file is shared by every octo-ckc process of a
parallel build, and a concurrent publish could overwrite it with a snapshot taken before a sibling model
was published; the dependent compile then failed with `Dependencies 'System-[2.4,3.0)' are unknown`
although the manifest was on disk (2026-10-06), or resolved a wide range to an older version (the
stale-pin class). `ListAsync` / `SearchAsync` still use the cache (listing only).
Pinned by `LocalFileSystemCatalogTests.IsExistingAsync_WithVersionRange_StaleCache_*`.

**Publish is atomic (review M10).** Because lookups trust file existence, `PublishAsync` writes the model to a
temp file *in the target directory* (`.ck-<name>-<ver>.json.<guid>.tmp`, never matched by the `ck-*.json`
enumeration) and renames it into place (`File.Move(..., overwrite: true)`; `File.Replace` on netstandard2.0),
deleting the temp file in all cases. The former `File.Copy(temp, target, overwrite)` rewrote the target in place, so
a parallel sibling compile could read a half-written model — and a concurrent reader even made the publish fail
("being used by another process"). Pinned by `PublishAsync_ForcedRepublish_ReaderNeverSeesAPartialFile_*`.

**Index files are atomic and serialized (review N3).** The three `catalog.json` index files (root, model library,
major versions) are read-modify-write. `PublishAsync` now updates them under a cross-process lock
(`ck-models/v2/.catalog-index.lock`, exclusive open = `flock` on Unix, `CatalogFileIo.AcquireLockAsync`) and every
index write — and the shared cache file (`CachedCatalog.WriteCacheAsync`) — goes through
`CatalogFileIo.WriteJsonAtomicallyAsync` (temp file in the same directory, renamed into place, temp always
deleted). Before, parallel publishers of sibling models lost each other's entries (models vanished from
`ListAsync` / `SearchAsync`) and readers could hit a half-written index. Pinned by
`PublishAsync_ParallelPublishersOnOneRoot_KeepEveryIndexEntry` (64 parallel publishers; red 3/3 before).

**The empty `ck-models/v2/.catalog-index.lock` file is expected.** It is created on the first publish and left in
place on purpose: the lock is the exclusive *open* of the file (released when the handle closes, also when a process
dies), not its existence, so a leftover file never blocks anyone. Deleting it after use would race with a waiting
publisher that already opened it. It is safe to delete while no build is running; nothing reads its content. The
leading dot keeps it out of the `ck-*.json` / `catalog.json` lookups. Temp files of an interrupted write
(`.<name>.<guid>.tmp` next to the target) are never read either and can be deleted at any time.

**Two catalog roots: `ck-models/v2/` and `ck-models/v3/` (F1.1-S6).** `CkCatalogLayout` decides where a
model is published: classic models (`ckLanguage` 1, exact pins) go to `ck-models/v2/`, `ckLanguage: 2` and
range-retaining models (and anything carrying `minEngineVersion`) to `ck-models/v3/`. Engines before CK v2 read
only `v2` and tolerate unknown JSON properties, so the separate root is what keeps them from misreading a v2
model. Engines from CK v2 on read both (`ReadRoots` = v3, then v2): `LocalFileSystemCatalog` lookups enumerate
both directories, the refresh merges both index trees (a model may have versions in both), and `GitHubCatalog`
takes the root from the cached `filePath`, otherwise probes v2 then v3 — v3 first only when the cache holds v3 models
(review G3 E-L4: no extra 404 per v1 model). A missing v3 root index is normal and is not retried, so it does not
spend the 404-retry budget. Each root has its own `catalog.json` tree and its own `.catalog-index.lock`.
A version lives in one root only: publishing it into the other root needs `force` and deletes the old file — in the
local **and** the GitHub catalog (review G3 E-M5). Known limitation (E-L3): the old root's `catalog.json` keeps a
stale listing entry for a moved version (listing only; lookups use the files). The catalog CI side (Pages deploy of `ck-models/v3/`) is F2.3.

**`minEngineVersion` (F1.1-S6).** The compiler writes `CkCompiledModelRoot.MinEngineVersion` =
`CkEngineVersion.CkV2MinEngineVersion` (**`3.5.1`**, the first libs release with the full CK v2 Phase 1 reader; a
deterministic constant — not the compiling engine's own version, so DebugL and release builds produce the same
output) for `ckLanguage: 2` and range-retaining models, **and the highest `minEngineVersion` of its resolved
dependencies** (review G3 E-M4: a v1 model on a v2 or range-retaining dependency gets it too and therefore lands in
`ck-models/v3/`, so an older engine never sees a model it cannot resolve); `null` otherwise (v1 output on v1
dependencies byte-identical). Models published earlier keep their stored value (published models are immutable; the
older `3.4.0` value stays valid for them). `ElementResolver` and both dependency resolvers refuse a model above the
running engine's version with **message 126** (`CkModelRequiresNewerEngine`, names the running engine as
major.minor.patch; a dependency is skipped like a 91): a 3.4.x or 3.5.0 engine refuses a v2 model, 3.5.1+ reads it.

**The running engine version is patch-exact.** `set-version.yml` stamps the AssemblyVersion as `Major.Minor.0.0`, so
it cannot tell 3.5.0 from 3.5.2. `CkEngineVersion.ReadRunningVersion` therefore reads, in this order, the
`AssemblyFileVersion` (`$(BuildNumberLong)`: `3.5.2.0` on an `r3.5.2` build, `0.1.YYMM.NNNN` on main, `3.5.N.R` on a
`test/3.5-*` build), then the `AssemblyInformationalVersion` (suffixes `-branch` and `+sourcelink` stripped), then
the AssemblyVersion; the first value that parses wins and nothing throws. The FileVersion is preferred because it is
always purely numeric. No pipeline-template change is needed. (`test/X.Y-*` lane builds report
`X.Y.<buildCounter>`, not a release patch.)

**Defined rules for non-release engines.** DebugL is `999.0.0` (accepts every model it can compare, refuses
`1000.0.0`). **Engines below 1.0 (main line, private-feed `0.1.*` builds) skip the check** — an explicit rule in
`CkEngineVersion.IsSatisfiedBy` (and `Current` is `null` for them): the main line carries no number comparable with
the release line, it is covered by the main-line floor `0.1.2610.9010` of
`octo-mesh-deployment/docs/ck-v2-engine-inventory.md` and by rebuilt images. **Tests of message 126 must not depend
on the ambient assembly version** (DebugL `999.0.0`, CI `0.1.*` = check skipped, r-tag builds `3.x`): pin it
with the internal test seam `using var _ = CkEngineVersion.OverrideCurrentForTests(new Version(3, 4, 149));`
(AsyncLocal, restored on dispose, never set in production; visible to Engine.Tests, Compiler.Tests and
the engine-mongodb `Runtime.Engine.MongoDb.IntegrationTests`), or pass `engineVersion` to `CheckModel`
directly. `CkEngineVersionTests` cover the version table (3.4.149/3.5.0 refuse, 3.5.1/3.5.2/3.6.0/999.0.0 accept,
0.x skips), the stamping (a dynamic assembly with AssemblyVersion `3.5.0.0` and FileVersion `3.5.2.0` reads 3.5.2)
and the unparsable fallback. Raise the constant when a later engine writes features this engine line cannot read.
`CatalogService.PublishAsync` resolves before it publishes, so it refuses such a model, too.

**Fail fast with the visible versions.** When a dependency range cannot be satisfied,
`CatalogDependencyResolver` lists the versions each readable catalog knows for that model
(`... 'System-[2.5,3.0)' does not match any visible version of System (LocalFileSystemCatalog: 2.4.0)`)
and names the ProjectReference rule (`DependencyResolutionMessageTests`).

Other repositories with sibling CK models must follow the same two rules (checked 2026-10-07:
`octo-construction-kit` — `Basic.Energy` and `EnergyCommunity` depend on `Basic` / `Basic.Energy`
without a ProjectReference; `octo-construction-kit-engine-mongodb` test models depend only on
System).
