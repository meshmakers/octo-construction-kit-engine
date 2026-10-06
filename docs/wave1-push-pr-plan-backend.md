# Wave 1 — push and PR plan (backend)

> Epic AB#5528 (SECRET attribute value type) + key ring (AB#5536) + artifact storage (AB#5559–AB#5565).
> State 2026-10-06, after `git fetch` in every repo. **Preparation only**: nothing pushed, no PR opened, no pipeline queued.
> Update 2026-10-06 (later): **O1 decided = option A** (System 2.5.0 ships with wave 1, two fleet switches accepted). The System 2.5 re-pin branches are prepared locally (§1 rows 20–25, §4.2), Stage B is spelled out in §9–§12. **Blocker found while preparing them: the CI version gate rejects PATCH re-pins (§4.4).**
> Context: [secret-rollout-progress.md](secret-rollout-progress.md), [concept-secret-attribute-type.md](concept-secret-attribute-type.md), [secret-sweep-dump-storage.md](secret-sweep-dump-storage.md).

## 1. Summary

| # | Repo | Branch | Remote | Ahead / behind origin/main | Trial rebase onto origin/main | Build after rebase | Classification |
|---|---|---|---|---|---|---|---|
| 1 | octo-construction-kit-engine | feat/gerald/secret-attribute-type | GitHub meshmakers | 45 / 0 | clean (fast-forward) | skipped (clean) | **Gate: System 2.5.0 cascade** (§4) |
| 2 | octo-sdk | feat/gerald/secret-attribute-type | GitHub meshmakers | 20 / 0 | clean (fast-forward) | skipped | safe after engine |
| 3 | octo-construction-kit-engine-mongodb | feat/gerald/secret-attribute-type | GitHub meshmakers | 8 / 0 | clean (fast-forward) | skipped | safe after engine |
| 4 | octo-common-services | feat/gerald/secret-attribute-type | GitHub meshmakers | 3 / 0 | clean (fast-forward) | skipped | safe after engine |
| 5 | octo-communication-sdk | feat/gerald/secret-attribute-type | GitHub meshmakers | 9 / 0 | clean (fast-forward) | skipped | safe after sdk |
| 6a | octo-mesh-adapter | feat/gerald/secret-key-ring | GitHub meshmakers | 1 / 0 | clean (fast-forward) | skipped | safe now (chart, inert without values) |
| 6b | octo-mesh-adapter | feat/gerald/secret-attribute-type (contains 6a) | GitHub meshmakers | 9 / 0 | clean (fast-forward) | skipped | System-cascade member (image is deployed to every tenant) |
| 7 | octo-bot-services | feat/gerald/secret-attribute-type | GitHub meshmakers | 17 / 0 | clean (fast-forward) | skipped | safe after engine/common (artifact store opt-in) |
| 8 | octo-communication-controller-services | feat/gerald/secret-attribute-type | GitHub meshmakers | 13 / 0 | clean (fast-forward) | skipped | **must wait (phase 3: System.Communication 3.40.0)** |
| 9 | octo-asset-repo-services | feat/gerald/secret-attribute-type | GitHub meshmakers | 10 / 0 | clean (fast-forward) | skipped | safe after engine; SchemaProvider rebuild |
| 10 | octo-identity-services | feat/gerald/secret-attribute-type | GitHub meshmakers | 10 / 0 | clean (fast-forward) | skipped | **must wait (phase 3: System.Identity 2.22.0)** |
| 11 | octo-ai-services | feat/gerald/secret-attribute-type | GitHub meshmakers | 6 / 0 | clean (fast-forward) | skipped | **must wait (phase 3: System.Ai 3.13.0)** |
| 12 | octo-mcp-service | feat/gerald/secret-attribute-type | GitHub meshmakers | 7 / 0 | clean (fast-forward) | skipped | safe after sdk + bot |
| 13 | octo-cli | feat/gerald/secret-attribute-type | GitHub meshmakers | 5 / 0 | clean (fast-forward) | skipped | safe after sdk + bot |
| 14 | octo-documentation | feat/gerald/secret-attribute-type | GitHub meshmakers | 7 / 0 | clean (fast-forward) | skipped | merge with the release (documents unreleased features) |
| 15 | octo-helm-pro | feat/gerald/secret-attribute-type | GitHub meshmakers | 1 / 0 | clean (fast-forward) | n/a (helm) | safe now (key ring env only when the value is set) |
| 16a | octo-helm-core | feat/gerald/secret-key-ring | GitHub meshmakers | 1 / 0 | clean (fast-forward) | n/a (helm) | safe now (old engines ignore the env) |
| 16b | octo-helm-core | feat/gerald/secret-attribute-type (= 16a + 7eca5c9) | GitHub meshmakers | 2 / 0 | clean (fast-forward) | n/a (helm) | safe (bot persistence/artifactStorage off by default, byte-identical render) |
| 17 | octo-communication-operator | feat/gerald/secret-key-ring | GitHub meshmakers | 1 / 0 | clean (fast-forward) | skipped | safe now (opt-in `ReceivesClusterSecrets`) |
| 18a | octo-mesh-deployment | feat/gerald/secret-key-ring | **Azure DevOps** meshmakers/OctoMesh | 1 / 1 | **clean** (worktree, 1 commit replayed) | n/a | safe after 16a + 17 released (macro guard) |
| 18b | octo-mesh-deployment | feat/gerald/artifact-storage (contains 18a) | **Azure DevOps** meshmakers/OctoMesh | 5 / 1 | **clean** (worktree, 5 commits replayed) | n/a | **must wait** for infra apply + Vault + k8s Secret per cluster |
| 19 | meshmakers-infrastructure | feat/gerald/artifact-storage | **Azure DevOps** meshmakers/OctoMesh | 7 / 0 | clean (fast-forward) | n/a | safe to merge (behind `octo_artifacts_enabled=false`); apply = human |
| 20 | octo-bot-services | feat/gerald/system-2.5-repin (worktree `main/wt-repin-bot-services`, from local main `28d4556`) | GitHub meshmakers | 1 / 0 | n/a (new) | DebugL green, 181/181 tests | Stage B re-pin (System.Bot) — **version gate, §4.4** |
| 21 | octo-communication-controller-services | feat/gerald/system-2.5-repin (`main/wt-repin-communication-controller-services`, from `fd39095`) | GitHub meshmakers | 1 / 0 | n/a (new) | DebugL green, unit 1042/1042; integration see §4.2 | Stage B re-pin (System.Communication) — **§4.4** |
| 22 | octo-identity-services | feat/gerald/system-2.5-repin (`main/wt-repin-identity-services`, from `dddaa40`) | GitHub meshmakers | 1 / 0 | n/a (new) | DebugL green, 752 passed / 1 skipped (unit + integration) | Stage B re-pin (System.Identity) — **§4.4** |
| 23 | octo-ai-services | feat/gerald/system-2.5-repin (`main/wt-repin-ai-services`, from `e9566e2`) | GitHub meshmakers | 1 / 0 | n/a (new) | DebugL green, unit 7/7; integration see §4.2 | Stage B re-pin (System.Ai) — **§4.4** |
| 24 | octo-common-services | feat/gerald/system-2.5-repin (`main/wt-repin-common-services`, from `864cd4c`) | GitHub meshmakers | 1 / 0 | n/a (new) | DebugL green, 192/192 | Stage B re-pin (System.Notification) — **§4.4** |
| 25 | octo-report-services | feat/gerald/system-2.5-repin (`main/wt-repin-report-services`, from `5eb13cb`) | GitHub meshmakers | 1 / 0 | n/a (new) | DebugL green, 4/4 | Stage B re-pin (System.Reporting) — **§4.4** |
| — | octo-tools | key ring | GitHub meshmakers | — | **already on origin/main** (`d0b22d3 AB#5536 New: local SECRET attribute key ring…` verified as ancestor of origin/main); local `feat/gerald/secret-key-ring` = main, 0 ahead | — | done; local branch can be deleted by the owner |

