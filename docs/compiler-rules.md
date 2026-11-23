---
description: CK v2 interfaces, attribute access, method definitions, visibility and derivable, and the compiler rules and message codes.
applies_to: src/ConstructionKit.Compiler/**, src/ConstructionKit.Engine/Messages/**, src/ConstructionKit.Engine/Text.resx
---

# CK v2 language elements and compiler rules

## CK v2: interfaces, attribute access, method definitions, visibility/derivable

Gated by **`ckLanguage: 2`** in `ckModel.yaml` (`CkModelPropertiesDto.CkLanguage`, `null` = 1). A model
without the key compiles byte-identical to the engine before CK v2 with range retention off (classic exact pins,
`CkCompilerOptions.RangeRetention = false`, the configuration of the golden comparison; the default output with range
retention is described in `docs/ck-versioning.md`) — compiled YAML **and** CK cache JSON
(`CkV1CompileOutputUnchangedTests` against the frozen golden in
`tests/ConstructionKit.Compiler.Tests/sampleData/v1Golden`, produced by octo-ckc built from `main` — 6189ef1d,
re-verified byte-identical against `origin/main` 01fb187 on 2026-10-08; the comparison covers the compiled YAML, the
cache JSON and the catalog JSON of a model without and one with a dependency).

**Methods: the engine owns the definitions, the runtime lives elsewhere.** The engine ships the method meta-model
(schema, DTOs, graphs, cache, compiler rules, `CkMethodIds`, generated method-id constants and parameter/result records,
`execution.handler` and `execution.mode` with their diff rules). It has no invocation, dispatch or handler runtime of its
own. The invocation runtime is shipped (CK v2 Phase 3) in other repositories: the handler SDK, dispatcher, invocation
pipeline (audit, authorization, idempotency keys), bus and in-process transports and asynchronous operations (operation
record, sweep) in **octo-common-services** (`src/Methods`, `octo-common-services/docs/method-handler-sdk.md`); typed GraphQL method mutations,
the gateway gate and `runtime.operations` in **octo-asset-repo-services** (`octo-asset-repo-services/docs/method-gateway.md`,
"Asynchronous methods"); pipeline handlers in **octo-communication-controller-services** (`src/CommunicationControllerServices/Methods`, runs the deployed pipeline on a mesh adapter). Async is limited to
service handlers (message 142).

| Key | Where | Contracts |
| --- | ----- | --------- |
| `minEngineVersion` (F1.1-S6) | compiled model only (`major.minor.patch`) | `CkCompiledModelRoot.MinEngineVersion`, `CkEngineVersion` (`CkV2MinEngineVersion`, `GetRequiredMinEngineVersion`, `IsSatisfiedBy`, `CheckModel`), `CkCatalogLayout` |
| `ckLanguage` | `ckModel.yaml` (enum 1/2), compiled model (integer ≥ 1, so a higher version reaches message 91 instead of a schema error) | `CkModelPropertiesDto.CkLanguage`, `EffectiveCkLanguage`, `MaxSupportedCkLanguage` |
| `interfaces` | new folder `interfaces/*.yaml` (`CompilerStatics.InterfacesFolder`), schema `construction-kit-elements-interface.schema.json` | `CkInterfaceId` (+ STJ/YAML/Newtonsoft converters, `CkIdInterfaceIdConverter`, `RtCkIdInterfaceIdConverter`), `CkInterfaceDto`, `CkInterfaceAttributeDto`, `CkModelRootBase.Interfaces`, `CkElementsRootDto.Interfaces`, `CkInterfaceGraph`, `ICkModelGraph.Interfaces` / `InterfacesByRtCk` / `GetOrCreateInterface`, `CkCacheRoot.Interfaces`, `ICkCacheService.GetRtCkInterface` / `GetRtCkInterfaces` |
| `implements` | `CkType` / `CkCompiledType` | `CkTypeDto.Implements` (`List<CkId<CkInterfaceId>>`, JSON via `CkIdInterfaceIdListConverter`), `CkTypeGraph.DeclaredImplements` / `AllImplementedInterfaces` |
| `access` | `CkTypeAttribute` (types, records, association roles) | `CkAttributeAccessDto` (`ReadWrite`/`ReadOnly`/`MethodOnly`/`Hidden`), `AttributeAccess` predicates, `CkTypeAttributeDto.Access` (nullable), `CkTypeAttributeGraph.Access` (effective, init setter like `Ownership`) |
| `visibility` | `CkType`/`CkCompiledType`, `CkRecord`, `CkEnum`, `CkAttribute`, `CkAssociationRole`, `CkInterface`, `CkMethod` (enum `Public`/`Internal`) | `CkVisibilityDto`, nullable `Visibility` on `CkTypeDto`, `CkRecordDto`, `CkEnumDto`, `CkAttributeDto`, `CkAssociationRoleDto`, `CkInterfaceDto`, `CkMethodDto`; effective (non-null) `Visibility` on `CkTypeGraph`, `CkRecordGraph`, `CkEnumGraph`, `CkAttributeGraph`, `CkAssociationRoleGraph`, `CkInterfaceGraph`, computed on `CkMethodGraph`; `CkModifiers.ResolveVisibility` (omitted = `Public`) |
| `derivable` | `CkType`/`CkCompiledType`, `CkRecord` (enum `Model`/`Any`) | `CkDerivableDto`, nullable `Derivable` on `CkTypeDto` / `CkRecordDto`, effective `Derivable` on `CkTypeGraph` / `CkRecordGraph`; `CkModifiers.ResolveDerivable` — **omitted = `Any` in a v1 model, `Model` in a `ckLanguage: 2` model**, resolved per declaring model by `CkModelGraph.ApplyCkV2Modifiers` (called by `AppendModel` and `ElementResolver.Resolve`) |
| interface `extends` / `associations` / `methods` / `deprecated` (F1.1-S5) | `CkInterface` (`attributes` no longer required; an interface may consist of `extends` only). Association member `CkInterfaceAssociation` = `{ id, targetCkTypeId \| targetCkInterfaceId, multiplicity?, isOptional }`; methods use the type method schema | `CkInterfaceDto.Extends` / `Associations` / `Methods` / `Deprecated` (nullable, `deprecated` omitted = false), `CkInterfaceAssociationDto`; `CkInterfaceGraph.DeclaredExtends` / `AllExtendedInterfaces` (transitive) / `AllAttributes` (own ∪ extended — implementations are checked against it) / `DefinedAssociations` / `AllAssociations` (`CkInterfaceAssociationGraph` with the declaring interface) / `DefinedMethods` / `AllMethods` (`CkInterfaceMethodGraph`) / `Deprecated` |
| type association `targetCkInterfaceId` (F1.1-S5) | `CkType`/`CkCompiledType` `associations[]` | `CkTypeAssociationDto.TargetCkInterfaceId`, `CkTypeAssociationGraph.TargetCkInterfaceId` (settable, null omitted). **Deviation from the contract table:** `targetCkTypeId` stays required — the interface **narrows** the target (target derives from `targetCkTypeId` and implements the interface; `${System}/Entity` = any implementor). Keeps the non-null `TargetCkTypeId` every runtime/GraphQL consumer relies on |
| `methods` | `CkType` / `CkCompiledType`, schema `construction-kit-elements-method.schema.json` | `CkMethodDto` family (`CkMethodKindDto`, `CkMethodParameterDto`, `CkMethodResultDto`, `CkMethodErrorDto`, `CkMethodAuthorizationDto`, `CkMethodExecutionDto`), `CkTypeDto.Methods`, `CkTypeGraph.DefinedMethods` / `AllMethods` (key = method id), `CkMethodGraph` (`QualifiedMethodId`, `TimeoutSeconds`), `CkMethodIds.Qualify` / `TryParse` (`System.Identity/User.ChangePassword-1`; `TryParse` never throws — an invalid element id wrapped in `TargetInvocationException` by `Activator` returns false, review L1) |

Notes:
- Interface ids always carry their version on the wire (`Named-1`): the YAML converter writes `FullName`,
  unlike type ids, because the version is the contract version and the schema requires it.
- F1.2-S3 visibility/derivable enforcement runs in `ReferenceResolver`, which compile **and** every import
  (`CatalogModelResolver` / `RepositoryModelResolver.HardResolveAsync`) execute, so a forged or old-compiler model
  is refused on import (pinned by `CkV2VisibilityCompileTests.ForgedBase_IsRefusedWhenTheDependentIsResolved`).
  The importing model is refused. **Known limitation (review G3 D-M1):** the re-validation of already installed
  dependents (`RepositoryModelResolver.SoftResolveAsync`) only collects inheritance failures, so a dependent of a
  base that became `internal` / `derivable: Model` stays `Available` until it is itself re-imported.
- F1.2-S2 Hidden parity (review N5/N6/M12): index and derived-rule paths are resolved **case-insensitively**
  (`InheritanceResolver.WalkAttributePath`; `passwordHash` reaches `PasswordHash`, as in Mongo). **Hidden on archived
  types (M12)** cannot be a compile rule: archives are runtime entities (`System.StreamData` `Archive` with
  `TargetCkTypeId` + column paths). The rule — **no archive column may reach a Hidden attribute** — is implemented by
  `ArchiveHiddenColumnGuard` (Runtime.Engine): a path reaches Hidden when a segment is Hidden **or when it ends at a
  record / record array whose record, transitively, contains a Hidden sub-attribute** (a whole-record column stores
  it; review G3 E-M2). Case-insensitive, `[*]` ignored, computed columns skipped; for a rollup the aggregation source paths are checked. `ArchiveLifecycleService`
  applies it on activation, retry and re-provisioning (`HiddenAttributeInArchiveException`), and
  `RevalidateAccessAsync(archiveRtId)` sets an **active** archive to `Failed` (stops ingesting) when a model change
  made one of its columns reach Hidden; it records the reason and the columns, the history stays readable without
  them and `RedactHiddenColumnsAsync` resumes the archive (`docs/archive-hidden-columns.md`). **Wiring owed by the consumers:** the mongodb `TenantContext` passes the CK
  cache, calls `RevalidateAccessAsync` for the tenant's archives after a CK model import, and the ingest/column
  builder (`ArchivePathTypeResolver.BuildRecordObject`) skips Hidden sub-attributes like Secret ones; the stream-data
  query paths (asset-repo) refuse columns for which `ArchiveHiddenColumnGuard.FindHiddenAttribute` returns a value.
  It needs the optional `ICkCacheService` constructor argument — the mongodb
  `TenantContext` must pass it; without it the check is skipped. **Models imported before F1.2-S2** that violate
  106–109 now fail on repository load (cache rebuild) — only dev tenants that imported pre-release CK v2 models can have such models.
- F1.1-S5 resolution: `InheritanceResolver.ResolveInterfaceHierarchy` computes `AllExtendedInterfaces` depth first
  (unknown entries and cycles are skipped there and reported as 118 by the compiler rules), and implementing an interface implements every
  interface it extends (`CkTypeGraph.AllImplementedInterfaces`, `ImplementingTypes`). The "exactly one target" rule
  of an interface association and "an interface needs at least one member" are compiler rules (120, 123), not schema
  `oneOf`/`anyOf`: the schema validator reports no message for a failed `anyOf` branch set.
- `visibility` / `derivable` values are PascalCase (`Public`/`Internal`, `Model`/`Any`), consistent with `access`;
  lowercase is a schema error. They are enforced across models by `CkVisibilityValidator` (112, 113).
- The CK cache JSON omits the CK v2 members while they hold their default (empty collections, access
  `ReadWrite`, visibility `Public`, derivable `Any`) through a `JsonTypeInfo` modifier in `CkCache` (`OmitCkV2Defaults`), so v1 caches stay
  byte-identical; reading tolerates the missing keys (trailing defaulted `[JsonConstructor]` parameters on
  `CkTypeGraph`, init setter on `CkTypeAttributeGraph.Access`).
- Message codes for CK v2: 90–113 and 118–142 are in use (table below); **spare: 111, 114–117**. Range
  retention (F0.2) has no message codes of its own — its failures are `ModelValidationException`s and the
  unbound-reference diagnostic is the source-generator diagnostic OM1004.
  Warnings (e.g. 110, 124) of a successful compile are printed by `octo-ckc -c compile` (to **stderr**, also with
  `-v none`, so stdout keeps only the `-cr` file path) and by the `CkCompile` MSBuild task (`Log.LogWarning`; not
  promoted by `TreatWarningsAsErrors`, which applies to the C# compiler only). Known v1 sources hitting 110:
  `octo-construction-kit/src/ConstructionKits/Octo.Energy.Demo/ConstructionKit/types/UkDale*.yaml` (top-level
  `attributes:` in `types/` — were silently ignored before); that project is not in `Octo.ConstructionKit.sln` nor in
  any pipeline, so no build breaks.
  `MessageCodes.cs` is generated from `MessageCodes.json` by `MessageCodes.tt` (not part of the build);
  **Message parameters are the machine-readable contract, message texts are not**: `OperationMessage.Parameters`
  (name → string, invariant culture) is filled by `OperationMessageTemplate.CreateMessage` for every message from the
  template's placeholder names (two copies: `ConstructionKit.Engine` and `Runtime.Engine`). Tools branch on
  `MessageNumber` + `Parameters`, never on `MessageText`, which may be reworded. A new message that needs a
  distinguishable reason takes a stable token as its own placeholder (see 133). The 4-argument `OperationMessage`
  constructor stays and yields empty parameters; `ToString()` is unchanged.
  `MessageCodesSyncTests` fails when the two tables differ in key, number, level or text, or a number repeats.

### Compiler rules and message codes

**Unknown type / record hints.** The messages `OCTO-CK018` (unknown association target type), `OCTO-CK019`
(unknown type) and `OCTO-CK034` (unknown record), and the `ModelValidationException`s of the same situations, end in
a `{hint}` computed by `Resolvers/UnknownCkElementHint` from the model graph. Numbers and levels are unchanged. The
hint tells three situations apart: the id's model is **not in the graph** (dependency or alias missing: "This may
happen because a dependency to another construction kit model is missing."), the model **is resolved but holds no
element of that name** ("The type 'Pump' does not exist in model Industry.Basic-2.6.0. Did you mean 'Machine'?",
up to three closest names), and an element of that name **exists in another model** ("... but a type of that name
exists in Industry.Basic-2.6.0. Check the model prefix of the reference."). `OCTO-CK018` also fills its
`{targetCkTypeId}` / `{roleId}` placeholders in the right order now (they were swapped at the call sites). Tests:
`UnknownCkElementHintTests`.

| Code | Key | Where | Rule |
| ---- | --- | ----- | ---- |
| 90 | `CkLanguageFeatureRequiresV2` | `ElementResolver` | `interfaces`, `implements`, `methods` (also its keys `replacesGenericCreate`, `authorization.dataAccess`), any `access` (type, record, association-role assignment), any `visibility` / `derivable` (type, method, record, enum, attribute, association role), or a type association `targetCkInterfaceId` without `ckLanguage: 2` |
| 91 | `CkLanguageNotSupported` | `ElementResolver`, catalog + repository dependency resolvers | `ckLanguage` outside 1..`MaxSupportedCkLanguage` (also raised when a compiled model is resolved, e.g. on import, and — review L2 — for every **dependency** model: it is not appended, its dependents are skipped, and a hard resolve throws with the message) |
| 92 | `CkInterfaceIdNotUnique` | `ElementResolver` | same interface id twice (`Named-1` and `Named-2` are different contracts) |
| 93 | `CkInterfaceNameCollidesWithType` | `ElementResolver` | interface name == type name of the same model (I-5, GraphQL type namespace) |
| 94 | `CkInterfaceAttributeUnknown` | `ReferenceResolver` | member references an unknown attribute; known members are merged into `CkInterfaceGraph.Attributes` |
| 95 | `ImplementsUnknownCkInterface` | `ReferenceResolver` | `implements` entry not in the graph |
| 96–99 | `CkInterfaceMemberMissing` / `…MultiplicityMismatch` / `…NameMismatch` / `…Hidden` | `InheritanceResolver` | I-1..I-4, checked at the declaring type against `AllAttributes` (inherited attributes satisfy a member) |
| 100 | `CkMethodIdNotUnique` | `InheritanceResolver` | duplicate method id on a type, or re-declaration of an inherited method id (no overrides) |
| 101 | `CkMethodParameterInvalid` | `ReferenceResolver` + `InheritanceResolver` | unknown record/enum reference; duplicate parameter name; `Record`/`Enum` without (or non-`Record`/`Enum` with) `valueCkRecordId` / `valueCkEnumId` |
| 102 | `CkMethodNameReserved` | `InheritanceResolver` | `Create`, `Update`, `Delete` (case-insensitive, without version) |
| 103 | `CkMethodErrorCodeInvalid` | `InheritanceResolver` | duplicate error code or `METHOD_` prefix |
| 104 | `CkMethodAuthorizationInvalid` | `InheritanceResolver` | `allowSelf: true` on a `Static` method; a role or scope name that is not letters/digits/`_ - . : /` starting with a letter or digit (N4 — names are compared as sets) |
| 105 | `RestrictedAttributeInDerivedRule` | `InheritanceResolver.ValidateRestrictedAttributeUse` | review M9: a `displayNameRule` / `displayDescriptionRule` path or a `Text` index path reaches a `Hidden` attribute (incl. record segments), or `ownerAttributePath` reaches a `Hidden` or `MethodOnly` one — those fields are readable/filterable/searchable and would leak it. Not covered (outside the engine compiler): asset-repo computed columns, association `targetCkAttributeIds` |
| 106 | `HiddenAttributeIndexed` | `InheritanceResolver.ValidateRestrictedAttributeUse` | F1.2-S2 (review N5): a non-text index (`Unique`, `UniqueNotDeleted`, `Ascending`, ...) reaches a Hidden attribute (a unique index reveals values through duplicate-key errors). Text indexes keep reporting 105. Review G3 E-M1: every index path is checked against the type, its derived types **and every other type of the same collection** (defining collection root + all its derived types, i.e. siblings, also from other models) — they share `attributes.<name>`, so a sibling's Hidden assignment would be indexed too |
| 107 | `HiddenAttributeAutoCompleteValues` | `ElementResolver.CheckHiddenAssignments` | F1.2-S2 (N5): a Hidden type/record assignment declares `autoCompleteValues` (published in the model; Secret equivalent: 71) |
| 108 | `HiddenAttributeOnAssociationRole` | `ElementResolver.CheckHiddenAssignments` | F1.2-S2 (N5): association-role attributes cannot be Hidden (no access guard on association attributes). `ReadOnly` / `MethodOnly` stay allowed |
| 109 | `UnknownIndexAttributePath` | `InheritanceResolver.ValidateRestrictedAttributeUse` | F1.2-S2 (review N6): in a `ckLanguage: 2` model every index path segment must name an attribute (matched case-insensitively, like Mongo). Paths starting with `Rt`/`CkTypeId` (entity system fields) and the database key `_id` (the `System/Entity` index `CkTypeId` + `_id`) are exempt; a path merged into a collection root from a derived type is checked at that type. v1 models: unchanged |
| 110 | `CkElementsInWrongFolder` | `CompilerService` (per element file) | **warning**: an element file declares a root key that is only read from another folder (e.g. `interfaces:` in `types/x.yaml`); those elements are ignored. Applies to every element kind (each folder reads only its own key) |
| 112 | `CkReferenceToInternalElement` | `CkVisibilityValidator` (end of `ReferenceResolver`) | F1.2-S3: a reference from another model to a `visibility: Internal` element — base type/record, attribute assignment (type, record, role, interface member), attribute `valueCkRecordId`/`valueCkEnumId`, `implements`, interface `extends`, type/interface association role and targets, method parameter/result records and enums. Models compared by name |
| 113 | `CkElementNotDerivable` | `CkVisibilityValidator` | F1.2-S3: a type or record derives from a type/record of another model whose effective `derivable` is `Model` (a v2 base without `derivable: Any`) |
| 118 | `CkInterfaceExtendsInvalid` | `ReferenceResolver` (unknown, self) + `InheritanceResolver.ValidateInterfaces` (cycle) | F1.2-S4: an `extends` entry is unknown, the interface itself, or the chain leads back (cycle; reported at every interface on it) |
| 119 | `CkInterfaceMemberConflict` | `InheritanceResolver.ValidateInterfaces` | F1.2-S4: across an interface and its parents one name stands for two attributes, or one attribute appears under two names. Repeating an inherited member identically is allowed |
| 120 | `CkInterfaceAssociationInvalid` | `ReferenceResolver` | F1.2-S4: association member with an unknown role, unknown target, or not exactly one of `targetCkTypeId` / `targetCkInterfaceId` |
| 121 | `CkInterfaceAssociationMissing` | `InheritanceResolver.ValidateAssociationMembers` | F1.2-S4: a type implementing the interface (incl. parents' members) lacks an outbound association (own or inherited) with the role, a compatible target (the type or a subtype; for an interface target an implementor, or an association narrowed to that interface or one extending it) and a multiplicity at least as strict as the member's (One < ZeroOrOne < N). Optional members are skipped |
| 122 | `CkInterfaceMethodConflict` | `InheritanceResolver.InheritInterfaceMethods` | F1.2-S4: a type redeclares an interface method id with another **invocation contract** (`CkMethodContract.FirstDifference` — kind, parameters as a set incl. `isOptional`/`sensitive`, result incl. its `sensitive` marker, error-code set; descriptions, authorization, execution and order may differ; the message names the first difference). Row I12 classifies every contract change of an interface method as Major. Interface methods are inherited into `CkTypeGraph.AllMethods` (declaring type = highest type of the chain that implements the interface); interface methods follow 100 (duplicate id) and 101-104 |
| 123 | `CkInterfaceHasNoMembers` | `ReferenceResolver` | F1.2-S4: no attribute, association or method member and no `extends` (replaces the former schema `minItems`) |
| 124 | `CkInterfaceDeprecated` | `ReferenceResolver` | F1.2-S4: **warning** for `implements`, `extends`, interface association targets and type association `targetCkInterfaceId` that reference a `deprecated: true` interface |
| 125 | `CkMethodGeneratedNameCollision` | `ElementResolver.CheckGeneratedMethodNames` | F1.4-S2 (review L15): two type methods of a model produce the same generated name `{Type}{Method}` (constant / parameter record) |
| 126 | `CkModelRequiresNewerEngine` | `ElementResolver`, catalog + repository dependency resolvers | F1.1-S6: the compiled model's `minEngineVersion` is above the running engine version (see "minEngineVersion" above) |
| 127 | `CkInterfaceMemberNotUnique` | `ReferenceResolver.CheckCkInterfaces` | review L17: an interface declares the same attribute twice or two members with the same name (case-insensitive); reported at the interface instead of silently dropping the duplicate. Implementation checks (96–99) run on the merged members, so a duplicate produces no follow-up error |
| 128 | `UnknownTargetCkInterfaceOfAssociation` | `ReferenceResolver` | F1.2-S4: a type association's `targetCkInterfaceId` is unknown |
| 129 | `CkInconsistentVisibility` | `CkVisibilityValidator` (end of `ReferenceResolver`) + `InheritanceResolver.InheritInterfaceMethods` | in a `ckLanguage: 2` model a **public** element references an **internal** element of its own model (same reference walk as 112, plus type association `targetCkAttributeIds`), a public interface declares an internal method, or a type redeclares a method of a public interface it implements as internal. One message per offending reference. Coverage of the walk is guarded by `CkVisibilityReferenceCoverageTests`; the classifier mirror is `CkVisibilityIndex.ExposeInternalElementsReachableFromPublicOnes` (defence in depth; it also treats associations of internal types that target a public type, and their roles, as public surface — N1) |
| 130 | `InvalidCompatibilityAcknowledge` | `CkCompatibilityValidator` (from `CompilerService`) | an entry of `compatibility.acknowledge` in `ckModel.yaml` has an empty `change`, a wildcard (`*`, `?`), an empty `reason`, or repeats a key. The JSON schema rejects empty reasons and wildcard keys first; this is the same rule for sources that skip the schema. Using the section in a `ckLanguage` 1 model is message 90 (feature `compatibility.acknowledge`) |
| 131 | `CkMethodHandlerInvalid` | `InheritanceResolver.ValidateMethodHandler` (M-6) | `execution.handler` of a method (type or interface) is not `service` or `pipeline` (exact, lower-case — the schema deliberately has no `enum` so the compiler names the method), `pipeline` without an explicit `timeoutSeconds`, or `pipeline` on an **interface** method (a pipeline binds by the well-known name equal to the qualified method id `Model/Type.Method-1` of one type; an implementing type redeclares the method with `handler: pipeline` itself — execution is not part of the invocation contract, rule 122). Relaxing the interface rule later is non-breaking, tightening it would not be |
| 132 | `CkMethodPipelineSensitiveParameter` | `InheritanceResolver.ValidateMethodHandler` (M-6) | a method with `execution.handler: pipeline` has at least one parameter with `sensitive: true` (the message names the method, the type and the sensitive parameters). A pipeline execution stores its input; a method with a sensitive parameter must use `handler: service`. Reported in addition to 131 when 131 applies too. A sensitive **result** is message 134. The runtime refusal in the pipeline handler stays as a second gate |
| 133 | `CkMethodReplacesGenericCreateInvalid` | `InheritanceResolver.ValidateMethodReplacesGenericCreate` / `ValidateAtMostOneReplacesGenericCreate` (M-7) | `replacesGenericCreate: true` on a method that is not `kind: Static`, on an **interface** method, on a method of an **abstract** type, or a second flagged method on a concrete type (own or inherited; reported on the type that declares one of them, naming all flagged methods). One message per offending method; parameters `methodId`, `ckTypeId`, `reason` (stable token `instanceMethod` / `interfaceMethod` / `abstractType` / `multipleMethods`, constants in `InheritanceResolver.ReplacesGenericCreateReasons`) and `explanation` (human text). The flag in a `ckLanguage` 1 model is message 90 (feature `replacesGenericCreate`), an older engine refuses a model that uses it with message 126 (raised `minEngineVersion`). Code taken as the next free number after 132 |
| 134 | `CkMethodPipelineSensitiveResult` | `InheritanceResolver.ValidateMethodHandler` (M-6) | a method with `execution.handler: pipeline` whose `result.sensitive` is `true` (the message names the method and the type). A pipeline execution stores its output, so a method with a sensitive result must use `handler: service`. Own code next to 132 because the remedy differs (a parameter can be dropped, a result cannot be hidden from the pipeline); both are reported when both apply, and 131 as well when it applies. Interface methods refuse `pipeline` already (131); a sensitive result on a `service` method (type or interface) is fine. The marker in a `ckLanguage` 1 model cannot occur (methods are v2 only, message 90); an older engine refuses a model that uses it with message 126 (raised `minEngineVersion`) |
| 135 | `CkMethodDataAccessInvalid` | `InheritanceResolver.ValidateMethodDataAccess` (M-8) | `authorization.dataAccess` of a method is `Read` on a method with `replacesGenericCreate: true` (creating an entity is a write; `Write` is fine) or a value outside `Read`/`Write` (a YAML model is stopped by the JSON schema first, so the message covers a JSON or programmatic model). One message per offending method with a `{reason}`. The key in a `ckLanguage` 1 model is message 90 (feature `authorization.dataAccess`); an older engine refuses a model that uses it with message 126 (raised `minEngineVersion`) |
| 136 | `BlueprintSeedCreateReplacedByMethod` | `BlueprintGenericCreateGuard` (Runtime.Engine, raised at blueprint apply, not by the compiler) | the seed data of a blueprint install, the diff of a blueprint update or the `Add` step of a blueprint migration would create a **new** entity (its rtId does not exist in the tenant) of a type whose static method declares `replacesGenericCreate` (own or inherited, so also a derived concrete type). Message parameters `rtId`, `ckTypeId`, `methodId` (the version-less qualified method id) and `reason` (`GENERIC_CREATE_REPLACED_BY_METHOD`, `GenericCreateReplacement.ReasonToken`); one message per entity. See "Blueprint apply and `replacesGenericCreate`" below |
| 137 | `ImportRtCreateReplacedByMethod` | `ImportRtModelCommand.PreflightGenericCreateAsync` (Runtime.Engine, raised by a user-initiated `ImportRt`, not by the compiler) | the import file would create a **new** entity of a type whose static method declares `replacesGenericCreate` (own or inherited). Message parameters `rtId`, `ckTypeId`, `methodId` and `reason` (`GENERIC_CREATE_REPLACED_BY_METHOD`); one message per entity, carried by one `ExchangeException` (`MessageNumber` 137, `Messages`). See "`ImportRt` and `replacesGenericCreate`" above |
| 138 | `ExportedRecordDropsRequiredMember` | `ExportedRecordMemberValidator` (from `CatalogModelResolver.CompileAsync`, after `SecretAttributeValidator`) | **warning**: an attribute assignment declared in the compiled model (type or record; interfaces are not walked) whose value type is `Record`/`RecordArray` and whose effective ownership is exported (`SeedOwned`/`TenantOwned`; assignment override over definition) reaches - through the referenced record, also from a dependency model, and nested exported record members - a member that is required (`isOptional` false), has no default value and whose effective ownership `IsExcludedFromExport()` (`RuntimeState`, `Secret`). This is the shape `RtEntityToTcDtoConverter.ConvertAttributes` drops (it evaluates record members individually). One message per member; parameters `ckElementId` (declaring type/record, versioned id), `attributeName`, `ckRecordId` (record that contains the member), `memberPath` (e.g. `States.ExternalId`), `memberOwnership`. Not reported: optional or defaulted members, anything below a record-valued attribute/member that is itself excluded (dropped as one unit, e.g. `MappingTargets`), records not reachable from an assignment of the compiled model (reported where they are used). A warning only, so existing models compile unchanged; no generated code, cache or catalog change. Tests: `ExportedRecordMemberValidatorTests`, `CkExportedRecordMemberCompileTests`. Rules: `docs/ck-semver-rules.md` "Record members and export" |
| 139 | `RtAttributeWriteRestricted` | `RtAttributeAccessCheck` (Runtime.Engine/Exchange; raised by `ImportRtModelCommand` on every RT import path and by `BlueprintGenericCreateGuard` / `BlueprintMigrationExecutor`, not by the compiler) | an RT import, blueprint seed, blueprint update diff or migration step sets an attribute with `access: Hidden` or `MethodOnly` (also a record member). Parameters `rtId`, `ckTypeId`, `attributePath`; one message per value, refused before anything is written. `ReadOnly` stays importable. See "RT export and import honour `access`" above |
| 140 | `RtIdentityOwnedWriteRestricted` | `RtIdentityOwnedWriteCheck` (Runtime.Engine/Exchange; raised by `ImportRtModelCommand` on the caller path of `ImportRt` only, not by the compiler) | a user-initiated import sets an edge of an identity association role (`IdentityOwnedWrites`; the identity-owned User attributes are MethodOnly since System.Identity 3.0.0 and refused by 139). Parameters `rtId`, `ckTypeId`, `path`; one message per edge, refused before anything is written. See "Identity-owned User fields and identity associations" above |
| 141 | `RtRecordMemberNotPreservable` | `ImportRtModelCommand.PreserveSecretRecordMembers` (Runtime.Engine/Exchange; caller path of `ImportRt` only, not by the compiler) | a caller upsert writes a record array whose record has no usable record key (none, or the key member is MethodOnly/Hidden) while a stored element holds a value in a MethodOnly/Hidden member (also nested); the elements cannot be matched, so the value would be lost. Parameters `rtId`, `ckTypeId`, `path`; refused before anything is written. With a usable key the member is carried over (omission rule, see "Omission inside records") |
| 142 | `CkMethodModeInvalid` | `InheritanceResolver.ValidateMethodMode` (M-9) + `ElementResolver.CheckCkLanguage` | `execution.mode` of a method (type or interface) is not `sync` or `async` (exact, lower-case — the schema has no `enum` so the compiler names the method), `async` together with `handler: pipeline` (a pipeline handler answers synchronously only, `pipelineMethodHandlers.md`; decision OD-10 limits async in Phase 3 to service handlers of bot job types), or the key in a model without `ckLanguage: 2` (reported in addition to the 90 for `methods`, naming the method). Parameters `methodId`, `ckTypeId`, `reason`. `async` + `idempotent: true` is allowed (an idempotent async method skips the idempotency key store as a sync one does). The mode is part of the invocation contract (rule 122) |

`InheritanceResolver.ResolveInterfacesAndMethods` also completes `AllImplementedInterfaces` (own ∪ every base
type's declared interfaces), `AllMethods` (nearest declaration wins; `CkMethodGraph.DeclaringCkTypeId` is the
declaring type) and `CkInterfaceGraph.ImplementingTypes`. All new references (`implements`, interface member
ids, method `valueCkRecordId` / `valueCkEnumId`) go through the `VariableResolver`, and `CkReferenceRewriter`
(F0.2 range retention) rewrites and floor-checks them like every other reference (`System@2/Named-1`).

Source generator: the `*CkIds` class gains `RtCk{Name}InterfaceId`, `Ck{Name}InterfaceId`,
`RtCk{Name}InterfaceIdString` and `{Type}{Method}MethodId` constants (e.g.
`SystemIdentityCkIds.UserChangePasswordMethodId = "System.Identity/User.ChangePassword-1"`; a method version > 1 is
appended: `UserChangePassword2MethodId`). Typed parameter/result records: see "v2 class modifiers and method contracts"
below.
**v2 class modifiers and method contracts (F1.4-S2).** For a `ckLanguage: 2` model (the source generator
passes `EffectiveCkLanguage` to `CkTypeCodeGenerator`) the Rt class is `sealed` for `isFinal` or for an effective
`derivable: Model` without a subtype in its own model; v1 output is unchanged. **`isAbstract` does not emit
`abstract`** (platform-owner decision, review G3 E-M3): `IRuntimeRepository` / `IRepositoryDataSource` generics
require `where TEntity : RtEntity, new()`, so an abstract Rt class could not be queried polymorphically. Revisit in
Phase 4 (System → ckLanguage 2) together with a non-`new()` query path.
`CkMethodCodeGenerator` emits per declared type method `{Type}{Method}Parameters` (`required` for required
parameters, nullable for optional ones, camelCase → PascalCase; CLR types: string/bool/DateTime/DateTimeOffset/
TimeSpan/int/long/double, `IReadOnlyList<string|long>`, `Rt{Enum}Enum`, `Rt{Record}Record`) with a `ToString()` that
redacts `sensitive` parameters as `***`, and `{Type}{Method}Result { Value }` when the method has a result
(the engine does not dispatch; the records are the `TParameters` / `TResult` of the typed handlers in
octo-common-services: `ICkMethodHandler<TParameters, TResult>` for a method with a result,
`ICkMethodHandler<TParameters>` for a method without one). Interface methods get no records. **Review L15:** the generated name
(`CkMethodIds.GeneratedName`, shared with the `{Name}MethodId` constants) is checked per model by the compiler —
**message 125** `CkMethodGeneratedNameCollision` (`User`+`ChangePassword` vs `UserChange`+`Password`); the existing
constant names stay unchanged (no separator). Pinned by `CkV2GeneratedContractsTests` (Roslyn compile + emitted
`ToString`).

**C# interfaces (F1.4-S1).** `CkInterfaceCodeGenerator` emits `public partial interface IRt<Name>` per CK
interface (`Named-1` → `IRtNamed`, `Named-2` → `IRtNamed2`) in the model's generated namespace (the same one as
the Rt classes — deviation from the concept's `.Contracts` sub-namespace, so cross-model references resolve like
base classes do). Attribute members are **get-only** properties with exactly the CLR type of the Rt property (taken
from `AttributeCodeGenerator`; nullable when the member is optional); `extends` becomes C# interface inheritance and
an inherited member is not repeated (CS0108). A generated Rt class lists its declared `implements` and implements
every member of those interfaces and their parents **explicitly** (`string IRtNamed.Name => Name;`; an optional
member the type does not assign returns `default`), so a required assignment of an optional member still conforms
and re-implementing an interface a base class already implements is legal. Association members are not emitted
(the Rt classes have no generated navigations yet); methods neither (a C# interface carries no
method members; handlers implement `ICkMethodHandler<…>` of octo-common-services instead). v1 models: nothing emitted,
type output unchanged. Pinned by `CkInterfaceCodeGeneratorTests` (Roslyn-compiles the generated code against
`Runtime.Contracts` with a consumer that assigns `RtAccount` to `IRtNamed`).

**Interfaces of another CK model.** A type may implement (and an interface may extend) an interface that
another model declares, e.g. `Basic/NamedEntity` implements `System/Named-1`. Its C# interface lives in the other
model's assembly, in `<that project's RootNamespace>.Generated.<Model>.v<Major>`, which the CK cache does not know.
`CkSourceGenerator` therefore reads the generated interfaces of the compilation's referenced assemblies
(`CkGeneratedInterfaceCollector.Collect`: public `IRt*` interfaces in namespaces `*.Generated.*.v<n>`, assembly-qualified
so the same name in two assemblies counts as ambiguous, sorted, compared by content so the generation reruns only when
the references change) and passes a `CkGeneratedInterfaceLookup` to
`CkTypeCodeGenerator` / `CkInterfaceCodeGenerator`. A foreign interface is emitted **fully qualified**
(`RtNamedEntity : RtEntity, global::Meshmakers.Octo.ConstructionKit.Models.System.Generated.System.v2.IRtNamed`, and
`string global::….IRtNamed.Name => Name;`), matched by model name, the model's major and the interface name. Interfaces
of the own model keep the short name, so models without foreign interfaces generate byte-identical code; the API
overloads without a lookup keep the old (short-name) output. The consuming project must **reference the C# package of
the other model** (for System: `Meshmakers.Octo.ConstructionKit.Models.System`, depends only on `Runtime.Contracts`); if
no referenced assembly (or more than one) declares the interface, the generator reports **OM1006** "Interface of another
CK model not found" naming the interface and the remedy (add the package / keep only one reference); the short name is
still emitted, so CS0246 follows in addition. Not covered: (a) member types of a foreign interface that are records or
enums of that model are still emitted by `AttributeCodeGenerator` with their short names (resolve them with a
`global using` as for foreign base classes); (b) two CK models generated in the **same** project where one implements
the other's interface: the other model's interfaces are generator output, not a reference, so OM1006 is reported - put
the models into separate projects. Cost: the collector runs on every compilation change but walks only assemblies that
have a `Generated` namespace; the comparer keeps the generation itself cached. Pinned by `CkForeignInterfaceCodeGeneratorTests`
(two-model fixture, library Roslyn-compiled into its own assembly: qualified output compiles, the old output fails with
CS0246, a foreign `extends` is qualified, a missing reference and an ambiguous match are reported, own-model output is
byte-identical with the lookup).
Docs generator: `Interfaces.md` per model (members incl. inherited ones, "Extends" line, association and method
tables, "Deprecated" marker) plus an "Implements" line and a methods table per type (only when present);
"Visibility: `Internal`" / "Derivable: `Model`" lines per element and an "(internal)" method marker, written only
when the value differs from the v1 default, so v1 docs are unchanged.
SemVer rules: `docs/ck-semver-rules.md`. Phase 1 base rules (additions Minor, removals Major, `access` changes Minor
with an "access/security" note, `ckLanguage` 1→2 Minor unless it flips the `derivable` default, `visibility`
Public→Internal and `derivable` Any→Model Major, the reverse Minor, type association `targetCkInterfaceId` set or
changed Major and cleared Minor) plus the F2.1 rule rows with one test each (`tests/ConstructionKit.Engine.Tests/SemVer/Rows/`):
N1–N5 internal elements (`CkVisibilityIndex` caps changes of internal elements and of members of internal
owners at Minor), I1–I12 interfaces (optional members Minor — an optional attribute member only with a new or previously internal definition of this model, association members keyed by role with a `target`
property), M1–M18 methods (one change per method field, element kinds `MethodParameter` / `MethodError`, the
rendered `signature` stays as a level-None summary: M12 under default-deny — roles any-of and case-insensitive (P3-3), scopes all-of, an omitted block or empty roles admit administrators only, block add/remove is one change, looser changes are behavioural "security: method access widened": I12 lifts every invocation-contract change of an interface method to Major), D1–D8 range retention (D8: a resolved dependency moving up inside its major on a range-retaining model is level None, every other dependency version move, legacy exact-pinned input included, is judged by one rule (Minor inside the major, Major across; no exact-pin special case; legacy input is not rejected, so the publish gate and `ValidateCascade` read old catalog versions), the verdict line names the move: a range/floor excluding the previously resolved version, or a resolved version going down, is Major; element kind `DependencyRange`,
model property `rangeRetention`), T1–T7 / E1–E2 / R1–R2 / A1–A3 public types, stable bases, enums, records, attribute
definitions and access (access tightening Major, security exception Minor + `RequiresAcknowledge` for
`securitySensitive` attributes), B1–B4 behavioural changes (v2 indexes are paired by case-insensitive field paths, index-type changes are one modification: removing the default of a required-assigned or public definition is Major in a v2 model; `CkClassifiedModelChange.IsBehavioural` /
`RequiresAcknowledge`, own "Behavioural changes" section in report and changelog, stable base =
`CkSemVerClassifier.IsStableBase`). The guard's `KnownGaps` list is empty.
