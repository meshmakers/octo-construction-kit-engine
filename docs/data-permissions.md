---
description: The data-permission rule set and the public evaluator that every read and write path must use.
applies_to: src/Runtime.Contracts/DataPermissions/**, src/Runtime.Engine/Security/**
---

# Data permissions: one rule set, one public check

## Data permissions: one rule set, one public check (AB#4969 / AB#7035 / AB#7126)

Data permissions (policies of `System.Identity`, resolved per tenant by `IDataPermissionResolver`) are enforced on
the repository session by the caller's `RtSecurityContext` (subject, roles, `IsSystem`); a session without one is the
System context and bypasses everything. The rules live in exactly one place:

| Piece | Where | Role |
|-------|-------|------|
| `RtDataAccessEvaluator.Classify` | `Runtime.Contracts/DataPermissions` | pure classification Open / Allowed / OwnedOnly / Denied per type (+ base types) and action; AuditOnly view |
| `DataSecurityFilterFactory` | `Runtime.Engine/Security` | read filter: classifies with the evaluator, the query renders the ownership predicate; denied reads behave as not found |
| `DataPermissionRules` | `Runtime.Engine/Security` (internal) | shared by guard and evaluator: type classification, ownership predicate (`RtCreatedBy` or `CkType.ownerAttributePath`, fail closed), audit events |
| `DataPermissionWriteGuard` | `Runtime.Engine/Security` (internal) | the write/delete funnel in `RuntimeRepositoryBase`: read visibility first (AB#7126), then batch write/delete checks per change set, atomic rejection, blueprint lock |
| `TypeWriteAccessGuard` | `Runtime.Engine/Security` (internal) | AB#8574: model-declared type write restriction (`writeAccess`: principal kinds and in-process channels), checked for every non-system session before the policy guard, on tenants with and without policies; message 144 (`docs/ck-type-write-access.md`) |
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
- **Write requires visibility (AB#7126).** The write guard applies the evaluator's first step before any write/delete
  decision: for an update, replace or delete of an existing entity, and for an association change at its origin, the
  Read classification decides first (`DataPermissionWriteGuard.CheckVisibilityAsync`, same `DataPermissionRules`
  classification and ownership predicate as the evaluator and the read filter). No read grant on the (protected) type,
  or an owned-only read grant and the caller is not the owner, means the entity is invisible and the change is refused
  **as if it did not exist** (`RuntimeRepositoryBase.CreateEntityNotFoundException`, a virtual hook: the persistence
  layer overrides it with the exception its own write path raises for a missing entity, so a hidden and a missing
  entity are indistinguishable; the whole change set is rejected). The check comes before the write decision, so a
  hidden entity never answers "access denied". Inserts have no visibility question and stay allowed, and neither do the associations whose origin is inserted by the same change set (the parent link of a new file or folder: no stored entity exists yet; without this exemption every create-with-association by an owned-only or write-only role was refused as not found, AB#7213). Like the
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
- **Stored type and delete by abstract type (AB#7212).** The guard's raw read (`StoredEntityCache` in
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
