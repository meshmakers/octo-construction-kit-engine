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

## Runtime semantics (later phases, for context)

- **Phase 2 (AB#4810):** `RtDisplayName` / `RtDisplayDescription` on `RtEntity` + DTO
  set; a `DisplayNameModifier` (`IPreDocumentModification<RtEntity>`, modeled on
  `AutoIncrementModifier`) evaluates the effective rule on Insert/Replace; GraphQL
  exposes the fields read-only. Empty evaluation stores `null`; the read layer always
  synthesizes `<ckTypeId>@<rtId>` so the API never returns an empty display name
  (filter/sort operate on the stored value).
- **Phase 3 (AB#4811):** recompute on partial updates when a referenced path (prefix
  match, so record sub-updates count) is part of the update.
- **Phase 4 (AB#4812):** backfill sweep over existing entities when a model import
  changes a rule.
- **Phase 5 (AB#4813):** consumers switch from `rtWellKnownName` to `rtDisplayName`.
