---
description: The CK models this repository owns, and the System model on ckLanguage 2 with its interfaces.
applies_to: src/SystemCkModel/**, src/SystemFilesCkModel/**
---

# CK models owned by this repository and System on ckLanguage 2

## CK Models Owned by This Repository

| Model | Path | Package | Depends on | Owner of the source |
| ----- | ---- | ------- | ---------- | ------------------- |
| `System` | `src/SystemCkModel` | `Meshmakers.Octo.ConstructionKit.Models.System` | - | this repository |
| `System.StreamData` | `src/StreamDataCkModel` | `Meshmakers.Octo.ConstructionKit.Models.StreamData` | `System` | this repository |
| `System.Files` | `src/SystemFilesCkModel` | `Meshmakers.Octo.ConstructionKit.Models.System.Files` | `System-[2.5,3.0)` | this repository (since AB#6479, was octo-asset-repo-services) |

**System.Files (AB#6479).** The model source moved here from `octo-asset-repo-services` so that every consumer
(asset repository, report services, the platform-services installer) gets it as a package of the libs train.
The content is **identical** to the `System.Files-1.0.0` that asset-repo published (the model is now `System.Files-1.1.0`, see "AB#6691" below): same
YAML, same PackageId, same generated namespace
(`Meshmakers.Octo.ConstructionKit.Models.System.Files.Generated.System.Files.v1`: `SystemFilesCkIds`,
`RtFolderRoot`, `RtFolder`, `RtFileSystemItem`, `AddCkModelSystemFilesV1`). The compiled yaml freezes the
dependency range to the newest System it sees (`System-2.5.0`), so a rebuild only matches the published
1.0.0 while the System the project builds against is still 2.5.0. Do not edit the YAML without a version
bump (`octo-ckc ValidateVersion`). `SystemFilesCkModel` builds after `SystemCkModel`
(project reference, same pattern as `StreamDataCkModel`, with `OctoLocalCatalogIsEnabled=true` so that System
resolves from the freshly built local catalog). `OctoEnforceRuntimeStateMarkers` is deliberately not armed
(it would demand ownership markers on an unchanged 1.0.0). `Runtime.Engine.Tests`
(`EmbeddedCkModelResourceTests`) guards the embedded model and the id `System.Files-1.1.0`.

**AB#6691 - one-time MINOR bumps after the range-retention flip (AB#6675).** With range retention as the compiler default, the first
compile of `System.Files` (1.0.0 -> **1.1.0**) and `System.StreamData` (1.17.1 -> **1.18.0**) is classified MINOR by SemVer row D6 ("the one-time
re-pin": exact pin -> declared range), so `ValidateVersion` fails with `OCTO-CK100` until the declared version is raised
(`octo-ckc -c ValidateVersion --apply` wrote exactly these two versions; `System` has no dependencies and needs no bump). **Compat impact:** the
compiled yaml of both models now carries `dependencyRanges`, `minEngineVersion: 3.5.1` and `System@2/...` references and is published under
`ck-models/v3/`; engines below 3.5.1 do not see them (message 126 / catalog path), so every service of an environment must run the CK v2 engine
before these versions are imported. Published versions stay immutable. **Why `main` stayed green:** the `Validate CK model versions` step installs
`octo-ckc` of the build's own version line from the private feed (`Derived octo-ckc toolVersion from version line`), i.e. the compiler of the
previous main build; the flip commit itself was validated with the pre-flip compiler (`Range retention: False`, "no bump required"), a later PR
or main build derives the flip compiler and fails.

**AB#6725 - release tags needed the flip compiler pinned (pin removed 2026-10-21).** The same derivation broke the first release tag after the
flip: an `r*` tag build takes only STABLE compilers of its own line, i.e. the pre-flip 3.5.21, and the compiler of the release being built is
published only by that very build (r3.5.22, build 54764: `OCTO-CK100`, "rangeRetention true -> false" = MINOR bump for the already published,
range-retaining System.Files-1.1.0 / System.StreamData-1.18.0). The `Validate CK model versions` call therefore passed
`toolVersion: '0.1.2610.14032'` (exact main-line build with the flip). Never bump the models to get past such a gate: the old compiler would
publish them with exact pins, immutable. **The pin is removed**: the flip compiler is on the 3.5 line since r3.5.23/r3.5.27 (stable 3.5.36 is on the feed), and
the exact main-line build had been purged from the private feed, which failed every build with "Version 0.1.2610.14032 ... is not found in NuGet
feeds" (PR 298 builds 58123 and 58142). The template derives the compiler of the build's own line again (main/PR: newest 0.1.*, r* tags: newest
stable 3.5.*). Do not pin an exact main-line build again (the feed purges them); a pin is only for a schema feature no released compiler knows yet, and it
is removed in the same release train.

**Single publisher (AB#6481).** This repository's CI is the only publisher of `System.Files` to the private and
public CK catalogs and the only source of its generated model documentation: the `Validate CK model versions`/
`Publish CK models` step lists `src/SystemFilesCkModel/ConstructionKit` (after `System`), and a dedicated `CopyFiles`
step stages the `.md` files of `SystemFilesCkModel/out` for the documentation site. The yaml is deliberately **not**
copied via `handle-artifacts.constructionKitLibraryPaths`: that puts it into `ck-libraries/`, which
`release-catalogs-train` publishes to the public catalog with `-r`, i.e. a second, forcing publisher built against the
newest System of the commit. The CK gate stays the only publisher. Because 1.0.0 is already published with identical
content, the first publish from here is the idempotent "already exists" skip. The asset-repository CI stops
publishing it together with the removal of its model project (AB#6480). A search of all `azure-pipelines.yml`
files for `SystemFilesCkModel` must therefore find only this repository.

## System on ckLanguage 2 (AB#6928, Epic AB#5584 F4.1)

`src/SystemCkModel` is **System-2.8.0**, `ckLanguage: 2` (2.5.0 was ckLanguage 1; 2.6.0 switched the language, 2.7.0 adds the interfaces, see
"System interfaces" below; 2.8.0 adds the `AggregationTypes` value `Integral` = 7, AB#7918, unpublished until the explicit System publish).
`src/SystemCkModel/ConstructionKit/CHANGELOG.md` records each version from 2.8.0 on (`ValidateVersion -cl` plus a Compatibility section). Every release of the line is a **Minor** against its predecessor and against 2.5.0 and must stay one:

- **Every type and record declares `derivable: Any`.** Raising the language flips the default from `Any` to `Model`; without the explicit
  keys every model deriving from `System/Entity` would fail with message 113 and the SemVer verdict would be Major (row T6, `OCTO-CK200`).
  `visibility` (omitted = `Public`) and attribute `access` (omitted = `ReadWrite`) do not flip, nothing else is declared. A **new** System type
  without `derivable` is `Model` like in any v2 model; give it `derivable: Any` when other models should derive from it.
  `Entity` and `Configuration` are stable bases by declaration (public, not final, `derivable: Any`): row B4 (unique index = Major +
  acknowledge) is judged through `CkSemVerClassifier.IsStableBase`. The v1 name branch of `IsStableBase` stays for System versions up to 2.5.x
  (their sources and published files are ckLanguage 1 and are still compared) and goes with the last ckLanguage 1 System.
- **minEngineVersion 3.5.1, `ck-models/v3/` only.** The compiler derives it from `ckLanguage: 2`. An engine below 3.5.1 does not see System 2.6
  (message 126 / catalog path); the embedded-import downgrade guard keeps a newer installed System when an older service starts
  (`EmbeddedCkModelImportGuard.Decide`: embedded `System-2.5.0` against installed `System-2.6.0` = `SkipNewerInstalled`, covered by
  `CkModelImportGuardDecisionTests` in engine-mongodb).
- **Message 109 and `_id`.** The `System/Entity` index `(CkTypeId, _id)` names the database key; rule 109 (unknown index path in a v2 model)
  exempts `_id` next to `Rt*` / `CkTypeId` (`InheritanceResolver.IsSystemIndexPath`, test `RestrictedAttributeRuleTests`).
- **Generated C#.** For a v2 model the Rt class of an `isFinal` type is `sealed`: `RtAutoIncrement`, `RtBlueprintBackup`, `RtBlueprintHistory`,
  `RtBlueprintInstallation`, `RtMigrationHistory`, `RtTenant`, `RtTenantConfiguration`, `RtTenantModeConfiguration` (no subclass or mock of them in the monorepo, checked 2026-10-19).
- **Publishing.** A service that embeds System 2.6 or later brings it to every tenant, and a model that is still exact-pinned to 2.5.0
  would go `ResolveFailed`; publishing was meant to wait for the exact-pin scan (F2.6, AB#6708). **System-2.6.0 was published nevertheless**
  by the engine main CI on 2026-10-23 09:02Z (private catalog `ck-models/v3`, also visible in `construction-kit-libraries`) right after PR #294 merged;
  a published version is immutable, hence the interfaces are **2.7.0**, not an extension of 2.6.0. Never publish a ckLanguage 2 System into a shared
  catalog or environment before every service there runs the CK v2 engine.
- **Proof.** `scripts/prove-system-ckl2.sh --monorepo <root>` (private catalogs, throw-away `HOME`, nothing shared is read or written): compiles
  System 2.5.0 (git ref `origin/main`) and the 21 dependent models (committed HEAD of each repository) into a "before" catalog, runs
  `ValidateVersion` (Minor), `ValidateCascade` (0 Breaks, 0 NeedsRepin), shows the SHA-256 list of System before/after, proves the compiled
  surface identical apart from the explicit modifiers, publishes 2.6.0 into a copy of the catalog (`ck-models/v3` only) and recompiles every dependent
  without editing its `ckModel.yaml` (0 output changes; only the resolved closure line moves, row D8). Tests: `SystemCkLanguage2Tests`
  (Compiler.Tests; includes the negative control that the same switch without the explicit modifiers is `OCTO-CK200`).
  `--additive` (AB#6929) accepts a candidate that only adds elements (the interfaces): the surface diff may consist of added lines only.

### System interfaces (AB#6929, Epic AB#5584 F4.1)

System-2.7.0 declares four interfaces in `src/SystemCkModel/ConstructionKit/interfaces/`; a model author implements them (`implements:`) instead of copying the attributes:

| Interface | Members | Notes |
| --- | --- | --- |
| `Named-1` | `Name` (required) | **No index is declared** (AB#7019, decision of 2026-10-19): an implementer declares the index it needs for sorted interface queries; no latency promise without one. `DisplayName` is deliberately not a member (a later `Displayable-1`). |
| `Describable-1` | `Description` (optional member) | The existing `PersistentQuery.Description` is optional, so the member is optional too. |
| `Enableable-1` | `Enabled` (required) | No System type has `Enabled`; nobody in System implements it. |
| `Hierarchical-1` | extends `Named-1`; required association member `System/ParentChild` (outbound `Parent`) to an implementor of `Hierarchical-1` | **No new association role**: it reuses the existing `ParentChild` (outbound `Parent` One, inbound `Children` N). An implementer declares `ParentChild` to a `Hierarchical-1` implementor (itself is fine); a root is a node without a Parent instance. Nobody in System implements it (no System type uses `ParentChild`). |

Per existing System type: only `PersistentQuery` (abstract; every query type inherits it: `SimpleRtQuery`, `AggregationRtQuery`, `GroupingAggregationRtQuery`, `StreamDataQuery` and its `*SdQuery` children) has `Name` and the optional `Description` and
implements `Named-1` and `Describable-1` (SemVer row T2, Minor). No other System type has the attributes `Name`, `Description` or `Enabled` under the System attribute ids (`BlueprintName`, `DatabaseName`, `CkModelName` are other
attributes), none uses `ParentChild`, so there are no further implementers; making a type implement an interface it does not satisfy would need a new required member (Major) and does not happen in 2.x.
Generated C# (Octo.Sdk generator): `IRtNamed`, `IRtDescribable`, `IRtEnableable`, `IRtHierarchical`; `RtPersistentQuery` implements the first two.
Tests: `SystemInterfacesTests` (Compiler.Tests: members, per-type table, no index added, Minor against 2.5.0 and 2.6.0, probe model that implements all four, rule 121 and the missing-`Name` error for `Hierarchical-1`),
`EmbeddedCkModelResourceTests.SystemCkModel_GeneratesTheFourInterfaces_AndTheQueryTypesImplementThem` (Runtime.Engine.Tests).
