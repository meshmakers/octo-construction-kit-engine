---
description: Range retention of CK v2 models, the compiler default and the audit script.
applies_to: src/ConstructionKit.Engine/Resolvers/**, src/ConstructionKit.Engine/DependencyGraph/**, scripts/audit-ck-ranges.sh
---

# CK v2 range retention

## CK v2 Range Retention (AB#5664 / AB#5665, Phase 1 F1.1-S2 AB#5905 — compiler default since AB#6675)

Range retention is the **compiler default**: every compile (MSBuild `CkCompile` and `octo-ckc Compile`, CK
language 1 and 2) writes the retained ranges. The switch is gone: the MSBuild property / environment
variable `OctoCkRangeRetention` and `octo-ckc -rr` are **obsolete and ignored** — a build that still sets
them logs the deprecation warning **`OCTO-CK300`** (`CkCompile` task warning; `ObsoleteOptionFilteringEnvironmentService`
strips `-rr` from the ckc arguments and warns on stderr). `CkCompilerOptions.RangeRetention` (default `true`)
remains only as a code seam for tests and re-pin tooling (`RangeRetention = false` = classic exact pins).
Compiled output of unchanged sources therefore differs (added `dependencyRanges`, `minEngineVersion: 3.5.1`,
`Name@major/` references); the first publish of an existing model needs a MINOR bump (SemVer row D6).
Audit sources before rebuilding: `octo-ckc -c AuditRanges -p <dir>` / `scripts/audit-ck-ranges.sh` (AB#6683).
Merge/release only after the O1 lift (AB#6279).

**Compile side (`CatalogModelResolver.CompileAsync` → `ApplyRangeRetentionAsync`).** The model is still
resolved and validated against the *highest* catalog version (the returned graph stays concrete — the
source-generator cache is unchanged). The **output** is a JSON-round-trip copy
(`CkCompiledModelCloner`) in which:

- `dependencyRanges: [{range, floor}]` lists every *declared* dependency; `floor` = declared range lower
  bound (`System-[2.4,3.0)` → `2.4.0`), never the highest catalog version;
- every reference into a dependency is **major-qualified and model-version-less**: `System@2/Entity-1`
  (`CkModelId.IsMajorQualified`, `CkModelId.MajorQualified()`, `ToMajorQualified()`); own references
  stay concrete (`Basic-2.4.0/TreeNode-1`);
- `dependencies` keeps the exact closure (legacy readers, pre-publish check, SemVer diff).

**One major per range (review H6).** References are stored `Name@<major>` of the floor's major, so with range
retention every declared range must stay inside that major: `System-[2.5,3.0)` is fine, `System-2.5`
(= `>=2.5.0`), `System-[2.0,)`, `System-[2.5,3.0]` and `System-[2.5,4.0)` are rejected at compile time
("… admit more than one major version …"). Chosen over storing a per-major binding because it is the simpler
safe option: a new major is a deliberate cascade anyway (concept §4.3.2). Flag off is unaffected. An exclusive
lower bound (`(2.4,3.0)`) gets the next patch as floor (`2.4.1`, review L13) so the floor is inside the range.

Floor check ("compile against the floor, verify against the highest"): every element the model
references in a dependency must exist in the floor version (or, if the floor itself was never
published, the lowest available version in the range), else `ModelValidationException`
"references elements that do not exist at the floor of its dependency range …".

**Resolve side (both catalog and repository resolvers).** `CkCompiledModelRoot.GetResolutionRanges()`
returns the effective ranges (`CkModelDependencyDto.GetEffectiveRange()`: range with the lower bound
raised to the floor) for range-retaining models and the exact pins for classic models — classic models
keep their exact-match semantics. Dependency resolvers expand children through it, load each resolved
model once (different ranges resolve to the same installed version), and bind major-qualified references
to the resolved version of the same name and major (`CkReferenceRewriter.BindMajorQualified`) before
`AppendModel`. Root models (`HardResolveAsync(CkCompiledModelRoot)` / `SoftResolveAsync`) are bound **on a
copy**, so an import persists the version-less form. A reference whose major is not installed stays
unbound and fails reference resolution.

**Source generation (D1).** The compiled yaml of a range-retaining model holds `System@2/...`, the compile
cache the generator restores (`obj/octo-ck-cache/*.json`) holds the concrete versions the model was resolved
against. `CkSourceGenerator` binds the references to the cache's model ids (`CkGenerationModelBinder`, using the
now public `CkReferenceRewriter`) before any per-element generator looks them up; without it every service model
that assigns a dependency attribute failed with `OM1003 CkAttributeId 'System@2/Enabled-1' not found in CkCache`
(first System.Bot). Pinned by `RangeRetentionSourceGenerationTests` (Compiler.Tests links the dependency-free
generator files). Service repos pick the fix up only with a new `Meshmakers.Octo.ConstructionKit.SourceGeneration`
package.

**Transitive references are floor-checked (review L14).** A model may reference a model it does not declare
(`Industry.Energy` uses `${Basic}` but declares only `Industry.Basic`). Its references are still rewritten to
`Basic@2/...`, and `CatalogModelResolver.CollectTransitiveDependenciesAsync` derives the guarantee for them from the
resolved intermediate models — their range-retaining dependency, or their exact pin as `[v]` with floor `v`; the
highest floor wins. The same floor check as for declared dependencies runs against it; a miss says the dependency is
transitive, names the intermediate and asks to declare the model in `ckModel.yaml`. No `dependencyRanges` entry is
written for transitive models (declared ranges stay verbatim). Pinned by
`FlagOn_TransitiveReference_IsFloorCheckedAgainstTheIntermediateRange`.

**Unbound references in source generation (D1 diagnostic, OM1004).** When a major-qualified reference is still
unbound after `CkGenerationModelBinder.BindToCache` (the compile cache holds no version of that model and major), the
generator reports `OM1004 Unbound major-qualified CK reference` naming the references and the cached model versions,
instead of the opaque `OM1003 … not found in CkCache`. Pinned by `UnboundReference_IsReportedWithReferenceAndCacheContent`.

**Range identity in the resolvers (D2).** `CkModelIdVersionRange.Equals` means *overlaps* (not transitive,
inconsistent with `GetHashCode`). `DependencyRangeMatcher` therefore matches structurally (same name, identical
`CkVersionRange`) whenever range retention is involved — a child range of a range-retaining dependency, or the root
ranges of a range-retaining root (`RangeRetainingDependencyRanges`: compile with the flag on, hard resolve of a
range-retaining compiled model). **Classic children of a classic root keep main's overlap merge** (review G3 E-H1):
with the flag off a v1 compile behaves exactly like main (`V1DependencyMergeTests`, verified against main 0db1c50 —
a library pinned to System-2.5.0 next to System 2.6.0 fails with "unknown base type", as on main, not with
error 66). With range retention `System-[2.5,3.0)` overlaps
both exact pins `System-[2.5.0]` and `System-[2.6.0]`; the overlap lookup threw "Sequence contains more than
one matching element" when a System minor was imported into a tenant holding exact-pinned and
range-retaining models (or silently merged the range into the wrong exact entry). Pinned by
`RangeRetentionRepositoryResolverTests`. `Equals` itself is unchanged (other callers rely on it).

**One version per dependency model (AB#6671, replaces the former limitation N4).** A range-retaining resolve of the
catalog (`CatalogDependencyResolver` with `RangeRetainingDependencyRanges` roots: compile with the flag on, hard
resolve of a range-retaining compiled model) no longer resolves every queued range on its own. `CatalogConstraintSolver`
collects every constraint of one model **name** over the whole closure — ranges and the exact pins of classic
dependencies — and chooses the **highest catalog version that satisfies all of them** (iterated to a fixed point,
because a chosen model contributes its own constraints; no backtracking over versions of dependents). Example:
`System-[2.5,3.0)` next to a classic dependent pinned to `System-[2.5.0]` with System 2.5.0 and 2.6.0 in the catalog
resolves System once, to 2.5.0 (before: error 66). When no version satisfies all constraints the compile fails with
`ModelValidationException.UnsatisfiableDependencyConstraints`, which names every constraint, who requires it and the
versions the catalogs show. In that mode the child ranges are entries of the walk matched structurally (the overlap
merge of `DependencyRangeMatcher` is not used). Classic resolves (flag off, classic roots) keep the old walk and the
overlap merge byte for byte (`V1DependencyMergeTests`). The repository side is unaffected: a tenant has exactly one
installed version per model, so an exact pin fails on its own (`ResolveFailed`) and a range resolves. Pinned by
`RangeRetentionCompileTests` (`…ResolveSystemOnce`, `…ConstraintsNoVersionSatisfies…`, `…ThreeLevelChain…`,
`…HighestWins`).

**Resolution by SemVer, the matrix (AB#4468).** Range `System-[2.5,3.0)` (floor 2.5.0) against the available versions:
`{2.5.0}` → 2.5.0; `{2.6.1}` → 2.6.1; `{2.4.9}` and `{3.0.0}` → rejected with a message naming the range and the visible
versions; `{2.5.0, 2.6.1, 3.0.0}` → 2.6.1 (highest in range). The same table runs through the catalog/compile resolver
(`DependencyResolutionMatrixTests`, Compiler.Tests; with and without range retention), the repository resolver
(`RepositoryResolutionMatrixTests`, Engine.Tests) and the tenant re-validation (engine-mongodb). The repository
side lists the installed versions through the optional `IRepositoryVersionLister` (implemented by
`RepositoryManagementService` → `IModelRepository`, by `DatabaseCkModelRepository` in engine-mongodb); a repository without
it reports the range without versions. **No special treatment for System and Meshmakers-shipped models.** Blueprint floor
semantics are not changed (AB#6672). Rules page: `docs/ck-semver-rules.md`, "Dependency resolution by SemVer".

**Message of an unsatisfiable dependency in a repository (AB#7381).** The repository (tenant) resolver judges against the repository's own
`Available` models, not against a catalog. `RepositoryDependencyResolver` therefore asks the optional `IRepositoryInstalledVersionLister`
(`ListInstalledVersionsAsync`: every held version with its state; `RepositoryManagementService` forwards to the `IModelRepository`, engine-mongodb
implements it) and throws `ModelValidationException.UnknownCkModelsInRepository`: `Industry.Basic-[2.5,3.0): the tenant has no Available version of
Industry.Basic (installed: 2.5.0 ResolveFailed)`, or `... has Available versions of X (2.4.0, 3.0.0), none satisfies the range`, or `... has no version of X
installed`; the text never says "catalog". A repository without the interface keeps the AB#4468 text ("does not match any visible version of X (...)");
the catalog resolver's message is unchanged. Tests: `RepositoryUnresolvedDependencyMessageTests`. The import itself (engine-mongodb) leaves a previously
installed version of the imported model in place when this error (or any validation error) aborts it - see engine-mongodb `CLAUDE.md`.

**Range audit (AB#6683).** The one-major-per-range rule lives in `CkDependencyRangeRule` (`FloorOf`, `IsWithinOneMajor`), called by
`CatalogModelResolver` and by `CkModelRangeAudit` / `octo-ckc -c AuditRanges -p <dir>` (read-only scan of `ckModel.yaml` files, non-zero
exit when a range is rejected) and `scripts/audit-ck-ranges.sh <monorepo-root>` (all repositories with CK sources, list in
`docs/ck-semver-rules.md`, "Range audit"). `RangeAuditTests.Rule_AgreesWithTheRangeRetainingCompile` pins audit = compiler.

The CK SemVer diff does not classify `DependencyRanges` yet (documented exclusion, Phase 2 F2.1). The
element schemas accept `@` in the model part of a reference; the compiled schema accepts
`dependencyRanges`. Tests: `RangeRetentionCompileTests` (flag on/off, YAML round trip with schema
validation, floor violation, resolve against a later minor without recompile, two-level chain).
