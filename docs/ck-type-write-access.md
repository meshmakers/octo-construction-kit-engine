# Type write restriction (`writeAccess`, AB#8574)

A CK type can restrict **who may write** its runtime entities: create, update, replace, delete, and change
association edges at either end. The restriction is declared in the model, enforced by the engine on every
caller-scoped write path (GraphQL mutations, REST, the user path of `ImportRt`) and ignored by system writes
(pipelines, blueprint seeds, migrations).

Sources: `CkTypeWriteAccessDto` (Contracts), `ElementResolver.CheckWriteAccess` (compiler, message 143),
`TypeWriteAccessGuard` (Runtime.Engine/Security, message 144), `RuntimeRepositoryBase.ApplyChangesGuardedAsync`,
`ImportRtModelCommand.PreflightTypeWriteAccess`.

## Model syntax

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

## Principal kind

`RtSecurityContext.PrincipalKind` (`RtPrincipalKind`: `Unknown` | `Interactive` | `Client`) and
`RtImportCallerContext.PrincipalKind` carry what the service that opened the session knows about the caller. The
default is `Unknown`. **For a restricted type `Unknown` is refused (fail closed)**; it is admitted only through a
listed channel. Services that open caller-scoped sessions set the principal kind from the caller's token
(`RtSecurityContext.ForUser(subject, roles, principalKind)`, `RtImportCallerContext.UserInitiated(subject,
principalKind)`); the engine never derives it.

## Compiler validation (message 143)

`Type '<type>' declares an invalid 'writeAccess': <reason>` for:

- a block with neither `principals` nor `channels`,
- an empty `principals` or `channels` list,
- an unknown principal kind,
- a channel that is not `<namespace>/<name>` (letters, digits, underscore; each part starts with a letter; the
  namespace may contain dots),
- a duplicate entry.

Unknown keys inside the block are refused by the schema (message 27).

## Runtime refusal (message 144)

`Write refused: the <operation> of entity '<rtId>' of type '<type>' is restricted by the 'writeAccess' of type
'<restricting type>'. The caller is principal '<kind>' on channel '<channel>'; only the principals [..] or the
channels [..] may write it`

Parameters: `operation` (`create`, `update`, `replace`, `delete`, `create or replace`, `association change`),
`rtId`, `ckTypeId`, `restrictingCkTypeId`, `principalKind`, `channel`, `principals`, `channels`.

- Repository write funnel (`ApplyChangesAsync` and the single-entity writes built on it): checked for every
  non-system session, on every tenant (with or without data policies), **before** the data-permission guard; the
  whole change set is rejected atomically with a `RuntimeRepositoryException` whose `OperationResult` carries the
  144 messages. The check judges types only, never a stored entity, so it reveals nothing about existence
  (AB#7126). A create names the exact type; for an update, replace, delete and both ends of an association the named
  type may be a base of the stored entity's type, so the blocks of every derived type are checked as well (write
  through the concrete type when a derived type is more restricted).
- Filter-based writes (`UpdateOneRtEntityAsync` / `UpdateManyRtEntityAsync` / `ReplaceOneRtEntityAsync` with a filter,
  `DeleteOneRtEntityAsync` / `DeleteManyRtEntitiesAsync`): checked in `RuntimeRepositoryBase` for non-system sessions
  before the persistence layer runs, against the given type, its base types and every derived type.
- Bulk import paths (`BulkInsertRtEntitiesAsync`, `BulkRtAssociationsAsync`): checked for non-system sessions.
- RT import, user path (`ImportAsCallerAsync`): preflight over the whole file (YAML list or the first pass over the
  JSON bulks) before the first write; one `ExchangeException` with message number 144 lists every offender.

## Compatibility

- **Additive.** Models without the key compile byte-identically and keep their `minEngineVersion`; types without the
  block behave exactly as before.
- **minEngineVersion:** a model that declares the block on any type carries
  `CkEngineVersion.WriteAccessMinEngineVersion` (`3.5.71`), so an older engine refuses the model with message 126
  instead of ignoring the restriction (see `ck-engine-version.md`).
- **SemVer row T8:** adding, removing or changing a block is Minor + `requiresAcknowledge`, marked behavioural
  (`ck-semver-rules.md`).
- **Schema:** the `writeAccess` key reaches model authors only through the schema publish path (CLAUDE.md, "JSON
  schema publish path"); a catalog model must not use it before the schema release.
- **Persistence:** the block is stored on the tenant's CK type document (engine-mongodb, `CkType.WriteAccess`,
  reflection round-trip gate); without it a tenant would not enforce the restriction.

## Tests

`CkTypeWriteAccessResolverTests` (143, 90), `CkV2CompileTests.WriteAccess_*` (compile, YAML round trip, graph,
docs, minEngineVersion, invalid blocks), `CkEngineVersionTests` (`*WriteAccess*`), `PublicSurfaceRowTests.T8_*`,
`TypeWriteAccessGuardTests` (principal kinds, channels, Unknown, inheritance, associations, message),
`TypeWriteAccessRepositoryTests` (write funnel, system session, bulk insert, atomic rejection),
`ImportRtModelCommandTypeWriteAccessTests` (caller path YAML/JSON, associations, system path, unrestricted tenant).
