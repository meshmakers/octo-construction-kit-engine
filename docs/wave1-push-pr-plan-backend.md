# Wave 1 — push and PR plan (backend)

> Epic AB#5528 (SECRET attribute value type) + key ring (AB#5536) + artifact storage (AB#5559–AB#5565).
> State 2026-10-06, after `git fetch` in every repo. **Preparation only**: nothing pushed, no PR opened, no pipeline queued.
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

Stage B (one coordinated release — System 2.5.0 cascade, see §4):
  engine (System 2.5.0, System.StreamData 1.17.0)
    ► sdk ► engine-mongodb ► common-services ► communication-sdk ► mesh-adapter (6b)
    ► bot ► asset-repo (+ SchemaProvider image) ► mcp, cli
    + patch re-pins of every CK model that pins System 2.4.0 (§4.2) — same train
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

A model's `System-[x,3.0)` range is frozen to the System version installed at compile/import time. After System 2.5.0 is imported, every installed model pinned to System-2.4.0 is `ResolveFailed` until a new version pinned to 2.5.0 is in the catalog (publish-once ⇒ patch bump required, AB#4999 rule; AB#5435 is the open root-cause item). That includes models **not** changed by wave 1:

| Must re-pin in Stage B (patch bump) | Repo | Note |
|---|---|---|
| System.Bot 3.3.0 → 3.3.1 | octo-bot-services | not on the branch yet |
| System.Notification 2.3.0 → 2.3.1 | octo-common-services | not on the branch yet |
| **System.Communication 3.39.0 → 3.39.1** | controller | conflict with the phase split: 3.40.0 is phase 3, so a re-pin-only 3.39.1 is needed in Stage B (or System 2.5.0 moves to Stage C, see open decision O1) |
| **System.Identity 2.21.0 → 2.21.1** | identity | same conflict as above |
| **System.Ai 3.12.0 → 3.12.1** | ai | same conflict as above |
| System.UI 2.8.0, System.Reporting 2.2.0 | octo-platform-services, octo-report-services | not part of wave 1 |
| Basic, Basic.Accounting, Basic.Energy, EnergyCommunity, Environment, Industry.*, OctoSdkDemo … | octo-construction-kit | "bump every CK model for System 2.5.0" (like `c0e61de` for 2.4.0); catalog train list is discovered since AB#4577 (no list edit needed for patch bumps) |
| Meshmakers.Accounting, FamilyOs, Demo.Tickets, Wwc26.Challenge, FdaSeen, EnergyIQ, Loxone, landing pages, ai-sandbox | producer repos | producer patch bump + `r*` tag; externally built adapters (finAPI, fda-seen, loxone, eda, modbus, octosystem adapter) must be re-released after the System bump |

### 4.3 Open decision O1 (recommendation)

- **Option A** — keep System 2.5.0 in wave 1: Stage B must also ship re-pin patch versions of System.Communication/Identity/Ai/Bot/Notification and all business models. Two fleet-wide cascades (2.5.0 now, model switches later).
- **Option B (recommended to evaluate)** — keep the engine code in wave 1 but ship the System 2.5.0 bump with phase 3, so there is one cascade. Cost: the engine branch (System 2.5.0, StreamData 1.17.0, test CK models in engine/engine-mongodb/asset-repo that use SECRET and need System ≥ 2.5 per the compiler rule) has to be split — not a trivial change; needs a trial and a full build.

## 5. Human release-lane steps

| Step | Who/where | Detail |
|---|---|---|
| 1 | Review | Engine `Directory.Build.targets` change (CK compile after project references; StreamData uses the local catalog — CI already passes `/p:OctoLocalCatalogIsEnabled=true`) |
| 2 | Stage A merges | helm-core 16a → operator 17 (chart 0.11.0) → mesh-deployment 18a; helm-pro (reporting 0.3.0, mcp 0.2.0, ai 0.24.0); mesh-adapter 6a. Verify Vault `instance_secret_key` exists per cluster (`setup-vault-octomesh-secrets.yml`, never rotate an existing one), `aiInstanceSecretKey == communicationInstanceSecretKey`, key backed up in Keeper; edge operator pipelines must pass the key too |
| 3 | Stage B prep | All Stage B PRs green against each other's prereleases (merge in order, wait for each NuGet publish — "publish the contract before the consumers"); prepare the re-pin patch bumps of §4.2 |
| 4 | Stage B on test-2 | Merge batch; make sure **all** pods roll (comm-controller, platform, report, refinery-studio included), identity first imports System 2.5.0; re-run `adapter-mesh-test-2-CD` if it failed in "Enumerate tenant universe" (incident 49625); then per tenant `FixAll -w -y` + `ClearCache -tid <t> -y`, check `LibraryStatus` (FixAll/ImportFromCatalog `-w` can report success without installing) |
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
2. **Phase split vs. re-pin**: System.Communication 3.39, System.Identity 2.21, System.Ai 3.12 (plus Bot, Notification, UI, Reporting and all business/app models) pin System 2.4.0; with 3.40/2.22/3.13 held back for phase 3 they need re-pin patch versions in Stage B, or they go `ResolveFailed`.
3. **Mesh adapters and external adapters** stay on old images if the adapter CD fails in the System-import window, and fail silently (no check rule on execution failures).
4. **Artifact-storage values before the Secret exists** stop the bot pod on the next deploy of that cluster (18b).
5. **AB#5583 coupled to System.Communication 3.40.0** — an unrelated bug fix is held by phase 3, or the version numbers must be re-planned.
6. CLAUDE.md changes in mesh-adapter and operator, and the non-building sdk commit `23e3da9`, should be fixed before review.
