# Proof: two additive System releases need zero dependent bumps (AB#6676, Epic AB#5584 F2.4)

Claim of Feature AB#5686 / Phase 2 exit: after two consecutive additive System releases the installed dependents are **not** `ResolveFailed`,
need **no rebuild and no version bump**, the tenant keeps working, `LibraryStatus` shows them `Available`, and a breaking System change is caught
before it reaches a tenant. This page describes the repeatable script, how to rerun it, and the evidence of the run of 2026-10-18 (local DebugL stack,
engine `main` at `f914578d`, range retention as compiler default, engine PR #279).

Script: `scripts/prove-additive-system-releases.sh` (helpers `scripts/ab6676/proof_lib.py`, `scripts/ab6676/pipeline.yaml`). It prints a Markdown
report to stdout and to `<work>/report.md`, and exits non-zero when a claim is not met.

## Rerun (Ariadne)

Prerequisites: the DebugL chain built from `main` (`Invoke-BuildRange -branch main -from octo-distributedEventHub -to octo-communication-controller-services -include octo-asset-repo-services,octo-identity-services,octo-platform-services,octo-cli,octo-construction-kit -verifyTests`),
infrastructure running (`Start-OctoInfrastructure` or the kind cluster), the stack **stopped**, and **one user login** of the context that may create and delete
tenants (`octo-cli -c LogIn -i --context local_OctoSystem`; machine client credentials get HTTP 403 on tenant create/delete). Never export
`OCTO_CLI_CLIENT_ID/SECRET` for admin calls yourself: octo-cli would replace the stored user token by a client-credentials token (the script unsets them
for admin calls, see `octoa`).

The "mesh adapter is Online" wait (phases `tenant-pipeline`, `tenant-final`) parses the JSON of `GetAdapters -j` (`proof_lib.py adapter-online`), so compact and
pretty-printed output both work (AB#6973; before, a grep for the spaced form never matched and a green run ended with a false FINDING after about 5 minutes).
`scripts/prove-additive-system-releases.sh --selftest` checks that helper against both spacings, Offline and empty output without a stack.

```bash
M=~/RiderProjects/meshmakers/main                       # monorepo root of the lane
# pre-flip compiler for the "v1 baseline" row: engine commit a9e5532d (parent of PR #279), built in a separate worktree
git -C $M/octo-construction-kit-engine worktree add --detach /tmp/engine-preflip a9e5532d
(cd /tmp/engine-preflip && ln -s $M/nuget ../nuget && dotnet build src/ConstructionKit.Compiler/ConstructionKit.Compiler.csproj -c DebugL)

# restart helper: Stop-Octo, then Start-Octo -nonInteractive $true with the mesh adapter on the proof tenant (see "Stack" below)
export OCTO_CLI_CLIENT_ID=claude-agent OCTO_CLI_CLIENT_SECRET=$(cat ~/.octo-cli/claude-agent.secret)   # machine client of the tenant contexts only
bash scripts/prove-additive-system-releases.sh --monorepo $M --work /tmp/ab6676-proof \
  --classic-ckc /tmp/engine-preflip/bin/DebugL/net10.0/publish/octo-ckc \
  --skill-dir <octo-claude-skills>/skills/octo/scripts --restart-cmd ./restart-stack.sh --guard-test --phase all
```

Phases can be run one by one (`--phase offline | tenant-prep | tenant-pipeline | tenant-release --release 1|2 | tenant-comm | tenant-final | tenant-guard | tenant-cleanup`).
The offline phase needs neither stack nor network and takes about 1.5 minutes; the whole run about 12 minutes.

**What it touches.** `octo-ckc` runs with a throw-away `HOME` (both GitHub catalogs switched off, no token) and `-lcr <private catalog>`: the shared local
catalog, the GitHub catalogs and nuget.org are neither read nor written. The tenant part creates one throw-away tenant (`tmp-ab6676`) and deletes it.
`--guard-test` publishes the proof releases **temporarily** into the stack's local catalog (`<lane>/.octo/local-catalog`) and restores it byte for byte from a
tar backup afterwards (also on failure). The forced `ImportCk -f` of the breaking release (`--forced-file-import`) is off by default because it makes the
tenant unloadable (finding F1); never use it outside a throw-away tenant.

**Stack.** The mesh adapter of `Start-Octo` registers for the tenant given with `-meshAdapterTenantId`; the tenant must exist (and have communication enabled)
before the adapter starts, hence the restart after `tenant-prep`:
`Start-Octo -branch main -configuration DebugL -nonInteractive $true -aiService $false -mcpService $false -dataRefineryStudio $false -frontendLibraries $false -meshAdapterTenantId tmp-ab6676 -meshAdapterId 670000000000000000000002`.

## How the proof is built

| Step | What | Where |
| ---- | ---- | ----- |
| Models | System plus 21 dependents compiled from the monorepo sources: the 12 models of `octo-construction-kit` (Basic, Basic.Accounting, Basic.Energy, EnergyCommunity, Environment, Industry.Basic/Energy/Fluid/Logistics/Maintenance/Manufacturing, OctoSdkDemo) and the service models (System.Files, System.StreamData, System.Bot, System.Communication, System.Identity, System.Notification, System.UI, System.Reporting, System.Ai) | private catalog `catalog-rr` (range-retaining, current compiler) and `catalog-v1` (pre-flip compiler, exact pins) |
| System n+1 = 2.6.0 | **additive**: one new optional attribute `RtProofNote1` on the base type `Entity` (every dependent derives from it) | `proof_lib.py make-release` |
| System n+2 = 2.7.0 | **additive**: another optional attribute `RtProofNote2` on `Entity` and a new enum `ProofKind2` | same |
| System n+3 = 2.8.0 | **breaking, declared as MINOR on purpose**: removes the attribute `DisplayName` (used by System.Identity, listed in its `usedSurface`) | same |
| Basic 2.6.0 | **breaking, declared as MINOR**: removes the attribute `Iban` (used by Basic.Accounting) | `proof_lib.py make-dependent-break` |
| System.Communication 4.10.0 | **additive**: a new enum (AB#4696 case) | `make-release --model System.Communication --kind additive-enum` |
| Per release | `ValidateVersion`, `ValidateCascade` (forced bumps = `NeedsRepin`), publish into the private catalog, **recompile of every dependent without editing its `ckModel.yaml`** and compare with its build before the release | offline phase |
| Tenant | throw-away tenant with the 12 dependents and the service models installed; after each release: `LibraryStatus`, login, a pipeline run on the local mesh adapter (upsert of a `Basic/Tree` through the dependent type, no redeploy), a runtime query, a bot job (`RunFixupScripts -w`; the `ImportCk` job itself is one too), and a scan of the service logs for `not found in CkCache`, `has missing dependencies`, `marked as ResolveFailed`, `too old for the tenant` | tenant phases |

"Build output changed" compares the compiled yaml of the dependent before and after the System release. With range retention the only difference is the
resolved `dependencies:` closure line (`System-2.5.0 -> System-2.7.0`); the retained `dependencyRanges` (`System-[2.5,3.0)`, floor 2.5.0, `usedSurface`) and
every `System@2/...` reference are byte-identical. That closure line is level `None` for SemVer (row D8, AB#5435): no bump, nothing to publish again.

## Evidence of the run (2026-10-18)

### Metric: dependent rebuilds per System release

21 dependents (newest version of each model, from source), `ValidateCascade` and recompile against the catalog that holds the new System.

| compiler | candidate | `ValidateVersion` | Compatible | Breaks | **NeedsRepin (forced bumps)** | build output changed (without editing `ckModel.yaml`) |
| -------- | --------- | ----------------- | ---------- | ------ | ----------------------------- | ----------------------------------------------------- |
| range retention (current) | System-2.6.0 (n+1) | VALID | 21 | 0 | **0** | **0 / 21** (resolved closure line only: 21) |
| range retention (current) | System-2.7.0 (n+2) | VALID | 21 | 0 | **0** | **0 / 21** (closure line only: 21) |
| classic exact pins (v1 baseline, engine `a9e5532d`) | System-2.6.0 | VALID | 0 | 0 | **21** | **21 / 21**, 14 of them do not even compile (`error 66`-class conflict, AB#5432) |
| classic exact pins | System-2.7.0 | VALID | 0 | 0 | **21** | **21 / 21**, 14 do not compile |

Version bumps needed: 0 (range retention) against 21 (classic). `git status` of all 21 dependent source directories: clean.
Range-retaining output of Basic compiled against System-2.7.0 without touching its `ckModel.yaml`: `dependencyRanges: - range: System-[2.5,3.0) floor: 2.5.0
usedSurface: [System@2/Description-1, System@2/Entity-1, System@2/Name-1, System@2/ParentChild-1]`, `minEngineVersion: 3.5.1`.

### Tenant `tmp-ab6676` (all 20 models Available, 0 ResolveFailed at every step)

Tenant models: System 2.5.0 (embedded) then 2.6.0, 2.7.0 imported with `ImportCk -f`; Basic 2.5.0, Basic.Accounting 1.10.0, Basic.Energy 1.10.0, EnergyCommunity 4.10.0,
Environment 3.6.0, Industry.Basic 2.6.0, Industry.Energy 3.6.0, Industry.Fluid 2.5.0, Industry.Logistics 3.3.0, Industry.Maintenance 2.5.0, Industry.Manufacturing 2.6.0,
OctoSdkDemo 2.5.0 and the service models System.UI 2.11.0, System.Identity 2.26.0, System.Bot 3.5.0, System.Files 1.1.0, System.Communication 4.9.0 (then 4.10.0),
System.Notification 2.6.0, System.StreamData 1.18.0.

| after | LibraryStatus | login | pipeline run (no redeploy) | runtime query | bot job | log failures |
| ----- | ------------- | ----- | -------------------------- | ------------- | ------- | ------------ |
| baseline, System 2.5.0 | 20 Available, 0 ResolveFailed | ok | - | - | - | - |
| import System-2.6.0 | 20 Available, 0 ResolveFailed | ok | Status 1 (success) | `Basic/Tree`: 1 instance | completed | 0 |
| import System-2.7.0 | 20 Available, 0 ResolveFailed | ok | Status 1 | 1 instance | completed | 0 |
| import System.Communication-4.10.0 (AB#4696 case) | 20 Available, 0 ResolveFailed | ok | Status 1 | 1 instance | completed | 0 |
| **restart of all services** (embedded System 2.5.0 vs installed 2.7.0) | 20 Available, 0 ResolveFailed | ok | Status 1 | 1 instance | completed | 0 |

Log line of the restart (the downgrade guard keeps the newer installed System; 110 occurrences, one per resolve):
`INFO TenantContext EmbeddedCkModelImportGuard.Report: Embedded CK model import skipped for tenant 'tmp-ab6676': downgrade prevented, the tenant has 'System-2.7.0', the service embeds 'System-2.5.0'`.
No line `not found in CkCache`, `has missing dependencies`, `marked as ResolveFailed` or `too old for the tenant` in any of the six service logs since each step.
In the tenant database every `CkModel` has `modelState` 1 (Available) after the last step.

Services covered with one request each: identity (login, client-credentials token for the tenant), asset repository (runtime query, entity create/update through the
pipeline, `ImportCk` and `LibraryStatus`), bot services (Hangfire jobs: `ImportCk`, `ImportFromCatalog`, `RunFixupScripts`), communication controller
(`ExecutePipeline`, adapter state), mesh adapter (pipeline run). Platform services were running but are not exercised (no request in the scenario).
`System.Reporting` / `System.Ai` are compiled and checked in the cascade (Compatible) but their services were not started in this stack (`reportingService`
off, `aiService $false`), so they have no tenant request.

### Breaking releases

| Case | Compile / `ValidateVersion` | `ValidateCascade` | Tenant |
| ---- | --------------------------- | ----------------- | ------ |
| System-2.8.0 removes `DisplayName`, declared 2.8.0 | `ValidateVersion`: `OCTO-CK100` "requires a MAJOR bump ... minimum version 3.0.0" (the compile gate is informational for a `ckLanguage 1` model such as System; same verdict service) | 1 Breaks: `System.Identity-2.26.0 uses System@2/DisplayName-1, which System-2.8.0 does not define` | see F1, F2 |
| Basic-2.6.0 removes `Iban`, declared 2.6.0 | not run (the cascade names the dependent) | 1 Breaks: `Basic.Accounting-1.10.0 uses Basic@2/Iban-1` | **refused by the tenant upgrade guard** (below) |

Tenant upgrade guard on the catalog path (`--guard-test`), Basic-2.6.0 (non-System base): `CheckUpgrade` lists `Would break: Basic.Accounting-1.10.0 ...`;
`ImportFromCatalog -m Basic-2.6.0` answers `The import would break installed models; nothing was imported ... (CK_UPGRADE_BLOCKED) ... repeat the command with --force`;
`LibraryStatus` unchanged (20 Available), the pipeline still runs.

The **publish gate** (`CkPublishGate`) exempts the local file system catalog by design; its refusals (OCTO-CK205/202/208, race, lowered version, case-insensitive
name, hand-edited file) are covered by the engine test suites: `dotnet test tests/ConstructionKit.Engine.Tests --filter "FullyQualifiedName~PublishGate|FullyQualifiedName~CompileGate|FullyQualifiedName~CkCompileGate|FullyQualifiedName~Adversarial"`
-> **182 passed, 0 failed, 1 skipped** (the opt-in catalog replay). The compile gate hard-fails only for `ckLanguage: 2` models (docs/ck-semver-rules.md).

### Adversarial cases against the range-retaining output (local catalog copy)

| # | Case | Result | Verdict |
| - | ---- | ------ | ------- |
| 1 | lowered version: unchanged content declared as `2.5.1` while 2.6.0/2.7.0 are published | `ValidateVersion` VALID | accepted limitation 2 of the compile gate (local mode: no baseline at or above the declared version; `OCTO-CK201` needs a published baseline) |
| 2 | hand-edited compiled file (changed description) published over System-2.7.0 | refused: `Model 'System-2.7.0' already exists in catalog` (needs `-r`); the remote gate answers `OCTO-CK205` | refused (local: no `-r`; remote: gate tests) |
| 3 | removed baseline: all earlier System versions deleted from the catalog, then validate the breaking release | `ValidateVersion` VALID | accepted limitation 1 (a model without baseline is a first publication) |
| 4 | case-variant name | cannot be built as a standalone case for System (references are by name); the case-insensitive baseline lookup is covered by the gate tests (`docs/ck-semver-rules.md`, "Baseline" rule 6) | covered by tests only |
| 5 | concurrent publish of two different System-2.9.0 | exactly one wins (exit 0), the other is refused `already exists` (exit 1); catalog index consistent | refused |

No new bypass found; limitations 1 and 2 are the ones the platform owner accepted on 2026-10-10.

## Findings (not hidden)

**F1 (high) - `ImportCk -f` of a breaking System release is accepted and leaves the tenant unloadable.** Reproduce with `--forced-file-import` on a throw-away tenant
(run 1 of the day): the import job completed, `LibraryStatus` still listed every model `Available` (also in the database: `modelState` 1), and from then on every
request of the tenant failed with `Failed loading tenant 'tmp-ab6676'. CkAttributeId 'System-2.8.0/DisplayName-1' of CkTypeId 'System.Identity-2.26.0/IdentityProvider-1' does not exist`;
`Delete` of the tenant fails with the same message (no API path back; the tenant had to be dropped in MongoDB). The file import has no usedSurface check
(the F2.5 guard of AB#6867/6870 covers `ImportFromCatalog`, batch import and `FixAll`), and the import's re-validation does not mark the dependents
`ResolveFailed` for a removed attribute (known limitation D-M1 of `SoftResolveAsync`, CLAUDE.md). Expected by AC 4: refused, or the dependents leave `Available` with a reason.

**F2 (medium) - `CheckUpgrade` / `ImportFromCatalog` do not judge a System release.** System is service-managed: `ImportFromCatalog -m System-2.8.0` answers
`No models need to be imported.` and `CheckUpgrade -m System-2.8.0` prints `Breaking Changes: False` although the release removes an attribute that System.Identity,
System.Bot and others use. A System release reaches a tenant through the embedded import of a new service (downgrade guard only) or through `ImportCk -f` (F1), so for
System the only protection against a breaking change is the pre-release gates (`ValidateVersion` / `ValidateCascade` / publish gate), which work as shown above.

**F3 (info, F2.6 scope) - published exact-pinned models turn incompatible.** After System 2.6.0/2.7.0 the `LibraryStatus` catalog column marks 15 catalog models
`Incompatible` (before: 2): Test, CkHealthMonitoring, EnergyIQ, Loxone, Meshmakers.Accounting, Meshmakers.Accounting.Tesla, Monitoring, SmartMeterInsights,
EnergyCommunity.Registration, Smoke.CkV2, Demo.Tickets, EnergyLanding.Community, FamilyOs, FdaSeen, Wwc26.Challenge (remote catalog models, none installed in the tenant).
This is the AB#4696 population (published with exact pins before the flip); the proof does not cover them: they need the one-time re-pin (F2.6, AB#5688 / AB#6708).
The local cascade of the unfiltered catalogs shows the same 24 `NeedsRepin` dependents.

**F4 (info) - the catalog entry `Test` is flagged `Incompatible`** in `LibraryStatus` although `CheckDependencies -m Test-1.1.0` (the local, range-retaining test model, `System-[2.0,3.0)`) says `[INSTALL]` against the service-managed System 2.7.0. The flag probably comes from an older exact-pinned `Test` entry of a remote catalog cache; not investigated here.

## AB#4696 acceptance criterion 2 (Loxone-style pipeline case)

A pipeline whose node resolves a type of a dependent (`Basic/Tree`, created and queried through `GetOrCreateRtEntitiesByType`, `CreateUpdateInfo`, `ApplyChanges@2`,
`GetRtEntitiesByType`) keeps running **without redeploy** after: System 2.6.0, System 2.7.0, an additive System.Communication 4.10.0 (the Loxone case: a communication
dependent on a bumped communication base) and a restart of all services; no `not found in CkCache` in the mesh-adapter log.

## Reproducibility notes

- The v1 baseline needs the compiler of engine commit `a9e5532d` (before the flip); without `--classic-ckc` the "before" rows are skipped.
- The tenant part depends on the service build of the lane (`Invoke-BuildRange ... -verifyTests`) and on the local catalog holding the 12 current models
  (`octo-construction-kit` build); the offline part reads only sources.
- Timings of the run (run 4/5 of the day, M-series Mac): offline ~75 s, tenant preparation ~35 s, stack restart ~110 s, import of one System release with all checks ~100 s.
- The tenant evidence combines run 4 (fresh tenant: preparation, restart, System-2.6.0/2.7.0, restart, guard test) and run 5 (the same steps on that tenant after the script fixes: wait for the adapter, retry of the first pipeline run). Run 5 could not recreate the tenant because the stored user login of `local_OctoSystem` had been replaced by a client-credentials token (fixed in the script: `octoa`); the System.Communication-4.10.0 step ran on the same tenant right afterwards. A clean end-to-end rerun with a fresh user login is the first thing Ariadne does.
