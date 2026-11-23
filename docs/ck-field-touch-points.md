---
description: Everything a new CK field has to touch across the contracts, engine, serialization and persistence.
applies_to: src/ConstructionKit.Contracts/**, src/ConstructionKit.Engine/Serialization/**, src/ConstructionKit.Engine/Resolvers/**
---

# Touch-point checklist for every new CK field

### Touch-point checklist (keep for every new CK field — contract §2.7)

**Mandatory persistence gate:** every new meta-model field must pass the generic reflection round-trip gate in
`octo-construction-kit-engine-mongodb` — `CkModelReflectionComparer` (F1.3-S3, H-M2 `c3a4911`) compares every
public property of the compiled model after import → Mongo → read-back, so a field that is not written or not read
back (the isRuntimeState class of bug) fails there. Row 10 below is not done until that gate is green.

**Mandatory classification gate:** every new meta-model field also needs a diff **and** a classifier rule.
`tests/ConstructionKit.Engine.Tests/SemVer/CkSemVerClassificationGuardTests.cs` fails, naming the item, when a public
element-DTO property is neither in `CkModelDiffService.ComparedProperties` nor in `ExcludedProperties` (with a written
reason), when a compared property has no probe in the guard's `PropertyProbes` or its probe yields no diff change,
when a change shape is missing in `CkModelDiffService.EmittableChanges`, when any shape reaches the defensive default
of `CkSemVerClassifier` ("no classification rule …"), when a `CkModelElementKind` value has no rule, and when the
rule rows of `docs/ck-semver-rules.md` (N*, I*, M*, T*, E*, R*, A*, B*, D*) and the tests named after them
(`N1_…`) drift apart. To satisfy it: diff the field, list its shape, add a probe, add the classifier rule, add the
docs row and its row test (`docs/ck-semver-rules.md`, "Classification guard"). Open items live only in the guard's
`KnownGaps` list (each with its story); it must be empty when F2.1 closes. Row 7 below is not done until
that gate is green.

| # | Touch point | `ckLanguage` | `interfaces` | `implements` | `access` | `methods` | `visibility` / `derivable` | interface completion (S5) |
| - | ----------- | ------------ | ------------ | ------------ | -------- | --------- | -------------------------- | ------------------------- |
| 1 | Source schema | meta (enum 1/2) | interface schema + elements root | `CkType` | `CkTypeAttribute` | method schema + `CkType` | type, record, enum, attribute, association-role, interface, method schemas | interface schema (`extends`, `associations`, `methods`, `deprecated`), type schema `targetCkInterfaceId` |
| 2 | Compiled schema | integer ≥ 1 | compiled root | `CkCompiledType` | shared `$ref` | `CkCompiledType` | `CkCompiledType` (rest via shared `$ref`) | via shared `$ref` |
| 3 | DTO | `CkModelPropertiesDto.CkLanguage` | `CkInterfaceDto`, `CkElementsRootDto`, `CkModelRootBase` | `CkTypeDto.Implements` | `CkTypeAttributeDto.Access` | `CkTypeDto.Methods` + `CkMethodDto` family | `CkVisibilityDto` / `CkDerivableDto` on the 7 element DTOs | `CkInterfaceDto`, `CkInterfaceAssociationDto`, `CkTypeAssociationDto.TargetCkInterfaceId` |
| 4 | Compiler hand-copies | `CompilerService` candidate, `CatalogModelResolver` | same + `interfaces/` loop | `CompilerService` type copy | by reference | `CompilerService` type copy | `CompilerService` type copy (others by reference) | by reference |
| 5 | Graph + `[JsonConstructor]` | `CkCacheRoot.Models` (set in `ElementResolver` / `AppendModel`) | `CkInterfaceGraph`, `CkModelGraph`, `CkCacheRoot`, cache getters | `CkTypeGraph` | `CkTypeAttributeGraph` (init setter) | `CkTypeGraph` + `CkMethodGraph` | settable effective properties, set by `CkModelGraph.ApplyCkV2Modifiers` | `CkInterfaceGraph` (defaulted `[JsonConstructor]` params), `CkInterfaceAssociationGraph`, `CkInterfaceMethodGraph`, `CkTypeAssociationGraph` |
| 6 | Resolvers + codes | 90/91, 126 | 92–94, 118–124, 127 | 95–99, 121, 124 | 90, 99, 105–108 | 100–104, 122, 125, 131–135, 142 | 90, 112, 113, 129 | 90, 118–124, 128 |
| 7 | SemVer diff + classifier rule + row test (gate: `CkSemVerClassificationGuardTests`, mandatory) | yes | yes | yes | yes | yes | yes (`CkModelDiffService.DiffModifiers`) | yes |
| 8 | Source generator | — | yes (ids + `IRt<Name>`) | yes (explicit impls) | — | yes (ids, parameter/result records) | `sealed` (v2; no `abstract`, E-M3) | `IRt<Name>` (extends = interface inheritance) |
| 9 | Docs generator | — | yes | yes | — | yes | yes (non-default only) | yes |
| 10 | Mongo entity + write + read-back (gate: `CkModelReflectionComparer`, mandatory) | engine-mongodb (Persistence agent) | | | | | engine-mongodb (Persistence agent) | engine-mongodb (Persistence agent) |
| 11 | GraphQL CK meta | asset-repo F1.5-S3 (CK meta introspection) | asset-repo F1.5-S2/S3 | asset-repo F1.5-S2 | asset-repo `CkTypeAttributeDtoType.access` | asset-repo F1.5-S3 | asset-repo F1.5-S3 | asset-repo F1.5-S2/S3 |
| 12 | Studio CK browser query (refinery-studio `src/app/graphQL/`) — v2 fields go into the CK v2 follow-up documents run by `CkLanguage2Service`, **never** into the v1 queries (`getCkTypeDetails` etc. must keep working against asset repos without the CK v2 meta API; octo-frontend-refinery-studio, section "CK language 2 in the CK browser") | `getCkModelsLanguage2` | `getCkInterfaces`, `getCkInterfaceDetails`, `getCkTypeLanguage2` | `getCkTypeLanguage2` | `getCkTypeAttributesAccess` | `getCkTypeLanguage2` | `getCkTypeLanguage2`, `getCkElementLanguage2` | `getCkInterfaceDetails` |

**`usedSurface` touch points**, same numbering: (2) compiled schema `dependencyRanges[].usedSurface` /
`usedSurfaceHash`; (3) `CkModelDependencyDto.UsedSurface` / `UsedSurfaceHash`; (4) computed by `CkUsedSurfaceCollector`
in `CatalogModelResolver.ApplyRangeRetentionAsync` (range retention only, so v1 output is unchanged); (7) documented
exclusion in `CkModelDiffService.ExcludedProperties` (row D7); (10) engine-mongodb `CkModelDependency.UsedSurface` /
`UsedSurfaceHash` (reflection gate via the C# kitchen sink); consumer: F2.5. Rules and limits:
`docs/ck-semver-rules.md`, "usedSurface".

**`compatibility.acknowledge` touch points**, same numbering: (1) meta schema `compatibility.acknowledge[]`
(`change` without wildcards, `reason` non-blank); (2) compiled schema, same shape; (3) `CkMetaRootDto.Compatibility`,
`CkModelCompileCandidate.Compatibility`, `CkCompiledModelRoot.Compatibility` (`CkCompatibilityDto`, `CkAcknowledgeDto`);
(4) `CompilerService` -> `CkCompatibilityValidator` -> candidate -> `CatalogModelResolver.CompileAsync` (omitted when
empty, so existing models serialize as before); (5) not part of the graph; (6) 130, and 90 in a v1 model;
(7) conscious exclusion `CkCompiledModelRoot.Compatibility` in `CkModelDiffService.ExcludedProperties` (the entries are
matched against the diff by `CkAcknowledgementResult`, they are not model structure) and `CkCompatibilityDto` /
`CkAcknowledgeDto` in the guard's `NotDiffedDtoTypes`; (8), (9) -; (10) conscious exclusion in `CkModelReflectionComparer.AllowList`
(`CkCompiledModelRoot.Compatibility`: build and publish-time metadata, read from the catalog JSON by the publish gate, not
needed in tenants); (11), (12) not needed. Rules: `docs/ck-semver-rules.md`, "Acknowledge".

**`execution.handler` (F3.1) touch points**, same numbering (the field lives in `CkMethodExecutionDto`, next to
`timeoutSeconds` / `idempotent`; values `service` (default) and `pipeline`, the string constants of `CkMethodHandlers`;
`null` = omitted = `service`, so existing models serialize byte-identically): (1) method schema
`CkMethodExecution.handler` (plain string, no `enum`: an unknown value is message 131 with the method id, not a schema
error); (2) compiled schema via the shared `$ref`; (3) `CkMethodExecutionDto.Handler` (`string?`, `OmitNull`);
(4) copied by reference with `Methods`; (5) `CkMethodGraph.Handler` (`[JsonIgnore]`, derived from the definition that
the graph already serializes); (6) message 131 (unknown value, `pipeline` without explicit `timeoutSeconds`, `pipeline`
on an interface method) and message 132 (`pipeline` with a `sensitive` parameter); (7) diff property `handler` (effective value, so adding `handler: service` is no change),
classifier row **M15** (service ↔ pipeline = Major), guard probe, `signature` summary shows `, handler pipeline` only for
the non-default; (8) — (parameter/result records and id constants are generated as before); (9) `Types.md` marks a
non-default method `(handler: pipeline)`; (10) **follow-up** (engine-mongodb `CkMethodExecution` map +
`CkModelReflectionComparer` gate — until then a tenant document drops the value on import); (11) **follow-up**
(asset-repo CK meta); (12) optional, Studio. Runtime routing of `pipeline` methods. Tests:
`CkV2MethodResolverTests` (`Code131_*`, `Code132_*`), `CkV2CompileTests.MethodHandler_*`, `CkV2GraphJsonRoundTripTests`,
`MethodRowTests.M15_*`.

**`replacesGenericCreate` (F3.7) touch points**, same numbering (the field lives on `CkMethodDto` itself, next to
`kind` / `visibility`; `bool`, omitted = `false`; a static method that is the only way to create entities of its type, the
refusal of the generic create is enforced by the asset repository): (1) method schema `CkMethod.replacesGenericCreate`
(boolean; the model-language key needs a **schema release** at schemas.meshmakers.cloud, a catalog model must not use it
before that); (2) compiled schema via the shared `$ref`; (3) `CkMethodDto.ReplacesGenericCreate` (`OmitDefaults` in YAML,
`JsonIgnore(WhenWritingDefault)` in JSON, so models that do not use the key serialize byte-identically); (4) copied by
reference with `Methods`; (5) `CkMethodGraph.ReplacesGenericCreate` (`[JsonIgnore]`, derived from the serialized definition);
(6) message 133 (`InheritanceResolver` rule M-7: static only, no interface method, no abstract type, at most one per
concrete type incl. inherited ones) and message 90 in a v1 model (`ElementResolver.CheckCkLanguage`); (7) diff property
`replacesGenericCreate`, classifier row **M16** (`false` → `true` Major, `true` → `false` Minor, a new flagged method on a type of the previous version is Major too — the diff emits the `false` → `true` change for it —, on a new type Minor),
guard probe, `signature` summary shows `, replaces generic create` only when set; (8) — (no generated code depends on it);
(9) `Types.md` marks the method `(replaces generic create)`; (10) engine-mongodb `CkMethod.ReplacesGenericCreate`
(reflection gate); (11) asset-repo CK meta + refusal; (12) optional, Studio. **minEngineVersion:**
`CkEngineVersion.GetRequiredMinEngineVersion` raises the value to `ReplacesGenericCreateMinEngineVersion` for a model with
a flagged method (models without it keep `CkV2MinEngineVersion`; dependents inherit it through their dependencies), so an
engine that predates the field refuses the model with message 126 instead of silently dropping the flag and serving the
generic create. The constant must equal the number of the release that first ships the field. Tests:
`CkV2MethodResolverTests` (`Code133_*`, `Code90_*`), `CkV2CompileTests.ReplacesGenericCreate_*`,
`CkV2GraphJsonRoundTripTests.ReplacesGenericCreate_*`, `CkEngineVersionTests`, `MethodRowTests.M16_*`.

**Method result `sensitive` (F3.4) touch points**, same numbering (the field lives on `CkMethodResultDto`, next to
`valueType`; `bool`, omitted = `false`; it marks the **whole** result value — scalar, enum or record — as never to be logged,
audited or traced; per-attribute marking inside a record result is out of scope; a `Secret` value type is **not** sensitive
implicitly, the marker is explicit; the redaction itself is enforced at runtime, the GraphQL exposure is tracked separately):
(1) method schema `CkMethodResult.sensitive` (boolean; the model-language key needs a **schema release** at
schemas.meshmakers.cloud, a catalog model must not use it before that; an older compiler refuses the key loudly because the
definition has `additionalProperties: false`); (2) compiled schema via the shared `$ref`; (3) `CkMethodResultDto.Sensitive`
(`OmitDefaults` in YAML, `JsonIgnore(WhenWritingDefault)` in JSON, so models that do not use the key serialize
byte-identically); (4) copied by reference with `Methods`; (5) `CkMethodGraph.HasSensitiveResult` (`[JsonIgnore]`, derived from
the serialized definition; false for a method without a result); (6) message 134 (`InheritanceResolver.ValidateMethodHandler`,
see the table) and rule 122: `CkMethodContract` compares the marker, a type that redeclares an interface method must keep it
(reason `result sensitive`); (7) diff property `resultSensitive` (emitted only when both versions have a result; a result added
or removed is one `result` change, row M6), classifier row **M17** (`false` → `true` Minor; `true` → `false` Minor +
`requiresAcknowledge` + behavioural "security: result redaction removed", change key
`TypeMethod:<Type>/<Method>#Modified:resultSensitive`; interface method either way Major via I12, because `resultSensitive` is
a contract property), guard probe `CkMethodResultDto.Sensitive`, the method `signature` summary appends ` sensitive` to the
result; (8) source generator: the generated `{Type}{Method}Result` gets a `ToString()` override printing `Value = ***` for a
sensitive result (unchanged record rendering otherwise); (9) `Types.md` marks the result column `(sensitive)`; (10) engine-mongodb
`CkMethodResult.Sensitive` (reflection gate, legacy document); (11) asset-repo CK meta; (12) optional, Studio.
**minEngineVersion:** `CkEngineVersion.GetRequiredMinEngineVersion` raises the value to `SensitiveResultMinEngineVersion` for a
model in which any type **or interface** method has a sensitive result (the maximum with the other markers; models without it
keep their minimum), so an engine that predates the field refuses the model with message 126 instead of silently dropping the
marker (fail-open: nothing would be redacted). The constant must equal the number of the release that first ships the field
(the next patch after the last `r3.5.x` tag at merge time). Tests: `CkV2MethodResolverTests` (`Code134_*`, `Code122_*`),
`CkV2CompileTests.SensitiveResult_*`, `CkV2GraphJsonRoundTripTests.SensitiveResult_*`, `CkV2GeneratedContractsTests`,
`CkEngineVersionTests`, `MethodRowTests.M17_*`, `CkV2SchemaTests`.

**`execution.mode` (F3.6) touch points**, same numbering (the field lives in `CkMethodExecutionDto`, next to
`timeoutSeconds` / `idempotent` / `handler`; values `sync` (default) and `async`, the string constants of `CkMethodModes`;
`null` = omitted = `sync`, so existing models serialize byte-identically; `async` means a call returns an operation handle once
the handler has accepted the work, and `timeoutSeconds` bounds that accept step, not the operation — decision OD-5,
`methodConsistency.md`): (1) method schema `CkMethodExecution.mode` (plain string, no `enum`: an unknown value is message 142
with the method id, not a schema error); (2) compiled schema via the shared `$ref`; (3) `CkMethodExecutionDto.Mode` (`string?`,
`OmitNull`); (4) copied by reference with `Methods`; (5) `CkMethodGraph.Mode` / `IsAsync` (`[JsonIgnore]`, derived from the
serialized definition); (6) message 142 (unknown value, `async` + `handler: pipeline`, the key in a `ckLanguage` 1 model) and rule
122: the mode is part of the **invocation contract** (`CkMethodContract`, effective value, reason `mode 'x' vs 'y'`), so a type
that redeclares an interface method keeps its mode; (7) diff property `mode` (effective value, so adding `mode: sync` is no
change), classifier row **M19** (sync ↔ async = Major; on an interface method also I12, `mode` is in
`CkMethodContract.MethodProperties`), guard probe `CkMethodExecutionDto.Mode`, the method `signature` summary shows `, mode async`
only for the non-default; (8) — (no generated code depends on it); (9) `Types.md` and `Interfaces.md` mark a non-default method
`(mode: async)`; (10) engine-mongodb: the value is persisted inside the embedded
`CkMethodDto.Execution` (class map `CkMethodExecutionDto`, `SetIgnoreIfNull`, covered by the reflection gate); an undeclared
mode is left out of the document and a document written before the field reads back as `null`, i.e. the effective mode `sync`
(`CkMethodModes.Effective`); (11) asset-repo CK meta `CkMethodExecution.mode: String!` (effective value); (12) optional,
Studio. **Schema:** the method schema reaches model authors only through the schema publish path (see "JSON schema publish
path" in `docs/ck-schema-and-json-serialization.md`).
**minEngineVersion:** `CkEngineVersion.GetRequiredMinEngineVersion` raises the value to `ExecutionModeMinEngineVersion` for a
model in which any type **or interface** method sets `execution.mode` (also `sync`; the maximum with the other markers; models
without the key keep their minimum and compile byte-identically), so an older CK v2 engine refuses the model with message 126
instead of dropping the key and running an `async` method as sync (decision Odin). The constant must equal the number of
the release that first ships the field (the next patch after the last `r3.5.x` tag at merge time; `3.5.67` when `r3.5.66` was the
last tag). The runtime that produces operation handles is F3.6. Tests: `CkV2MethodResolverTests` (`Code142_*`,
`Code122_*Mode*`), `CkV2CompileTests.MethodMode_*`, `CkV2GraphJsonRoundTripTests.AsyncMode_*`, `CkV2SchemaTests`,
`MethodRowTests.M19_*`, `InterfaceRowTests.I12_*` (case `mode changed`), `CkEngineVersionTests` (`*ExecutionMode*`).

**Method `authorization.dataAccess` (F3.4b) touch points**, same numbering (the field lives on `CkMethodAuthorizationDto`,
next to `roles` / `allowSelf` / `scopes`; enum `CkMethodDataAccessDto` `Read` | `Write`, PascalCase like `visibility`; nullable, omitted =
`Write`; it declares the data permission a caller needs on the target record of the method call — `Read` = the method only reads its
target, a read grant suffices; the gateway check that consumes it is tracked separately, nothing in the engine enforces it): (1) method schema
`CkMethodAuthorization.dataAccess` (string enum `Read`/`Write`; the model-language key needs a **schema release** at
schemas.meshmakers.cloud, a catalog model must not use it before that; an older compiler refuses the key loudly because the definition has
`additionalProperties: false`); (2) compiled schema via the shared `$ref`; (3) `CkMethodAuthorizationDto.DataAccess`
(`CkMethodDataAccessDto?`, `OmitNull` in YAML, `JsonIgnore(WhenWritingNull)` + `JsonStringEnumConverter` in JSON, so models that do not use the key
serialize byte-identically); effective value `CkMethodAuthorization.Resolve(authorization)` (omitted or no block = `Write`); (4) copied by
reference with `Methods`; (5) `CkMethodGraph.DataAccess` (`[JsonIgnore]`, effective value, derived from the serialized definition; the
cache JSON carries the key only where it is declared); (6) message 135 (`InheritanceResolver.ValidateMethodDataAccess`) and message 90 in a
v1 model (`ElementResolver.CheckCkLanguage`, feature `authorization.dataAccess`); (7) diff property `dataAccess` (effective values, emitted
independently of the authorization block), classifier row **M18** (`Write` → `Read` Minor + `requiresAcknowledge` + behavioural "security:
method access widened", change key `TypeMethod:<Type>/<Method>#Modified:dataAccess`; `Read` → `Write` Major), guard probe
`CkMethodAuthorizationDto.DataAccess`, the method `signature` summary appends `, dataAccess Read` only for `Read`; (8) source generator: the
generated `{Type}{Method}Parameters` record gets `public const CkMethodDataAccessDto CkDataAccess` (effective value); (9) `Types.md` marks a
`Read` method `(data access: Read)`, nothing for the default; (10) engine-mongodb `CkMethodAuthorization.DataAccess` (reflection gate);
(11) asset-repo CK meta and (12) Studio: not part, cut when Studio needs to show the field. **minEngineVersion:**
`CkEngineVersion.GetRequiredMinEngineVersion` raises the value to `DataAccessMinEngineVersion` for a model in which any type **or interface**
method declares `dataAccess` (also `Write`; the maximum with the other markers; models without it keep their minimum), so an engine that
predates the field refuses the model with message 126 instead of dropping the key and treating a `Read` method as `Write`. The constant must
equal the number of the release that first ships the field (the next patch after the last `r3.5.x` tag at merge time; `3.5.40` when `r3.5.39` was
the last tag). Tests: `CkV2MethodResolverTests` (`Code135_*`), `CkV2CompileTests.DataAccess_*`, `CkV2GraphJsonRoundTripTests.DataAccess_*`,
`CkV2GeneratedContractsTests`, `CkEngineVersionTests`, `MethodRowTests.M18_*`, `CkV2SchemaTests`.

**Type `writeAccess` touch points**, same numbering (the block lives on `CkTypeDto.WriteAccess`,
`CkTypeWriteAccessDto { Principals, Channels }`, constants `CkWritePrincipals`; `null` = omitted = unrestricted, so
existing models serialize byte-identically; full description `docs/data-permissions.md`): (1) type schema
`CkType.writeAccess` / `CkCompiledType.writeAccess` -> shared definition `CkTypeWriteAccess` (`additionalProperties:
false`, values as plain strings: an unknown value is message 143, an unknown key message 27; needs a **schema release**);
(2) compiled schema, same definition; (3) `CkTypeDto.WriteAccess` (`OmitNull`, `JsonIgnore(WhenWritingNull)`);
(4) `CompilerService` type copy; (5) `CkTypeGraph.WriteAccess` (declared block only, trailing `[JsonConstructor]` parameter,
omitted from the cache JSON when null; the runtime walks `BaseTypes` and requires every block of the chain to admit);
(6) message 143 (`ElementResolver.CheckWriteAccess`) and 90 in a v1 model; runtime message 144; (7) diff property
`writeAccess` (canonical sorted set, `CkModelDiffService.FormatWriteAccess`), classifier row **T8** (Minor +
`requiresAcknowledge`, behavioural, reason restricted / widened / changed), guard probes `CkTypeDto.WriteAccess`,
`CkTypeWriteAccessDto.Principals` / `.Channels`; (8) —; (9) `Types.md` line "Write access: …" on the declaring type;
(10) engine-mongodb `CkType.WriteAccess` (embedded DTO, reflection gate) — **required**: without it a tenant drops the
block and does not enforce it; (11) asset-repo CK meta and (12) Studio: follow-up, not needed for enforcement.
**minEngineVersion:** `CkEngineVersion.WriteAccessMinEngineVersion` (`3.5.71`) for a model in which any type declares the
block; must equal the release that first ships the field. **Enforcement:** `TypeWriteAccessGuard` in
`RuntimeRepositoryBase.ApplyChangesGuardedAsync` (every non-system session, every tenant, before the data-permission guard),
the bulk import paths for non-system sessions, and the caller path of `ImportRtModelCommand`. `RtSecurityContext.PrincipalKind`
(default `Unknown`, refused by restricted types) and `RtSecurityContext.WriteChannel` (set only by service code,
`WithWriteChannel`) carry the caller; `RtImportCallerContext` has the same two properties. Tests: see the doc.

**`securitySensitive` touch points**, same numbering: (1) attribute schema `CkAttribute.securitySensitive`;
(2) compiled schema via the shared `$ref`; (3) `CkAttributeDto.SecuritySensitive` (`bool?`, omitted when null); (4) copied
by reference; (5) `CkAttributeGraph.SecuritySensitive`, omitted from the cache JSON while false (`OmitCkV2Defaults`);
(6) message 90 in a v1 model; (7) diff property `securitySensitive`, rows A3 and T7, guard probe; (8) —;
(9) "Security-sensitive: `true`" line in `Attributes.md`; (10) engine-mongodb `CkAttribute.SecuritySensitive`
(reflection gate); (11) asset-repo CK meta API and (12) Studio: follow-up (not needed for classification).

Tests: `CkV2SchemaTests`, `CkV2ContractTests`, `CkV2SemVerTests` (`tests/ConstructionKit.Engine.Tests/CkV2`),
`CkV2InterfaceResolverTests` / `CkV2MethodResolverTests` / `CkV2ModifierResolverTests` /
`CkV2InterfaceCompletionResolverTests` (one failing and one passing case per code, on the C#
kitchen sink `sampleData/ckv2KitchenSink/Builder.cs`), `CkV2GraphJsonRoundTripTests` (graph → cache JSON → graph),
`CkIdsCodeGeneratorCkV2Tests`, and in `ConstructionKit.Compiler.Tests` `CkV2CompileTests` (YAML kitchen sink end to
end incl. `interfaces/` folder, gate, docs and range retention of `implements`) and `CkV1CompileOutputUnchangedTests`.
