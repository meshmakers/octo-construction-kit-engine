# SECRET attribute value type — concept

> Status: Approved for implementation · 2026-10-05, amended 2026-10-06 (§2.1) · Owner: Gerald Lochner
> Work item: AB#5528 (Bug, Epic 4969). Related: AB#5371, AB#5522 (entity forms), AB#5551 (installation-level adapter credential for impersonation). AB#5527 closed as duplicate; AB#5529 closed as a false alarm.
> Sources: two planning passes (engine/storage/API and existing secrets/migration), verified against the code on 2026-10-05.

---

## 1. Problem

Credential attributes of `System/Configuration` subtypes and several app models (passwords, client secrets, API keys, bot tokens, private keys, refresh tokens) are plain `String` attributes:

- The asset repository's GraphQL returns them in clear text. The generic projection `runtimeEntities { attributes { attributeName value } }` returns **every** attribute when `attributeNames` is omitted; typed fields such as `SystemCommunicationEMailReceiverConfiguration.password: String!` are readable; `updateRuntimeEntities` echoes them back.
- Frontend documents (`getEntitiesByCkType`, `getRuntimeEntityById`, 36 Studio configuration documents), MCP generic CRUD tools and octo-cli read them. Anyone with read access on a configuration entity reads its credentials; AI agents using MCP may have them in transcripts.
- `isRuntimeState` only protects values from blueprint re-apply. `AttributeOwnershipDto.Secret` (AB#5187) exists — "keep on re-apply, leave out of `ExportRt`" — but no production model uses it.

What already exists and is reused: `InstanceSecretCrypto` (octo-sdk, AES-256-GCM, prefix `enc:v1:`, no key id) with key `instance_secret_key` per cluster in Vault, already used for Helm repository passwords, `ValueOverride.Value` (`IsSecret`) and AI tokens. Its `Decrypt` already passes plaintext through.

## 2. Decisions (product owner, 2026-10-05)

| # | Topic | Decision |
|---|---|---|
| 1 | Approach | Dedicated CK `AttributeValueType` **`SECRET`**, enforced by the engine (not a marker flag) |
| 2 | Versioning | **String → SECRET counts as Minor** (new rule in `CkSemVerClassifier`): stored data stays readable because readers accept legacy plaintext during the transition. System.Communication goes to 3.41, not 4.0 |
| 3 | Key | **Reuse the existing `instance_secret_key`** as the first active key of the new key ring (key id `k1`). The new format carries a key id, which makes later rotation possible despite the old "never rotate" note |
| 4 | Records | **Secret sub-attributes inside records are supported in v1** (see §4.6) |
| 5 | Cross-environment restore / tenant copy | ~~Secrets with an unknown key id become "not set"~~ — **superseded 2026-10-06 (§2.1 item 2)**: the ciphertext is kept, readers see "not set" + `keyMissing`; the restore job reports which secrets need re-entry |
| 6 | Pipelines reading secrets from the database | New privileged **`RevealSecret@1` node** in the mesh adapter (in-process decrypt, counted) |
| 7 | Adapter service accounts | Migrate to **impersonation** (AB#5114): clear the stored secret, nothing remains stored. The installation-level adapter credential that impersonation needs is follow-up **AB#5551** |
| 8 | Scope | Identity-provider `ClientSecret`s are **in scope**; identity Data Protection keys are a **separate work item** |
| 9 | Blueprint seeds | Seeds contain **no secret values**; secrets are set after installation (Studio, CLI, rotate endpoints). A lint enforces it. Amended 2026-10-06 (§2.1 item 1): only empty or omitted values — placeholders are values and fail the lint |
| 10 | Deadlines | Strict mode per environment **14 days** after the sweep reports zero plaintext; pre-sweep plaintext backups kept **7 days**, treated as secrets, then deleted |
| 11 | ~~AB#5529 public repository~~ | ~~Rotate and replace, no history rewrite~~ — void: AB#5529 was a false alarm and is closed (2026-10-06) |
| 12 | Older clients | **Minimum version plus gate**: models using SECRET require `System >= 2.5`, so old engines fail with a clear dependency error |

### 2.1 Decisions 2026-10-06 (product owner, round 2)

| # | Topic | Decision |
|---|---|---|
| 1 | Placeholders | **Dropped entirely.** A `<…>` / `TODO_SET_*` string sent through any API is an ordinary value and is encrypted like any other; the write-path rule "placeholder → not set" (top level and records) and the identity-provider `400` special case are gone. Seed lint: a Secret slot may only be empty or omitted; `TODO_SET_*` and `<…>` are errors. **Migration only:** legacy plaintext strings found in storage that are exactly a placeholder (`TODO_SET_<UPPER_SNAKE>` or a single `<…>`) become "not set" once — encrypt sweep and CK migration hook, counted as `PlaceholdersNormalized` (`SecretAttributeConventions.IsLegacyPlaceholder`; `IsAllowedSeedValue` = null/empty/whitespace only) |
| 2 | Restore / unknown key id | **Keep the ciphertext.** A protected value whose key id is not in the ring stays stored encrypted. Readers see it as not set (`isSet = false`) plus `keyMissing = true` (GraphQL `OctoSecretState { isSet, keyMissing }`, generic `RtEntityAttribute.secretKeyMissing`, SDK `OctoSecretStateDto.KeyMissing`, `RtEntityAttributeDto.SecretKeyMissing`); sweep/verify reports list them as re-entry tasks (`SecretSweepResult.Unreadable`). It becomes readable automatically once its key is added to the ring. It is removed only by re-entry (new value) or the explicit admin sweep mode **`CleanupUnreadable`** (replaces `ClearUnknownKid`, same numeric value 3; high risk, role-gated, dump first, `ConfirmCleanupUnreadable`). **No decrypt / plaintext export mode is offered by any API** (the engine-internal emergency `Decrypt` mode stays, reachable only in-process). Restore flow: Verify → Encrypt (plaintext / `enc:v1` → `enc:v2`), report unreadable values as the re-entry list, no clearing. Reveal paths (controller adapter configuration, mesh adapter `RevealSecret@1`, identity, AI, service-account token): unknown key id → treated as not set (`null`) with an error log without the value (`ISecretAttributeProtector.RevealOrNull`). Carry-over keeps the ciphertext; a required secret that is stored but unreadable counts as **present** for "required" checks but `isSet = false` for readers. DR / environment handover: see §6 |
| 3 | Confirmed as implemented | Omitted secret on replace keeps the value, explicit `null` clears. Wire format unchanged: `{isSet}` / `{}` = unchanged, string = new value, `null` = clear, anything else rejected; records keep by record key. Corrupt envelope reads as not set + warning. `RevealSecret@1` refuses identity System. An undecryptable secret ships to the adapter as `null` + error log. An identity provider with an undecryptable secret is skipped. wwc26 `SecretKey` is not converted. MCP secret writes only through high-risk tools. `ValueOverride.Value` stays `String`; the new Secret record member is `SecretValue` (record key `Path`, §4.6) |
| 4 | Work items | AB#5551 = installation-level adapter credential for impersonation (follow-up to decision 7). AB#5529 was a false alarm and is closed — no phase-0 incident work |

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
| `RtSecretValue.Protected` | passed through (trusted internal callers: upsert preservation, restore, sweep) — also with a key id that is not in the ring (kept, §2.1 item 2) |
| `null` | cleared |

There is no placeholder rule (§2.1 item 1): `<…>` and `TODO_SET_*` are ordinary values and are encrypted.

Paths that bypass `BulkRtMutation` call the step explicitly: `RuntimeRepositoryBase.BulkInsertRtEntitiesAsync`, `InsertOneRtEntityForMigrationAsync`, `RewriteAttributeValueForMigrationAsync`, `CreateTransientRtEntity`.

**Implementation (AB#5532).** The step is `ISecretWriteNormalizer` / `SecretWriteNormalizer` (`Runtime.Engine.Secrets`, singleton, exposed as `IBulkRtMutation.SecretWriteNormalizer`). Concrete choices:
- An **omitted** secret on a replace is carried over like `""` (clearing is explicit, §4.3); `null` clears. A replace that leaves a required secret without a value is rejected before anything is deleted or written.
- `LegacyPlaintext` is re-encrypted when a key is configured (an `enc:v1` value is decrypted first); without keys it is kept as stored. Only new input (`Pending`, plain string) requires a key. A plain string is input on the API path and legacy on storage paths (`SecretValueOrigin`).
- Legacy placeholders (migration only, §2.1 item 1): a **stored legacy string** (`LegacyPlaintext`, never input) that is empty or exactly `<…>` / `TODO_SET_<UPPER_SNAKE>` (`SecretAttributeConventions.IsLegacyPlaceholder`) is written as `null` wherever the write step meets it (carry-over, CK migration writes); the encrypt sweep and the CK migration hook convert the stored ones once.
- Clearing: `IEntityUpdateInfo.ClearSecretAttributes` (factories `EntityUpdateInfo.CreateUpdate/CreateReplace(id, entity, clearSecretAttributes)`); the rule engine validates it (messages 21–23) and turns each entry into an explicit `null`. Required secrets on create: `""` and `null` count as missing (message 2); on update only an explicit `null` violates a required secret (message 5); after carry-over: message 24. A stored protected value with an unknown key id counts as present.
- The CK migration writes are normalised by their engine caller (`CkModelMigrationService`), not inside the MongoDB overrides; `CreateTransientRtEntity` never sets a Secret value. The bulk import does not enforce required secrets (AB#4772 policy).
- Blueprint re-apply keeps stored secrets inside seed-owned records too (`ImportRtModelCommand.PreserveSecretRecordMembers`, by record key); the comparer never diffs Secret attributes or members and masks them in reported record changes.
- `AttributeValueConverter` accepts a string, `null`, an `RtSecretValue` or the read marker (a JSON object / dictionary = unchanged) for a Secret; any other input (numbers, lists, arbitrary objects) is an `InvalidAttributeValueException` naming the type only.
- `SecretAttributeNotQueryableException` (API error code `SecretAttributeNotQueryable`) and `SecretValueNotStorableException` live in `Runtime.Contracts.Secrets`, so every repository implementation throws the same types.
- Engine serialisers write an `RtSecretValue` as `{"isSet":…}` only (type-level STJ/Newtonsoft converters, YAML converter); a marker or object sent back means "unchanged"; `ExportRt` omits Secret attributes by value type as well as by ownership.

### 3.7 Server-side decryption
`ISecretAttributeProtector` (contract in Runtime.Contracts, implementation in Runtime.Engine): `Protect`, `Unprotect` (handles `enc:v1`, `enc:v2`, legacy plaintext with a warning counter; throws `UnknownSecretKeyIdException` for an unknown key id), `IsProtectedEnvelope` (strict), `TryParseEnvelope`, `NeedsReprotect`, `Reprotect`. Extension `rtEntity.GetSecretPlaintext(attr, protector)`.

**Read state and reveal (2026-10-06, §2.1 item 2).**
- `SecretValueState { NotSet, Set, KeyMissing }` (`Runtime.Contracts.Secrets`), from `ISecretAttributeProtector.GetReadState(value, context?)` or the extension `rtEntity.GetSecretReadState(attr, protector)`: protected + known key id → `Set`; protected + unknown key id → `KeyMissing`; pending non-empty → `Set`; legacy non-empty → `Set`, except a legacy placeholder (→ `NotSet` until migrated) and a corrupt value (an `enc:v2` envelope stored as a legacy string → `NotSet` + warning log and `octo.secrets.unreadable{reason=corrupt}`, never the value); `null`/empty → `NotSet`. APIs map `isSet = (state == Set)`, `keyMissing = (state == KeyMissing)`.
- Callers without a protector use the pure helper `SecretValueStates.GetReadState(value, knownKeyIds)` or `GetReadState(value, null)`. **With no key ring at all** (`null`) every protected value counts as `Set` and `KeyMissing` is never returned — such a caller cannot know key availability and must report `keyMissing` as unknown (`null` / omitted), never `false`. This is what the octo-sdk `RtEntityToDtoMapper` does when no protector is available (`SecretIsSet = isSet without key ring`, `SecretKeyMissing = null`); with a protector it uses `GetReadState`. The serializer marker `{"isSet":…}` (`RtSecretValueWireFormat.IsSet`) has no key ring either and therefore marks an unknown-kid value `isSet: true`; user-facing APIs (asset repo GraphQL) must compute `isSet`/`keyMissing` with `GetReadState`, not from the marker.
- **"Set at" (Q2):** `RtSecretValue.SetAt` (`DateTime?`, UTC) on protected values — `Protect` stamps new input with the current time; carry-over, `Reprotect` (key rotation), restore and `CleanupUnreadable` keep it; values converted from legacy storage (encrypt sweep, write step on a stored legacy string) have none. Factory `RtSecretValue.Protected(envelope, setAt)`, `WithSetAt(...)`. Not part of equality and **not** in the `{isSet}` marker. MongoDB stores it next to the envelope as BSON field `t` (WP3). Readers get it via `ISecretAttributeProtector.DescribeSecret(value, context?)` / `rtEntity.DescribeSecret(attr, protector)` / pure `SecretValueStates.Describe(value, isKnownKeyId)` → `SecretReadInfo(State, Form, KeyId, SetAt)` with `SecretStorageForm { NotSet, Plaintext, EncV1, EncV2, KeyMissing, Corrupt }`.
- **Secrets overview (Q1):** `ISecretInventoryService` (registered by `AddRuntimeEngine()`): `ListAsync(tenantId, SecretInventoryQuery { CkTypeId, Forms, NeedsReEntry, Search, Skip, Take = 50 })` → `SecretInventoryPage(Items, TotalCount)` of `SecretInventoryItem(CkTypeId, RtId, RtWellKnownName, DisplayName, AttributePath, AttributeName, Required, Form, KeyId, SetAt, NeedsReEntry)`; `SummarizeAsync(tenantId)` → `SecretInventorySummary { Total, NotSet, Plaintext, EncV1, EncV2, KeyMissing, Corrupt, NeedsReEntry, EncV2ByKeyId }`. Paths are camelCase (`password`, `endpoints[key=prod].token`, `credentials.token`, `endpoints[0].token` without a record key); `NeedsReEntry = KeyMissing || Corrupt || (NotSet && Required)`; `DisplayName` is the stored `RtDisplayName`. Same scan as the sweep (`SecretEntityScanner`), never values. Exposed by the asset repository as GraphQL `secrets { inventory summary }` (handover §7).
- Reveal paths use `ISecretAttributeProtector.RevealOrNull(value, context?)` (and `GetSecretPlaintext`, which now delegates to it): an unknown key id, a tampered / wrong-key envelope or a copied `enc:v2` legacy string returns `null` ("not set") with an error log (tenant, CK type, attribute, key id — never the value) and `octo.secrets.unreadable{reason=unknown_key_id|decrypt_failed|corrupt}`. Configuration problems still throw (`SecretEncryptionNotConfiguredException` when the host has no keys at all, `LegacyPlaintextSecretRejectedException` in strict mode). `Unprotect` keeps its throwing contract. Both new interface members have default implementations, so other implementations keep compiling. Every decrypt increments `octo.secrets.decrypt{tenant,ckType,attribute,service}`; logs never contain the value. No public decrypt endpoint in v1. Architecture tests in asset repo and MCP forbid `Unprotect` outside an allowlist.

## 4. API semantics (asset repository, SDK, MCP)

1. **Typed output field**: `password: OctoSecretState!` with `{ isSet: Boolean!, keyMissing: Boolean! }` (`keyMissing` added 2026-10-06: stored, but its key id is not in the ring — show "key missing — re-enter"). Old documents that select the field as a scalar fail validation loudly instead of leaking.
2. **Generic projection**: `value: null` plus `RtEntityAttribute.secretIsSet: Boolean` and `RtEntityAttribute.secretKeyMissing: Boolean` (both null for non-secret attributes). Sending `null` back is safe.
3. **Input** stays `String`: omitted, `null` (in an update set) or `""` = unchanged. **Clearing** is explicit: `clearSecretAttributes: [String!]` on update inputs (typed and generic) and `MutationDto`. Clearing a required secret is an error; creating requires a value for required secrets.
4. **Filters**: only `IS_NULL` / `IS_NOT_NULL`. Sort, text search, aggregations, group-by are refused (`SecretAttributeNotQueryable`). Secret attributes are excluded from query columns, RtQuery, archive paths and CrateDB columns.
5. **Data permissions** stay type-level row filters: read shows `isSet`, writing a secret needs write permission. No permission grants decryption through the public API.
6. **Secrets in records (decision 4)**: a record (array) attribute is written as a whole, so "unchanged" must be resolved per element. Rule: when a record array is replaced, a secret sub-value that is `null`/`""` in an incoming element is carried over from the stored element **with the same record key** — a record type with secret sub-attributes must declare a key sub-attribute (new compiler rule: `recordKey`), otherwise by **position** for single records. Projection of a record element shows `{ isSet }` for the secret sub-field. This also brings `ValueOverride.Value` (`IsSecret`) and AI provider records under the SECRET type; their `enc:v1` values migrate like any other.

   **Record key — implementation (AB#5531).** The key is declared on the record definition, not on the attribute assignment, because it is a property of the record type that every record-array attribute using it shares:

   ```yaml
   records:
     - recordId: ValueOverride
       recordKey: Path            # names a sub-attribute of the record
       attributes:
         - id: ${this}/Path
           name: Path             # valueType: String, required
         - id: ${this}/Value
           name: Value            # valueType: String - stays readable (image tags etc.)
         - id: ${this}/SecretValue
           name: SecretValue      # valueType: Secret (new, decision 2026-10-06 §2.1 item 3)
   ```

   `ValueOverride.Value` stays `String`; secret overrides use the new Secret member `SecretValue`. The key `Path` must be unique among the overrides that carry a `SecretValue`.

   - Schema: `recordKey` (string, PascalCase attribute name) in `construction-kit-elements-record.schema.json`; `CkRecordDto.RecordKey`; the effective key (own or inherited from the nearest base record) is `CkRecordGraph.RecordKey`.
   - Compiler: a record whose own or inherited attributes contain a Secret attribute must have an effective `recordKey` (message 76) — on the record type, regardless of whether it is used as `Record` or `RecordArray`, because a record type defined for single use can be reused in an array by another model. A declared key must name a required `String`, `Int`, `Int64` or `Enum` sub-attribute that is not Secret (message 77).
   - SemVer: setting, clearing or changing `recordKey` is Minor.
   - Single `Record` attributes still carry over by position (there is exactly one element); the key is used for `RecordArray`. The carry-over itself is WP2 (AB#5532).
   - **Implementation (AB#5532):** carry-over applies on update and replace, at any nesting depth; `null`, `""` or an omitted sub-value is carried over (records keep by record key, §2.1 item 3). There is no clear value inside records (placeholders are values since 2026-10-06): a record secret is cleared by **removing its element** (record array) or the record (single record: `null` for the record attribute); an element written with a new record key starts without a secret. Keys compare by value (integers across CLR types, otherwise ordinal text). A record array without a key (pre-AB#5531 models) gets no carry-over.
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
| 1 | Engine release: value type, protector, key ring (`k1` = existing key), legacy read, write rules, sweep service, SemVer rule, seed lint, System-2.5 gate. Deploy key-ring configuration to every engine host |
| 2 | Consumers able to handle ciphertext **before** any model change: communication controller reveals secrets when building adapter configuration (`SerializeWithRevealedSecrets`), `PoolService`/`WorkloadEncryptionService` move to the protector, service-account provisioning writes through it; mesh adapter `GetRtEntities*`/`ApplyChanges` handle SECRET; `RevealSecret@1` node; Studio and libraries stop selecting secret fields (36 documents → typed query without secret + is-set count + mutation returning only rtId); all clients on the new engine (decision 12) |
| 3 | Model changes (Minor): System.Communication 3.41, Identity, AI, Tesla, EnergyCommunity, Loxone adapter, Reporting, apps — attributes become `valueType: Secret`, ownership `Secret`. CK migration step only normalises legacy placeholder strings (and empty strings) to null, once. **From here nothing projects the value, even if still stored as plaintext** |
| 4 | Sweep: Hangfire `SecretSweepJob` (bot services) over all tenants incl. `octosystem` and child tenants: `Encrypt` once (plaintext and `enc:v1` → `enc:v2:k1`; legacy placeholder plaintext `TODO_SET_*` / `<…>` → `null`, counted in `PlaceholdersNormalized`), then recurring `Verify`. Each run starts with a fresh tenant dump (secret material, 7 days). Service accounts: clear secrets and switch to impersonation (decision 7), redeploy data flows |
| 5 | Strict mode 14 days after zero plaintext (decision 10): legacy plaintext no longer readable. Implementation: `SecretEncryption:StrictMode` (env `OCTO_SECRETENCRYPTION__STRICTMODE`, default `false`) — `Unprotect` of legacy clear text throws `LegacyPlaintextSecretRejectedException` and counts `octo.secrets.strict_mode.rejected_reads{tenant,ckType,attribute,service}`; `enc:v1` stays readable (`LegacyV1Key`); `Reprotect` (encrypt/reprotect sweep, write path) still converts clear text, counted as a plaintext read; `ISecretAttributeProtector.IsStrictMode` |
| 6 | Rotation of credentials exposed before the migration (§5.4) |

Rollback: phases 1–3 are code-only. After phase 4, older binaries cannot read ciphertext; the emergency path is `Sweep(Decrypt)` with the key, not a binary rollback. Phase 2 code must have shipped one release before phase 4.

Sweep API: `ISecretMaintenanceService.SweepTenantAsync(tenantId, mode: Verify | Encrypt | Reprotect | CleanupUnreadable | Decrypt)` returning counts per form and key id; system-API endpoint and octo-cli commands. `CleanupUnreadable` (value 3, formerly `ClearUnknownKid`) is admin-only, high risk and needs `SecretSweepOptions.ConfirmCleanupUnreadable`; `Decrypt` is engine-internal and not exposed by any API (§2.1 item 2).

**Implementation (AB#5532).** Contract in `Runtime.Contracts.Secrets` (`ISecretMaintenanceService`, `SecretSweepMode`, `SecretSweepOptions`, `SecretSweepResult`, `SecretFormCounts`, `SecretSlotReport`, `SecretSweepClearedValue`, `SecretSweepUnreadableValue`, `SecretSweepFailure`), implementation in `Runtime.Engine` over `IRuntimeRepositoryProvider` + `GetRtEntitiesByTypeAsync` (batches, archived = deleted entities included: processed and counted in `ArchivedEntitiesScanned`, their unknown-key-id values counted in `ArchivedUnreadableValues` but never listed in `Unreadable` / `Cleared`) + `RewriteAttributeValueForMigrationAsync`. Counts are the forms as found (`notSet`, `placeholder`, `plaintext`, `enc_v1`, `enc_v2` per kid, unknown kid per kid, failed) per tenant and per CK type / attribute path; `Unreadable` (CkTypeId, RtId, AttributePath, KeyId) is the re-entry list — values of an unknown key id, kept by every mode except `CleanupUnreadable`; `Cleared` lists only the values `CleanupUnreadable` deleted; normalised legacy placeholders are counted in `PlaceholdersNormalized`. The rewrite is a compare-and-swap (`IRuntimeRepository.RewriteAttributeValueIfUnchangedForMigrationAsync`, compared with `StoredAttributeValueComparer`: protected by envelope, legacy by text, a record (array) as a whole); an attribute changed after the sweep read it is skipped and counted in `SkippedConcurrentlyModified`, not failed. `Encrypt` also turns stored legacy placeholder strings into `null`; `Decrypt` needs `ConfirmDecrypt`, `CleanupUnreadable` needs `ConfirmCleanupUnreadable`. `NormalizePlaceholdersAsync(tenant, ckModelName)` is the phase-3 migration hook, called by `CkModelMigrationService` after every successful migration (no key needed). Meter `Meshmakers.Octo.Secrets`: counters `octo.secrets.sweep.rewritten` / `octo.secrets.sweep.failed`, observable gauge `octo.secrets.values{tenant,model,form,kid}` from the last full sweep in the process (the bot job's process).

### 5.3 Verification
OTel gauge `octo.secrets.values{env,tenant,model,form=plaintext|enc_v1|enc_v2,kid}` from the sweep, alert when plaintext > 0 after strict mode; counter `octo.secrets.plaintext_reads` from legacy reads; per-tenant sweep report in the job result; raw-BSON integration tests asserting plaintext bytes are absent.

### 5.4 Rotation of already exposed credentials
Assume every value was read. Order: (1) cross-tenant/platform: Grafana `AdminPassword`, Helm repository passwords, identity-provider secrets; (2) service accounts — replaced by impersonation in phase 4 (installation-level credential: AB#5551); (3) third-party, manually in provider portals: Anthropic/WeClapp API keys, finAPI, Discord, SAP/SFTP/Loxone, Turnstile; (4) self-rotating: Tesla refresh token, AI OAuth tokens.

## 6. Blueprints, backups, exports

- Seeds may only leave SECRET attributes **empty or omit them**; a blueprint/CK lint (`BlueprintSeedSecretLint`, message 10) fails the build otherwise (decision 9, §2.1 item 1). `<…>` and `TODO_SET_*` are values and fail the lint too. Empty values import as "not set"; Secret ownership keeps an existing value on re-apply.
- Dumps (`DumpRepositoryJob`, `.octobak.zip`) keep their format and contain ciphertext after phase 4.
- **Restore (any source):** Verify → Encrypt (older plaintext / `enc:v1` dumps → `enc:v2`, legacy placeholders → not set) → report. Values whose key id is not in the ring are **kept** and listed as the re-entry list (`Unreadable`); nothing is cleared (§2.1 item 2). They read as not set + `keyMissing` and become readable on their own once their key is added to the ring.
- **Restore on a host without key ring** (AB#5532/AB#5539): the re-entry list is still produced. Only a **key-free Verify** runs — it classifies by key id without decrypting: every `enc:v2` whose key id is not in the (empty) ring and every `enc:v1` while no `LegacyV1Key` is configured counts as key missing (`UnknownKeyId`, listed in `Unreadable` / `secretsToReEnter`; `enc:v1` with the key id `enc:v1` = `SecretValueStates.LegacyV1KeyId`); clear text stays `Plaintext`. Nothing is encrypted, cleared or written. The run is recorded as mode `Verify`, trigger `Restore`, outcome `Succeeded` with the reason "No key ring configured: secrets were classified only; set the key ring and run Encrypt". The environment status carries `warnings: ["NoKeyRing"]`. Once the key ring is set, run Encrypt (and Reprotect if the source key was added).
- The same `enc:v1` rule applies to readers on any host: without `LegacyV1Key` an `enc:v1` string reads as `isSet = false`, `keyMissing = true` (form `KEY_MISSING`, key id `enc:v1`); the encrypt sweep keeps it (listed for re-entry) instead of failing.
- **Disaster recovery / environment handover** (cross-environment or child-tenant restore, a new environment with a new key ring):
  - *Default:* secrets arrive unreadable (`keyMissing = true`) and are re-entered from the re-entry list (Studio, octo-cli, rotate endpoints). Re-entry replaces the kept ciphertext.
  - *Optional ops step* (keeps the secrets without re-entry): temporarily add the source environment's key to the target key ring (`SecretEncryption:Keys:<source kid>`, not active) → restore → run the `Reprotect` sweep (everything moves to the target's active key) → verify that `Unreadable` is empty → remove the source key from the ring again. The source key travels only through the secret store (Vault/Keeper), never with the dump.
  - Only when a key is gone for good may an admin run `CleanupUnreadable` (dump first) to delete the remaining unreadable values. It never deletes an `enc:v1` value that is unreadable only because `LegacyV1Key` is not configured (a configuration gap, recoverable by setting the legacy key): such values stay in `Unreadable` and are counted in `SecretSweepResult.SkippedLegacyV1KeyMissing`.
- **Deleted entities (retention, AB#5532/AB#5544):** deleting an entity archives it (`rtState = Archived`); the document — and with it the stored ciphertext of its SECRET attributes — is retained as long as the archived document is retained; purging the document removes it (a hard-deleted entity has nothing left to protect). Archived entities are not part of the secrets inventory / summary, exactly like in every public query. The sweeps (Verify, Encrypt, Reprotect, CleanupUnreadable) still process their stored values, so no clear text or unreadable leftover stays at rest; they count them separately (`ArchivedEntitiesScanned`, `ArchivedUnreadableValues`) and never list them in the re-entry list (`unreadable[]` / `secretsToReEnter`) or in `cleared[]`. Failures on archived entities are still listed in `failures[]` (operational issue at rest).
- `ExportRt` / deep-graph export never contain SECRET values (ownership Secret); identity-provider secrets, report connection strings and the Loxone adapter password stop being exported once converted.
- Separately: inline API keys/passwords inside stored pipeline definitions (`AnthropicAiQueryNode.ApiKey`, `FinApiAuthNode.Password`) are not covered by the value type — scan and move them to configuration entities.

## 7. Work packages

| WP | Repo | Content | Depends on | Effort |
|---|---|---|---|---|
| 1 | octo-construction-kit-engine | Value type, schema, `RtSecretValue`, protector + options, converter, source generator, compiler rules (incl. record key), SemVer rule, System-2.5 gate, seed lint | — | L |
| 2 | octo-construction-kit-engine | Write path (§3.6), record carry-over (§4.6), rule engine, blueprint comparer, sweep service, migration hook | 1 | M |
| 3 | octo-construction-kit-engine-mongodb | BSON serializer, filter/sort/text refusals, index guard, CrateDB exclusion, migration paths, test CK model | 1, 2 | M |
| 4 | octo-sdk | Mapper, `MutationDto.ClearSecretAttributes`, source generator, JSON converters (marker only), `InstanceSecretCrypto` delegates to the new envelope code | 1 | M |
| 5 | octo-asset-repo-services | GraphQL (§4), schema snapshot and integration tests | 3, 4 | L |
| 6 | octo-helm-core, octo-communication-operator, octo-mesh-adapter chart, octo-tools, octo-mesh-deployment | Key ring delivery (`k1` = existing key) | — (can start now) | M |
| 7 | octo-communication-controller-services | Reveal for adapter configuration, `PoolService`/`WorkloadEncryptionService` → protector, provisioning, impersonation switch for service accounts, System.Communication 3.41 | 1–5 | L |
| 8 | octo-mesh-adapter, octo-communication-sdk | SECRET in `GetRtEntities*`/`Backfill`/`ApplyChanges`, `RevealSecret@1` node, redact debug snapshots, node type-switch errors | 1, 4 | M |
| 9 | octo-bot-services | `SecretSweepJob`, restore Verify → Encrypt with re-entry report (`Unreadable`), admin `CleanupUnreadable`, metrics | 2 | M |
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
