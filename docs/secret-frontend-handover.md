# SECRET attribute value type — frontend handover (AB#5542, AB#5544)

> From: PO AB#5528 (SECRET value type) · To: Studio / frontend-libraries PO via Chief Product Owner
> Concept: [concept-secret-attribute-type.md](concept-secret-attribute-type.md) · Progress: [secret-rollout-progress.md](secret-rollout-progress.md)
> State: backend implemented on local branches `feat/gerald/secret-attribute-type` (not pushed, not deployed), including the admin API of sections 6–12 (decisions 2026-10-06). Field names are binding.

## 1. Why the frontend must move first (rollout gate)

Phase 2 (Studio documents secret-safe) must ship **before** phase 3 (models switch credential attributes to `valueType: Secret`) in every environment. After phase 3, a document that selects a secret field as a scalar fails GraphQL validation (intended: loud failure instead of a leak). Affected today: the ~36 Studio configuration documents, `getEntitiesByCkType`, `getRuntimeEntityById`, `updateRuntimeEntities` selections, identity-provider form, entity forms (AB#5522).

Attributes that become Secret in phase 3 (System.Communication 3.40): `Password` (Sap, Sftp, EMailSender, EMailReceiver, Loxone, HelmRepository, FinApi), `PrivateKey`, `PrivateKeyPassphrase` (Sftp), `ClientSecret` (FinApi, MicrosoftGraph, ServiceAccount), `ApiKey` (Ai, WeClapp), `BotToken` (Discord), `AdminPassword` (Grafana), new Secret record member `ValueOverride.SecretValue` (record key `Path`; `ValueOverride.Value` stays String, see §5). System.Identity 2.22.0: identity-provider `ClientSecret`. Later (WP11): AI tokens, app models.

## 2. Asset repository GraphQL contract

### Types
```graphql
type OctoSecretState {
  isSet: Boolean!        # false when not set OR when the stored value cannot be read (key missing / corrupt)
  keyMissing: Boolean!   # true: a value is stored, but its key id is not in this environment's key ring (e.g. after a restore from another environment) -> re-entry needed
  setAt: DateTime        # when the current value was set (UTC); null for values set before this feature (legacy) or not set
}

# Typed entity field:
#   required secret:  password: OctoSecretState!
#   optional secret:  apiKey: OctoSecretState      (nullable in the schema, but always returns an object { isSet: false } when not set)
# Record member (records with secrets always declare a record key):
#   endpoints { key label token { isSet } }

type RtEntityAttribute {
  attributeName: String
  value: SimpleScalar      # always null for a Secret attribute
  secretIsSet: Boolean       # null for every non-secret attribute; true/false for secrets
  secretKeyMissing: Boolean  # null for non-secret attributes; true when stored but unreadable (unknown key id)
  secretSetAt: DateTime      # null for non-secret attributes / legacy / not set
}
```
The CK model API reports the value type as `SECRET` (`AttributeValueTypesDto.Secret = 17`).

### Inputs
- Typed create/update inputs: the secret field stays `String` (`password: String`; record members `token: String`).
- Generic `RtEntityAttributeInput { attributeName, value, secretIsSet }` — `secretIsSet` is accepted and ignored, so echoing back what was read is safe.
- Both `<Type>InputUpdate` and `RtEntityUpdate` have `clearSecretAttributes: [String!]` (camelCase attribute names).
- Semantics of a secret input value:

| Input | Effect |
|---|---|
| non-empty string | sets / rotates the secret (server encrypts) |
| omitted, `null`, `""` | unchanged |
| echoed read object (`{ isSet, keyMissing, setAt }` or any subset) / `secretIsSet` | unchanged |
| name in `clearSecretAttributes` | cleared (error if the secret is required) |
| inside records: member omitted / `null` / `""` | carried over from the stored element with the same record key |

- Create requires a value for required secrets. A stored but unreadable (key missing) secret counts as present for "required" checks.
- Placeholders have **no** meaning: `<…>` or `TODO_SET_<NAME>` sent as input is an ordinary value and is encrypted like any other (decision 2026-10-06).
- A new value and an entry in `clearSecretAttributes` for the same field in one request is rejected (message 23) — the UI stages "clear" and sends it on Save (Q8).

### Queries
- Filters on a secret: only `IS_NULL` / `IS_NOT_NULL` ("is set" = field exists and is not null).
- Sort, attribute search, aggregation, group-by, query columns (incl. persistent-query row cells) on a secret are refused.

### Error codes
| Code | When | Details |
|---|---|---|
| `SecretAttributeNotQueryable` | filter other than IS_NULL/IS_NOT_NULL, sort, search, aggregation, group-by, query column, query-row cell on a secret | `extensions.attributePath`, `extensions.operation` |
| `ASSET1004` (validation) | required secret missing on create (msg 2), clearing a non-secret/unknown name (21), clearing a required secret (22), set and cleared together (23), required secret missing after record carry-over (24), non-string input | message numbers in `OctoDetails` as `"NN: text"`, never values |
| `SecretEncryptionNotConfigured` | the server has no key ring | — |

### Example operations
```graphql
# Typed query of a configuration entity with secrets (select { isSet }, never the scalar)
query { runtime { systemCommunicationEMailSenderConfiguration(rtId: "…") {
  items { rtId name userName password { isSet } } } } }

# Generic projection
query { runtime { runtimeEntities(ckId: "System.Communication/EMailSenderConfiguration", rtId: "…") {
  items { attributes { items { attributeName value secretIsSet } } } } } }
# -> { "attributeName": "password", "value": null, "secretIsSet": true }

# Set / rotate (other secrets untouched); the mutation response only exposes { isSet }
mutation { runtime { systemCommunicationEMailSenderConfigurations {
  update(entities: [{ rtId: "…", item: { password: "<entered value>" } }]) { rtId password { isSet } } } } }

# Clear explicitly
mutation { runtime { systemCommunicationEMailSenderConfigurations {
  update(entities: [{ rtId: "…", item: {}, clearSecretAttributes: ["password"] }]) { rtId password { isSet } } } } }

# Count entities with a configured secret
query { runtime { systemCommunicationEMailSenderConfiguration(
  fieldFilter: [{ attributePath: "password", operator: IS_NOT_NULL }]) { totalCount } } }
```
(Typed field/type names follow the existing codegen naming; the integration tests use `AssetRepositoryIntegrationTest/ServiceCredential`.)

## 3. UI behaviour (AB#5542, AB#5544)

- **Write-only fields**: show a "set" / "not set" badge from `{ isSet }` / `secretIsSet`; the input is empty with hint "leave empty to keep"; only send the field when the user typed a value; offer an explicit **Clear** action for optional secrets (→ `clearSecretAttributes`); required secrets cannot be cleared.
- **Mutations** return only `rtId` and `{ isSet }`.
- **Property grid / runtime browser**: SECRET shows the badge, never a value. Generic attribute lists: use `secretIsSet`.
- **Entity forms (AB#5522)**: map `SECRET` automatically to the write-only field; drop the credential-name heuristic once models use SECRET.
- **CK attribute editor**: offer `SECRET`; explain rules: no default values / auto-complete / auto-increment, not indexable, not in display rules/formulas/owner paths/query columns, ownership always Secret, records with secrets need a `recordKey` (required String/Int/Int64/Enum, not Secret), model must depend on `System >= 2.5`.
- **MeshmakersAccounting app** (`meshmakers-app`): its seed must drop the `TODO_SET_*` placeholders (empty values), and `graph-credentials.service.ts` / `imap-connection.service.ts` must switch from string comparison to `isSet` (see `secret-app-model-changes.md`).

## 4. Identity providers REST (`{tenantId}/v1/identityProviders`)

DTOs `GoogleIdentityProviderDto`, `MicrosoftIdentityProviderDto`, `FacebookIdentityProviderDto`, `AzureEntraIdProviderDto`:
- `clientSecret` is always `null` in GET list/by id and POST/PUT responses (write-only). The PUT response is built from the stored entity (no echo).
- New `clientSecretIsSet: boolean` (omitted when null) — SDK field added (octo-sdk f6bee03); identity fills it after the next build.
- POST requires a non-empty `clientSecret`. PUT: non-empty rotates; `null` / `""` / omitted keep. No clear (secret is required).
- A placeholder-looking value is an ordinary secret (no special case).
- A stored secret whose key id is unknown reads as `clientSecretIsSet: false` (provider skipped with an error log); `clientSecretKeyMissing: boolean` is added next to it (contract §8).
- frontend-libraries `identityProviderDto.ts` (`clientSecret?: string`) → write-only + `clientSecretIsSet?: boolean`. The Studio form already only sends the secret when entered.

## 5. Data-flow editor: `RevealSecret@1`

Privileged node that decrypts one secret attribute of an entity into the DataContext (counted, never logged; revealed values are masked as `***` in debug snapshots, dry-run output and execution results). Reading entities with `GetRtEntities*` yields only the marker `{ "isSet": true|false }` for secrets.

| Field | Type | Required / default |
|---|---|---|
| `type` | `"RevealSecret@1"` | required |
| `attributeName` | string, case-insensitive; dotted path only through single `Record`s (record arrays refused) | required |
| `ckTypeId` | RtCkId (Studio hint `ckTypeSelector`) | one of `ckTypeId` / `ckTypeIdPath` |
| `ckTypeIdPath` | JSONPath | |
| `rtId` | 24-hex OctoObjectId; wins over `rtIdPath` | one of `rtId` / `rtIdPath` |
| `rtIdPath` | JSONPath | |
| `targetPath` | JSONPath | default `"$"` |
| `targetValueWriteMode` | enum | default `Overwrite` |
| `targetValueKind` | enum | default `Simple` |
| `documentMode` | enum | default `Extend` |
| `identity` | `Caller` \| `ServiceAccount` (`System` is refused by the node) | default `Caller` |
| `description` | string | optional |

Schema groups: Entity (ckTypeId, ckTypeIdPath, rtId, rtIdPath), Secret (attributeName), Paths, Write Mode, Execution (identity), General (description). The node is in the generated `pipeline-schema.json` of the mesh adapter.

```yaml
- type: RevealSecret@1
  ckTypeId: System.Communication/EMailSenderConfiguration
  rtIdPath: $.config.rtId
  attributeName: Password
  targetPath: $.smtp.password
  identity: ServiceAccount
```
Palette: entity + secret-attribute picker (attributes with value type SECRET only), no value preview; debug views show the marker / `***`. Type-switch nodes (`ConvertDataType`, `SetPrimitiveValue`, `If`, `Switch`, `ExecuteCSharp`, `DataMapping`) fail with "Secret not supported" on secret values; reading `<path>.isSet` as Boolean works.

### Helm value overrides (System.Communication 3.40)
`ValueOverride.Value` stays a String (non-secret overrides such as image tags stay readable). Secret overrides go to the new Secret record member **`SecretValue`** (record key `Path`, must be unique among overrides carrying a `SecretValue`). Studio writes plaintext into `SecretValue` and must stop calling the controller's `encrypt-value` endpoint for new values; legacy `IsSecret` + `enc:v1` in `Value` still deploys but is deprecated. `encrypt-value` returns `enc:v1` and refuses `enc:v2` input.

## 6. Roles (Q3, Q15)

| Role | Grants |
|---|---|
| any user with read access to an entity | set/not-set badge (`isSet`, `keyMissing`, `setAt`) — set/clear actions hidden without write access (Q15) |
| write access to the entity | set / rotate / clear a secret (typed or generic mutation) |
| `AdminPanelManagement` (existing) | secrets overview, re-entry tasks, encryption status, sweep run list (§7, §9) |
| **`SecretManagement`** (new, seeded by `System.Identity.Bootstrap`, constant `CommonConstants.SecretManagementRole`) | trigger sweeps, delete a pre-sweep dump early (§9) |

Missing role: GraphQL error code `Forbidden` (asset repo); REST `403` (bot). Both roles are tenant roles; the initial tenant administrator gets `SecretManagement` like `AdminPanelManagement`. In existing tenants identity grants `SecretManagement` once, automatically and additively, on its first start with `System.Identity.Bootstrap` 1.4.0 to `TenantOwners` and to every user or group holding both `AdminPanelManagement` and `TenantManagement`; anyone else (e.g. `AdminPanelManagement` only) needs an explicit grant.

## 7. Secrets overview — asset repository GraphQL (Q1, Q2, Q5, Q16)

Lives in the **asset repository** (tenant GraphQL endpoint), because every secret is a runtime-entity attribute of the tenant database — this includes identity-provider client secrets (`System.Identity/*IdentityProvider`) and AI tokens/credentials (`System.Ai/*`), so one inventory covers Q16. No child-tenant aggregation: query each child tenant's endpoint (Studio links per child tenant).

```graphql
type Query { secrets: SecretsQuery }          # requires AdminPanelManagement

type SecretsQuery {
  inventory(
    first: Int = 50, after: String,
    ckTypeId: String,                 # exact CK type (derived types included)
    forms: [SecretStorageForm!],      # filter by storage form
    needsReEntry: Boolean,            # only re-entry tasks
    search: String                    # matches rtId, ckTypeId (full or short type name), rtWellKnownName, display name, attribute path (never values)
  ): SecretInventoryConnection!
  summary: SecretInventorySummary!
  usages(ckTypeId: String!, rtId: OctoObjectId!, attributePath: String!): [SecretUsage!]!
}

enum SecretStorageForm {
  NOT_SET        # null / missing
  PLAINTEXT      # legacy clear text still stored (before the Encrypt sweep)
  ENC_V1         # legacy enc:v1 (instance key)
  ENC_V2         # protected, key id known
  KEY_MISSING    # protected, key id not in this environment's key ring
  CORRUPT        # stored value cannot be parsed (reads as not set, warning logged)
}

type SecretInventoryConnection { totalCount: Int!, pageInfo: PageInfo!, items: [SecretInventoryItem!]! }

type SecretInventoryItem {
  ckTypeId: String!
  rtId: OctoObjectId!
  rtWellKnownName: String
  displayName: String            # stored display name → string `Name` attribute → rtWellKnownName; null = show rtId
  attributePath: String!         # camelCase; record members as "endpoints[key=prod].token" / "credentials.token"
  attributeName: String!         # CK attribute name (PascalCase) of the top-level attribute
  required: Boolean!
  form: SecretStorageForm!
  keyId: String                  # ENC_V2 / KEY_MISSING only
  setAt: DateTime                # Q2; null for legacy / not set
  needsReEntry: Boolean!         # KEY_MISSING or CORRUPT, or NOT_SET and required
  usedBy: [SecretUsage!]!        # Q5
}

type SecretUsage {
  dataFlowRtId: OctoObjectId
  dataFlowName: String
  pipelineRtId: OctoObjectId!
  pipelineName: String
  nodePath: String!              # node position in the pipeline definition
  match: SecretUsageMatch!
}
enum SecretUsageMatch { EXACT, BY_TYPE }   # EXACT: RevealSecret@1 with ckTypeId + rtId + attributeName; BY_TYPE: rtIdPath (dynamic) on the same ckTypeId + attributeName

type SecretInventorySummary {
  total: Int!, notSet: Int!, plaintext: Int!, encV1: Int!, encV2: Int!, keyMissing: Int!, corrupt: Int!, needsReEntry: Int!
  encV2ByKeyId: [KeyIdCount!]!
}
type KeyIdCount { keyId: String!, count: Int! }
```

Listing rule: inside records, an optional Secret member that is not set is omitted (nothing to re-enter, e.g. a Helm value override that is a plain value); set, key-missing, corrupt and required-but-missing members are listed, while top-level optional secrets that are not set stay listed (entity-level settings); `summary` counts follow the same rule.
Search also matches the CK type, case-insensitively: the full `ckTypeId` (e.g. `System.Communication/Application`) or the short type name after the slash (`Application`).
`displayName` falls back consistently: stored display name → the type's string `Name` attribute → `rtWellKnownName` → null (the UI then shows the rtId).

Re-entry tasks (Q5) are **live**: `inventory(needsReEntry: true)`. A task is done when a new value is set (→ `ENC_V2`) or the secret is explicitly cleared via `clearSecretAttributes` ("Not needed", optional secrets only) or removed by the `CleanupUnreadable` sweep. Works the same in a child tenant after a child-tenant restore. Values are never returned.

## 8. Identity-provider REST additions

`GoogleIdentityProviderDto`, `MicrosoftIdentityProviderDto`, `FacebookIdentityProviderDto`, `AzureEntraIdProviderDto`: `clientSecretIsSet: boolean`, **`clientSecretKeyMissing: boolean`**, **`clientSecretSetAt: string (ISO-8601) | null`** (all omitted when null). The overview in §7 lists these secrets too (as `System.Identity/*` entities).

## 9. Encryption status, sweeps and dumps — bot services REST (Q3, Q4, Q6, Q7, Q17)

Enums as names, JSON camelCase. SDK: `IBotServicesClient` (methods listed per endpoint).

| Endpoint | Role | Response | SDK |
|---|---|---|---|
| `GET {tenantId}/v1/secrets/status` | any user with tenant access (Q4/Q17: editors need it to disable secret inputs) | `SecretEnvironmentStatusDto` | `GetSecretEnvironmentStatusAsync` |
| `POST {tenantId}/v1/jobs/secret-sweep?mode=Verify\|Encrypt\|CleanupUnreadable&confirm=true` | `SecretManagement` | `JobResponseDto { id }`; `400 ConfirmationRequired` for Encrypt/CleanupUnreadable without `confirm=true`; `400` for Decrypt; Reprotect accepted (CLI/ops only, not offered in Studio) | `StartSecretSweepAsync(tenantId, mode, confirm)` |
| `GET {tenantId}/v1/jobs/secret-sweep/report` | `AdminPanelManagement` | last `SecretSweepReport`; `404` if none | `GetSecretSweepReportAsync` |
| `GET {tenantId}/v1/secrets/sweep-runs?limit=20` | `AdminPanelManagement` | `SecretSweepRunDto[]`, newest first (last 50 kept per tenant) | `GetSecretSweepRunsAsync` |
| `DELETE {tenantId}/v1/secrets/sweep-runs/{runId}/dump` | `SecretManagement` | `204`; `404` unknown run / no dump; `409` dump already deleted | `DeleteSecretSweepDumpAsync` |
| `POST system/v1/secrets/sweep?mode=…&confirm=true`, `GET system/v1/secrets/reports` | system tenant admins | ops only; `confirm=true` required for CleanupUnreadable | `StartSecretSweepAllTenantsAsync(mode, confirm)` |

```jsonc
// SecretEnvironmentStatusDto (environment-level, identical in every tenant)
{
  "keyRingConfigured": true,          // false: secret inputs disabled with a hint, writes would fail with SecretEncryptionNotConfigured
  "activeKeyId": "k1",                // null when not configured
  "knownKeyIds": ["k1"],
  "legacyV1KeyConfigured": true,
  "strictMode": false,
  "strictModeSince": null,            // ISO-8601 when strict mode is scheduled/active
  "recurringVerifyCron": "0 3 * * *", // daily Verify (Q6); null when disabled
  "lastVerifyAt": "2026-10-06T03:00:12Z", // this tenant's last Verify run, null if none
  "warnings": []                      // warning codes (AB#5534): "NoKeyRing" when keyRingConfigured=false,
                                      // "NoLegacyV1Key" when the tenant's last completed sweep found enc:v1 values but no legacy key is configured;
                                      // unknown codes: show as a generic warning
}

// SecretSweepRunDto
{
  "runId": "<hangfire job id>",
  "mode": "Encrypt",                  // Verify | Encrypt | Reprotect | CleanupUnreadable
  "trigger": "Manual",                // Manual | Recurring | Restore
  "outcome": "Succeeded",             // Succeeded | CompletedWithFailures | Skipped | Failed | Running
  "startedAt": "…", "completedAt": "…",
  "triggeredBy": "user name or null",
  "totals": { /* BEFORE the run - forms as found: notSet, plaintext, encV1, encV2, encV2ByKeyId, unknownKeyId, failed, total, legacy */ },
  "totalsAfter": { /* AFTER the run (follow-up Verify; = totals for a Verify run); null while running / skipped / failed before it */ },
  "valuesRewritten": 12,              // values written by this run (encrypted, re-protected, placeholders normalised, cleared)
  "encryptedCount": 8,                // plaintext / enc:v1 -> enc:v2 in this run (part of valuesRewritten)
  "skippedConcurrentlyModified": 0,   // > 0 makes a writing run CompletedWithFailures ("run the sweep again")
  "placeholdersNormalized": 0,        // legacy plaintext placeholders converted once to not set (migration only)
  "skippedLegacyV1KeyMissing": 0,     // enc:v1 values kept by CleanupUnreadable because only the legacy key is missing
  "reason": null,                     // e.g. "Interrupted (service restart)" for runs that never finished, or the no-key-ring hint
  "unreadableCount": 0,
  "dump": {                           // null for Verify (no dump)
    "fileName": "…presweep.tar.gz",
    "exists": true,
    "sizeBytes": 123456,
    "createdAt": "…",
    "expiresAt": "…",                 // createdAt + 7 days
    "deletedAt": null,                // set when deleted early or expired
    "deletedBy": null
  }
}
```
**No key ring (AB#5534/AB#5539):** when `keyRingConfigured` is `false` (equivalently `warnings` contains `"NoKeyRing"`), Studio shows a **prominent banner** on the secrets overview and on every page with secret inputs ("No key ring configured on this environment — secrets cannot be saved; restored secrets were only classified. Configure the key ring, then run Encrypt."). Secret inputs stay disabled; Encrypt / CleanupUnreadable are not offered (they would be skipped). `NoLegacyV1Key` gets a smaller warning on the secrets overview ("enc:v1 values found but no legacy key configured — they count as key missing").

A restore on a bot **without key ring** still produces the re-entry list: the post-restore run is a key-free Verify only (run `mode: "Verify"`, `trigger: "Restore"`, `outcome: "Succeeded"`, reason "No key ring configured: secrets were classified only; set the key ring and run Encrypt"). Its report lists every value it cannot read without keys in `unreadable[]` / `secretsToReEnter` (all `enc:v2` — key id not in the empty ring — and all `enc:v1` with `keyId: "enc:v1"`); nothing is written. Studio shows these as re-entry tasks like any other unreadable value.

- **Before / after (AB#5532/5533/5539, live defect 2026-10-06):** `totals` of a run are the forms as FOUND, `totalsAfter` the state after it - the "Result" column shows `totalsAfter ?? totals`, plus `encryptedCount` ("8 encrypted"). An Encrypt that converted nothing used to say "Succeeded" with identical counts: the MongoDB rewrite looked for derived-type entities (all `System.Communication/*Configuration`) in a non-existent per-type collection and every value was "modified concurrently"; fixed, and such skips now yield `CompletedWithFailures` with `skippedConcurrentlyModified` > 0. Each report step also carries `encryptedCount`.

Runs left `Running` by a crash are marked `Failed` with reason "Interrupted (service restart)" shortly after the bot starts. The report and each step also carry `skippedLegacyV1KeyMissing`. MCP offers the same inventory read-only via `get_secret_inventory` (risk low). Dumps are never downloadable (no endpoint). The report (`SecretSweepReport`) gains `unreadable[] { ckTypeId, rtId, attributePath, keyId }` (re-entry list, decision 2026-10-06) and `placeholdersNormalized`; `cleared[]` is only filled by `CleanupUnreadable`. Sweep modes in Studio (Q6): **Verify** (no confirmation), **Encrypt** and **CleanupUnreadable** (confirmation dialog; a pre-sweep dump is taken first; CleanupUnreadable removes values whose key id is unknown — irreversible except via the dump; `enc:v1` values that are unreadable only because no legacy key is configured are kept and stay in `unreadable[]`).

## 10. Restore / unknown key id (decision 2026-10-06)

Restoring a dump from another environment (or a child tenant) keeps secrets **encrypted**: they show `isSet: false`, `keyMissing: true`, form `KEY_MISSING`, and appear as re-entry tasks. Adding the source key id to the environment's key ring makes them readable again automatically (ops step; then `Reprotect` via CLI moves them to the active key). They are removed only by re-entry or the `CleanupUnreadable` sweep. No API decrypts or exports plaintext.

On a bot without key ring the restore runs a key-free Verify only and still lists the re-entry tasks (§9); nothing is encrypted until the key ring is configured and Encrypt runs. A legacy `enc:v1` value on a host without `LegacyV1Key` is treated the same way everywhere: `isSet: false`, `keyMissing: true`, form `KEY_MISSING`, `keyId: "enc:v1"` (not a real key id — show it as "legacy key (enc:v1)"); it becomes readable once the legacy key is configured. `CleanupUnreadable` does **not** remove such values (missing legacy key = configuration gap, not key loss): they stay in `unreadable[]` and are counted separately (engine `SecretSweepResult.SkippedLegacyV1KeyMissing`).

## 11. Debug snapshots (Q12)

`DebugPointDataDto` (pipeline debugger, delivered to Studio) gains `redactedPaths: string[]` — JSONPaths rooted at the snapshot object, e.g. `"$.output.smtp.password"`, `"$.input.config.clientSecret"`. The value at such a path stays `"***"`; Studio marks exactly these paths (no heuristics). Values masked inside longer strings (e.g. `"Bearer ***"`) are listed with the path of the containing string.

## 12. Error codes summary (new)

| Code | Where | When |
|---|---|---|
| `Forbidden` | asset repo GraphQL | `secrets { … }` without `AdminPanelManagement` |
| `SecretEncryptionNotConfigured` | asset repo GraphQL | writing a secret without key ring (Studio should already have disabled the input from `keyRingConfigured=false`) |
| `ConfirmationRequired` | bot REST 400 | Encrypt / CleanupUnreadable without `confirm=true` (code in the error body's `statusDescription`) |
| `403` | bot REST | missing `SecretManagement` / `AdminPanelManagement` |

## 13. Implementation notes (2026-10-06)

- The asset repo's existing blueprint mutations report a missing role as `FORBIDDEN`; the new `secrets` query uses `Forbidden` as specified here — match case-insensitively.
- Bot `{tenantId}/v1/secrets/...` routes (status, sweep-runs, dump delete) accept only the token's own tenant; a parent-tenant administrator switches into the child tenant (Studio links per child tenant, Q16). The sweep trigger and report routes on `{tenantId}/v1/jobs/...` also accept parent-tenant administrators.
- `setAt` is recorded from now on (new or rotated values); values set earlier and values converted by the Encrypt sweep show `setAt: null`.
- An inventory entry is only listed for record members whose record element exists (an unset optional record contributes no slot).
- Deleted (archived) entities are never listed in `inventory` / `summary` (same as `runtimeEntities` and typed queries) and never appear in a sweep report's `unreadable[]` / `cleared[]` / `secretsToReEnter`; the sweeps still process their stored values (concept §6, AB#5532/AB#5544).
- `usedBy`/`usages` scan the tenant's pipeline definitions once per request, only when selected; they match the exact CK type of the RevealSecret@1 node.
- Sweep report paths (`unreadable[]`, `cleared[]`, `failures[]`) use the same camelCase notation as `inventory.attributePath`, so report rows link to inventory rows.
- Sweep `totals` additionally carry `placeholder` and `unknownKeyIdByKeyId`.
- A restore run appears in `sweep-runs` with `trigger: Restore`, mode `Encrypt` and `dump: null` (the uploaded backup is the pre-state).
- `redactedPaths` contains only `$.input…` / `$.output…` paths (dry-run intents are not delivered to Studio).
- Helm overrides: an entry with a `SecretValue` is treated as secret even without `IsSecret`; Studio does not need to set the flag.
- Pipelines see `{ "isSet": false, "keyMissing": true }` for a stored value with an unknown key id; echoing any marker back means "unchanged".
- Without a key ring on the host every protected value reads as key missing (`keyRingConfigured: false` in the status explains it).

## 14. Pre-sweep dump restore and key-id retention (AB#5559, 2026-10-06)

Bot (local branch, contract binding):

| Endpoint | Role | Response |
|---|---|---|
| `POST {tenantId}/v1/secrets/sweep-runs/{runId}/restore-dump?confirm=true` | `SecretManagement`, own tenant only | `JobResponseDto { id }`; `400 ConfirmationRequired`; `404` unknown run / no dump / no longer stored; `409 DumpDeleted`; `409 DumpKeyMissing` |

- Pre-sweep dumps are stored encrypted (`.octoenc`, key ring) in the artifact store; they are never downloadable. A restore decrypts inside the bot, restores the tenant database and runs Verify afterwards.
- **UI warning (mandatory):** restoring a dump taken before the first Encrypt sweep brings plaintext secrets back — the dialog must say so and offer "Run Encrypt afterwards". Confirmation dialog + `SecretManagement` role, like CleanupUnreadable.
- `GET {tenantId}/v1/secrets/status` gains `requiredKeyIds: string[]` (key ids still needed to read existing encrypted dumps) and the warning code `DumpKeyMissing` (an existing dump's key id is not in the ring → that dump cannot be restored). Show it in the encryption-status card next to `NoKeyRing`.
- Sweep-run rows: offer "Restore dump" only when `dump.exists` is true and `dump.deletedAt` is null.
- Tenant dump downloads keep their existing contract (clients still receive the plain `.octobak`/tar.gz); encrypted storage is transparent. Encrypted downloads no longer support HTTP range requests (no resume).
- SDK: `IBotServicesClient.RestoreSecretSweepDumpAsync(tenantId, runId, confirm)`; MCP `restore_secret_sweep_dump` (high risk); octo-cli `RestoreSecretSweepDump`.
