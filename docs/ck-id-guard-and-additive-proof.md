---
description: Analyzer OM1005 against versioned generated CK ids at run time, and the repeatable proof that additive System releases need no dependent bumps.
applies_to: src/ConstructionKit.Analyzers/**, scripts/prove-additive-system-releases.sh, scripts/ab6676/**
---

# Versioned CK id guard OM1005 and the additive System release proof

## Versioned generated CK ids at run time - guard OM1005 (AB#6673)

Rule (also in the monorepo `CLAUDE.md`): runtime code never looks up CK elements by a generated **versioned** id. The generated
`<Model>CkIds` classes carry both forms per element: `RtCk<X>TypeId` / `RtCk<X>RoleId` / ... (version-less `RtCkId<T>`, what the CK
cache is queried with) and `Ck<X>TypeId` / ... (`CkId<T>` bound to the model version the service was compiled against). A tenant may
hold a **newer** System than the service embeds (the embedded-import downgrade guard keeps it), so the versioned id is not in the
tenant's CK cache - an entity insert of an older service failed that way (AutoIncrement, F1.0). `CkModelId` (the embedded model id
for import / `IsCkModelSatisfiedAsync`, which compares by name and minimum version) is not an element id and stays allowed.

Guard: the Roslyn analyzer `CkVersionedIdAnalyzer` (project `src/ConstructionKit.Analyzers`) reports **OM1005** for every
reference to a `…CkIds.Ck<Element>Id` field of type `CkId<T>`; allowed are the `RtCk…` constants, `CkModelId`, and a versioned id that is
converted at once with `.ToRtCkId()`. Not analysed: generated code and assemblies whose name ends with `Tests`. It ships inside the
`Meshmakers.Octo.ConstructionKit.Contracts` package (`analyzers/dotnet/cs`), so every project that uses `CkId` / `RtCkId` gets it.

**Severity and rollout (review of PR #278).** OM1005 is **Info (suggestion) by default**: the package never breaks a consumer build, not
even with `TreatWarningsAsErrors` (a Warning would break it too, an Error certainly). A repository raises it after it has audited itself:
`.editorconfig` `dotnet_diagnostic.OM1005.severity = error` (or `warning`), or `<WarningsAsErrors>$(WarningsAsErrors);OM1005</WarningsAsErrors>`
in `Directory.Build.props` (verified: both turn the Info diagnostic into a build error, with `TreatWarningsAsErrors` on or off);
`= none` opts out. Sequence: (1) audit the consumers (git grep `\w+CkIds\.Ck(?!ModelId\b)\w+`, see `docs/ck-versioned-id-guard.md`), fix
or list the offenders; (2) release the package - consumers see a suggestion only; (3) each repo that is clean sets the severity to `error`
in the same PR as its package update. Tests: `CkVersionedIdAnalyzerTests` (Roslyn `CompilationWithAnalyzers`: reported with the version-less
replacement, every occurrence, allowed forms, test projects and generated code skipped, default severity Info, raised to error and
switched off through the specific diagnostic options). Verified end to end with a real `dotnet build` of a consumer project with
`TreatWarningsAsErrors`: default builds green, `WarningsAsErrors=OM1005` and the `.editorconfig` line fail with OM1005.

## Proof: additive System releases need zero dependent bumps (AB#6676, Epic AB#5584 F2.4)

`scripts/prove-additive-system-releases.sh` (helpers in `scripts/ab6676/`, write-up and evidence in `docs/ck-additive-system-proof.md`) is the
repeatable scenario behind the Phase 2 exit criterion "two consecutive additive System releases require zero dependent bumps, a breaking
change is caught". It is **parameterised** (`--system-base`, compiler paths, tenant, context), **rerunnable** (work directory and tenant are recreated)
and **restores the baseline** (throw-away tenant deleted; the stack's local catalog is only touched with `--guard-test` and is restored byte for byte).

- **Offline phase** (no stack, no network): `octo-ckc` runs with a throw-away `HOME` whose `settings.json` switches both GitHub catalogs off, and every call
  gets `-lcr <private catalog>`. It compiles System plus the dependents (the models of `octo-construction-kit` and the service models) from the
  monorepo sources into a private catalog, derives System n+1 / n+2 (additive: a new optional attribute on `Entity`, later also a new enum) and a breaking
  n+3 (removes `DisplayName`, declared MINOR on purpose) with `scripts/ab6676/proof_lib.py make-release`, and prints per release: `ValidateVersion`,
  the `ValidateCascade` verdicts (forced bumps = `NeedsRepin`) and the number of dependents whose build output changes when they are recompiled
  **without editing their `ckModel.yaml`** (the resolved `dependencies:` closure line is reported separately, SemVer row D8). With
  `--classic-ckc <compiler built before engine commit 8f69cca1>` the same models are compiled with exact pins: that is the "before" row (v1 baseline).
  Also: breaking System n+3 and Basic n+1 (`ValidateVersion` OCTO-CK100, cascade names the dependents) and five adversarial cases.
- **Tenant phases** (need the local stack: `Start-Octo -nonInteractive $true ... -meshAdapterTenantId <tenant> -meshAdapterId 670000000000000000000002`,
  `octo-cli`, a user context that may create/delete tenants): create a tenant, install the dependents (`ImportFromCatalog`), import System n+1 / n+2
  with `ImportCk -f`, and after each: `LibraryStatus` (Available / ResolveFailed counts), login, a pipeline run on the local mesh adapter without redeploy
  (upserts a `Basic/Tree`), a runtime query, a bot job and a scan of the service logs for `not found in CkCache` / `missing dependencies` /
  `marked as ResolveFailed`. `--restart-cmd` restarts the stack after the tenant exists (the mesh adapter must register for it) and after n+2 (service
  restart = embedded import with the downgrade guard: "downgrade prevented" in the log). `--guard-test` publishes the proof releases temporarily into the
  stack's local catalog and runs `CheckUpgrade` / `ImportFromCatalog` (the tenant upgrade guard of F2.5).
- Never use it against a shared environment. The forced `ImportCk -f` of the breaking System release (`--forced-file-import`) leaves the tenant
  unloadable (finding F1 in the doc): throw-away tenant only, and it needs a manual database cleanup afterwards.
