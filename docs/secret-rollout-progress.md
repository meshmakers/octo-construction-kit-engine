# SECRET attribute value type — implementation progress

> Epic AB#5528 · concept: [concept-secret-attribute-type.md](concept-secret-attribute-type.md) · frontend handover: [secret-frontend-handover.md](secret-frontend-handover.md) · app/adapter model changes: [secret-app-model-changes.md](secret-app-model-changes.md)
> Local branches only (`feat/gerald/secret-attribute-type`; key ring: `feat/gerald/secret-key-ring`). Nothing pushed, published or deployed. State 2026-10-06 (round 2 included).

## Status per work package

| WP | WI | Repo(s) | Status | Commits | Tests (final run) |
|---|---|---|---|---|---|
| 1 | AB#5531 | octo-construction-kit-engine; octo-common-services (meter) | Resolved | 982b858; common 97d9428 | see engine row 2; common 193 |
| 2 | AB#5532 | octo-construction-kit-engine | Resolved | 2dd6f72, cee750a, 1279717, 4c307b9, 90a1843 (strict mode), 2f5a7e3 (strict wire format), cce531b3 (enc:v2 legacy refused) | CK.Engine 924, Runtime.Engine 1357, Compiler 11, Blueprint 32, SystemTests 54+8(1 skip)+6 |
| 3 | AB#5533 | octo-construction-kit-engine-mongodb | Resolved | 860eae1, 37decde, c8ca3e4, d806e34 (diagnostics redaction), 96e6c9c (point/typed reads normalised) | unit 921, integration 515 (2 skip) |
| 4 | AB#5534 | octo-sdk | Resolved | e9a570d, ef04919, f6bee03, 2c4d40e, 66b05d5, 0d8edf8 (InstanceSecretCrypto enc:v1 only) | 406 (133+241+4+28) |
| 5 | AB#5535 | octo-asset-repo-services | Resolved | 0590d0f, ac9007c, 6b1c858 | unit 235, integration 460 (1 known locale failure, 1 skip) |
| 6 | AB#5536 | helm-core, operator, mesh-adapter chart, octo-tools, mesh-deployment (feat/gerald/secret-key-ring); octo-helm-pro | Resolved | helm-pro ef37529 | helm lint/template |
| 7 | AB#5537 | octo-communication-controller-services | Resolved | d6a77f1, c13f55b (System.Communication 3.40), b45fb55, f8099e4, 544d88f, 5d22922 | unit 1120, integration 59 |
| 8 | AB#5538 | octo-communication-sdk; octo-mesh-adapter (branched from feat/gerald/secret-key-ring) | Resolved | comm-sdk 346a2ab, a3802ae, 6549701, f26b6d8, 5f89cf5, 95808d1; mesh-adapter e950337, 620e005, 04f20a7, edaa376, 1755b86, 500a3a5 | comm-sdk 1246+230+5; mesh-adapter 1996, integration 89 (mongo 8.0.15) |
| 9 | AB#5539 | octo-bot-services | Resolved | ef17ec6, a6f2fec | 254 |
| 10 | AB#5540 | octo-identity-services (System.Identity 2.22.0) | Resolved | 3b3e090, b127d86, 3f32b22, 5aef611 | 319+14+247, integration 204 (1 skip) |
| 11 | AB#5541 | octo-ai-services (System.Ai 3.13.0); other repos documented | Resolved | ai ab93f44, 18d1fee, c41c88c; engine doc d19f1c06 | integration 239, unit 7 |
| 12/14 | AB#5542 / AB#5544 | frontend (other PO) | Handover note delivered | engine f2b15c36, 0bebefb1 | — |
| 13 | AB#5543 | octo-mcp-service, octo-cli, octo-documentation | Resolved | mcp bb44198, fe566eb; cli 5574867, b3135a8; docs 844658cf, b696e10d, 1973c804 | mcp 945; cli 86+45; docs EN build ok |

Final `Invoke-BuildAll -configuration DebugL -excludeFrontend $true`: 38/38 repos green.

## Round 2 — user decisions 2026-10-06 and Secrets admin API (AB#5544 backend)

Decisions recorded in the concept (§2.1). Contract: [secret-frontend-handover.md](secret-frontend-handover.md) §6–13 (committed first as 01fda94e, aligned 3f191672, 410ade02).

