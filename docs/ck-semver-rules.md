# Construction Kit SemVer Rules and Version Validation

The version of a Construction Kit model lives in the `modelId` suffix of `ckModel.yaml`
(e.g. `modelId: Basic-2.0.2`) and is maintained manually by the developer. The
`ckc ValidateVersion` command enforces that this version is *honest*: it diffs the current model
against the last published version, classifies every change according to the fixed rule set below,
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
| `-p, --path` | Root path(s) of Construction Kit model directories. Multiple paths are validated in the given order — pass them in dependency order so cascade violations read comprehensibly. |
| `-cn, --catalogName` | Restricts baseline retrieval (and the dependency check) to one catalog. Without it, all readable catalogs are queried and the highest published version wins (catalog order: Embedded → LocalFileSystem → PrivateGitHub → PublicGitHub). |
| `-o, --output` | Additionally writes the report as Markdown (e.g. for PR comments). |
| `-rf, --refresh` | Forces a catalog cache refresh before the baseline is determined. Always use this in CI. |
| `-cl, --changelog` | Writes/updates the `CHANGELOG.md` section of the declared version next to `ckModel.yaml`. Only runs after successful validation; older sections are never rewritten. |
| `-rmm, --requireMigrationForMajor` | Escalates a missing migration for a required major bump from a warning to an error. |
| `-lce, -lcr` | Enable/point the local file system catalog for this invocation (same semantics as `Compile`). |

### Validation rule

With *published* = last published version and *minimum* = published + exactly one bump of the
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
| `OCTO-CK103` | A dependency range is satisfied by no published version | Publish the dependency first or correct the range |
| `OCTO-CK104` | Major bump without a matching migration (only with `--requireMigrationForMajor`) | Add a migration with `toVersion` == declared version |

Exit codes: `0` = valid, non-zero = violation or error (CI-friendly).

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
| `valueType` of an attribute changed | Data format breaks |
| `valueCkEnumId` / `valueCkRecordId` changed | Reference target breaks |
| Attribute assignment references a different attribute definition (`id` changed) | Value semantics may break (defensive) |
| Type attribute changed from optional to **required** (`isOptional: true → false`) | Existing instances may become invalid |
| New **required** attribute **without** `defaultValues` (or referencing an attribute of another model) | Existing instances become invalid (defensive when not inspectable) |
| `derivedFromCkTypeId` / `derivedFromCkRecordId` changed | Inheritance hierarchy breaks (GraphQL schema, queries) |
| Multiplicity **tightened** (permissiveness One < ZeroOrOne < N decreases) | Existing associations may become invalid |
| `inboundName` / `outboundName` of an association role changed | Navigation/GraphQL breaks |
| Type association removed | Consumers use the navigation |
| `targetCkAttributeIds` of a type association changed | Referential integrity changes (defensive) |
| Enum value **removed** or `key` of an existing `name` changed | Stored values become unreadable |
| `useFlags` changed | Value semantics break |
| `isExtensible: true → false` | Runtime extensions are no longer allowed |
| `isAbstract: false → true` or `isFinal: false → true` | Instantiation/derivation breaks |
| Unique index added (`Unique`, `UniqueNotDeleted`) | Existing data may be invalid |
| Type is no longer a collection root (`isCollectionRoot: true → false`) | Collection semantics break (defensive) |
| Dependency removed | Consumers may rely on the transitively provided model (defensive) |
| Dependency switched to a new **major** version | Transitively breaking |

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
| `isRuntimeState` changed | Blueprint re-apply behavior changes |
| Attribute `metaData` changed | Metadata only, no data break |
| `enableChangeStreamPreAndPostImages` changed | Change stream behavior, no data break |
| Type becomes a collection root (`isCollectionRoot: false → true`) | Additive |
| New type association | Additive |
| New dependency | Additive |
| Dependency version changed without a major switch | Compatible |

### Patch

| Change | Reasoning |
| ------ | --------- |
| `description` (all element kinds, model meta) | Purely documentational |
| Pure formatting/comment changes in the source YAMLs | Compiled model identical → empty diff → no bump required |

### Defensive default

A change without an explicit rule is classified as **Major**. Since only a minimum level is
enforced, an overly strict classification is annoying for the developer but never wrong — an
overly lax one, however, is dangerous.

**Convention for new schema fields:** every new public property on the element DTOs
(`CkTypeDto`, `CkAttributeDto`, `CkEnumDto`, `CkRecordDto`, `CkAssociationRoleDto` and their
nested DTOs) must be registered in `CkModelDiffService.AccountedProperties` together with a diff
implementation and a classification rule (or a documented, conscious exclusion). A guard test in
`ConstructionKit.Engine.Tests` fails when a DTO property is not accounted for, and this page must
be extended with the new rule.

### Renames

A rename is not structurally detectable and appears as remove+add — which correctly requires a
major bump. The report hints at possible renames when removals and additions of the same element
kind occur in one diff.

## Migration reconciliation

If the diff requires a major bump, the command checks whether the model defines a migration with
`toVersion` equal to the declared version. If not, a warning lists the breaking changes — the
engine's no-migrations bridge allows schema-only majors, so this is not an error by default.
`--requireMigrationForMajor` escalates it to `OCTO-CK104`.

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
- The rule set is not configurable; catalog metadata (`versionInfo`, `IsBreaking` flags) is a
  follow-up.
