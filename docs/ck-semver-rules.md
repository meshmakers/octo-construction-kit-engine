# Construction Kit SemVer Rules and Version Validation

The version of a Construction Kit model lives in the `modelId` suffix of `ckModel.yaml`
(e.g. `modelId: Basic-2.0.2`) and is maintained manually by the developer. The
`ckc ValidateVersion` command enforces that this version is *honest*: it diffs the current model
against its baseline — the newest published version of the same major (see "Baseline" below) —, classifies every change according to the fixed rule set below,
and fails when the declared version does not satisfy the derived minimum bump level.

The command **never writes** `ckModel.yaml` — the developer stays in control of the version; the
tool only checks it. Enforcement happens as a CI gate (PR/main builds).

## The command

```
ckc ValidateVersion -p <ck-folder> [-p <ck-folder2> ...]
                    [-cn <catalogName>] [-o <report.md>]
                    [-rf|--refresh] [-cl|--changelog] [-rmm|--requireMigrationForMajor]
                    [-lce <bool>] [-lcr <path>]
```

| Argument | Description |
| -------- | ----------- |
| `-p, --path` | Root path(s) of Construction Kit model directories. Multiple paths are validated in the given order — pass them in **dependency order** (dependencies first): a later package may depend on a version a sibling package introduces in the same run (see the sibling-resolution note below). |
| `-cn, --catalogName` | Pins **baseline retrieval and the FR-9 dependency-existence check** (`OCTO-CK102`/`OCTO-CK103`) to the named catalog. Without it, all readable catalogs are queried (catalog order: Embedded → LocalFileSystem → PrivateGitHub → PublicGitHub) and the baseline follows the rules in "Baseline" below. **It does not pin the compile-stage dependency *resolution*** — see the note below. |
| `-o, --output` | Additionally writes the report as Markdown (e.g. for PR comments). |
| `-rf, --refresh` | Forces a catalog cache refresh before the baseline is determined. Always use this in CI. |
| `-cl, --changelog` | Writes/updates the `CHANGELOG.md` section of the declared version next to `ckModel.yaml`. Only runs after successful validation; older sections are never rewritten. |
| `-rmm, --requireMigrationForMajor` | Escalates a missing migration for a required major bump from a warning to an error. |
| `-lce, -lcr` | Enable/point the local file system catalog for this invocation (same semantics as `Compile`). |

> **`-cn` pins the baseline, not the compile-stage dependency resolution.** Determining the
> baseline (which version is "the last published one") and the FR-9 dependency-existence check are
> restricted to the named catalog. Compiling the current model in memory, however, resolves its
> dependency closure across **all** readable catalogs — this is by design (the compiler is the same
> one used everywhere), but it means a stale entry in another catalog (typically a leftover local
> `LocalFileSystemCatalog` model with different content) can still poison transitive resolution and
> flip the diff of a *dependent* model to a spurious level, even though `-cn` was given. Combine
> `-cn` with a clean, per-run local catalog to eliminate this.
>
> **Recommended for CI and reproducible local runs:** `-cn <catalog> -rf -lcr <empty-dir>` — pin the
> baseline to one catalog, force a cache refresh, and point the local catalog at a fresh empty
> directory so no stale local content participates in resolution. The shipped CI step in
> `octo-construction-kit` does exactly this via a per-run `$(OctoLocalCatalogRootPath)`.
>
> **Same-run sibling dependency resolution.** A commit that bumps a dependency model and its
> consumer together (e.g. `Basic.Energy` 1.1.4 → 1.2.0 plus `EnergyCommunity` 4.0.0 depending on
> `Basic.Energy-[1.2,2.0)`) declares a range no catalog can satisfy yet — the new dependency
> version is only published *during* the subsequent build. To keep this from dying with a false
> `OCTO-CK103`, every package that validates successfully (including first publications) is
> registered for the rest of the invocation: its declared version satisfies the FR-9
> dependency-existence check of later packages, and its compiled model is published to the local
> file system catalog (the run-isolated `-lcr` directory in CI) so the compile stage of later
> packages resolves it. This is why the `-p` paths must be in dependency order — a consumer
> validated *before* its same-run dependency still fails with `OCTO-CK103`, honestly.

### Baseline (AB#5450)