| Change | Repo: commits |
|---|---|
| Placeholders dropped (ordinary values; seed lint only empty; migration converts legacy exact placeholders once) | engine 7b72c618; identity ec8942b; controller 23db3be; mcp de6154c; docs c80b9381 |
| Unknown key id keeps ciphertext (`keyMissing`, RevealOrNull, `CleanupUnreadable` with confirm + dump, restore without clearing) | engine 47d9c59d, f36b6891; controller 23db3be, 9751352; mesh-adapter a52090d; ai b0f1765, f6c2485; identity ec8942b, df9a67b; bot 88d813b, 92c2849 |
| Set-at timestamp (BSON `t`), secret inventory service | engine f0873974; mongodb b6e0901 |
| Marker accepts `keyMissing`/`setAt`; SDK state mapping | engine 6ed01f7f; sdk ef4f1a3, b284171 |
| Admin GraphQL `secrets { inventory, summary, usages }`, `OctoSecretState { isSet, keyMissing, setAt }` | asset-repo 51e2dab, 7a9f255 |
| Role `SecretManagement`; IdP `clientSecretKeyMissing`/`clientSecretSetAt` | identity 00daef1, 294bc4b; sdk ba2456d |
| Bot status, sweeps with roles/confirm, run history with dumps + delete | bot 88d813b, 92c2849; sdk ba2456d, afa2b63; cli 24658ec, 25d0f5d; mcp de6154c, 43bd8ff |
| Debug snapshots `redactedPaths` | sdk 5dd6b8a, 293a6f3; comm-sdk c701c56, 7d48adb; controller 8379072, 6167517 |
| Review fixes: generic input ignores secretKeyMissing/secretSetAt; pipeline markers show keyMissing | asset-repo 0c2b1b4; comm-sdk bc366aa; mesh-adapter 9b32e0c |
| Concept addendum, app model changes (MeshmakersAccounting seed + `graph-credentials.service.ts`/`imap-connection.service.ts` → `isSet`) | engine 16c57abd |

Final full build after round 2: 38/38 green. Round-2 test run (final HEADs; asset-repo 263+471, comm-sdk 1270+230+5, mesh-adapter 2005+89 after the last fixes): engine Runtime 1426, CK.Engine 925, Compiler 11+6, Blueprint 32, SystemTests 54+9; sdk 458; mongodb 925 + 518; comm-sdk 1256+230+5; mesh-adapter 2000+89; bot 312; controller 1138+59; asset-repo 262 + 471 (1 known locale failure); identity 19+252+324+205; ai 7+242; mcp 954; cli 45+97.

### Follow-ups and live-check fixes (2026-10-06, after the user authorised PO decisions)

| Change | Repo: commits |
|---|---|
| Bot admin policies match platform role claims (403 fixed) | bot 2e33c69 |
| Inventory omits unset optional record secrets, searches by CK type, display-name fallback | engine 186d6451; asset-repo 3381e54 |
| `SkippedLegacyV1KeyMissing`, run `reason`, interrupted runs marked Failed after restart | sdk 23e3da9, 5d63bf5; bot 94c2fd7 |
| SDK converters delegate to the engine wire format | sdk 8fbb226 |
| MCP `get_secret_inventory` (risk low) | mcp a82beb0, c478d37 |
| Identity 503 `SecretEncryptionNotConfigured` instead of 500; SecretManagement granted once per tenant to holders of AdminPanelManagement + TenantManagement and to TenantOwners | identity f5b9ca6, 49bf716 |
| AI helper remarks | ai dc5f5ca |
| Encrypt sweep wrote nothing (rewrite targeted per-type collection; derived types live in the root collection) and lingering tenant lock skipped the next run; runs now show before/after (`totalsAfter`, `encryptedCount`) | mongodb 0e48249, 00f7dea; engine 2c592af6; sdk 8211487; bot f3830e4, ba3f4ad |
| Durable pre-sweep dump storage: concept + WIs AB#5559–AB#5566; follow-up AB#5571 (other migration write paths with the same collection issue) | engine fcadfd35, 7c23aeb3 |
| Inventory/summary skip deleted (archived) entities; sweeps still process them at rest, never as re-entry tasks | engine 0c0ce08a; asset-repo 8852cad |