Trial-rebase method: every branch except octo-mesh-deployment has `behind = 0`, i.e. origin/main is an ancestor and the rebase is a no-op fast-forward. octo-mesh-deployment was rebased in a throwaway worktree (`main/wt-plan-mesh-deployment`, temp branch `tmp/wt-plan-artifact-storage`) onto `ff82235 Release manifest: hotfix weclapp 3.4.146`: no conflicts; worktree and temp branch removed. No conflicts anywhere ⇒ no DebugL build was needed (the last full `Invoke-BuildAll -configuration DebugL` on these HEADs was 38/38 green per the progress doc). Limitation: the PR CI builds against the **published** feed, not the local 999 feed, so each PR is only green once its upstream PRs are merged and published (§3).

## 2. CI trigger notes

| Repo(s) | Push trigger | PR validation | What publishes |
|---|---|---|---|
| all .NET repos (engine, sdk, engine-mongodb, common, comm-sdk, mesh-adapter, bot, controller, asset-repo, identity, ai, mcp, cli, operator) | `branches: dev/*, test/*, main`; `tags: exclude '*'` ⇒ **pushing `feat/*` builds nothing** | no `pr:` key ⇒ GitHub PR builds run with the ADO default (all target branches), Release config; publishing is gated on `isMain`/`isRelease` | `main`: NuGet prerelease, Docker image, CK models to **PrivateGitHubCatalog** (`effectivePublishCatalog`), then main CD to **test-2** (`deploy-octo-mesh-core-services-test-2` is triggered by the service CIs and `octo-helm-core-CI` on main). `r*` tag (queued by the release trains, the tag push itself starts nothing): **PublicGitHubCatalog** + release images |
| octo-helm-core, octo-helm-pro | `dev/*, test/*, main` | default | main CI = chart publish; **helm-core main CI triggers the core-services test-2 CD** |
| octo-documentation | `dev/*, test/*, main` and tags `r*` | default | main/`r*` publish the docs site |
| octo-mesh-deployment, meshmakers-infrastructure (ADO repos) | no repo CI on the changed paths (deploy pipelines read values from main at deploy time; infra is applied by Semaphore/Ansible by hand) | ADO branch policies (check per repo) | nothing on merge; the **next deploy** of any cluster reads the merged values |

Consequence: merging a library PR to main is not inert — the next downstream main build picks up the new prerelease package and main CD rolls it to test-2.

## 3. Merge order and dependencies

```
Stage A (safe now, independent of the engine):
  octo-helm-core  secret-key-ring (16a)  ──► octo-communication-operator (17) ──► octo-mesh-deployment secret-key-ring (18a)
  octo-helm-pro (15) · octo-mesh-adapter secret-key-ring (6a) · meshmakers-infrastructure (19, merge only)
  octo-helm-core 7eca5c9 (16b, inert defaults)

Stage B (one coordinated release — System 2.5.0 cascade, see §4; exact order and runbook in §9):
  engine (System 2.5.0, System.StreamData 1.17.0)
    ► sdk ► engine-mongodb ► common-services (+ re-pin 24 System.Notification) ► communication-sdk
    ► re-pins in catalog order: bot 20 (System.Bot) ► controller 21 (System.Communication) ► ai 23 (System.Ai)
                                identity 22 (System.Identity) · report 25 (System.Reporting) · platform (System.UI, Studio PO, §12)
    ► octo-construction-kit "bump every CK model for System 2.5.0" (Basic first, train orders topologically)
    ► mesh-adapter (6b) ► bot (7) ► asset-repo (9, + SchemaProvider image) ► mcp (12), cli (13)
    ► test-2 rollout of ALL service images + adapter CD + per-tenant lane (§9.3)
    ► producer/app models + externally built adapters (§4.5, §11)
  docs (14) with the release

Stage C (phase 3, after key ring is live everywhere + adapter/operator hub Enforce AB#5063/AB#5059 + Studio phase 2 + app model changes):
  controller (System.Communication 3.40.0) ► identity (System.Identity 2.22.0 + System.Identity.Bootstrap 1.4.0) ► ai (System.Ai 3.13.0)
  ► app models (meshmakers-app, energy-community, one-time-ticket, report-services) with RevealSecret@1 pipelines

Stage D (per cluster): infra apply ► Vault ► k8s Secret octo-artifact-storage ► octo-mesh-deployment artifact-storage values (18b)
```