One shared, read-only resolver (`ICkBaselineResolver` in `ConstructionKit.Engine/SemVer/`) decides which version a
model is compared against. `ValidateVersion` uses it; the compile gate (AB#6294) and the publish gate (F2.3) will use
the same resolver, so every gate agrees. It queries each catalog separately (never the merged highest version).

1. **Same major.** The baseline is the newest version within `[major.0.0, major+1.0.0)` of the declared version.
   EnergyCommunity `3.4.0` with `3.3.0` and `4.5.0` published is compared against `3.3.0`; a declared `3.2.0` fails
   with `OCTO-CK101` against `3.3.0`. A maintenance release on an older major line therefore passes the normal gate.
2. **New major.** When the declared major has no version yet, the baseline is the newest version of the highest
   *lower* major: `4.0.0` is still diffed against the newest `3.x`, so the migration check (`OCTO-CK104`) and the
   changelog keep working. A major allows any change; when the changes only need a minor or patch bump, the report
   notes a "valid major bump without structural need". Only a model without any version is a first publication.
3. **Never the model under test (AB#5434, symptom 2).** Entries of the `LocalFileSystemCatalog` at or above the
   declared version are never the baseline: they come from earlier local builds or from `ValidateVersion`'s own
   sibling registration, not from a publish. They are listed as ignored in the report. A local entry *below* the
   declared version can be the baseline (marked "local, not published"). A published (remote) entry equal to the
   declared version stays the baseline, so an unchanged version with structural changes still fails with
   `OCTO-CK100`. On a version tie the published entry wins over a local copy. Running `ValidateVersion -rf` twice gives
   the same baseline and verdict.
4. **Read-only.** The resolver never publishes, refreshes or restores. (`ValidateVersion` itself still registers a
   validated model in the local catalog for sibling resolution; stopping that write is AB#5434 symptom 1, F2.3.)
5. **Visible.** The report names the baseline version and catalog (e.g. `Published: 3.3.0 (PrivateGitHubCatalog)`),
   marks a local baseline, says when the baseline comes from the previous major line, and lists ignored local entries.

When a catalog source is unreachable and no same-major baseline was found, the resolver does not fall back to a
lower major: the result is `OCTO-CK102`, as before.

Blueprints (`octo-bpm validateVersion`) keep their own baseline lookup; they have no structural diff and are immutable
per version (checked for AB#5450, not changed).

### Validation rule

With *published* = the baseline (see above) and *minimum* = published + exactly one bump of the
highest level in the diff:

- Diff empty and declared == published → **valid**.
- Diff empty and declared > published → **valid** (reported as a note: bump without structural
  change, e.g. a semantic change — legitimate).
- Diff non-empty and declared >= minimum → **valid** (higher than required is ok — only the
  *minimum* level is enforced).
- Diff non-empty and declared < minimum (in particular: version left untouched) → **error**.
- Declared < published → **error** (downgrade).

### Error codes

| Code | Meaning | Remediation |
| ---- | ------- | ----------- |
| `OCTO-CK100` | Declared version below the required minimum | Raise the version in `ckModel.yaml` to at least the reported minimum |
| `OCTO-CK101` | Declared version below the published version (downgrade) | Use a version >= the published one |
| `OCTO-CK102` | Catalog source unreachable — baseline (or dependency check) impossible | Check network/VPN and catalog configuration, retry with `--refresh` |
| `OCTO-CK103` | A dependency range is satisfied by no published version and no sibling package validated earlier in the same run | Publish the dependency first, correct the range, or pass the dependency's source path before the consumer's (dependency order) |
| `OCTO-CK104` | Major bump without a matching migration (only with `--requireMigrationForMajor`) | Add a migration with `toVersion` == declared version |

Exit codes: `0` = valid, non-zero = violation or error (CI-friendly). Concretely, a version
violation (any `OCTO-CK1xx` finding, an unknown catalog, or a compile failure) exits with the
process code `-6` (`ModelValidationException`); because process exit codes are unsigned bytes this
surfaces as `250` in PowerShell's `$LASTEXITCODE` and `250` in a POSIX shell's `$?`. **CI must gate
on `-ne 0`, never on `-eq 1`** — the command never exits with `1`.

### First publication vs. unreachable catalogs

"No catalog responds" and "catalogs respond, model unknown" are strictly separated:

- Model unknown in all (reachable) catalogs → **first publication** → valid, any version is
  accepted as the starting point.
- Catalog source unreachable during the last cache refresh → **error** `OCTO-CK102`. A missing
  baseline is never silently interpreted as a first publication.

The report also prints the catalog cache age of the baseline; `--refresh` forces a refresh
(mandatory in CI, where a stale cache would validate against the wrong baseline).

## The fixed rule set (V1)

The highest applicable level in the overall diff determines the minimum bump level. The rule set
is built-in and not configurable. It is implemented as the central rule table in
`ConstructionKit.Engine/SemVer/CkSemVerClassifier.cs` — keep this page and that table in sync.

Validation always compares the **compiled, canonically sorted** models
(`CkCompiledModelRoot`), never raw source YAMLs. Pure formatting/comment changes in the sources
therefore produce an empty diff and require no bump.

References to elements of the model itself are compared ignoring the model version; references
into other models are compared by name and major version. A dependency switching to a new major
therefore surfaces as a breaking reference change, while minor/revision dependency bumps only
surface in the dependency diff.

### Major (breaking)

| Change | Reasoning |
| ------ | --------- |
| Type, attribute, enum, record, or association role **removed** | Consumers reference the element |
| Attribute assignment removed from a type, record, or association role | Consumers reference the attribute |
| `valueType` of an attribute changed (every change except `String → Secret`, including `Secret → String`) | Data format breaks |
| `valueCkEnumId` / `valueCkRecordId` changed | Reference target breaks |
| Attribute assignment references a different attribute definition (`id` changed) | Value semantics may break (defensive) |
| Type attribute changed from optional to **required** (`isOptional: true → false`) | Existing instances may become invalid |
| New **required** attribute **without** `defaultValues` (or referencing an attribute of another model) | Existing instances become invalid (defensive when not inspectable) |
| `derivedFromCkTypeId` / `derivedFromCkRecordId` changed | Inheritance hierarchy breaks (GraphQL schema, queries) |
| Multiplicity **tightened** (permissiveness One < ZeroOrOne < N decreases) | Existing associations may become invalid |
| `inboundName` / `outboundName` of an association role changed | Navigation/GraphQL breaks |
| Type association removed | Consumers use the navigation |
| Type association added referencing a role with multiplicity **One** (either direction) | New mandatory association — entity creation without it is rejected |
| Type association added referencing a role of **another model** | Multiplicity not inspectable (defensive) |
| `targetCkAttributeIds` of a type association changed | Referential integrity changes (defensive) |
| Enum value **removed** or `key` of an existing `name` changed | Stored values become unreadable |
| `useFlags` changed | Value semantics break |
| `isExtensible: true → false` | Runtime extensions are no longer allowed |
| `isAbstract: false → true` or `isFinal: false → true` | Instantiation/derivation breaks |
| Unique index added (`Unique`, `UniqueNotDeleted`) | Existing data may be invalid |
| Type is no longer a collection root (`isCollectionRoot: true → false`) | Collection semantics break (defensive) |
| Dependency removed | Consumers may rely on the transitively provided model (defensive) |
| Dependency switched to a new **major** version | Transitively breaking |
| CK v2: interface **removed** | Implementing types and interface consumers break |
| CK v2: interface member removed, required member added, member `id` changed, member made required | The contract changed — publish a new interface version (`Named-2`) instead (rows I3–I7) |
| CK v2: `implements` entry removed from a type | Consumers querying the type through the interface break |
| CK v2: interface `extends` entry added or removed | The inherited members change the contract — publish a new interface version instead |
| CK v2: interface association member removed, required one added, `target` or `multiplicity` changed, made required | The contract changed — publish a new interface version instead (rows I3–I7) |
| CK v2: interface method added or removed | The contract changed — publish a new interface version instead (rows I11, M1); field changes follow rows M2–M14 |
| CK v2: type association `targetCkInterfaceId` set or changed | The allowed targets narrow — existing associations may become invalid |
| CK v2: method **removed** | Callers of the method break |
| CK v2: breaking method field change (rows M3–M10, M12) | Callers break — publish a new method version (`ChangePassword-2`) instead |
| CK v2: `ckLanguage` lowered (`2 → 1`) | CK v2 elements may disappear (defensive) |
| CK v2: attribute-assignment `access` tightened (`ReadWrite < ReadOnly < MethodOnly < Hidden`; row T7) | Generic GraphQL clients and dependents lose write/read access |
| CK v2: `visibility` `Public → Internal` (type, record, enum, attribute, association role, interface, method; resolved value, omitted = `Public`) | Other models referencing the element break |
| CK v2: `derivable` `Any → Model` (type, record; resolved value) | Other models deriving from the element break |
| CK v2: `ckLanguage` raised (`1 → 2`) without declaring `derivable: Any` on every type/record | The `derivable` default flips from `Any` to `Model`; reported as a `derivable` change per element |

### Minor (additive)

| Change | Reasoning |
| ------ | --------- |
| New type, attribute, enum, record, association role | Purely additive |
| New **optional** attribute on an existing type/record/association role | Additive |
| New **required** attribute **with** `defaultValues` (same model) | Additive, existing data can be filled |
| New enum value | Additive (precedent: `isExtensible` semantics) |
| `isExtensible: false → true` | Relaxation |
| Attribute changed from required to optional | Relaxation |
| Multiplicity **relaxed** (permissiveness One < ZeroOrOne < N increases) | Relaxation |
| `isAbstract: true → false`, `isFinal: true → false` | Relaxation |
| Non-unique index added, any index removed | Query behavior, no data break |
| `defaultValues` / `autoCompleteValues` / `autoIncrementReference` changed | Behavior of newly created instances changes |
| `isRuntimeState` changed (deprecated alias) | Blueprint re-apply behavior changes |
| Attribute `ownership` changed (resolved value) | Blueprint re-apply and export behavior change |
| Attribute-assignment `ownership` override set, cleared or changed | Re-apply and export behavior change for that assignment |
| `valueType` changed from `String` to `Secret` (AB#5528, decision 2) | Stored values stay readable: readers accept legacy plaintext in a Secret slot until the encrypt sweep has run, and the effective ownership becomes `Secret` (re-apply keeps the value, export drops it). Clients that select the value as a string must switch to the is-set state before the model change (concept phase 2) |
| Record `recordKey` set, cleared or changed (AB#5528) | Only decides how secret sub-values are carried over when a record array is replaced; no data or schema change |
| Attribute `metaData` changed | Metadata only, no data break |
| `enableChangeStreamPreAndPostImages` changed | Change stream behavior, no data break |
| Type becomes a collection root (`isCollectionRoot: false → true`) | Additive |
| New type association referencing a non-mandatory role of the same model (no multiplicity One) | Additive |
| New dependency | Additive |
| Dependency version changed without a major switch | Compatible |
| CK v2: new interface | Purely additive |
| CK v2: new `implements` entry on a type | Additive |
| CK v2: new method | Additive |
| CK v2: attribute-assignment `access` relaxed (resolved value; omitted = `ReadWrite`; row T7) | "access/security" changelog note |
| CK v2: `access` tightened on an attribute that is `securitySensitive` in both versions (row T7 exception) | Accepted security exception: Minor + `requiresAcknowledge` |
| CK v2: attribute `securitySensitive` set or cleared (row A3) | Marker only, no data or API change |
| CK v2: `ckLanguage` raised (`1 → 2`; omitted = 1) | Older engines reject the model with message 91 instead of misreading it. Major instead when it flips a `derivable` default (see Major) |
| CK v2: `visibility` `Internal → Public` | Relaxation |
| CK v2: interface `deprecated` set or cleared | Dependents get (or lose) a compile warning; nothing breaks |
| CK v2: type association `targetCkInterfaceId` cleared | Relaxation |
| CK v2: `derivable` `Model → Any` | Relaxation |

### Patch

| Change | Reasoning |
| ------ | --------- |
| `description` (all element kinds, model meta — including CK v2 interfaces, type/interface methods, method parameters and error codes, row M14) | Purely documentational |
| `displayNameRule` / `displayDescriptionRule` changed on a type | Computed display values change only, no data/schema break |
| Pure formatting/comment changes in the source YAMLs | Compiled model identical → empty diff → no bump required |
| `isRuntimeState: true` rewritten as `ownership: RuntimeState` (or `false` as `SeedOwned`) | Same resolved ownership → empty diff → **no bump required**. Both markers are compared on their resolved value, so migrating a declaration to the enum costs nothing; only a genuine change of owner does. |

### CK v2 rule rows (F2.1)

Every row has an id and a test named after it (`N1_…` in `tests/ConstructionKit.Engine.Tests/SemVer/Rows/`); the
classification guard checks both directions. Rows apply to `ckLanguage: 2` elements (interfaces, methods, visibility,
range retention, `securitySensitive`); no level of a v1 rule changes. Rows T, E, R and A restate the v1 rules for public
types, enums, records and attribute definitions with one test each and add the CK v2 access rule (T7).

**Internal elements (AB#6266).** Other models can never reference an internal element, so it is not part of the
compatibility surface. A change counts as internal when the element — itself or through its owner (type, record,
association role, enum, interface, method) — is `internal` in every version in which it exists.

| Row | Change | Level |
| --- | ------ | ----- |
| N1 | Add, remove or modify an element that is internal in the baseline and in the current version (type, record, enum, attribute, association role, interface, method) | at most Minor (description-only stays Patch), reason "internal element, not part of the compatibility surface" |
| N2 | Any change to a member of an internal owner (type/record/role attribute, type association, index, implemented interface, interface member, method parameter or error) | at most Minor |
| N3 | Element removed that was public in the baseline | Major (unchanged) |
| N4 | `visibility` public → internal / internal → public | Major / Minor |
| N5 | Element made public and changed in the same release | classified by the public rules (no loophole) |

Minor rather than none: a same-version re-import is short-circuited, so a structural change still needs a bump to reach
tenants.

**No internal element is reachable from a public one (AB#6334 / AB#6335, gate findings H1/H3).** The rows above are only
sound when "internal" really means "unreachable from other models". The compiler therefore rejects **inconsistent
visibility** in a `ckLanguage: 2` model with message **129** (`CkInconsistentVisibility`): a public element may only
reference public elements of its own model.

| Public referrer | References that must be public |
| --------------- | ------------------------------ |
| Type | `derivedFromCkTypeId`, `implements`, attribute assignments (attribute definition), association role, association `targetCkTypeId` / `targetCkInterfaceId` / `targetCkAttributeIds` |
| Attribute definition | `valueCkRecordId`, `valueCkEnumId` |
| Record | `derivedFromCkRecordId`, attribute assignments |
| Association role | attribute assignments |
| Interface | `extends`, attribute members, association members (role, targets), **methods** (a public interface may not declare an internal method) |
| Public method of a public type | parameter and result `valueCkRecordId` / `valueCkEnumId` |

A type may also not redeclare a method of a public interface it implements as `internal` (129). Allowed: internal →
public, internal → internal, public → public, internal methods on public types (they may use internal records and
enums). Without the rule, an internal record behind a public attribute could lose a member as a "Minor" change while a
dependent model that uses the public attribute breaks (gate case X1: error 109 downstream). Defence in depth: when the
classifier is handed a model in which a public element still reaches an internal one (compiled by an older ckc), that
internal element — transitively — is treated as public and the cap of N1/N2 does not apply; an internal method of a
public interface follows I11. `CkVisibilityReferenceCoverageTests` fails when a CK DTO gains an element reference that the
visibility walk does not cover.

**Interfaces (AB#6267).** An interface `X-n` grows by optional members; every change that breaks implementors or
consumers is Major and the reason recommends publishing `X-(n+1)` (e.g. `Named-2`) next to `X-n`. Association members are
keyed by their role, so a changed target is one change (`target`), not remove + add.

| Row | Change | Level |
| --- | ------ | ----- |
| I1 | Optional interface attribute added | Minor when its attribute definition is declared in this model **and** is new in this release or was internal in the baseline; otherwise Major (AB#6337): a type of another model may already assign the existing definition — as `Hidden` (error 99) or under another name (I-3) — and the member binds to it. Remedy: a new attribute definition for the member, or a new interface version |
| I2 | Optional interface association added | Minor (optional association members are never checked against implementors, rule 121, so they cannot collide) |
| I3 | Required attribute or association added | Major |
| I4 | Member removed or renamed (remove + add) | Major |
| I5 | Member's attribute id changed (value type, record or enum change of the member) | Major |
| I6 | Association `multiplicity` or `target` changed | Major |
| I7 | Member `isOptional` true → false / false → true | Major / Minor |
| I8 | `extends` entry added or removed | Major |
| I9 | `deprecated` set or withdrawn | Minor |
| I10 | Interface added / removed | Minor / Major |
| I11 | Method added to an interface | Major (an optional interface method does not exist yet); an `internal` method on a public interface is a compile error (129), so it cannot be added as a capped Minor |
| I12 | Invocation contract of an interface method changed (`kind`; parameter added or removed; parameter `valueType`, record, enum, `isOptional` in either direction, `sensitive`; `result`; error code added or removed) | Major — a type in another model that redeclares the method with the previous contract breaks with error 122; this lifts M2, M5 (required → optional) and M13 on interface methods. Metadata (descriptions, authorization, execution) keeps its M-row level. Type methods are unchanged (M1–M14) |

**Invocation contract (AB#6336, gate finding H2).** A type may redeclare a method of an interface it implements; error
**122** fires only when the redeclaration has another **invocation contract**: `kind`, the parameters as a set keyed
by name (`valueType`, `valueCkRecordId`, `valueCkEnumId`, `isOptional`, `sensitive`), `result` (present or absent and
its type) and the set of error codes. Descriptions, `authorization`, `execution` and the order of parameters and
errors may differ; the redeclaring type's own authorization and execution apply. The definition lives in one place,
`CkMethodContract`, used by error 122 and by row I12.

**Methods (AB#6268).** Type methods and interface methods follow the same rows (for interface methods, I12 lifts every
contract change to Major). The diff emits one change per method
field; parameters (`Method parameter '<owner>/<method>/<name>'`) and error codes (`Method error '<owner>/<method>/<code>'`)
are members of their own. The rendered `signature` is still reported as a readable before/after summary with level
`None`; the level comes from the field changes. A change of several fields takes the highest level.

| Row | Change | Level |
| --- | ------ | ----- |
| M1 | Method added to a public type / removed | Minor / Major (on an interface the addition is row I11) |
| M2 | Optional parameter added | Minor (interface method: Major, row I12) |
| M3 | Required parameter added; parameter removed or renamed | Major |
| M4 | Parameter value type, record id or enum id changed | Major |
| M5 | Parameter optional → required / required → optional | Major / Minor; on an interface method relaxing is Major too (row I12) |
| M6 | Result changed (none ↔ value, other type, record or enum) | Major; widening a result record by an optional attribute is a record change |
| M7 | Error code removed | Major |
| M8 | Error code added | Major (Minor needs `errors: open`, which does not exist yet) |
| M9 | `kind` static ↔ instance | Major |
| M10 | `idempotent` true → false / false → true | Major / Minor |
| M11 | `timeoutSeconds` changed | Minor (behavioural) |
| M12 | Authorization stricter (role removed or roles emptied, **scope added**, `allowSelf` true → false, block removed) / looser (role added — also to an empty list —, **scope removed**, `allowSelf` set, block added that only grants) | Major / Minor; mixed → Major. Roles are any-of, **scopes all-of**, and authorization is **default-deny**: an omitted block or empty roles admit administrators only (AB#6338, platform-owner decision 2026-10-10; the F3.4 gateway enforces the same). A block added or removed is one change compared field by field: an omitted block counts as no roles and no scopes; for self-calls the stricter reading applies (removing a block that allowed them, or adding one that forbids them, is Major — whether an owner may call without a block is decided by F3.4). Every looser change is also listed under "Behavioural changes" as "security: method access widened" |
| M13 | Parameter `sensitive` changed | Minor (interface method: Major, row I12) |
| M14 | Description of the method, a parameter or an error | Patch |

**Public types, stable bases, enums, records, attribute definitions (AB#6269).** A *stable base* is a type other
models may derive from: in a `ckLanguage: 2` model a public, non-final type with effective `derivable: Any`; in a v1
model only `System/Entity` and `System/Configuration` (derived, no meta-model field). Stable bases are public types,
so T1–T7 apply to them unchanged.

| Row | Change | Level |
| --- | ------ | ----- |
| T1 | Optional attribute added to a public type | Minor |
| T2 | Implemented interface added | Minor |
| T3 | Attribute, association, implemented interface or method removed | Major |
| T4 | Base type changed | Major |
| T5 | `isAbstract` / `isFinal` false → true / true → false | Major / Minor |
| T6 | `derivable` Any → Model / Model → Any | Major / Minor |
| T7 | Attribute `access` stricter (order `ReadWrite < ReadOnly < MethodOnly < Hidden`) / looser | Major / Minor. **Security exception** (platform-owner decision 2026-10-10): tightening an attribute that is `securitySensitive: true` in the baseline and the current version is Minor + `requiresAcknowledge`; every other change of such an attribute follows the normal rules |
| E1 | Enum value added (also to a non-extensible enum) | Minor |
| E2 | Enum value removed or renumbered; `useFlags` changed; `isExtensible` true → false | Major |
| R1 | Optional record attribute added | Minor |
| R2 | Record attribute removed, its attribute id changed, optional → required | Major |
| A1 | Attribute definition `description` / `metaData` | Patch / Minor |
| A2 | Attribute definition value type, record id or enum id (`String → Secret` stays Minor, AB#5528) | Major |
| A3 | Attribute `securitySensitive` set or cleared (`ckLanguage: 2`, resolved value: omitted = false) | Minor |

`securitySensitive: true` marks password hashes, security stamps, tokens and 2FA secrets (System.Identity marks them in
Phase 4, F4.2). It is a stored meta-model field (schema, DTO, graph, Mongo, reflection round-trip gate); a v1 model that
declares it fails with message 90.

**Behavioural changes (AB#6270).** Changes that keep the schema compatible but change runtime behaviour keep their
level and are flagged `IsBehavioural`; the verdict report lists them under "Behavioural changes" (after the change
list) and the changelog moves them from "Added" / "Changed" into a section `### Behavioural changes`. Changes that
require an acknowledge (`RequiresAcknowledge`) are listed there too, with "(requires acknowledge)"; breaking ones stay
under "Breaking" with the same suffix. The acknowledge itself is built in F2.2 / F2.3. **Format change for v1 models:**
a changelog section that contains behavioural changes gets the new heading and those lines move under it; a report
gets the extra list. Without behavioural changes the output is unchanged.

| Row | Change | Level |
| --- | ------ | ----- |
| B1 | Attribute default values, `displayNameRule` / `displayDescriptionRule`, `autoCompleteValues`, `autoIncrementReference`, `enableChangeStreamPreAndPostImages`, method `timeoutSeconds` | unchanged (Minor/Patch), marked behavioural. **Exception (AB#6341, ckLanguage 2):** removing the default values of an attribute definition that is assigned as required anywhere in the model or is public is Major — otherwise "required with default" (Minor) plus "default removed" (Minor) would reach "required without default" (Major) in two minor releases |
| B2 | Non-unique index added or removed (any index removed) | Minor, marked behavioural |
| B3 | Unique index added on a type that is not a stable base | Major, no marker |
| B4 | Unique index added on a stable base | Major + `requiresAcknowledge`; the reason names the impact on derived types in other models |

**Range retention (AB#6271).** For a range-retaining model the declared ranges are compared by dependency name
(`Dependency range '<name>'`). The exact closure (`dependencies`) is still diffed as before; removing the rule "resolved
dependency changed → Minor" is F2.4 (AB#5686).

| Row | Change (range-retaining model) | Level |
| --- | ------------------------------ | ----- |
| D1 | Floor raised within the same major | Minor |
| D2 | Floor lowered or range widened within the same major | Minor |
| D3 | Upper bound narrowed within the same major | Minor |
| D4 | Range or floor moves to another major | Major |
| D5 | Range dependency added / removed | Minor / Major (as for exact pins) |
| D6 | Model switches from exact pins to range retention, or back (`rangeRetention`) | Minor, reason points to the one-time re-pin (F2.6) |
| D7 | `usedSurface` / `usedSurfaceHash` changed | not classified on its own (derived from the model's own changes); listed in `CkModelDiffService.ExcludedProperties` with that reason (AB#4472) |

### usedSurface (AB#4472)

A range-retaining compiled model records, per declared dependency, which elements and members of that dependency it
uses: `dependencyRanges[].usedSurface` (sorted, de-duplicated, major-qualified, version-less ids) and
`usedSurfaceHash` (`sha256:<hex>` over the list, one id per line). Both are computed by the compiler
(`CkUsedSurfaceCollector`) and stored in the catalog JSON and in the tenant's `CkModel` document.

**What is tracked.**

- Element level, for every reference the compiler resolves: base types and base records, implemented interfaces,
  interface `extends`, reused attribute definitions (type, record, role and interface assignments), records and enums
  used as value types (attributes, method parameters and results), association roles, association target types,
  interfaces and target attributes. Deriving from a type or implementing an interface binds its whole public
  surface, so these are recorded as the element (`System@2/Entity-1`).
- Member level: attribute paths into an inherited dependency type — index fields and `ownerAttributePath` — as
  `System@2/Entity-1.Name` (the nearest base type that declares the attribute).
- Not tracked: references into the model itself; references into transitive dependencies that the model does not
  declare (they have no `dependencyRanges` entry; they are floor-checked); display rules (`displayNameRule`) and
  computed-column formulas, which are not parsed for attribute paths. Internal dependency elements cannot appear,
  the compiler rejects references to them (message 112).

**Which change classes it covers (the research question of AB#4472).** A change of a dependency can be tolerated for a
consumer — even across a major — exactly when it touches nothing listed in that consumer's `usedSurface`: removing or
renaming an element or member, changing its value type, record or enum, its multiplicity, a base type, or tightening
`access` / `visibility` / `derivable` of a listed element. With the F2.1 rules every such change of a *public* element
is Major, so the classifier and `usedSurface` together answer "would break: Industry.Energy uses
System@2/Entity-1.Name" (F2.5, AB#5687, consumes it at tenant import).

**What it cannot prove.** Behavioural and semantic changes keep the surface intact: a changed default value, display
rule, index, method timeout or authorization semantics, a changed meaning of an enum value or attribute, or data a
migration rewrites. These are flagged as behavioural changes (rows B1–B4) but not tracked per consumer. A derive or an
implement binds the whole element, so a consumer counts as affected by every member change of that element, even of
members it never reads.

`usedSurface` is not classified on its own (row D7): it is derived from the model's own references, which are diffed and
classified elsewhere.

### Defensive default

A change without an explicit rule is classified as **Major**. Since only a minimum level is
enforced, an overly strict classification is annoying for the developer but never wrong — an
overly lax one, however, is dangerous.

### Classification guard (AB#6272)

A meta-model field cannot reach main unless it has a diff **and** a classifier rule. The guard is
`tests/ConstructionKit.Engine.Tests/SemVer/CkSemVerClassificationGuardTests.cs`; it fails, naming the item, when

1. a public property of an element DTO (`CkTypeDto`, `CkAttributeDto`, `CkEnumDto`, `CkRecordDto`,
   `CkAssociationRoleDto`, the CK v2 interface and method DTOs and their nested DTOs) is neither in
   `CkModelDiffService.ComparedProperties` nor in `CkModelDiffService.ExcludedProperties` — or a new DTO type is
   neither diffed nor excluded with a reason;
2. a compared property has no probe in the guard test, or its probe produces no diff change, or a change of a
   shape that is missing in `CkModelDiffService.EmittableChanges`;
3. a change emitted by a probe, or a synthetic change of any shape in `EmittableChanges` (element kind, change
   kind, property), reaches the defensive default of `CkSemVerClassifier` (`ClassifyChange`,
   `ClassifyDependencyChange`, `ClassifyAttributeAssignmentChange`);
4. a `CkModelElementKind` value has no rule (no shape, or every shape reaches the default);
5. an exclusion has no written reason (`ExcludedProperties` maps each property to its reason);
6. the rule rows of this page and the tests drift apart: a table row whose first cell is a row id
   (`N1`, `I3`, `M12`, `T7`, `E1`, `R2`, `A1`, `B4`, `D5`, …) needs a test whose name starts with that id and an
   underscore (`N1_InternalElementRemoved_IsMinor`), and a test named like that needs the row.

**How to add a meta-model field:** compare it in `CkModelDiffService` (diff code, `ComparedProperties`, and the
emitted shape in `EmittableChanges`), add a probe for it to `PropertyProbes` in the guard test, add the classifier
rule to `CkSemVerClassifier`, and add the rule row to this page with a row test. If the field is deliberately not
part of the compatibility surface, add it to `ExcludedProperties` with the reason instead.

**Known gaps.** Open items live in one place, `KnownGaps` in the guard test, each with the story that closes it:
the rule rows T1–T7, E1–E2, R1–R2, A1–A2 (AB#6269) and B1–B4 (AB#6270). The list only shrinks — and it must be empty when F2.1 closes (AB#6273).

### Renames

A rename is not structurally detectable and appears as remove+add — which correctly requires a
major bump. The report hints at possible renames when removals and additions of the same element
kind occur in one diff.

## Migration reconciliation

If the diff requires a major bump, the command checks whether the model defines a migration with
`toVersion` equal to the declared version. If not, a warning lists the breaking changes — the
engine's no-migrations bridge allows schema-only majors, so this is not an error by default.
`--requireMigrationForMajor` escalates it to `OCTO-CK104`.

Migration reconciliation is **skipped while the declared version itself is still wrong** (verdict
`VersionTooLow` or a downgrade). In that state `OCTO-CK100`/`OCTO-CK101` already require the
developer to raise the version; reconciling migrations at the same time would name the (too-low)
*declared* version as the missing migration's `toVersion` while `OCTO-CK100` simultaneously demands a
higher minimum — a contradictory hint. Once the version is corrected, the next run reconciles
against the now-correct `toVersion` (the check self-heals). For an already-valid version the
reconciliation runs normally.

## Changelog generation

With `--changelog`, the command writes/updates a `CHANGELOG.md` next to `ckModel.yaml` from the
classified diff: one section per version with date, bump level, and every change including its
classification (`### Breaking` / `### Added` / `### Changed`). Existing sections of older
versions are never rewritten; repeated runs replace only the section of the currently declared
version (idempotent). Generation only runs after successful validation. Without the flag, the
command is fully read-only.

## Known limitations (V1)

- **Parallel branches:** two branches that change the same model and both correctly bump to the
  same version validate green against the same published baseline. Only after merge+publish of
  the first does the second one's validation trip (main build as second gate).
- **Foreign attribute defaults:** whether a *required* attribute referencing an attribute
  definition of another model carries default values cannot be inspected — such additions are
  classified Major defensively.
- **Dependency ranges of exact-pinned (v1) models are classified via their resolved versions** (range-retaining models
  are compared on their declared ranges, rows D1–D7). The compiled baseline model
  persists only the *resolved* dependency versions, not the declared ranges (and the compiled
  model schema is closed, so persisting ranges is a catalog-format evolution). A range edit that
  changes the resolved version is classified (major switch → Major, otherwise → Minor); a range
  edit that leaves the resolved version unchanged (e.g. lowering the floor, or raising the
  ceiling while no matching version is published yet) is not visible to the diff and requires no
  bump. The declared ranges themselves are still validated for satisfiability (`OCTO-CK103`).
  Persisting declared ranges in the compiled model is a follow-up alongside the catalog
  `versionInfo` metadata.
- The rule set is not configurable; catalog metadata (`versionInfo`, `IsBreaking` flags) is a
  follow-up.