Decided by the PO (authorised): SecretManagement is granted automatically only to holders of both AdminPanelManagement and TenantManagement (and TenantOwners), run-once marker, never re-added after an admin removes it; MCP inventory summary is opt-in (`summary=false` by default) because it scans the whole tenant.
Live check 12:39: key ring active locally (keyRingConfigured=true, activeKeyId k1, no controller key errors) after starting the stack from main/octo-tools. Final full build 38/38; suites green (sdk 462, bot 344, mcp 975, identity 19+266+327+209, ai 7+242, engine 1435, asset-repo unit 263).

### Backup/restore storage (AB#5559, AB#5560, AB#5561, AB#5562–5565), 2026-10-06

| Change | Repo: commits |
|---|---|
| Encrypted dump files OCTOENC1 (`ISecretFileProtector`) | engine 90ef596c |
| Artifact storage library (FileSystem/S3/AzureBlob) | common-services 5be5af9, 1d7abe7 |
| Bot: pre-sweep dumps, tenant dumps, restore staging in the store (encrypted), pre-sweep restore job, requiredKeyIds/DumpKeyMissing | bot acf3594, ef5b8b5, 056e838, b1520ba, 0076a96, 73e610e |
| SDK/MCP/CLI restore-dump | sdk 0383660; mcp 2ffad1a; cli 242fe9f |
| Helm values (persistence, scratch, artifactStorage) | octo-helm-core 7eca5c9 |
| Per-cluster infra + values + runbooks (not applied) | meshmakers-infrastructure feat/gerald/artifact-storage 94d737d, 5b052cd, 202131a, 69777d1, c9520a1, 8d2f3fe, 4c18705; octo-mesh-deployment feat/gerald/artifact-storage e80bbc4, 935658a, 2dd85bb, 71ab58b |
| Concept, provider capabilities, decisions, handover §14 | engine fcadfd35, 93c3fce5, 9c7d8cee, e8617785 |

Decided by the PO: test-2 artifact bucket in a dedicated Hetzner project; Azure dedicated artifact storage account per cluster (phase 1 shared key, phase 2 workload identity); prod-1 own SOS bucket with bucket-scoped IAM; tenant dumps encrypted with the key ring when configured (downloads decrypt on the fly); pre-sweep dumps always encrypted, never downloadable; legacy local dumps expire under the old cleanup (no migration); retention presweep 7 d app-side / 8 d lifecycle, tenant-dumps and restore-staging 24 h / 1 d lifecycle; infra resources behind `octo_artifacts_enabled` (default false). Open: scratch volume per cluster (recommend emptyDir with sizeLimit), Hetzner location placeholder `fsn1`, live name checks.

## Open decisions (taken autonomously, to confirm)

