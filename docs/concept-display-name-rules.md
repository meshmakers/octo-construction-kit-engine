# Display Name Rules (rtDisplayName / rtDisplayDescription)

Epic AB#4808. CK types can declare how a runtime entity's display name and display
description are computed from its attribute values. On save, the engine evaluates the
rule and stores the result in the read-only system fields `rtDisplayName` /
`rtDisplayDescription` (fields land in Phase 2, AB#4810). This gives all consumers
(entity pickers, lists, CLI/MCP) a human-readable name without overloading
`rtWellKnownName`, which is an identifier (blueprint/migration targeting) and null for
almost all domain entities.

## Model declaration (Phase 1, AB#4809 — this repo)

Two optional type-level properties in the CK elements YAML:

```yaml
types:
- typeId: Space
  derivedFromCkTypeId: ${Basic}/NamedEntity
  displayNameRule: "${roomNumber} - ${name ?? globalId}"
  displayDescriptionRule: "${description ?? spaceType}"
```

### Rule dialect

`${attributePath}` interpolation — the established OctoMesh dialect shared with the
blueprint variable interpolator, the CK compiler `VariableResolver` and the mesh-adapter
`PlaceholderReplaceNode` — extended with a `??` coalesce operator *inside* a placeholder:
the first path in the chain that yields a non-empty value wins.

- Paths address **own attributes** of the entity, including record paths
  (`${thermalRequirements.spaceTemperature}`, arbitrarily nested).
- **No association traversal** — that would create recompute cascades when a referenced
  entity changes.
- A rule must contain at least one placeholder; a literal-only rule is a compile error.
- Parser: `ConstructionKit.Contracts/DisplayRules/DisplayRuleParser.cs` (shared with the
  runtime save-path evaluation in Phase 2). Parse errors are collected, not thrown.

### Inheritance

Rules are inherited along the `derivedFromCkTypeId` chain; a derived type may override
with its own rule. Resolution: nearest non-empty rule wins, resolved per rule
independently (a type can override `displayNameRule` while inheriting
`displayDescriptionRule`). Implemented as a base-chain walk in
`InheritanceResolver.ResolveDisplayRules` writing the *effective* rule onto
`CkTypeGraph.DisplayNameRule` / `.DisplayDescriptionRule`.

### Compile-time validation

After attribute flattening, `InheritanceResolver.ValidateDisplayRules` validates each
**declared** rule (errors are reported once, at the declaring type — attribute merge is
additive, so a rule valid at the declaring type is valid for all inheritors):

- Message 67 `DisplayRuleSyntaxInvalid` — parse errors (unterminated placeholder, empty
  or invalid path, literal-only rule).
- Message 68 `DisplayRuleAttributePathUnknown` — a referenced path does not resolve
  against the type's flattened attributes / record structure.

### SemVer

Changing a display rule is a **patch**-level model change (`ck-semver-rules.md`): only
computed display values change, no data or schema break.

### Persistence

The declared rules round-trip through the MongoDB model cache
(`octo-construction-kit-engine-mongodb`: `CkType` entity,
`DatabaseCkModelRepository` import + read-back) — the standard three-point round-trip
every new `CkTypeDto` property needs (see the `isRuntimeState` precedent, AB#4589).

## Runtime semantics

- **Phase 2 (AB#4810, implemented):** `RtDisplayName` / `RtDisplayDescription` on
  `RtEntity` (+ TcDto/converter, SDK DTOs, serializer/mapper, query columns
  `rtDisplayName`/`rtDisplayDescription`, readable via `RtPathEvaluator` but deliberately
  not settable). The `DisplayNameModifier` (`IPreDocumentModification<RtEntity>` in
  octo-construction-kit-engine-mongodb, modeled on `AutoIncrementModifier`) evaluates the
  effective rule on Insert/Replace; parse results are memoized by rule text. Empty
  evaluation stores `null`. GraphQL (octo-asset-repo-services) exposes `rtDisplayName`
  as non-null with the `<ckTypeId>@<rtId>` fallback synthesized at read time and
  `rtDisplayDescription` nullable — on the generic entity type, the per-CK-type object +
  interface types and simple query rows; not present on any mutation input type.
  Filter/sort operate on the stored value, not the synthesized fallback.
- **Phase 3 (AB#4811, implemented):** smart recompute on partial updates. When an update
  carries a root attribute referenced by the type's display rules (records are updated by
  their root attribute, so record sub-updates count), `BulkRtMutation` re-reads the stored
  entities once per batch and re-evaluates the rules against stored + updated attributes
  (pure logic in `DisplayFieldUpdateRecompute`, memoized parses via
  `DisplayRuleParser.ParseCached`). Updates not touching referenced attributes cause no
  extra read. Clear semantics on partial update documents: `null` = "not recomputed, leave
  unchanged", empty string = explicit clear sentinel — the Mongo update mapper translates
  it to `$unset`, the local mapper to `null`. Guarded (optimistic-concurrency) updates get
  the same treatment.
- **Phase 4 (AB#4812, implemented — octo-construction-kit-engine-mongodb):** backfill
  sweep over existing entities when a model import changes a rule.
  - *Detection:* `TenantContext.ImportCkModelAsync` captures the declared rules of all
    available CK types directly from Mongo before the import (mirroring
    `GetSchemaVersionsDirectAsync` — the import hard-deletes the previous version's
    CkType documents, so there is no old snapshot afterwards) and diffs them after the
    import (`DisplayRuleChangeDetector`). Declared-rule diff is sufficient: a change is
    swept polymorphically from the declaring type, which covers all inheritors.
  - *Durable tasks:* one record per (tenant, type) in the non-CK system collection
    `display_rule_sweep` (`IDisplayRuleSweepStore`, modeled on `TenantSetupRetryStore`):
    lease-protected claim, bounded retries, re-enqueue resets the budget. Enqueue
    failures never fail the import (logged as error).
  - *Sweep:* `DisplayRuleSweepHostedService` (opt-in via
    `AddDisplayRuleSweepBackgroundService`, registered in the asset repository host;
    options `DisplayRules:Sweep`) drains due tasks. `DisplayRuleSweeper` pages the
    polymorphic type query sorted by `_id`, evaluates each entity with its own type's
    effective rules and writes only changed fields as partial updates (empty string =
    clear sentinel; `RtChangedDateTime` untouched). Idempotent — a retry only redoes the
    remainder. Types above the collection roots fan out to the collection roots of their
    subtree.
  - Evaluation semantics are shared across save/update/sweep via
    `Runtime.Contracts/DisplayRules/RtDisplayRuleEvaluator`.
- **Phase 5 (AB#4813, implemented — octo-frontend-libraries):** the entity pickers
  (`RuntimeEntitySelectDataSource` / `RuntimeEntityDialogDataSource` in
  `@meshmakers/octo-services`, used by the MeshBoard entity selectors and widget config
  dialogs) display `rtDisplayName` (Name column + select input, plus a Description
  column) and the text search filters on `rtDisplayName` instead of `rtId`.
  `rtWellKnownName` stays an identifier everywhere (octo-cli archive/auto-increment
  usages deliberately unchanged); octo-mcp-service and Refinery Studio pick the fields up
  via the SDK DTOs / released frontend libraries.
