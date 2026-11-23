---
description: The data-permission rule set and the public evaluator that every read and write path must use.
applies_to: src/Runtime.Contracts/DataPermissions/**, src/Runtime.Engine/Security/**, src/ConstructionKit.Contracts/DataTransferObjects/CkTypeWriteAccessDto.cs
---

# Data permissions: one rule set, one public check

## Data permissions: one rule set, one public check

Data permissions (policies of `System.Identity`, resolved per tenant by `IDataPermissionResolver`) are enforced on
the repository session by the caller's `RtSecurityContext` (subject, roles, `IsSystem`); a session without one is the
System context and bypasses everything. The rules live in exactly one place:

| Piece | Where | Role |
|-------|-------|------|
| `RtDataAccessEvaluator.Classify` | `Runtime.Contracts/DataPermissions` | pure classification Open / Allowed / OwnedOnly / Denied per type (+ base types) and action; AuditOnly view |
| `DataSecurityFilterFactory` | `Runtime.Engine/Security` | read filter: classifies with the evaluator, the query renders the ownership predicate; denied reads behave as not found |
| `DataPermissionRules` | `Runtime.Engine/Security` (internal) | shared by guard and evaluator: type classification, ownership predicate (`RtCreatedBy` or `CkType.ownerAttributePath`, fail closed), audit events |
| `DataPermissionWriteGuard` | `Runtime.Engine/Security` (internal) | the write/delete funnel in `RuntimeRepositoryBase`: read visibility first, then batch write/delete checks per change set, atomic rejection, blueprint lock |
| `TypeWriteAccessGuard` | `Runtime.Engine/Security` (internal) | model-declared type write restriction (`writeAccess`: principal kinds and in-process channels), checked for every non-system session before the policy guard, on tenants with and without policies; message 144 (see "Type write restriction" below) |
| `IRtDataPermissionEvaluator` | `Runtime.Contracts/DataPermissions` (impl. `RtDataPermissionEvaluator`, registered singleton) | **the public question** "may this caller read / write / delete this entity (or type)?" |

`IRtDataPermissionEvaluator` is the only public way to ask (a gateway or any caller-bound surface must not copy
the classification):

- `EvaluateAsync(repository, caller, RtEntityId target, RtDataAction action, publishAudit = false, ct)` and
  `EvaluateTypeAsync(repository, caller, ckTypeId, action, publishAudit = false, ct)`.
- Result `RtDataPermissionVerdict { Decision, Reason, AuditOnlyViolation }`, no entity data:
  `Decision` = `Allowed` | `NotVisible` | `NotPermitted`. **`NotVisible` must be answered like a missing entity**
  (not found); `NotPermitted` is a refusal. `Reason` is a language-neutral code (`Open`, `Granted`,
  `GrantedAsOwner`, `AuditOnly`, `NoGrant`, `NotOwner`, `EntityNotFound`, `BlueprintLocked`).
- Entity level: visibility is decided first with the Read classification (what the read filter hides is
  `NotVisible` for every action, also when the entity does not exist and the verdict depends on it); then the
  action's own grant, ownership for owned-only grants and, for Write/Delete, the blueprint lock of opted-in
  types. The stored entity is read (raw, system session) only when the verdict depends on it.
- Type level (no entity, e.g. a static method): Read without any grant is `NotVisible`; Write/Delete without a
  grant is `NotPermitted`; an owned-only grant is allowed like the generic insert.
- System callers and tenants without policies are `Allowed` / `Open`. AuditOnly policies never refuse; the
  verdict carries `AuditOnlyViolation` and `publishAudit: true` publishes the same audit event the repository
  would (`DataPermissions.ReadViolation` / `WriteViolation` / `BlueprintLockViolation`). The default call has
  no side effect. (The evaluator does not publish the guard's AuditOnly visibility event of a write; its
  `AuditOnlyViolation` flag keeps the meaning of the write/read grant audit.)
- **Write requires visibility.** The write guard applies the evaluator's first step before any write/delete
  decision: for an update, replace or delete of an existing entity, and for an association change at its origin, the
  Read classification decides first (`DataPermissionWriteGuard.CheckVisibilityAsync`, same `DataPermissionRules`
  classification and ownership predicate as the evaluator and the read filter). No read grant on the (protected) type,
  or an owned-only read grant and the caller is not the owner, means the entity is invisible and the change is refused
  **as if it did not exist** (`RuntimeRepositoryBase.CreateEntityNotFoundException`, a virtual hook: the persistence
  layer overrides it with the exception its own write path raises for a missing entity, so a hidden and a missing
  entity are indistinguishable; the whole change set is rejected). The check comes before the write decision, so a
  hidden entity never answers "access denied". Inserts have no visibility question and stay allowed, and neither do the associations whose origin is inserted by the same change set (the parent link of a new file or folder: no stored entity exists yet, without this exemption every create-with-association by an owned-only or write-only role was refused as not found). Like the
  evaluator, an id without a stored entity is answered the same way as an invisible one when the caller's read access
  is restricted (so existence is never revealed, and an upsert/replace by a role without full read is refused for
  existing and new ids alike); with unrestricted read it is left to the normal write path. The stored entity is read
  (raw, once per entity) only for an owned-only read grant (or an AuditOnly read policy). AuditOnly policies never refuse: an AuditOnly read policy that would hide an entity publishes
  one `DataPermissions.WriteViolation` event per type (none additionally when the type's write audit already
  published); system callers and tenants without policies are untouched. **Compatibility:** a role that holds Write
  or Delete on a type but no Read (e.g. a custom role with only `files.manage`) can still insert but no longer
  update, replace or delete existing records of that type; grant the matching Read policy (the shipped
  `FileManagement` role has it). Tests: `DataPermissionWriteGuardVisibilityTests`; the matrix test
  `Entity_AgreesWithWriteGuard` now covers every cell, `NotVisible` included.
- **Stored type and delete by abstract type.** The guard's raw read (`StoredEntityCache` in
  `DataPermissionWriteGuard`) uses a stored record only when it is stored as the addressed type or one of its derived
  types (stored `CkTypeId`, compared like the read filter by `SemanticVersionedFullName` in `StoredTypeMatch`; a stored
  entity without `CkTypeId`, or an addressed type the CK cache does not know, counts as matching). A record of a sibling
  type sharing the collection is therefore missing for the visibility and ownership checks (restricted read: not
  found before validation, like a missing record). For every non-system caller in a tenant with policies, the funnel
  then runs a **stored-type step after the rule validation, before anything is written**
  (`BulkRtMutationOptions.BeforeApplyAsync`, `StoredTypeTargets`): an update, replace or delete target, or an
  association origin, stored as a type outside the addressed subtree answers through `CreateEntityNotFoundException`,
  so it answers exactly like a missing target (after the same validation) and is never written or deleted through
  another type. It reads only types whose collection also holds concrete types outside their subtree
  (`StoredTypeMatch.SharesCollectionWithOtherTypes`, an unresolvable collection root counts as shared), one
  `DocumentsAsync` per such type, and reuses the guard's raw reads (`rawStoredEntities` of
  `DataPermissionWriteGuard.CheckAsync`); system sessions and tenants without policies never run it, so
  `RecordArrayItemUpdateTests` (system session) keep their command count. `DeleteOneRtEntityByRtIdAsync` with an abstract type judges the type write
  restriction on the addressed type and its derived types before it reads the record, answers a missing rtId and a
  record outside the addressed subtree (`StoredTypeMatch.AcceptedStoredTypeNames`) through
  `CreateEntityNotFoundException` (it no longer returns silently), and refuses a hidden record under the addressed
  (abstract) type, never under the concrete type of the stored record. Callers without a visibility question (system
  session, tenant without policies, full read) keep the answers of before: an invalid payload of a missing record is a
  validation error. Tests: `HiddenEqualsMissingWriteTests`, `HiddenOrMissingAddressingTests`,
  `HiddenOrMissingAbstractDeleteTests`, `HiddenOrMissingPayloadTests`, `UnrestrictedCallerWriteTests` (real funnel,
  real `EntityRuleEngine`, in-memory collection).
- Tests: `RtDataPermissionEvaluatorTests` (matrix, agreement with the guard) in `Runtime.Engine.Tests`; the
  differential test against the real repository is `DataPermissionEvaluatorDifferentialTests` in
  engine-mongodb.

## Type write restriction (`writeAccess`)

A CK type can restrict **who may write** its runtime entities: create, update, replace, delete, and change
association edges at either end. The restriction is declared in the model, enforced by the engine on every
caller-scoped write path (GraphQL mutations, REST, the user path of `ImportRt`) and ignored by system writes
(pipelines, blueprint seeds, migrations).

Sources: `CkTypeWriteAccessDto` (Contracts), `ElementResolver.CheckWriteAccess` (compiler, message 143),
`TypeWriteAccessGuard` (Runtime.Engine/Security, message 144), `RuntimeRepositoryBase.ApplyChangesGuardedAsync`,
`ImportRtModelCommand.PreflightTypeWriteAccess`.

### Model syntax

```yaml
types:
  - typeId: KnowledgePage
    derivedFromCkTypeId: ${System}/Entity
    writeAccess:
      principals: [ Interactive ]              # Interactive and/or Client
      channels: [ System.Ai/KnowledgeTools ]   # trusted in-process channels, <namespace>/<name>
```

| Key | Values | Meaning |
|-----|--------|---------|
| `principals` | `Interactive`, `Client` (case-sensitive) | a signed-in user / a client-credentials service principal may write |
| `channels` | `<namespace>/<name>`, e.g. `System.Ai/KnowledgeTools` | a session that service code opened on this channel may write |

A write is **allowed** when the caller's principal kind is listed **or** the session carries a listed channel.
There are no client ids in the model: client ids are tenant- and cluster-specific, and a channel is a marker that
in-process service code sets on the session it opens (`RtSecurityContext.WithWriteChannel`); it never comes from a
token, a header or other caller input.

Rules:

- **Requires `ckLanguage: 2`** (message 90 otherwise).
- **Inheritance:** derived types inherit the block. When a derived type declares its own block, **every** block along
  the inheritance chain must admit the write (a derived type can narrow, never widen).
- **Associations:** an association change counts as a write of the entities at **both** ends; an edge with a
  restricted entity at either end needs the caller to be admitted by that type.
- **System sessions** (`RtSecurityContext.System`, plain `IOctoSession`) and the system path of the RT import
  (blueprint seeds, `ImportAsync` without caller) are never restricted.
- **Unrestricted types** (no block) ignore the principal kind entirely.

### Principal kind

`RtSecurityContext.PrincipalKind` (`RtPrincipalKind`: `Unknown` | `Interactive` | `Client`) and
`RtImportCallerContext.PrincipalKind` carry what the service that opened the session knows about the caller. The
default is `Unknown`. **For a restricted type `Unknown` is refused (fail closed)**; it is admitted only through a
listed channel. Services that open caller-scoped sessions set the principal kind from the caller's token
(`RtSecurityContext.ForUser(subject, roles, principalKind)`, `RtImportCallerContext.UserInitiated(subject,
principalKind)`); the engine never derives it.

### Compiler validation (message 143)

`Type '<type>' declares an invalid 'writeAccess': <reason>` for:

- a block with neither `principals` nor `channels`,
- an empty `principals` or `channels` list,
- an unknown principal kind,
- a channel that is not `<namespace>/<name>` (letters, digits, underscore; each part starts with a letter; the
  namespace may contain dots),
- a duplicate entry.

Unknown keys inside the block are refused by the schema (message 27).

### Runtime refusal (message 144)

`Write refused: the <operation> of entity '<rtId>' of type '<type>' is restricted by the 'writeAccess' of type
'<restricting type>'. The caller is principal '<kind>' on channel '<channel>'; only the principals [..] or the
channels [..] may write it`

Parameters: `operation` (`create`, `update`, `replace`, `delete`, `create or replace`, `association change`),
`rtId`, `ckTypeId`, `restrictingCkTypeId`, `principalKind`, `channel`, `principals`, `channels`.

- Repository write funnel (`ApplyChangesAsync` and the single-entity writes built on it): checked for every
  non-system session, on every tenant (with or without data policies), **before** the data-permission guard; the
  whole change set is rejected atomically with a `RuntimeRepositoryException` whose `OperationResult` carries the
  144 messages. The check judges types only, never a stored entity, so it reveals nothing about existence. A create names the exact type; for an update, replace, delete and both ends of an association the named
  type may be a base of the stored entity's type, so the blocks of every derived type are checked as well (write
  through the concrete type when a derived type is more restricted).
- Filter-based writes (`UpdateOneRtEntityAsync` / `UpdateManyRtEntityAsync` / `ReplaceOneRtEntityAsync` with a filter,
  `DeleteOneRtEntityAsync` / `DeleteManyRtEntitiesAsync`): checked in `RuntimeRepositoryBase` for non-system sessions
  before the persistence layer runs, against the given type, its base types and every derived type.
- Bulk import paths (`BulkInsertRtEntitiesAsync`, `BulkRtAssociationsAsync`): checked for non-system sessions.
- RT import, user path (`ImportAsCallerAsync`): preflight over the whole file (YAML list or the first pass over the
  JSON bulks) before the first write; one `ExchangeException` with message number 144 lists every offender.

### Compatibility

- **Additive.** Models without the key compile byte-identically and keep their `minEngineVersion`; types without the
  block behave exactly as before.
- **minEngineVersion:** a model that declares the block on any type carries
  `CkEngineVersion.WriteAccessMinEngineVersion` (`3.5.71`), so an older engine refuses the model with message 126
  instead of ignoring the restriction (see `ck-versioning.md`).
- **SemVer row T8:** adding, removing or changing a block is Minor + `requiresAcknowledge`, marked behavioural
  (`ck-semver-rules.md`).
- **Schema:** the `writeAccess` key reaches model authors only through the schema publish path (`docs/ck-schema-and-json-serialization.md`, "JSON
  schema publish path"); a catalog model must not use it before the schema release.
- **Persistence:** the block is stored on the tenant's CK type document (engine-mongodb, `CkType.WriteAccess`,
  reflection round-trip gate); without it a tenant would not enforce the restriction.

### Tests

`CkTypeWriteAccessResolverTests` (143, 90), `CkV2CompileTests.WriteAccess_*` (compile, YAML round trip, graph,
docs, minEngineVersion, invalid blocks), `CkEngineVersionTests` (`*WriteAccess*`), `PublicSurfaceRowTests.T8_*`,
`TypeWriteAccessGuardTests` (principal kinds, channels, Unknown, inheritance, associations, message),
`TypeWriteAccessRepositoryTests` (write funnel, system session, bulk insert, atomic rejection),
`ImportRtModelCommandTypeWriteAccessTests` (caller path YAML/JSON, associations, system path, unrestricted tenant).