- WP2: an omitted secret on replace is carried over; explicit `null` clears (top level); inside records `null`/`""` keep the stored value by record key, a placeholder clears.
- WP2/WP4: one strict wire format: marker `{"isSet":bool}` or `{}` = unchanged, string = new value, `null` = clear, anything else is rejected without echoing the value.
- Superseded 2026-10-06: placeholders have no API meaning anymore (decided by the user).
- Changed by the user 2026-10-06: restore on a bot without key ring runs a key-free Verify (enc:v2 with unknown kid and enc:v1 without LegacyV1Key = key missing, listed for re-entry; nothing written); status `warnings: ["NoKeyRing"]` (engine 38650199, f489b4cd; bot cdd0a06, 500299a, a6108bb; sdk 65f77e7, d39b3f9). CleanupUnreadable keeps enc:v1 values when only the legacy key is missing.
- Confirmed by the user: legacy plaintext placeholder reads as not set until the sweep; bot `{tenantId}/v1/secrets/...` routes accept only the token's own tenant.
- WP3: a corrupt envelope reads as "not set"; the index guard skips Secret paths with a warning.
- WP4: pre-existing generator issue (derived mutation DTOs inherit the parent query DTO) accepted for v1.
- WP5: wrong-typed GraphQL input for a secret field is echoed back to the caller by GraphQL.NET coercion errors (accepted; no logging).
- WP7: `ValueOverride.Value` stays String; secrets go to the new Secret member `SecretValue` (recordKey `Path`) — deviation from the concept example because `Value` carries non-secret Helm overrides seeded by four blueprints.
- WP7: a secret that cannot be decrypted ships to the adapter as null (error log) instead of failing the whole configuration; adapter-own service accounts move to impersonation with AB#5551 (installation-level adapter credential).
- WP7/WP8/WP11/WP2/WP4 (decryption oracle): ciphertext supplied as text is never decrypted — no unwrap on reveal, `encrypt-value` stays enc:v1 and refuses enc:v2, enc:v2 found as a legacy string is refused by the protector and kept opaque, `InstanceSecretCrypto` decrypts enc:v1 only.
- WP8: `RevealSecret@1` refuses identity `System`; Caller/ServiceAccount remain (real gate = AB#5128).
- WP9: the bot never offers `Decrypt`; post-restore ClearUnknownKid only after Verify found unknown kids and a pre-clear dump succeeded.
- WP10: an identity provider whose secret cannot be decrypted is skipped (tenant's other providers keep working).
- WP11: wwc26 `SecretKey` recommended NOT to convert (it is a displayed answer, not a credential).
- WP13: secret writes through MCP only via high-risk `set_entity_secrets` / `create_entity_with_secrets`; `start_secret_sweep` is high risk as a whole.
- helm-pro: the AI chart falls back to `aiInstanceSecretKey`.

## Rollout order and human actions

1. **Publishing (human):** engine release with System 2.5.0 + System.StreamData 1.17.0 first (review the `Directory.Build.targets` change in CI: CK compile after project references, local catalog for StreamData); then sdk → engine-mongodb → common-services → communication-sdk → mesh-adapter → bot → controller (System.Communication 3.40) → services; SchemaProvider image rebuild for the schema change; CI of new test projects needs the published engine.
2. **Key ring everywhere before phase 3:** Vault/ADO values for helm-core and the helm-pro charts (`communicationInstanceSecretKey`), operator `ClusterSecretsOptions`; verify `aiInstanceSecretKey == communicationInstanceSecretKey` per cluster; back up the key in Keeper.
3. **Preconditions for phase 3:** adapter-hub and operator-hub authorization `Enforce` (AB#5063/AB#5059) — adapter configurations carry revealed secrets; Studio phase-2 work (handover note) shipped; meshmakers-app / energy-community / one-time-ticket / report-services changes per `secret-app-model-changes.md` (incl. GraphQL queries filtering on secret values → IS_NULL/IS_NOT_NULL).
4. **Phase 3** model switches (System.Communication 3.40, System.Identity 2.22.0, System.Ai 3.13.0, app models with their RevealSecret@1 pipelines in the same blueprint version + DataFlow redeploy).
5. **Phase 4** sweep: bot `secret-sweep-encrypt` per environment; persistent volume for `Bot:SecretSweep:BackupStoragePath` first; Dash0 alerts on `octo.secrets.values{form="plaintext"}` and `octo.secrets.strict_mode.violations`.
6. **Phase 5** strict mode (`SecretEncryption:StrictMode=true`, bot `StrictModeSince`) 14 days after zero plaintext; pre-sweep dumps deleted after 7 days.
7. §5.4 rotations of previously exposed credentials remain human tasks.
8. **Local stack restart** needed to activate the branch builds (services were running while the packages/binaries were rebuilt).

## Remaining follow-ups

- Engine/SDK: SDK read loop can delegate to `RtSecretValueWireFormat.Read` (duplicate removal).
- octo-ai-services: XML remarks of `AiSecretAttributes.GetStoredSecret` still describe the pre-engine-fix behaviour; optional stored tail hint to avoid decrypt per list call.
- engine-mongodb: `AsQueryable` reads are not normalised (only identity user/role stores use it).
- Identity: saving a secret without key ring returns 500 instead of a clearer status.
- octo-adapter-finapi `FinApiAuthNode`, Loxone adapter: register credentials for masking (not owned).
- German docs build fails on a pre-existing duplicate apiReference sidebar label.
- Edge operator pipelines: must set `communicationInstanceSecretKey` / `operator.clusterSecrets.instanceSecretKey` from Vault (decision only).
