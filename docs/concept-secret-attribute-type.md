# SECRET attribute value type — concept

> Status: Approved for implementation · 2026-10-05 · Owner: Gerald Lochner
> Work item: AB#5528 (Bug, Epic 4969). Related: AB#5529 (credentials committed in Git), AB#5371, AB#5522 (entity forms). AB#5527 closed as duplicate.
> Sources: two planning passes (engine/storage/API and existing secrets/migration), verified against the code on 2026-10-05.

---

## 1. Problem

Credential attributes of `System/Configuration` subtypes and several app models (passwords, client secrets, API keys, bot tokens, private keys, refresh tokens) are plain `String` attributes:

- The asset repository's GraphQL returns them in clear text. The generic projection `runtimeEntities { attributes { attributeName value } }` returns **every** attribute when `attributeNames` is omitted; typed fields such as `SystemCommunicationEMailReceiverConfiguration.password: String!` are readable; `updateRuntimeEntities` echoes them back.
- Frontend documents (`getEntitiesByCkType`, `getRuntimeEntityById`, 36 Studio configuration documents), MCP generic CRUD tools and octo-cli read them. Anyone with read access on a configuration entity reads its credentials; AI agents using MCP may have them in transcripts.
- `isRuntimeState` only protects values from blueprint re-apply. `AttributeOwnershipDto.Secret` (AB#5187) exists — "keep on re-apply, leave out of `ExportRt`" — but no production model uses it.
- Real credentials were committed in seed data and published in a public catalog (AB#5529).

What already exists and is reused: `InstanceSecretCrypto` (octo-sdk, AES-256-GCM, prefix `enc:v1:`, no key id) with key `instance_secret_key` per cluster in Vault, already used for Helm repository passwords, `ValueOverride.Value` (`IsSecret`) and AI tokens. Its `Decrypt` already passes plaintext through.

## 2. Decisions (product owner, 2026-10-05)

| # | Topic | Decision |
|---|---|---|
| 1 | Approach | Dedicated CK `AttributeValueType` **`SECRET`**, enforced by the engine (not a marker flag) |
| 2 | Versioning | **String → SECRET counts as Minor** (new rule in `CkSemVerClassifier`): stored data stays readable because readers accept legacy plaintext during the transition. System.Communication goes to 3.40, not 4.0 |
| 3 | Key | **Reuse the existing `instance_secret_key`** as the first active key of the new key ring (key id `k1`). The new format carries a key id, which makes later rotation possible despite the old "never rotate" note |
| 4 | Records | **Secret sub-attributes inside records are supported in v1** (see §4.6) |
| 5 | Cross-environment restore / tenant copy | Secrets with an unknown key id become **"not set"**; the restore job reports which secrets need re-entry |
| 6 | Pipelines reading secrets from the database | New privileged **`RevealSecret@1` node** in the mesh adapter (in-process decrypt, counted) |
| 7 | Adapter service accounts | Migrate to **impersonation** (AB#5114): clear the stored secret, nothing remains stored |
| 8 | Scope | Identity-provider `ClientSecret`s are **in scope**; identity Data Protection keys are a **separate work item** |
| 9 | Blueprint seeds | Seeds contain **placeholders only**; secrets are set after installation (Studio, CLI, rotate endpoints). A lint enforces it |
| 10 | Deadlines | Strict mode per environment **14 days** after the sweep reports zero plaintext; pre-sweep plaintext backups kept **7 days**, treated as secrets, then deleted |
| 11 | AB#5529 public repository | **Rotate and replace**, no history rewrite |
| 12 | Older clients | **Minimum version plus gate**: models using SECRET require `System >= 2.5`, so old engines fail with a clear dependency error |

## 3. Data model and engine

### 3.1 Value type
- `AttributeValueTypesDto.Secret = 17` (`ConstructionKit.Contracts/DataTransferObjects/AttributeValueTypesDto.cs`), `"Secret"` in `construction-kit-elements-attribute.schema.json` (enum), GraphQL `SECRET`. Rebuild the SchemaProvider image.
- `SystemCkModel` → **System-2.5.0** in the same release; the compiler requires models using SECRET to depend on `System >= 2.5` (decision 12).

### 3.2 Compiler rules (next to `InheritanceResolver.cs`, new `MessageCodes`)
A SECRET attribute may not have `defaultValues`, `autoCompleteValues` or `autoIncrementReference`; may not be indexed; may not be referenced by display rules, formulas, owner-attribute paths or query columns; its effective ownership is always `Secret` (an override is a compile error). It may be a type attribute or a record sub-attribute (decision 4), not an association role.

### 3.3 In-memory type
`RtSecretValue` (namespace `Runtime.Contracts.RepositoryEntities`) with states `Pending(plaintext)` (write paths only), `Protected(envelope)` (reads) and `LegacyPlaintext` (a String found in a Secret slot during migration). `ToString()` returns `***`. The BSON serializer refuses to write `Pending`. `GetAttributeStringValue` on a Secret attribute throws; the new accessor is `GetAttributeSecretValueOrDefault`. The engine and SDK source generators emit `RtSecretValue?` (engine) / `OctoSecretStateDto` (SDK query) / `string?` (SDK mutation), so consumers that read `.Password` as a string stop compiling instead of silently receiving ciphertext.

### 3.4 Storage and envelope
- BSON: `attributes.<name>: { _t: "OctoSecret", e: "<envelope>" }`. A string in a Secret slot is legacy (plaintext or `enc:v1:`), a sub-document is protected — the raw query `{"attributes.x": {$type: "string"}}` finds what still needs migrating. Public input is always a string and always encrypted, so ciphertext cannot be copied onto another entity.
- Envelope: `enc:v2:<kid>:<base64url(nonce[12] ‖ tag[16] ‖ ciphertext)>`, AES-256-GCM, random nonce, AAD = ASCII header `enc:v2:<kid>:`. **No tenant id or rtId in the AAD** — restores into a renamed database, tenant copies and child-tenant restore (AB#5227) must keep working.
- `enc:v1:` values (no key id, no AAD) are decrypted with the legacy key = the same `instance_secret_key` (decision 3).

### 3.5 Key ring
- Configuration `SecretEncryption:Keys:{kid}` (base64, 32 bytes), `SecretEncryption:ActiveKeyId`, `SecretEncryption:LegacyV1Key`, bound in `AddRuntimeEngine()` so every engine host gets it: asset repo, communication controller, mesh adapter, bot, identity, platform, report, AI, MCP.
- First key: `k1` = the existing `instance_secret_key` (decision 3); `LegacyV1Key` = the same value. Rotation later: add `k2`, switch `ActiveKeyId`, run the re-protect sweep, remove `k1` (and `LegacyV1Key` once no `enc:v1` remains).
- Delivery: Vault `secret/meshmakers/<cluster>/octomesh` → ADO → helm `secrets.secretEncryptionKeys` in the backend Secret → shared `octo-mesh.system-env` block; operator-deployed workloads via `ClusterSecretsOptions.SecretEncryptionKeys` (only when `receivesClusterSecrets`); local dev in `Start-Octo.psm1`; kind in `Install-OctoKubernetes`.
- Without keys a service starts and answers "is set"; writing or decrypting throws `SecretEncryptionNotConfiguredException`.
- The key must be backed up (Vault plus Keeper); provisioning refuses to overwrite it. Losing it means every secret must be re-entered (external credentials only — no business data is lost).
- ASP.NET Data Protection is **not** used: its key ring sits unencrypted in the system database next to the ciphertext (separate work item, decision 8).

### 3.6 Write rules (engine, unconditional)
In `Runtime.Engine/Repositories/BulkRtMutation.cs` (insert, replace, update-by-type), like the existing `BinaryLinked` handling:

| Incoming | Result |
|---|---|
| non-empty `string` | encrypted with the active key |
| `""` | removed from the write → stored value unchanged (on replace: carried over from the stored entity) |
| `RtSecretValue.Protected` | passed through (trusted internal callers: upsert preservation, restore, sweep) |
| `null` | cleared |
| placeholder `<…>` | stored as null ("not set") — matches AB#5114 semantics for service accounts |

Paths that bypass `BulkRtMutation` call the step explicitly: `RuntimeRepositoryBase.BulkInsertRtEntitiesAsync`, `InsertOneRtEntityForMigrationAsync`, `RewriteAttributeValueForMigrationAsync`, `CreateTransientRtEntity`.

**Implementation (AB#5532).** The step is `ISecretWriteNormalizer` / `SecretWriteNormalizer` (`Runtime.Engine.Secrets`, singleton, exposed as `IBulkRtMutation.SecretWriteNormalizer`). Concrete choices:
- An **omitted** secret on a replace is carried over like `""` (clearing is explicit, §4.3); `null` clears. A replace that leaves a required secret without a value is rejected before anything is deleted or written.
- `LegacyPlaintext` is re-encrypted when a key is configured (an `enc:v1` value is decrypted first); without keys it is kept as stored. Only new input (`Pending`, plain string) requires a key. A plain string is input on the API path and legacy on storage paths (`SecretValueOrigin`).
- Placeholders are `<…>` **and** `TODO_SET_<UPPER_SNAKE>` (`SecretAttributeConventions.IsPlaceholder`, shared by the seed lint and the write path).
- Clearing: `IEntityUpdateInfo.ClearSecretAttributes` (factories `EntityUpdateInfo.CreateUpdate/CreateReplace(id, entity, clearSecretAttributes)`); the rule engine validates it (messages 21–23) and turns each entry into an explicit `null`. Required secrets on create: `""`, placeholders and `null` count as missing (message 2); after carry-over: message 24.
- The CK migration writes are normalised by their engine caller (`CkModelMigrationService`), not inside the MongoDB overrides; `CreateTransientRtEntity` never sets a Secret value. The bulk import does not enforce required secrets (AB#4772 policy).
- Blueprint re-apply keeps stored secrets inside seed-owned records too (`ImportRtModelCommand.PreserveSecretRecordMembers`, by record key); the comparer never diffs Secret attributes or members and masks them in reported record changes.
- Engine serialisers write an `RtSecretValue` as `{"isSet":…}` only (type-level STJ/Newtonsoft converters, YAML converter); a marker or object sent back means "unchanged"; `ExportRt` omits Secret attributes by value type as well as by ownership.

### 3.7 Server-side decryption
`ISecretAttributeProtector` (contract in Runtime.Contracts, implementation in Runtime.Engine): `Protect`, `Unprotect` (handles `enc:v1`, `enc:v2`, legacy plaintext with a warning counter), `IsProtectedEnvelope` (strict), `TryParseEnvelope`, `NeedsReprotect`, `Reprotect`. Extension `rtEntity.GetSecretPlaintext(attr, protector)`. Every decrypt increments `octo.secrets.decrypt{tenant,ckType,attribute,service}`; logs never contain the value. No public decrypt endpoint in v1. Architecture tests in asset repo and MCP forbid `Unprotect` outside an allowlist.

## 4. API semantics (asset repository, SDK, MCP)

1. **Typed output field**: `password: OctoSecretState!` with `{ isSet: Boolean! }`. Old documents that select the field as a scalar fail validation loudly instead of leaking.
2. **Generic projection**: `value: null` plus `RtEntityAttribute.secretIsSet: Boolean` (null for non-secret attributes). Sending `null` back is safe.
3. **Input** stays `String`: omitted, `null` (in an update set) or `""` = unchanged. **Clearing** is explicit: `clearSecretAttributes: [String!]` on update inputs (typed and generic) and `MutationDto`. Clearing a required secret is an error; creating requires a value for required secrets.
4. **Filters**: only `IS_NULL` / `IS_NOT_NULL`. Sort, text search, aggregations, group-by are refused (`SecretAttributeNotQueryable`). Secret attributes are excluded from query columns, RtQuery, archive paths and CrateDB columns.
5. **Data permissions** stay type-level row filters: read shows `isSet`, writing a secret needs write permission. No permission grants decryption through the public API.
6. **Secrets in records (decision 4)**: a record (array) attribute is written as a whole, so "unchanged" must be resolved per element. Rule: when a record array is replaced, a secret sub-value that is `null`/`""` in an incoming element is carried over from the stored element **with the same record key** — a record type with secret sub-attributes must declare a key sub-attribute (new compiler rule: `recordKey`), otherwise by **position** for single records. Projection of a record element shows `{ isSet }` for the secret sub-field. This also brings `ValueOverride.Value` (`IsSecret`) and AI provider records under the SECRET type; their `enc:v1` values migrate like any other.

   **Record key — implementation (AB#5531).** The key is declared on the record definition, not on the attribute assignment, because it is a property of the record type that every record-array attribute using it shares:

   ```yaml
   records:
     - recordId: ValueOverride
       recordKey: Key            # names a sub-attribute of the record
       attributes:
         - id: ${this}/Key
           name: Key
         - id: ${this}/OverrideValue
           name: Value            # valueType: Secret
   ```

   - Schema: `recordKey` (string, PascalCase attribute name) in `construction-kit-elements-record.schema.json`; `CkRecordDto.RecordKey`; the effective key (own or inherited from the nearest base record) is `CkRecordGraph.RecordKey`.
   - Compiler: a record whose own or inherited attributes contain a Secret attribute must have an effective `recordKey` (message 76) — on the record type, regardless of whether it is used as `Record` or `RecordArray`, because a record type defined for single use can be reused in an array by another model. A declared key must name a required `String`, `Int`, `Int64` or `Enum` sub-attribute that is not Secret (message 77).
   - SemVer: setting, clearing or changing `recordKey` is Minor.
   - Single `Record` attributes still carry over by position (there is exactly one element); the key is used for `RecordArray`. The carry-over itself is WP2 (AB#5532).
   - **Implementation (AB#5532):** carry-over applies on update and replace, at any nesting depth; `null`, `""` or an omitted sub-value is carried over, a placeholder clears it (the only way to clear a secret inside a record). Keys compare by value (integers across CLR types, otherwise ordinal text). A record array without a key (pre-AB#5531 models) gets no carry-over.
   - WP3 (AB#5533): the MongoDB CK record document must round-trip `recordKey`, otherwise the runtime cache reads `null` (same failure class as AB#4589 for `isRuntimeState`).
7. **MCP**: entity CRUD tools inherit the server behaviour; tools that set secrets are classified high risk; `clearSecretAttributes` argument added. **octo-cli**: new admin command `SecretStatus` (wraps the verify sweep) and `ReprotectSecrets`.

## 5. Existing secrets — migration

### 5.1 Inventory (credential attributes to convert)

| Model | Attributes |
|---|---|
| System.Communication (`attributes/configuration.yaml`, `finApiConfiguration.yaml`, `grafanaConfiguration.yaml`, `helmDeployment.yaml`) | `Password` (Sap, Sftp, EMailSender, EMailReceiver, Loxone, HelmRepository, FinApi), `PrivateKey`, `PrivateKeyPassphrase` (Sftp), `ClientSecret` (FinApi, MicrosoftGraph, ServiceAccount), `ApiKey` (Ai, WeClapp), `BotToken` (Discord), `AdminPassword` (Grafana — cross-tenant), record `ValueOverride.Value` |
| Identity (`identity-attributes.yaml`) | identity-provider `ClientSecret` (Google, Microsoft, Facebook, AzureEntra) — currently seed-owned and exported |
| AI (`aiAttributes.yaml`) | `EncryptedValue`, `AccessToken`, `RefreshToken`, `TrustedDeviceToken` (already `enc:v1`) |
| Meshmakers.Accounting.Tesla | `ClientSecret`, `RefreshToken` (written back by a pipeline) |
| EnergyCommunity.Registration | `RegistrationCaptchaSecret` |
| Loxone adapter CK | `Password` |
| Reporting | `ConnectionString` |
| one-time-ticket, wwc26-landing-page | `TicketSecret`, `SecretKey` |

Out of scope: identity client secrets and password hashes (already hashed). Not secret: usernames, client ids, tenant ids.

Shared attribute definitions used by non-secret attributes must be split first — the value type sits on the attribute definition.

### 5.2 Phases (per environment: local → test-2 → staging-1 → prod-1/prod-2; proceed only when the sweep reports zero plaintext)

| Phase | Content |
|---|---|
| 0 (now) | AB#5529: rotate the committed Office365 passwords and the Graph client secret, replace with placeholders, new blueprint version, replace public catalog files, delete local plaintext dumps |
| 1 | Engine release: value type, protector, key ring (`k1` = existing key), legacy read, write rules, sweep service, SemVer rule, seed lint, System-2.5 gate. Deploy key-ring configuration to every engine host |
| 2 | Consumers able to handle ciphertext **before** any model change: communication controller reveals secrets when building adapter configuration (`SerializeWithRevealedSecrets`), `PoolService`/`WorkloadEncryptionService` move to the protector, service-account provisioning writes through it; mesh adapter `GetRtEntities*`/`ApplyChanges` handle SECRET; `RevealSecret@1` node; Studio and libraries stop selecting secret fields (36 documents → typed query without secret + is-set count + mutation returning only rtId); all clients on the new engine (decision 12) |
| 3 | Model changes (Minor): System.Communication 3.40, Identity, AI, Tesla, EnergyCommunity, Loxone adapter, Reporting, apps — attributes become `valueType: Secret`, ownership `Secret`. CK migration step only normalises placeholders to null. **From here nothing projects the value, even if still stored as plaintext** |
| 4 | Sweep: Hangfire `SecretSweepJob` (bot services) over all tenants incl. `octosystem` and child tenants: `Encrypt` once (plaintext and `enc:v1` → `enc:v2:k1`), then recurring `Verify`. Each run starts with a fresh tenant dump (secret material, 7 days). Service accounts: clear secrets and switch to impersonation (decision 7), redeploy data flows |
| 5 | Strict mode 14 days after zero plaintext (decision 10): legacy plaintext no longer readable |
| 6 | Rotation of credentials exposed before the migration (§5.4) |

Rollback: phases 1–3 are code-only. After phase 4, older binaries cannot read ciphertext; the emergency path is `Sweep(Decrypt)` with the key, not a binary rollback. Phase 2 code must have shipped one release before phase 4.

Sweep API: `ISecretMaintenanceService.SweepTenantAsync(tenantId, mode: Verify | Encrypt | Reprotect | ClearUnknownKid | Decrypt)` returning counts per form and key id; system-API endpoint and octo-cli commands.

**Implementation (AB#5532).** Contract in `Runtime.Contracts.Secrets` (`ISecretMaintenanceService`, `SecretSweepMode`, `SecretSweepOptions`, `SecretSweepResult`, `SecretFormCounts`, `SecretSlotReport`, `SecretSweepClearedValue`, `SecretSweepFailure`), implementation in `Runtime.Engine` over `IRuntimeRepositoryProvider` + `GetRtEntitiesByTypeAsync` (batches, archived included) + `RewriteAttributeValueForMigrationAsync`. Counts are the forms as found (`notSet`, `placeholder`, `plaintext`, `enc_v1`, `enc_v2` per kid, unknown kid per kid, failed) per tenant and per CK type / attribute path; `Cleared` is the re-entry list. `Encrypt` also turns stored placeholders into `null`; `Decrypt` needs `ConfirmDecrypt`. `NormalizePlaceholdersAsync(tenant, ckModelName)` is the phase-3 migration hook, called by `CkModelMigrationService` after every successful migration (no key needed). Meter `Meshmakers.Octo.Secrets`: counters `octo.secrets.sweep.rewritten` / `octo.secrets.sweep.failed`, observable gauge `octo.secrets.values{tenant,model,form,kid}` from the last full sweep in the process (the bot job's process).

### 5.3 Verification
OTel gauge `octo.secrets.values{env,tenant,model,form=plaintext|enc_v1|enc_v2,kid}` from the sweep, alert when plaintext > 0 after strict mode; counter `octo.secrets.plaintext_reads` from legacy reads; per-tenant sweep report in the job result; raw-BSON integration tests asserting plaintext bytes are absent.

### 5.4 Rotation of already exposed credentials
Assume every value was read. Order: (1) AB#5529 credentials immediately; (2) cross-tenant/platform: Grafana `AdminPassword`, Helm repository passwords, identity-provider secrets; (3) service accounts — replaced by impersonation in phase 4; (4) third-party, manually in provider portals: Anthropic/WeClapp API keys, finAPI, Discord, SAP/SFTP/Loxone, Turnstile; (5) self-rotating: Tesla refresh token, AI OAuth tokens.

## 6. Blueprints, backups, exports

- Seeds may only contain `<…>` placeholders or empty values for SECRET attributes; a blueprint/CK lint (next to `CkLintRuntimeStateMarkers.cs`) fails the build otherwise (decision 9). Placeholders import as null; Secret ownership keeps an existing value on re-apply.
- Dumps (`DumpRepositoryJob`, `.octobak.zip`) keep their format and contain ciphertext after phase 4. Same-environment restore runs `Sweep(Encrypt)` afterwards to cover older plaintext dumps.
- Cross-environment or child-tenant restore: unknown key id → null, report of secrets to re-enter (decision 5).
- `ExportRt` / deep-graph export never contain SECRET values (ownership Secret); identity-provider secrets, report connection strings and the Loxone adapter password stop being exported once converted.
- Separately: inline API keys/passwords inside stored pipeline definitions (`AnthropicAiQueryNode.ApiKey`, `FinApiAuthNode.Password`) are not covered by the value type — scan and move them to configuration entities.

## 7. Work packages

| WP | Repo | Content | Depends on | Effort |
|---|---|---|---|---|
| 0 | meshmakers-app, meshmakers.github.io | AB#5529 incident | — | S |
| 1 | octo-construction-kit-engine | Value type, schema, `RtSecretValue`, protector + options, converter, source generator, compiler rules (incl. record key), SemVer rule, System-2.5 gate, seed lint | — | L |
| 2 | octo-construction-kit-engine | Write path (§3.6), record carry-over (§4.6), rule engine, blueprint comparer, sweep service, migration hook | 1 | M |
| 3 | octo-construction-kit-engine-mongodb | BSON serializer, filter/sort/text refusals, index guard, CrateDB exclusion, migration paths, test CK model | 1, 2 | M |
| 4 | octo-sdk | Mapper, `MutationDto.ClearSecretAttributes`, source generator, JSON converters (marker only), `InstanceSecretCrypto` delegates to the new envelope code | 1 | M |
| 5 | octo-asset-repo-services | GraphQL (§4), schema snapshot and integration tests | 3, 4 | L |
| 6 | octo-helm-core, octo-communication-operator, octo-mesh-adapter chart, octo-tools, octo-mesh-deployment | Key ring delivery (`k1` = existing key) | — (can start now) | M |
| 7 | octo-communication-controller-services | Reveal for adapter configuration, `PoolService`/`WorkloadEncryptionService` → protector, provisioning, impersonation switch for service accounts, System.Communication 3.40 | 1–5 | L |
| 8 | octo-mesh-adapter, octo-communication-sdk | SECRET in `GetRtEntities*`/`Backfill`/`ApplyChanges`, `RevealSecret@1` node, redact debug snapshots, node type-switch errors | 1, 4 | M |
| 9 | octo-bot-services | `SecretSweepJob`, restore re-protect / clear unknown kid, metrics | 2 | M |
| 10 | octo-identity-services | Identity-provider `ClientSecret` → SECRET + reveal | 1–5 | M |
| 11 | octo-ai-services, meshmakers-app, energy-community, octo-adapter-loxone, octo-report-services, app repos | Model changes, pipeline changes (`RevealSecret@1`) | 1–8 | S each |
| 12 | octo-frontend-libraries, octo-frontend-refinery-studio | Codegen, 36 Studio documents, set/unset UI, entity forms map SECRET automatically, CK attribute editor offers SECRET | 5 | M |
| 13 | octo-mcp-service, octo-cli, octo-documentation | Risk classes, `clearSecretAttributes`, `SecretStatus`/`ReprotectSecrets`, docs and key operations guide | 4, 2 | S–M |

Build order follows `Invoke-BuildAll`: engine → sdk → engine-mongodb → common → communication-sdk → mesh-adapter → bot → communication-controller → services → frontends.

## 8. Risks

- **Write paths that skip encryption** — mitigated by the serializer refusing `Pending` and raw-BSON tests.
- **Mixed versions** — pinned adapter charts with an old engine fail on the sub-document; all components move in one release (decision 12).
- **Studio shipping after the model change** breaks codegen'd documents — phase 2 before phase 3.
- **Pipelines that read secrets via `GetRtEntitiesByType`** silently get `{ isSet }` — they must switch to `RevealSecret@1` in phase 2.
- **Adapter configuration is cached until redeploy** — rotations need a redeploy (except where `RevealSecret@1` reads on demand).
- **Reusing `instance_secret_key`** (decision 3) means a leak of that key exposes both the old `enc:v1` data and the new secrets; mitigated by key ids, which allow moving to a new key with one re-protect sweep.
- **Key loss** — backup in Vault plus Keeper, provisioning guard.

## 9. Follow-ups

- Separate work item: identity Data Protection key ring stored unencrypted in the system database (decision 8).
- Backend defects found while planning AB#5522: record attributes always report `isOptional: false`; `targets(ckId: "System/Entity")` fails ("no defining collection root"); `roleName` given as role id is ignored without an error.