| CK model | New version | Repo | Dependency floor | Lands in |
|---|---|---|---|---|
| System | 2.5.0 (modelId-only change, the "engine knows SECRET" marker, decision 12) | engine | — | Stage B |
| System.StreamData | 1.17.0 (re-pin only; 1.16.0 pinned System 2.4.0 hard) | engine | System-[2.5,3.0) | Stage B |
| System.Communication | 3.40.0 (Secret credential attributes **+ AB#5583 `FoldedBefore`**) | controller | System-[2.5,3.0), System.Bot-[3.0,4.0) | Stage C |
| System.Identity | 2.22.0 (IdP ClientSecret → Secret) | identity | System-[2.5,3.0) | Stage C |
| System.Identity.Bootstrap | 1.4.0 (role `SecretManagement`) | identity | — | Stage C (or split earlier, §6) |
| System.Ai | 3.13.0 | ai | System-[2.5,3.0), System.Communication-[3.0,4.0) | Stage C |

## 4. The System 2.5.0 gate (lessons from the 2026-10-05 test-2 incident)

### 4.1 Why the engine PR is not "safe to merge" although the code is backwards compatible

The engine code is backwards compatible (legacy plaintext stays readable, nothing changes without the key ring, strict mode off). But `SystemCkIds.CkModelId.ToVersionRange()` is an **exact** range (`[2.5.0]`, `CkModelId.ToVersionRange` → `"[" + Version + "]"`). Every service built against the new engine refuses a system tenant that holds System 2.4.0 (`System tenant database does not exist…`), and every old pod refuses one that holds 2.5.0. On 2026-10-05 exactly this (System 2.4.0 via the main train + CD) took test-2 down: asset-rep failed for every tenant until identity imported the new System; then the not-yet-rolled comm-controller and **all mesh adapters** failed silently; ~70 models in 22 tenants went `ResolveFailed`.

Rule for wave 1: **do not let the engine merge roll to test-2 via the ordinary main CD piecemeal.** Merge Stage B as one batch, with the downstream PRs ready and approved, and run the test-2 tenant update lane immediately (§5). For staging/prod use the release train with prepared tenant updates, never the main train.

### 4.2 Hard pins: everything pinned to System 2.4.0 must be re-published

A model's `System-[x,3.0)` range is frozen to the System version installed at compile/import time. After System 2.5.0 is imported, every installed model pinned to System-2.4.0 is `ResolveFailed` until a new version pinned to 2.5.0 is in the catalog (publish-once ⇒ version bump required — MINOR under the CI version gate, §4.4; AB#5435 is the open root-cause item). That includes models **not** changed by wave 1:

Verified 2026-10-06 against the **published** baselines in PrivateGitHubCatalog (`meshmakers/construction-kit-libraries-build`): every model below pins `System-2.4.0`; the PublicGitHubCatalog (prod) is still on the System **2.3.0** generation for all of them.

Owned service models — prepared locally on `feat/gerald/system-2.5-repin` (worktrees under `main/`, one commit each, `System-[2.0,3.0)` → `System-[2.5,3.0)` + version bump, nothing else; blueprints unchanged, see note):

| Model | Published (private) pins | Prepared (PATCH, as instructed) | Gate-conformant (§4.4) | Repo / worktree | Commit |
|---|---|---|---|---|---|
| System.Bot | 3.3.0 → System-2.4.0 | 3.3.1 | **3.4.0** | octo-bot-services / `main/wt-repin-bot-services` | `64b7d89` |
| System.Communication | 3.39.0 → System-2.4.0, System.Bot-3.3.0 | 3.39.1 (compiled: System-2.5.0, System.Bot-3.3.1) | **3.40.0** ⇒ phase 3 becomes 3.41.0 | octo-communication-controller-services / `main/wt-repin-communication-controller-services` | `a5ed45f` |
| System.Identity | 2.21.0 → System-2.4.0 | 2.21.1 | **2.22.0** ⇒ phase 3 becomes 2.23.0 | octo-identity-services / `main/wt-repin-identity-services` | `2561283` |
| System.Ai | 3.12.0 → System-2.4.0, System.Communication-3.39.0, System.Bot-3.3.0 | 3.12.1 (compiled: System-2.5.0, System.Communication-3.39.1, System.Bot-3.3.1) | **3.13.0** ⇒ phase 3 becomes 3.14.0 | octo-ai-services / `main/wt-repin-ai-services` | `9075792` |
| System.Notification | 2.3.0 → System-2.4.0 | 2.3.1 | **2.4.0** | octo-common-services / `main/wt-repin-common-services` | `eddc2b5` |
| System.Reporting | 2.2.0 → System-2.4.0 | 2.2.1 | **2.3.0** | octo-report-services / `main/wt-repin-report-services` | `d479719` |
| System.StreamData | 1.16.0 → System-2.4.0 | 1.17.0 already on the engine branch, floor `System-[2.5,3.0)`, compiled output pins `System-2.5.0` (verified in `bin/DebugL`) — MINOR, gate-conformant | — | engine | (branch) |
| System.UI | 2.6.0 → System-2.4.0 (main); 2.7.0/2.8.0 only on `feat/gerald/studio-rebuild` (unpushed) | **not touched** (octo-platform-services is off-limits) | **2.7.0** from main, see §12 | octo-platform-services | — |

How it was verified (all local, DebugL, worktrees under `main/` so `Octo.User.props` and the `../nuget` 999 feed apply):

- Built with an **isolated** local catalog (`/p:OctoLocalCatalogRootPath=<scratch>`, seeded only with System 2.3/2.4/2.5) in catalog order bot → controller → ai, so the shared `main/.octo/local-catalog` (which holds the phase-3 System.Communication 3.40.0 / System.Ai 3.13.0 / System.Identity 2.22.0) was neither read nor written. Every compiled `ck-*.yaml` pins `System-2.5.0`; System.Communication pins `System.Bot-3.3.1`, System.Ai pins `System.Communication-3.39.1` + `System.Bot-3.3.1`. Building against the shared catalog would have pinned System.Ai to the phase-3 `System.Communication-3.40.0` — keep that in mind for any local build of these branches.
- Builds: 0 errors; 0 warnings except 2 pre-existing `NODE_TLS_REJECT_UNAUTHORIZED` node warnings in identity (environment).
- Tests: bot 181/181; controller unit 1042/1042 (MTP executable); identity unit 550/550 + integration 202 passed / 1 skipped; common 192/192 (SystemTests excluded per CLAUDE.md); report 4/4; ai unit 7/7.
- Controller integration (44/56 red) and ai integration (121/214 red) fail with `CkCacheException … not found in CkCache` **only because the local 999 feed is phase 3**: the `Models.System.Communication` package embeds 3.40.0 and `Models.System.Bot` 3.3.0, so a model pinned to `System.Bot-3.3.1` / `System.Communication-3.39.1` cannot resolve in the test tenant. Proof: rebuilding the same sources against a catalog whose newest entries match the feed (System.Bot 3.3.0, System.Communication 3.40.0) makes them green — controller integration 56/56, ai integration 214/214. The worktrees were rebuilt afterwards with the Stage B catalog, so `bin/DebugL` shows the Stage B pins. In CI (published feed, catalog order respected) this mismatch does not exist.
- Blueprints in these repos (System.Communication.MainLatest/Release `[3.22.0,4.0)`, System.Identity.Bootstrap `[2.14.0,3.0)`, System.Ai.Default `[3.1.2,4.0)`, System.Notification.Bootstrap `[2.1.0,3.0)`) are **left unchanged**: `MongoRuntimeRepositoryProvider.EnsureCkModelInstalledAsync` skips the install when a satisfying-or-higher version is present, the services import their own model into every tenant at startup, and System.Communication/System.UI are service-managed (floor redirected to the descriptor version, AB#4294). The floor trap bites app blueprints whose floor names a non-service model (§4.5).

Not owned (document only) — octo-construction-kit, app and customer producers: see §4.5.

### 4.3 Decision O1 — **decided 2026-10-06: option A**

- **Option A** — keep System 2.5.0 in wave 1: Stage B must also ship re-pin versions of System.Communication/Identity/Ai/Bot/Notification and all business models. Two fleet-wide cascades (2.5.0 now, model switches later).
- ~~Option B~~ (not taken) — keep the engine code in wave 1 but ship the System 2.5.0 bump with phase 3, so there is one cascade. Cost: the engine branch (System 2.5.0, StreamData 1.17.0, test CK models in engine/engine-mongodb/asset-repo that use SECRET and need System ≥ 2.5 per the compiler rule) has to be split — not a trivial change; needs a trial and a full build.

### 4.4 Blocker: the CI version gate rejects PATCH re-pins (open decision G1)

Every owned service repo runs `validate-ck-versions.yml` (`octo-ckc ValidateVersion` against PrivateGitHubCatalog, AB#4420) **before** it publishes, and the build fails on a finding. `CkSemVerClassifier.ClassifyDependencyChange` rates a compatible dependency version change (`System 2.4.0 → 2.5.0`) as **MINOR**. A patch re-pin is therefore `OCTO-CK100 VersionTooLow`. Verified locally with `octo-ckc ValidateVersion -cn LocalFileSystemCatalog -lcr <catalog with the published baselines>` on the six prepared re-pins:

```
System.Bot:          OCTO-CK100: Declared version 3.3.1 ... required MINOR bump ... at least 3.4.0
System.Identity:     OCTO-CK100: Declared version 2.21.1 ... at least 2.22.0
System.Notification: OCTO-CK100: Declared version 2.3.1 ... at least 2.4.0
System.Reporting:    OCTO-CK100: Declared version 2.2.1 ... at least 2.3.0
System.Communication / System.Ai (without System.Bot 3.3.1 in the catalog):
  Multiple versions of construction kit model 'System' were resolved as transitive dependencies: System-2.5.0, System-2.4.0. Conflicting versions are referenced by: System.Bot-3.3.0
```

With System.Bot 3.3.1 (System-2.5.0) added to the catalog, a scratch copy of System.Communication declared as **3.40.0** validates `VALID` (`MINOR Dependency 'System' 2.4.0 → 2.5.0`, `MINOR Dependency 'System.Bot' 3.3.0 → 3.3.1`). The precedent agrees: the System 2.4.0 cascade bumped MINOR everywhere (System.Bot 3.3.0, System.Reporting 2.2.0, System.UI 2.6.0, Demo.Tickets 1.1.0), and System.StreamData 1.17.0 on this branch says "MINOR bump (dependency version changed)". The "always patch" rule from AB#4999 predates the gate.

Two consequences, independent of the version numbers:
1. **Catalog order is a hard build dependency**, not only a runtime one: the controller CI cannot compile System.Communication until a System.Bot pinned to System 2.5.0 is in PrivateGitHubCatalog, and the ai CI needs System.Bot + System.Communication re-pins first (multi-version conflict above). Same for every app model that depends on Basic/Basic.* (octo-construction-kit must publish first).
2. Every dependency floor in the dependency chain must be satisfiable by the published catalog at CI time (`CheckDependenciesAsync`, OCTO-CK103): System 2.5.0 must be published before any re-pin PR builds.

**Open decision G1 (PO).** The prepared commits use PATCH versions as instructed; they will fail CI as they are.
- **G1-a (recommended):** re-pin with MINOR — System.Bot 3.4.0, System.Notification 2.4.0, System.Reporting 2.3.0, System.UI 2.7.0 (no collision), and System.Communication **3.40.0**, System.Identity **2.22.0**, System.Ai **3.13.0** for the re-pins, which moves phase 3 to **3.41.0 / 2.23.0 / 3.14.0** (the phase-3 branches are local and unpublished; renumber the `modelId` + description lines + any `3.40`/`2.22`/`3.13` references in their docs/tests; AB#5583 `FoldedBefore` then ships as part of 3.41.0 or as its own 3.41.0 with the secret switch at 3.42.0). Amending the re-pin commits is a one-line change per worktree (`modelId`) plus the commit message. Local side effect: the shared `main/.octo/local-catalog` and the 999 feed hold phase-3 content under 3.40.0/2.22.0/3.13.0; after the renumber the PO's next full DebugL build of both lanes overwrites them (local catalog publishes are forced) — build the phase-3 branches last.
- **G1-b:** keep PATCH and bypass the gate for these PRs. There is no bypass parameter in `validate-ck-versions.yml` (tpl-v0.5.x/0.6.x); it would need a template change or a temporary removal of the step in four repos. Not recommended.

### 4.5 Not owned: models outside the prepared repos that pin System 2.4.0 (document only)

Published version = highest in PrivateGitHubCatalog (`construction-kit-libraries-build`); public = PublicGitHubCatalog (`meshmakers.github.io`). Every one of them needs a new version built against System 2.5.0 **after** its dependencies are re-published. "Gate" = minimum version per §4.4; "patch" = the PO's original convention (only valid with G1-b). Local checkouts are partly stale (e.g. meshmakers-app HEAD has Meshmakers.Accounting 3.1.0, origin/main 3.2.0) — bump from origin/main.

| Model | Producer repo (local checkout) | Private (pins) | Public (pins) | New version gate / patch | Notes |
|---|---|---|---|---|---|
| Basic | octo-construction-kit (`main/octo-construction-kit`) | 2.2.0 (System-2.4.0) | 2.1.0 (System-2.3.0) | 2.3.0 / 2.2.1 | first; everything below depends on it |
| Basic.Accounting | octo-construction-kit | 1.8.0 (Basic-2.2.0, System-2.4.0) | 1.7.0 | 1.9.0 / 1.8.1 | |
| Basic.Energy | octo-construction-kit | 1.8.0 (Basic-2.2.0, System-2.4.0) | 1.7.0 | 1.9.0 / 1.8.1 | |
| Environment | octo-construction-kit | 3.4.0 | 3.3.0 | 3.5.0 / 3.4.1 | |
| Industry.Basic | octo-construction-kit | 2.4.0 | 2.3.0 | 2.5.0 / 2.4.1 | before Industry.* |
| Industry.Energy | octo-construction-kit | 3.4.0 | 3.3.0 | 3.5.0 / 3.4.1 | |
| Industry.Fluid | octo-construction-kit | 2.3.0 | 2.2.0 | 2.4.0 / 2.3.1 | |
| Industry.Logistics | octo-construction-kit | 3.1.0 | 3.0.0 | 3.2.0 / 3.1.1 | |
| Industry.Maintenance | octo-construction-kit | 2.3.0 | 2.2.0 | 2.4.0 / 2.3.1 | |
| Industry.Manufacturing | octo-construction-kit | 2.4.0 | 2.3.0 | 2.5.0 / 2.4.1 | |
| EnergyCommunity | octo-construction-kit | 4.7.0 (Basic-2.2.0, Basic.Energy-1.8.0, System-2.4.0) | 4.6.0 | 4.8.0 / 4.7.1 | |
| OctoSdkDemo | octo-construction-kit | 2.3.0 | 2.2.0 | 2.4.0 / 2.3.1 | |
| Meshmakers.Accounting | meshmakers-app (`main/meshmakers-app`) | 3.2.0 (Basic-2.2.0, Basic.Accounting-1.8.0, System-2.4.0) | 3.1.0 | 3.3.0 / 3.2.1 | after Basic/Basic.Accounting; blueprints MeshmakersAccounting / .Tesla floor `Meshmakers.Accounting-[3.1.0,4.0)`, `Basic-[2.0.3,3.0)`, `Basic.Accounting-[1.7.0,2.0)` ⇒ raise to the new versions (floor trap) |
| Meshmakers.Accounting.Tesla | meshmakers-app | 1.2.0 (System-2.4.0) | 1.1.0 | 1.3.0 / 1.2.1 | blueprint floor `[1.1.0,2.0)` ⇒ raise |
| EnergyCommunity.Registration | energy-community (`main/energy-community`) | — (in no catalog) | — | 2.5.0 / 2.4.1 | ImportCk lane (`ck/bin/.../out/ck-energycommunity.registration-2.yaml`); EnergyCommunity.Base blueprint floors `Basic-[2.1…)`, `Basic.Energy-[1.7…)`, `EnergyCommunity-[4.6…)`, `System.StreamData-[1.13…)` ⇒ raise |
| EnergyLanding.Community | landing-pages (`main/landing-pages`) | 1.1.0 (System-2.4.0) | 1.0.0 | 1.2.0 / 1.1.1 | |
| Demo.Tickets | one-time-ticket (`main/one-time-ticket`) | 1.1.0 (System-2.4.0) | 1.0.1 | 1.2.0 / 1.1.1 | OneTimeTicket.* blueprint floor `Demo.Tickets-[1.0.1,…)` ⇒ raise + blueprint bump |
| Wwc26.Challenge | wwc26-landing-page (`main/wwc26-landing-page`) | — | — | 1.3.0 / 1.2.2 | ImportCk lane (no publish, AB#5101) |
| FamilyOs | family-os (`main/family-os`) | 1.1.0 (System-2.4.0) | 1.0.1 | 1.2.0 / 1.1.1 | FamilyOs.* blueprint floor `[1.0.1,…)` ⇒ raise |
| FdaSeen | octo-fda-seen (`~/RiderProjects/fda/seen/octo-fda-seen`) | 1.0.1 (Basic-2.2.0, System-2.4.0) | — | 1.1.0 / 1.0.2 | FdaSeen.Base floors `Basic-[2.2.0…)`, `FdaSeen-[1.0.1…)` ⇒ raise; public publish with `OctoPrivateGitHubCatalogIsEnabled=false` |
| EnergyIQ | demo-energy-iq (`main/demo-energy-iq`) | 2.11.0 (Basic-2.2.0, Basic.Energy-1.8.0, System-2.4.0) | — | 2.12.0 / 2.11.1 | |
| SmartMeterInsights | smart-meter-insights (`main/smart-meter-insights`) | 1.3.0 (Basic-2.2.0, Basic.Energy-1.8.0, System-2.4.0) | 1.2.0 | 1.4.0 / 1.3.1 | SmartMeterInsights.Base floors (`Basic-[2.0.3…)`, `Basic.Energy-[1.4…)`, `SmartMeterInsights-[1.2.0…)`, `System.StreamData-[1.7…)`) ⇒ raise |
| Loxone | octo-adapter-loxone (`main/octo-adapter-loxone`) | 4.9.0 (Basic-2.2.0, System.Communication-3.39.0, System-2.4.0, System.Bot-3.3.0) | 4.7.0 | 4.10.0 / 4.9.1 | **after** System.Bot + System.Communication re-pins; ranges `Basic-[2.0,)`, `System.Communication-[3.12,)` are open-ended ⇒ would pick the phase-3 3.4x line later |
| System.UI | octo-platform-services (Studio PO) | 2.6.0 (System-2.4.0) | 2.5.0 | 2.7.0 / 2.6.1 | §12 |
| octo-construction-kit sample blueprints | octo-construction-kit | — | — | — | Samples.* floors (`Basic-[2.0.3…)`, `Industry.*-[2.2.0…)`, `System.Notification-[2.1.0…)`) name pre-2.4 versions — fine for tenants that have the models, broken on fresh tenants (pre-existing; raise with the bump) |
| inactive / unpublished | ai-sandbox-ck `Sandbox-1.0.0`, fire-guardians `FireGuardians-1.0.0`, PaketService `PaketService-1.0.0`, `OctoEnergyDemo` (unversioned), octo-ai-services `docs/drafts` `System.Ai.Agents` (draft, not built) | — | — | — | no action unless a tenant still has them (`LibraryStatus`) |

Test CK models inside Stage B repos (engine `System.TestIdentity`/`Test`, engine-mongodb `Test-1/2`, asset-repo and mesh-adapter integration test models) are never published; they only need System ≥ 2.5 where they use SECRET (already done on the branches).

## 5. Human release-lane steps

| Step | Who/where | Detail |
|---|---|---|
| 1 | Review | Engine `Directory.Build.targets` change (CK compile after project references; StreamData uses the local catalog — CI already passes `/p:OctoLocalCatalogIsEnabled=true`) |
| 2 | Stage A merges | helm-core 16a → operator 17 (chart 0.11.0) → mesh-deployment 18a; helm-pro (reporting 0.3.0, mcp 0.2.0, ai 0.24.0); mesh-adapter 6a. Verify Vault `instance_secret_key` exists per cluster (`setup-vault-octomesh-secrets.yml`, never rotate an existing one), `aiInstanceSecretKey == communicationInstanceSecretKey`, key backed up in Keeper; edge operator pipelines must pass the key too |
| 3 | Stage B prep | All Stage B PRs green against each other's prereleases (merge in order, wait for each NuGet publish — "publish the contract before the consumers"); the re-pin PRs of §4.2 (versions per decision G1, §4.4) |
| 4 | Stage B on test-2 | Full order and per-tenant runbook in §9. Merge batch; make sure **all** pods roll (comm-controller, platform, report, refinery-studio included), identity first imports System 2.5.0; re-run `adapter-mesh-test-2-CD` if it failed in "Enumerate tenant universe" (incident 49625); then per tenant `FixAll -w -y` + `ClearCache -tid <t> -y`, check `LibraryStatus` (FixAll/ImportFromCatalog `-w` can report success without installing) |
| 5 | SchemaProvider | Rebuild/deploy the schema-provider image for the GraphQL schema change (`deploy-octo-mesh-schema-provider.yml`) |
| 6 | Release | services/helm/catalog trains with `r*` tags (helm-core, helm-pro, ck-libraries strictly sequential — shared Pages repo); catalogs train: `dryRun=true` skips the publish stage and proves nothing; a CK add/remove/major needs `allowCatalogRemovals` only for removals |
| 7 | dev lane | Next main → `test/0.2-dev` sync must translate System.Communication 3.40 / System.Ai 3.13 onto the 4.x line (dev-lane recipe step 4) |
| 8 | Stage D per cluster | Semaphore: infra playbooks/terraform with `octo_artifacts_enabled=true` (runbooks `docs/runbooks/octo-artifact-storage-*.md`), bot credentials into Vault, k8s Secret `octo-artifact-storage` in the namespace (and `octo-dev` on test-2), **then** merge the cluster's values (18b). Open: Hetzner location placeholder `fsn1`, scratch volume decision, bucket name checks |
| 9 | Phase 3/4/5 | as in the progress doc: model switches, encrypt sweep per environment, strict mode 14 days after zero plaintext |

## 6. Per-repo PR proposals

PR titles use the WP id; body bullets are proposals.

| Repo | PR title | PR body (bullets) |
|---|---|---|
| engine | `AB#5531 New: SECRET attribute value type in the engine (System 2.5.0)` | value type, schema, `RtSecretValue`, protector + key ring (`enc:v2:<kid>`), compiler/SemVer rules · write path, record carry-over, clear, sweep service (Encrypt/Reprotect/CleanupUnreadable), strict mode off by default · set-at timestamp, secret inventory service · OCTOENC1 streaming file protector for pre-sweep dumps · System 2.5.0 + System.StreamData 1.17.0 (**triggers the System cascade, §4**) · concept/handover docs (AB#5528/5542/5544/5559/5561/5562) |
| sdk | `AB#5534 New: SECRET attribute value type support in the SDK` | mapper, `ClearSecretAttributes`, generator, JSON converters delegating to the engine wire format · `InstanceSecretCrypto` decrypts enc:v1 only · bot client: sweeps, runs, restore-dump, environment status · `ClientSecretIsSet`, `SecretManagement` role, redacted debug paths |
| engine-mongodb | `AB#5533 New: Store, query-guard and index-guard SECRET attributes in MongoDB` | BSON sub-document with set-at · query/index refusals, diagnostics redaction · legacy plaintext normalised on reads, conditional rewrite incl. derived types in the root collection |
| common-services | `AB#5561 New: Artifact storage abstraction and secrets meter` | FileSystem/S3/Azure Blob providers · `Meshmakers.Octo.Secrets` meter in observability (AB#5531) · no behaviour change unless configured |
| comm-sdk | `AB#5538 New: Secret-safe pipeline diagnostics` | mask revealed secrets in node errors, snapshots, execution errors · refuse Secret values in type-switch nodes · redacted JSONPaths on debug snapshots · key-missing markers |
| mesh-adapter (6a) | `AB#5536 New: SECRET attribute key ring in the mesh adapter chart` | renders `OCTO_SECRETENCRYPTION__*` only when set · operator supplies it for `ReceivesClusterSecrets` |
| mesh-adapter (6b) | `AB#5538 New: RevealSecret@1 and SECRET handling in the mesh adapter` | `RevealSecret@1` (refuses identity System) · GetRtEntities/ApplyChanges handle SECRET, strict-mode refusals · mail credentials registered for masking · unknown key id = not set · integration Mongo pinned to 8.0.15 |
| bot | `AB#5539 New: Secret sweep job, post-restore handling and artifact store` | sweep jobs (serialized per tenant), run history, admin API with platform-role policies · restore without key ring = key-free Verify · artifact store for pre-sweep dumps/tenant dumps/restore staging (opt-in) · pre-sweep restore job, required key ids |
| controller | `AB#5537 New: Reveal and protect secrets in the communication controller (System.Communication 3.40.0)` | reveal for adapter configuration, decryption oracle closed · System.Communication 3.40.0 credential attributes, `ValueOverride.SecretValue` · deploys refused when a service-account secret is unreadable · redacted debug paths · **split AB#5583 out (see §7)** |
| asset-repo | `AB#5535 New: SECRET attribute value type in the GraphQL API` | `OctoSecretState {isSet,keyMissing,setAt}`, `clearSecretAttributes`, refusals · admin GraphQL `secrets {inventory, summary, usages}` (AB#5544) · SchemaProvider image rebuild needed |
| identity | `AB#5540 New: Identity-provider ClientSecret as SECRET (System.Identity 2.22.0)` | ClientSecret as Secret, `clientSecretIsSet/KeyMissing/SetAt` · providers with unreadable secrets skipped · `SecretManagement` role + Bootstrap 1.4.0 + one-time grant (AB#5544) · 503 `SecretEncryptionNotConfigured` |
| ai | `AB#5541 New: AI model secrets as SECRET value type (System.Ai 3.13.0)` | AI secrets via `RevealOrNull` · enc:v2 text in legacy slots never unwrapped · masked credential tails |
| mcp | `AB#5543 New: Secret-safe MCP tools and secret sweep tools` | `set_entity_secrets`/`create_entity_with_secrets` (high risk) · sweep, status, runs, inventory, restore-dump tools · provider client secrets scrubbed |
| cli | `AB#5543 New: SecretStatus, ReprotectSecrets and restore-dump commands` | write-only provider secret, `GetIdentityProviders` never prints it · CleanupUnreadable, DeleteSecretSweepDump, restore-dump |
| docs | `AB#5543 New: Documentation for the SECRET value type and the key ring` | user + operator guide, key-ring guide, restore flow, metrics · versioning rules (EN+DE) · German build has a pre-existing duplicate-label failure |
| helm-pro | `AB#5536 New: Secret encryption key ring env for reporting, MCP and AI charts` | derived from `communicationInstanceSecretKey`, nothing rendered when unset · chart bumps reporting 0.3.0, mcp 0.2.0, ai 0.24.0 |
| helm-core (16a) | `AB#5536 New: Deliver the SECRET attribute key ring to every engine host` | ring derived from the existing instance secret (k1 + legacy) · optional rotation override validated at render · operator chart 0.11.0 `clusterSecrets.instanceSecretKey` |
| helm-core (16b rest) | `AB#5560 New: Bot persistence and artifact storage values` | PVC, scratch, artifactStorage, serviceAccount/workload identity · all off by default, byte-identical renders |
| operator | `AB#5536 New: Inject the SECRET attribute key ring into opted-in workloads` | `ClusterSecrets.SecretEncryption*` · same `ReceivesClusterSecrets` gate as the data-store credentials |
| mesh-deployment (18a) | `AB#5536 New: Instance secret as SECRET key k1 for the operator` | `VAULT_instance_secret_key` wired with unresolved-macro guard · VAULT-SETUP doc |
| mesh-deployment (18b) | `AB#5562 New: Bot artifact storage values for all clusters` | test-2 Hetzner S3, staging-1/prod-2 Azure Blob, prod-1 Exoscale SOS · **merge per cluster only after its Secret exists** (otherwise the bot pod does not start) |
| infrastructure | `AB#5562 New: Artifact storage buckets and bot credential distribution` | per-cluster bucket/account + lifecycle backstop · runbooks keep keys off process args · opt-in `octo_artifacts_enabled` (default false) |

## 7. Cleanup proposals (proposals only — no history was rewritten)

| Repo | Commits | Proposal |
|---|---|---|
| sdk | `23e3da9` (deletes `SecretMarkerRules.cs` while the converters still use it) + `8fbb226` | **does not build alone** — squash the two (or move the deletion into `8fbb226`) if the PR is rebase-merged; irrelevant with squash/merge commit |
| sdk | `6824bdb` → `65f77e7`; `ef4f1a3` + `b284171` | fixup |
| engine | 9 progress-doc commits (`f8b9d000 9f243911 f675f1f6 b7c3895e 72d98781 55999b46 581b73e0 43db0d7b 78a7037b`) | squash into one, or keep the progress doc out of the PR entirely (internal tracking) |
| engine | handover doc chain `f2b15c36 0bebefb1 01fda94e 3f191672 410ade02 634638ba e47fba79 e8617785` | squash into one doc commit |
| comm-sdk | `346a2ab` adds CLAUDE.md notes, `a3802ae` moves them to docs | squash so CLAUDE.md is never touched |
| asset-repo | `0590d0f` adds CLAUDE.md notes, `6b1c858` moves them | squash |
| identity | `3b3e090` changes CLAUDE.md, `b127d86` restores it | squash (add+revert pair) |
| mesh-adapter | `3295807` **net +4 lines in CLAUDE.md** | move the section to README/docs (agent rule 6: CLAUDE.md must not change) |
| operator | `bd14682` **net +1 table row in CLAUDE.md** | move to README/docs |
| controller | `fda4ef7`, `4fc499d` (AB#5583, Bug under Epic 3444) | separate PR; note it adds `FoldedBefore` to System.Communication **3.40.0**, so the bug fix is coupled to the phase-3 model version — either ship it as its own 3.40.0 (and the secret switch becomes 3.41.0) or accept that AB#5583 waits for phase 3 |
| controller / identity / ai | model commits `c13f55b`, `3b3e090`, `ab93f44` | consider splitting each branch into a pre-phase-3 code PR (consumers ready for ciphertext) and the model-switch PR; feasibility not verified (later fixes may depend on the generated 3.40/2.22/3.13 types) |
| helm-core / mesh-adapter / mesh-deployment | key-ring commit shared by two branches | merge the key-ring PR with a merge commit or rebase-merge (not squash), otherwise the follow-up branch shows a duplicate commit |
| octo-tools | local `feat/gerald/secret-key-ring` (0 ahead) | delete locally after confirmation |

### Commits carrying a different WI id than the branch's WP

| Repo (expected WP) | Commits with other WI |
|---|---|
| engine (5531/5532) | 5528 (concept/progress), 5541 `d19f1c06`, 5542 `f2b15c36 0bebefb1`, 5544 `01fda94e 3f191672 410ade02 634638ba e47fba79`, 5539 `f9f7b91c fcadfd35 7c23aeb3`, 5566 `02aaa711`, 5561 `93c3fce5` (parent 2157), 5559 `90ef596c e8617785`, 5562 `9c7d8cee` (parent 2157) |
| sdk (5534) | 5540 `f6bee03`; 5543 `2c4d40e afa2b63`; 5544 `5dd6b8a ba2456d 293a6f3`; 5559 `0383660` |
| common-services (5531) | 5561 `5be5af9 1d7abe7` (Epic 2157, not 4969) |
| comm-sdk (5538) | 5534 `95808d1` |
| mesh-adapter (5538) | 5536 `3295807` (key-ring base) |
| bot (5539) | 5544 `88d813b`; 5534 `500299a a6108bb`; 5561 `acf3594 056e838`; 5559 `ef5b8b5 b1520ba 0076a96 73e610e` |
| controller (5537) | **5583 `fda4ef7 4fc499d`** (unrelated bug, Epic 3444) |
| asset-repo (5535) | 5544 `7a9f255 3381e54 8852cad` |
| identity (5540) | 5544 `00daef1 49bf716` |
| mcp / cli (5543) | 5559 `2ffad1a` / `242fe9f` |
| helm-core secret-attribute-type (5536) | 5560 `7eca5c9` (Epic 2157) |
| mesh-deployment artifact-storage (5562–5565) | 5536 `294f6b3` (key-ring base) |

AB#5544 is the Studio-UI WP but carries the backend admin API in five repos; AB#5528 itself is a Bug (Active) under Epic 4969, the WPs are Issues under 4969, the storage items 5560–5565 sit under Epic 2157.

## 8. Top risks

1. **System 2.5.0 = fleet-wide breaking change** (exact range check + hard model pins). Merging the engine and letting main CD roll piecemeal reproduces the 2026-10-05 test-2 outage. Needs a coordinated Stage B and the tenant update lane; see O1.
2. **Phase split vs. re-pin**: System.Communication 3.39, System.Identity 2.21, System.Ai 3.12 (plus Bot, Notification, UI, Reporting and all business/app models) pin System 2.4.0; with 3.40/2.22/3.13 held back for phase 3 they need re-pin versions in Stage B, or they go `ResolveFailed`. Re-pins are prepared (§4.2) but **the CI version gate requires MINOR, not PATCH (§4.4, decision G1)** — as committed they fail CI.
2a. **Catalog order is a build dependency**: re-pin CIs fail (OCTO-CK103 / multi-version conflict) until System 2.5.0 and their upstream re-pins are in PrivateGitHubCatalog (§10).
3. **Mesh adapters and external adapters** stay on old images if the adapter CD fails in the System-import window, and fail silently (no check rule on execution failures).
4. **Artifact-storage values before the Secret exists** stop the bot pod on the next deploy of that cluster (18b).
5. **AB#5583 coupled to System.Communication 3.40.0** — an unrelated bug fix is held by phase 3, or the version numbers must be re-planned.
6. CLAUDE.md changes in mesh-adapter and operator, and the non-building sdk commit `23e3da9`, should be fixed before review.

## 9. Stage B — batch contents, exact order and test-2 runbook

### 9.1 Preconditions

- Stage A merged and released; key ring verified per cluster (§5 step 2).
- Decision G1 taken and the re-pin commits amended accordingly (§4.4); phase-3 branches renumbered if G1-a.
- All Stage B PRs open, reviewed, approved — merged only in the order below, each waiting for its main CI (NuGet + PrivateGitHubCatalog publish) before the next.
- **Merge freeze** on every other repo that builds a service image for the duration (any unrelated main build of a service picks up the new engine prerelease and main CD rolls it to test-2 piecemeal — the 2026-10-05 failure mode).
- A test-2 window is announced; `octo-cli` contexts `test-2_<tenant>` exist for all tenants (`octo-cli -c UseContext -n test-2_<tenant>`; `-ctx`/`--context` does not exist); BotServiceUrl present in each context (otherwise ImportCk fails with "Bot services URI is missing").

### 9.2 Merge / publish order

| # | PR | Publishes | Wait for |
|---|---|---|---|
| B1 | engine (1) — System 2.5.0, System.StreamData 1.17.0 | NuGet prerelease + System 2.5.0 + StreamData 1.17.0 → PrivateGitHubCatalog | CI green **and** both models visible in `construction-kit-libraries-build` |
| B2 | sdk (2) → engine-mongodb (3) → common-services (4) → **re-pin 24 System.Notification** → communication-sdk (5) | NuGet; System.Notification re-pin → catalog | each CI green |
| B3 | **re-pin 20 System.Bot** | System.Bot re-pin → catalog; bot image | catalog entry (controller CI compiles against it) |
| B4 | **re-pin 21 System.Communication** | catalog; controller image | catalog entry (ai CI + Loxone compile against it) |
| B5 | **re-pin 23 System.Ai** · **re-pin 22 System.Identity** · **re-pin 25 System.Reporting** · **System.UI re-pin** (platform, Studio PO, §12) | catalog; ai/identity/report/platform images | CI green |
| B6 | octo-construction-kit: "bump every CK model for System 2.5.0" (like `c0e61de` for 2.4.0) | Basic … EnergyCommunity → catalog (train/CI orders topologically) | catalog entries |
| B7 | mesh-adapter 6b → bot 7 (rebased onto re-pin 20) → asset-repo 9 (+ SchemaProvider rebuild, `deploy-octo-mesh-schema-provider.yml`) → mcp 12, cli 13 | images | CI green |
| B8 | test-2 rollout + tenant lane (§9.3) | — | all tenants `LibraryStatus` clean |
| B9 | producer/app models (§4.5) in dependency order + externally built adapters (§11) | catalog + images | per producer |
| B10 | docs (14) | docs site | — |

The phase-3 PRs of controller/identity/ai (rows 8, 10, 11) are **Stage C** and must be rebased onto the re-pin commits; their model version must stay above the re-pin version.

### 9.3 test-2 rollout and per-tenant tenant-update lane

1. Make sure **every** octo-mesh pod runs a build with the new engine: asset-rep, identity, comm-controller, bot, platform, report, ai, mcp, office, refinery-studio (incident 2026-10-05: the comm-controller was not rolled and failed every execution). Identity's `CkModelUpgradeService` imports System 2.5.0 (+ System.Identity / System.Notification) into octosystem and all tenants — until then every new pod reports `System tenant database does not exist…`, which here means version mismatch, not a missing database.
2. Mesh adapters: check that `adapter-mesh-test-2-CD` ran **after** identity imported System 2.5.0; if it failed in "Enumerate tenant universe" (incident run 49625), re-queue it (definition 159, chartVersion = new chart, tenantFilter all). Since AB#5514 the octosystem adapter is included. Verify adapters by a pipeline execution, not by pod Ready.
3. Per tenant (all tenants incl. octosystem and nested ones):
   ```
   octo-cli -c UseContext -n test-2_<tenant>
   octo-cli -c LibraryStatus -io                       # before: list ResolveFailed / Update / Fix
   octo-cli -c RefreshCatalogs -cn PrivateGitHubCatalog
   octo-cli -c FixAll -w -y                            # updates every model the catalog has a 2.5.0 generation for
   octo-cli -c ClearCache -tid <tenant> -y             # -tid AND -y are both required
   octo-cli -c LibraryStatus -io                       # after: must show 0 need action
   ```
   - **`-w` can lie**: `FixAll -w` / `ImportFromCatalog -w` have reported "Import completed" while `LibraryStatus` still showed the old version `ResolveFailed`; a second identical run 30 s later installed it. Always re-check `LibraryStatus`; repeat once if unchanged.
   - Action `Update` with a migration path: `octo-cli -c CheckUpgrade -cn PrivateGitHubCatalog -m <Model>-<Version>` (the version is mandatory, otherwise "Target 1.0.0"/404), then `FixAll -w -y`.
   - Single model: `octo-cli -c ImportFromCatalog -cn PrivateGitHubCatalog -m <Model>-<Version> -w`.
   - Action `Fix` without any catalog version (EnergyCommunity.Registration, Wwc26.Challenge): build the producer locally against System 2.5.0 and `octo-cli -c ImportCk -f <ck/bin/.../out/ck-*.yaml> -w`, then `ClearCache`. **ImportCk is not atomic (AB#5507)**: an interrupted import removes the installed version — check `LibraryStatus` immediately and retry.
   - Blueprints after a model bump: `octo-cli -c RefreshBlueprintCatalogs`, `octo-cli -c PreviewBlueprintUpdate -tv <Blueprint>-<Version>`, `octo-cli -c UpdateBlueprint -tv <Blueprint>-<Version> -m Merge`.
   - Without `ClearCache` pipelines keep running against the old cache (`CkCacheException … not found in CkCache`).
4. Success criteria: `LibraryStatus` clean per tenant and pipeline executions succeed (energyiq throughput back to normal). Dash0 `octo.ck.library.state` lags up to 1 h (hourly sweep) and failed checks stay "ongoing" ~6 min — not a success signal. Models of a `ResolveFailed` library are not served via GraphQL, so entity counts are only possible after the fix.
5. staging/prod: never via the main train. Release train with prepared tenant updates; the public catalog is still on the System **2.3.0** generation, so the release jumps 2.3.0 → 2.5.0 for every model (the 2.4.0-generation versions exist only in the private catalog).

## 10. Catalog and train implications

- **Publish order in PrivateGitHubCatalog** (main CI publishes after its own gate): System 2.5.0 + System.StreamData 1.17.0 (engine) → System.Notification, System.Bot → System.Communication → System.Ai; System.Identity, System.Reporting, System.UI in any order after System; octo-construction-kit (Basic first) → app/producer models. This is a **build** dependency (§4.4: OCTO-CK103 and the multi-version conflict) and a runtime one (FixAll can only install what the catalog has).
- **Versions must exist in the catalog before the services that pin them roll out.** A service imports its own embedded model at startup, but the tenant lane (FixAll) and every dependent compile resolve through the catalog. Never roll an image whose model version is missing from the catalog of that environment.
- "Published" means visible to readers: the private catalog is served via GitHub Pages, whose deploy serializes bursts and can lag a cascade by many minutes (AB#4872), and readers cache for 60 s. Before queueing the next re-pin, check `ck-models/v2/<letter>/<Model>/<major>/catalog.json` (`latestVersion`) in `construction-kit-libraries-build` **and** on the Pages URL; on tenants run `RefreshCatalogs -cn PrivateGitHubCatalog` first.
- Publish-once: an already-published version is never replaced — a wrong re-pin needs a new version, not a re-run.
- **Release (prod/staging)**: `r*` tags publish to Private + PublicGitHubCatalog. The release trains run strictly sequentially (helm-core → helm-pro → ck-libraries; shared Pages repo `meshmakers.github.io`). Producers that publish to the public catalog must do so with `/p:OctoPrivateGitHubCatalogIsEnabled=false` (fda-seen lesson), otherwise the public model pins an unreleased private-only System line.
- **ck-libraries train (def 203)**: `dryRun=true` **skips the PublishCatalogs stage** and proves nothing about the artefacts; only `dryRun=false` verifies. A queue attempt does validate template expressions. Since AB#4577 the CK/blueprint lists are discovered (patch/minor bumps need no list edit); a CK **removal** needs `allowCatalogRemovals=true`; a CK major bump changes the file name (`ck-<name>-<major>.yaml`). Producer CIs publish their own CKs on the `r*` build, the train publish is a second pass — for blueprints there is no such net (blueprint versions must be bumped together with the floors, §4.5).
- dev lane: the next main → `test/0.2-dev` sync must translate the re-pin versions onto the 4.x line (dev-lane recipe step 4), and with G1-a the phase-3 numbers 3.41/2.23/3.14.

## 11. Adapter / image re-release list

Every image that embeds the engine (directly, via `Meshmakers.Octo.Sdk.MeshAdapter`/`Runtime.Engine`, or the System model) checks `SystemCkIds.CkModelId.ToVersionRange()` (exact `[2.5.0]` after the bump) and fails **silently** (Online + Healthy, every execution `TenantException`) against a tenant on a different System version. Re-release all of them right after Stage B on each environment; verify by a pipeline execution, not by pod health.

| Image / workload | Repo (local) | Embeds engine via | Lane | Last release tag |
|---|---|---|---|---|
| octo-mesh-adapter (all tenant mesh adapters **and** the octosystem adapter) | octo-mesh-adapter | Runtime.Engine | Stage B (6b) + `adapter-mesh-<env>-CD` (re-queue if it failed in the System-import window) | r3.4.147 |
| octo-finapi-adapter (meshmakers, salzburgdev on prod-1) | octo-adapter-finapi | Sdk.MeshAdapter | train def 204 `targetApp=octo-adapter-finapi` → CI 202 → CD 224 (no code change, floating `3.4.*`) | r1.0.12 |
| fda-seen-adapter (+ FdaSeen app) | `~/RiderProjects/fda/seen/octo-fda-seen` | Sdk.MeshAdapter | CI 222; **adapter CD 228 is manual** (instance, imageTag=auto) — CD 227 rolls only the app | (main builds) |
| eda adapter (voestalpine, energy communities) | octo-adapter-eda | Sdk.MeshAdapter | release `r*` + adapter CD | r3.4.146 |
| loxone adapter | octo-adapter-loxone | Sdk.Adapters + Loxone CK (needs CK re-pin 4.10.0, §4.5) | release `r*` | r3.4.138 |
| mqtt adapter | octo-adapter-mqtt | Sdk.MeshAdapter | no `r*` tag yet — check its CD | — |
| demo adapters | octo-adapter-demos | Sdk.MeshAdapter | release `r*` | r3.4.106 |
| zenon plug | octo-plug-zenon | Runtime.Engine + Models.System + Models.System.Communication | release `r*` (no Dockerfile — edge package) | r3.3.2 |
| maco-app backend | maco-app | Runtime.Engine.MongoDb | its own release | r0.0.12 |
| modbus plug, sap adapter | octo-plug-modbus, octo-adapter-sap | Sdk.Adapters only (hub client, no SystemContext) | not affected by the System range; re-release with the next platform release | r3.4.106 / r3.2.3 |
| octosystem adapter | (octo-mesh-adapter image) | — | covered by `adapter-mesh-*-CD` since AB#5514; on 2026-10-05 it needed `UpdateWorkloadChartVersion` + `DeployWorkload` by hand | — |

Detection on a cluster: list deployments whose image is not the current `octo-mesh-adapter:<new>` (finAPI lesson), and watch `System.Communication/PipelineStatistics.lastExecutionAt` — errors before `ReportExecutionStartAsync` are invisible to the controller statistics (AB#5491–5493).

## 12. System.UI — recommendation for the Studio PO

State: main has System.UI **2.6.0** (published, pins System-2.4.0). `feat/gerald/studio-rebuild` (17 commits ahead, unpushed) carries 2.7.0 (`ffee4ff`, EntityForm type) and 2.8.0 (`4d3dba5`, display attributes). System.UI is service-managed (platform-services imports it into every tenant), so the platform pod must roll in Stage B with a System.UI pinned to 2.5.0 — otherwise System.UI is `ResolveFailed` in every tenant after System 2.5.0 is imported.

**Recommendation: a separate re-pin of System.UI from main for wave 1**, not "repin inside 2.8.0":
- Change only `System-[2.0,3.0)` → `System-[2.5,3.0)` and the version: **2.7.0** under the CI gate (G1-a; 2.6.1 only with G1-b), branch from main, PR in Stage B step B5.
- Then rebase `feat/gerald/studio-rebuild` onto it: its tip version 2.8.0 stays valid (MINOR over 2.7.0, EntityForm/display attributes are additive); the intermediate "2.7.0" commit message becomes historical (never published). Raise the floor on the branch to `System-[2.5,3.0)` too.
- Why not inside 2.8.0: it would couple the fleet-wide System cascade to the readiness of the Studio rebuild (EntityForms, cockpit blueprints, frontend), and Stage B cannot ship until that branch is reviewed; a failed or late Studio PR would leave System.UI `ResolveFailed` fleet-wide. The separate re-pin is a one-line change with no schema risk.
- Local note: the shared `main/.octo/local-catalog` already holds the studio branch's 2.7.0/2.8.0 (pinning System-2.5.0); a main-lineage 2.7.0 re-pin would collide locally with the studio 2.7.0 entry — build it with an isolated `OctoLocalCatalogRootPath` as done for the other re-pins.
