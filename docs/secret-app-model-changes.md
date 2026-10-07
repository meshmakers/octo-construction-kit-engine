# SECRET value type: required changes in app and adapter models (WP11, AB#5541)

> From: PO AB#5528 (SECRET value type). To: the owners of meshmakers-app, energy-community,
> octo-adapter-loxone, octo-report-services, one-time-ticket and wwc26-landing-page.
> Concept: [concept-secret-attribute-type.md](concept-secret-attribute-type.md) (§5.1, §5.2 phases 2/3, §6),
> frontend contract: [secret-frontend-handover.md](secret-frontend-handover.md),
> progress: [secret-rollout-progress.md](secret-rollout-progress.md).
> State: scanned read-only on 2026-10-06 against local `main` of every repo listed. Nothing was
> changed in these repos. octo-ai-services is implemented (last section).
> Amended 2026-10-06 for the round-2 decisions (concept §2.1): **placeholders are ordinary values**
> (seeds must leave Secret slots empty; `TODO_SET_*` / `<…>` fail the seed lint), and a stored value
> whose key id is not in the ring is **kept** and reads as `isSet: false, keyMissing: true`.

No credential values are quoted in this document. Where a seed holds a value it is described by
its form only (placeholder / empty / value).

## 0. Rules that apply to every repo below

1. **Model change (phase 3)**: `valueType: Secret` and `ownership: Secret` on the attribute
   definition; **remove `isRuntimeState: true`** from the same definition (declaring both is the
   lint error `OCTO-CK003`; Secret ownership is implied). String → Secret is a **Minor** bump
   (decision 2). The model must depend on **`System-[2.5,3.0)`** directly (decision 12, compiler
   message 70ff.).
2. **Required secrets**: an empty string no longer satisfies a required attribute, and clearing a
   required secret is refused (rule engine message 22). A secret that a flow clears or that is
   legitimately absent must be `isOptional: true` on the type (Minor).
3. **Seeds**: a Secret slot may only be **empty or omitted** (decision 9, concept §2.1 item 1).
   `<…>` and `TODO_SET_<UPPER_SNAKE>` are values now and fail the seed lint (`BlueprintSeedSecretLint`,
   message 10) — replace them with `''` or drop the attribute. Only values that are *already stored* as
   legacy plaintext placeholders are converted once to "not set" (phase-3 migration hook
   `NormalizePlaceholdersAsync`, encrypt sweep). A placeholder written through any API after the switch
   is encrypted and reads as `isSet: true`.
4. **Pipelines**:
   - `GetRtEntities*` / queries return a Secret only as the marker `{ "isSet": true|false }`.
     Reading the plaintext needs **`RevealSecret@1`** (mesh adapter). `If@1`, `SetPrimitiveValue@1`,
     `Switch@1`, `ConvertDataType@1`, `ExecuteCSharp@1`, `DataMapping@1` fail with "Secret not
     supported" on a marker; test `<path>.isSet` as Boolean instead.
   - `RevealSecret@1` refuses an attribute that is **not yet** Secret, so a pipeline switched to it
     must ship in the **same blueprint version** as the model change, and the DataFlow must be
     redeployed right after the blueprint install.
   - Configurations reached through `GetPipelineConfigByWellKnownName@1` (a `Uses` edge) keep
     working unchanged: the communication controller reveals Secret values when it ships the
     configuration to the adapter (AB#5537). They are cached until the DataFlow is redeployed.
   - Writing a plaintext with `CreateUpdateInfo@1` (`attributeValueType: String`) sets / rotates the
     secret (the engine encrypts it). **`""` now means "unchanged"**; clearing needs `null`. A
     placeholder string is an ordinary value (it would be stored encrypted and read as set).
   - Anonymous HTTP routes have no caller to read as: use `identity: ServiceAccount`
     (`System` is refused by `RevealSecret@1`).
5. **GraphQL**: a Secret is never returned. Typed field `x: OctoSecretState { isSet, keyMissing }`,
   generic projection `value: null` + `secretIsSet` + `secretKeyMissing`. `keyMissing: true` = a value is
   stored but its key is not in the ring (restore from another environment) — treat it as not set and
   ask for re-entry; required checks still see it as present. Never decide "set?" by comparing strings. Filters on a Secret allow only `IS_NULL` /
   `IS_NOT_NULL`; `IN`, `EQUALS`, `MATCH_REG_EX`, sort, search fail with `SecretAttributeNotQueryable`.
   Inputs stay `String`; `""`/omitted = unchanged; explicit clear via `clearSecretAttributes`.
6. **Order per environment**: phase 2 (consumers tolerant to both forms) before phase 3 (model
   switch). A row marked "phase 2" below must be live before System.Communication 3.41 (or the
   repo's own model change) reaches that environment.

## 1. meshmakers-app (Meshmakers.Accounting, Meshmakers.Accounting.Tesla)

Repo: `/Users/gerald/RiderProjects/meshmakers/main/meshmakers-app` (branch `main`, ea98fac).
The Meshmakers.Accounting model (3.1.0) has **no** credential attribute. The credentials the app
manages live in System.Communication types (`AiConfiguration.ApiKey`,
`MicrosoftGraphConfiguration.ClientSecret`, `EMailReceiverConfiguration.Password`,
`FinApiConfiguration.ClientSecret`/`Password`, `ServiceAccountConfiguration.ClientSecret`) and in
the Tesla add-on model.

### 1.1 Tesla model — phase 3

| File | Current | Required change |
|---|---|---|
| `src/Meshmakers.Accounting.Tesla.CkModel/ConstructionKit/attributes/teslaConfigurationAttributes.yaml` | `ClientSecret`, `RefreshToken`: `valueType: String`, `isRuntimeState: true` | `valueType: Secret`, `ownership: Secret`, drop `isRuntimeState`. Descriptions: "write the plaintext; reads only report whether it is set". |
| `src/Meshmakers.Accounting.Tesla.CkModel/ConstructionKit/types/teslaConfiguration.yaml` | `RefreshToken` required, `ClientSecret` optional | Keep. (`RefreshToken` stays required: the pipeline only ever overwrites it.) |
| `src/Meshmakers.Accounting.Tesla.CkModel/ConstructionKit/ckModel.yaml` | `Meshmakers.Accounting.Tesla-1.1.0`, `System-[2.0,3.0)` | `Meshmakers.Accounting.Tesla-1.2.0`, `System-[2.5,3.0)` |
| `src/blueprints/MeshmakersAccounting.Tesla/seed-data/configurations/tesla.yaml` | `ClientSecret`, `RefreshToken` = `<…>` placeholders | Set both to `''` (placeholders fail the seed lint since 2026-10-06). Raise header dependency `Meshmakers.Accounting.Tesla-[1.0,2.0)` → `[1.2,2.0)`. |
| `src/blueprints/MeshmakersAccounting.Tesla/blueprint.yaml` | `MeshmakersAccounting.Tesla-1.12.0`; `System-[2.0,)`, `Meshmakers.Accounting.Tesla-[1.1.0,2.0)` | `1.13.0`; `System-[2.5,)`, `Meshmakers.Accounting.Tesla-[1.2.0,2.0)`. Update the post-install text (secrets are write-only; re-apply keeps them via Secret ownership). |

### 1.2 Tesla fetcher pipeline — phase 3 (same blueprint version as 1.1)

File: `src/blueprints/MeshmakersAccounting.Tesla/seed-data/data-flows/tesla-charging-invoices.yaml`
(pipeline `67d4a2f0b2e4d8c3a1f00302`). Header dependency `Meshmakers.Accounting.Tesla-[1.0,2.0)` →
`[1.2,2.0)`.

The pipeline reads `RefreshToken` from `GetPipelineConfigByWellKnownName@1` (`$.config`). That keeps
working after the switch (the controller reveals it), **but** the configuration is a deploy-time
snapshot while Tesla rotates the refresh token on every run and the pipeline writes the new one back
(lines 73–91). The second run after a deploy would present a stale token. Read it on demand instead:

```yaml
# current (lines 62-65)
            - type: SetPrimitiveValue@1
              targetPath: $.tokenRequest.refresh_token
              valuePath: $.config.attributes.RefreshToken
              valueType: String
# required
            - type: RevealSecret@1
              description: Read the current (rotated) refresh token from the database
              ckTypeId: Meshmakers.Accounting.Tesla/TeslaConfiguration
              rtId: 67d4a2f0b2e4d8c3a1f00300
              attributeName: RefreshToken
              targetPath: $.tokenRequest.refresh_token
              identity: ServiceAccount
```

The write-back (`CreateUpdateInfo@1` lines 78–89, `attributeName: RefreshToken`,
`attributeValueType: String`, `valuePath: $.token.refresh_token`) stays as is: a plaintext string
written into a Secret is encrypted by the engine on `ApplyChanges@2`. The pipeline's service account
must be allowed to read `TeslaConfiguration` (data permissions). Redeploy the "Tesla Charging
Invoices" DataFlow after installing 1.13.0.

### 1.3 Frontend `src/meshmakers` — phase 2 (must be live before System.Communication 3.41)

The three "is the secret still unset?" queries filter with `IN [placeholder, ""]` on attributes that
become Secret in System.Communication 3.41. After the switch they fail with
`SecretAttributeNotQueryable`, and the settings cards break.

| File | Current | Required change |
|---|---|---|
| `src/app/graphQL/getAiSecretState.graphql` | `fieldFilter: [{ attributePath: "ApiKey", operator: IN, comparisonValue: $unsetValues }]` on `systemCommunicationAiConfiguration` | New document `getAiSecretUnset.graphql`: `fieldFilter: [{ attributePath: "ApiKey", operator: IS_NULL }]` → `totalCount` (1 = not set). Keep the old one as fallback until 3.41 is everywhere. |
| `src/app/graphQL/getGraphSecretState.graphql` | same with `ClientSecret` on `systemCommunicationMicrosoftGraphConfiguration` | same pattern, `IS_NULL` on `ClientSecret` |
| `src/app/graphQL/getImapPasswordState.graphql` | same with `Password` on `systemCommunicationEMailReceiverConfiguration` | same pattern, `IS_NULL` on `Password` |
| `src/app/pages/settings/ai-settings.ts` (`SECRET_UNSET_VALUES`, l. 30/97), `src/app/services/graph-credentials.service.ts` (`SECRET_UNSET_VALUES`, l. 12–15/66), `src/app/services/imap-connection.service.ts` (`PASSWORD_UNSET_VALUES`, l. 9–11/86) | compare against `TODO_SET_*` / `<…>` / `""` via the `IN` query (string comparison) | **Switch to `isSet`** (plus `keyMissing` → show "key missing — re-enter"): select the typed `OctoSecretState { isSet keyMissing }` field (or `secretIsSet` / `secretKeyMissing` in the generic projection) of the one configuration entity instead of filtering by value. Transition until 3.41 is everywhere: run the legacy `IN` query and on a GraphQL error with code `SecretAttributeNotQueryable` use `isSet`. Then delete `SECRET_UNSET_VALUES` / `PASSWORD_UNSET_VALUES` and every `TODO_SET_*` comparison for Secret attributes — a placeholder is a value now (it would read as `isSet: true`), and the seed no longer carries any (§1.4). The non-secret placeholder constants (`PLACEHOLDER_AZURE_TENANT_ID`, `PLACEHOLDER_CLIENT_ID`, `PLACEHOLDER_USERNAME`) are not affected by the value type; drop them together with the seed placeholders of those String attributes if the seed switches them to empty as well. |
| `src/app/graphQL/getTeslaConfig.graphql` + `src/app/models/tesla-config.model.ts` (`isUnset`, `clientSecretSet`, `refreshTokenSet`) | generic `attributes { items { attributeName value } }` — today this **sends ClientSecret and RefreshToken to the browser in clear text**; set-ness derived from the value | Select `attributes { items { attributeName value secretIsSet } }`; `clientSecretSet = secretIsSet ?? !isUnset(value)` (same for `refreshToken`). After phase 3 `value` is `null` and `secretIsSet` decides. |
| `src/app/pages/settings/tesla-config.ts` (`saveConfig`) | sends secrets only when entered | No change (matches the write-only contract; `""` would now mean "unchanged" anyway). |
| `src/schema.graphql` + `*.generated.ts` | String fields | Re-run codegen against the 3.41 / Tesla 1.2.0 schema (typed fields become `OctoSecretState`). |
| `src/app/graphQL/updateImapConnection.graphql`, `updateAiApiKey.graphql`, `getAiConfiguration.graphql`, `getImapConnection.graphql`, `getGraphCredentials.graphql` | already never select a secret, mutations return only `rtId` | No change. |

### 1.4 Pipelines and seeds of the base blueprint — pipelines unchanged, seeds must drop placeholders

- `src/blueprints/MeshmakersAccounting/seed-data/data-flows/*.yaml`, `data/_pipelines/*.yaml`:
  `AnthropicAiQuery@1` uses `apiKeyConfigurationName: AnthropicAiConfig`, `FinApiAuth@1` uses
  `clientSecretPath`/`passwordPath: $.config.*` from `GetPipelineConfigByWellKnownName@1`. No
  inline API key or password in any stored pipeline definition. No pipeline reads a credential
  entity with `GetRtEntities*`.
- **Seed change required (MeshmakersAccounting blueprint, same version as the 3.41 dependency bump):**
  `src/blueprints/MeshmakersAccounting/seed-data/configurations/integrations.yaml` and
  `data/_general/rt-config-email-*.yaml` hold `<…>` / `TODO_SET_*` placeholders in credential
  attributes that become Secret in System.Communication 3.41 (`ClientSecret`, `Password`, `ApiKey`).
  Replace them with empty values (`''`) — placeholders fail the seed lint since 2026-10-06 and would be
  stored encrypted as "set". Already installed tenants: stored legacy placeholders are converted to
  "not set" once by the CK migration hook / encrypt sweep; Secret ownership keeps real values on
  re-apply. The frontend must not rely on `TODO_SET_*` for those attributes any more (§1.3).

### 1.5 `scripts/om_set_secrets.ps1` — phase 3 note

Imports the secrets with `ImportRt -r`. Plaintext values are encrypted on import (fine). Changed
semantics: an empty `serviceAccount.clientSecret` writes `ClientSecret: ""`, which now means
**"keep the stored value"** instead of clearing it. For the secret-less (impersonation) case write
`value: null` or leave the attribute out and let the controller's impersonation switch clear it
(decision 7). The same applies to an empty Tesla `clientSecret`.

## 2. energy-community (EnergyCommunity.Registration)

Repo: `/Users/gerald/RiderProjects/meshmakers/main/energy-community` (branch `main`, c33caef).

### 2.1 Model — phase 3

| File | Current | Required change |
|---|---|---|
| `ck/ConstructionKit/attributes/MemberRegistrationAttributes.yaml` | `RegistrationCaptchaSecret`: `valueType: String`, `isRuntimeState: true` | `valueType: Secret`, `ownership: Secret`, drop `isRuntimeState` |
| `ck/ConstructionKit/types/RegistrationConfiguration.yaml` | assigned as `name: CaptchaSecret`, `isOptional: true` | keep |
| `ck/ConstructionKit/ckModel.yaml` | `EnergyCommunity.Registration-2.4.0`, no direct `System` dependency | `2.5.0`; add `System-[2.5,3.0)` |
| `src/blueprints/EnergyCommunity.Billing/seed-data/configurations/registration.yaml` (l. 18) | empty value | compliant; no change |
| `src/blueprints/EnergyCommunity.Billing/blueprint.yaml` | `EnergyCommunity.Billing-2.4.2`, `EnergyCommunity.Registration-[2.4,3.0)` | `2.5.0`, `EnergyCommunity.Registration-[2.5,3.0)` (+ the same floor in the data-flow seed header, `member-registration.yaml` l. 6) |

### 2.2 Registration pipeline — phase 3 (same blueprint version)

Files (kept in sync by `sync-pipelines.py`):
`src/blueprints/EnergyCommunity.Billing/seed-data/data-flows/member-registration.yaml` (l. 194–226)
and its mirror `src/blueprints/pipelines/Billing/member-registration.yaml` (l. 158–190). The route
is anonymous (`FromHttpRequest@2`, `allowAnonymous: true`).

After the switch `GetRtEntitiesByType@1` yields `CaptchaSecret` as `{ "isSet": … }`; the `If@1 …
RegexMatch "\S"` and the `SetPrimitiveValue@1` that copies the secret both fail with "Secret not
supported" — public registration would stop.

```yaml
# current
        - type: If@1
          description: a CAPTCHA secret is configured
          path: $.registrationConfig.Items[0].Attributes.CaptchaSecret
          operator: RegexMatch
          value: "\\S"
          valueType: String
          ...
                - type: SetPrimitiveValue@1
                  targetPath: $.captcha.request.secret
                  valuePath: $.registrationConfig.Items[0].Attributes.CaptchaSecret
                  valueType: String
# required
        - type: If@1
          description: a CAPTCHA secret is configured
          path: $.registrationConfig.Items[0].Attributes.CaptchaSecret.isSet
          operator: Equals
          value: true
          valueType: Boolean
          ...
                - type: RevealSecret@1
                  description: The Turnstile secret, read in process and masked in diagnostics
                  ckTypeId: EnergyCommunity.Registration/RegistrationConfiguration
                  rtIdPath: $.registrationConfig.Items[0].RtId
                  attributeName: CaptchaSecret
                  targetPath: $.captcha.request.secret
                  identity: ServiceAccount
```

The existing `Project@1` that drops `$.captcha.request` stays (the response must not echo the
secret; `RevealSecret@1` also masks it in debug output and execution results). Redeploy the "Member
Registration API" DataFlow after installing Billing 2.5.0.

### 2.3 Frontend `src/energy-community` — phase 2

| File | Current | Required change |
|---|---|---|
| `src/app/graphQL/getRegistrationCaptchaState.graphql` | `{ attributePath: "CaptchaSecret", operator: MATCH_REG_EX, comparisonValue: "\\S" }` | Add `getRegistrationCaptchaSet.graphql` with `{ attributePath: "CaptchaSecret", operator: IS_NOT_NULL }`. In `src/app/datasources/registration-configuration.datasource.ts` (`loadCaptchaState`) run the legacy query and fall back to the new one on `SecretAttributeNotQueryable` (before the switch `IS_NOT_NULL` would count the seeded `""` as set). Remove the fallback after rollout. |
| `src/app/graphQL/updateRegistrationCaptchaSecret.graphql` | selects only `rtId` | No change — already does not echo the secret (requirement "captcha mutation must not echo" is met). |
| `src/app/graphQL/getRegistrationConfiguration.graphql`, `updateRegistrationConfiguration.graphql` | never select the secret | No change. |
| `schema.graphql`, `graphQL/globalTypes.ts` | `captchaSecret: String` | Re-run codegen after 2.5.0 (`captchaSecret: OctoSecretState` on the output type, input stays `String`). |

### 2.4 Other findings (not covered by the value type)

- `src/blueprints/EnergyCommunity.EdaIntegration/seed-data/adapters/eda-adapter.yaml` (l. 85–93):
  `System.Communication/AdapterConfiguration` is a JSON **String** that carries
  `EdaHttpAdapter.Password` (empty in the seed). A password an operator enters there is stored and
  returned in clear text. Follow-up for the EC owner: move it to a configuration entity with a
  Secret attribute or to a `ValueOverride` with `IsSecret: true` (`SecretValue`, System.Communication
  3.41).
- No pipeline reads a System.Communication credential entity with `GetRtEntities*`; all use
  `GetPipelineConfigByWellKnownName@1`. `ValueOverride` seeds (`EnergyCommunity.App`) contain only
  `IsSecret: false` entries.

## 3. octo-adapter-loxone (Loxone CK + edge adapter)

Repo: `/Users/gerald/RiderProjects/meshmakers/main/octo-adapter-loxone` (branch `main`, d840ef9).

The Loxone CK attribute `Password` (`src/AdapterEdgeLoxone.CkModel/ConstructionKit/attributes/connection.yaml`)
is defined but **assigned to no type** (`Miniserver` uses only `Host`/`Port`). The real credential is
`System.Communication/LoxoneConfiguration.Password`, which becomes Secret in System.Communication
3.41 (WP7) and reaches the adapter revealed through `GlobalConfiguration`.

| File | Current | Required change | Phase |
|---|---|---|---|
| `src/AdapterEdgeLoxone.CkModel/ConstructionKit/attributes/connection.yaml` | `Password: valueType: String` (unused) | `valueType: Secret`, `ownership: Secret` so it cannot be reused as a clear-text attribute (removing it would be Major). | 3 |
| `src/AdapterEdgeLoxone.CkModel/ConstructionKit/ckModel.yaml` | `Loxone-4.9.0`; `Basic-[2.0,)`, `System.Communication-[3.12,)` | `Loxone-4.10.0`; add `System-[2.5,3.0)` | 3 |
| `src/AdapterEdgeLoxone/Nodes/LoxoneConnectionNodeConfiguration.cs` (`LoxoneConnectionResolver.Resolve`), `Nodes/LoxonePollTriggerNode.cs` (l. 76–96), `Nodes/FromLoxoneStateChangeNode.cs` (`ExtractConnectionConfig`), `Nodes/LoxoneBrowseNode.cs` | password resolved from `passwordPath`, inline `password` or `GlobalConfiguration` | Call `nodeContext.RegisterSecret(password)` (communication-sdk AB#5538) right after resolving, so debug snapshots, dry runs and execution errors mask it. Deprecate the inline `password` node property (an inline password is stored in clear text in the pipeline definition); prefer `passwordPath` / `configurationName`. | 2 |
| `src/AdapterEdgeLoxone/Services/LoxoneAdapterService.cs`, `Nodes/LoxoneGlobalConfiguration.cs` | read `Password` from the revealed configuration | No change (needs the controller with `SerializeWithRevealedSecrets`, phase 2 of WP7). | — |

No seed or pipeline in the repo contains a Loxone password.

## 4. octo-report-services (System.Reporting)

Repo: `/Users/gerald/RiderProjects/meshmakers/main/octo-report-services` (branch `main`, 5eb13cb).
The service hosts the runtime engine (`AddRuntimeEngine`), the key ring is delivered by the
octo-helm-pro reporting chart (ef37529).

| File | Current | Required change |
|---|---|---|
| `src/ReportingCkModel/ConstructionKit/attributes/report-attributes.yaml` | `ConnectionString: valueType: String` | `valueType: Secret`, `ownership: Secret` |
| `src/ReportingCkModel/ConstructionKit/types/connectionInfo.yaml` | `ConnectionString` required | keep (created with a value by the designer) |
| `src/ReportingCkModel/ConstructionKit/ckModel.yaml` | `System.Reporting-2.2.0`, `System-[2.0,3.0)` | `System.Reporting-2.3.0`, `System-[2.5,3.0)` |
| `src/ReportingServices/Storage/OctoSettingsStorage.cs` | `GetConnections`: `ConnectionString = x.ConnectionString`; `AddConnection`: `ConnectionString = connectionInfo.ConnectionString` | `GetConnections`: `ConnectionString = protector.Unprotect(x.ConnectionString, new SecretAccessContext(tenantId, "System.Reporting/ConnectionInfo", "ConnectionString"))` (inject `ISecretAttributeProtector`; `null` when not set). `AddConnection`: `ConnectionString = RtSecretValue.Pending(connectionInfo.ConnectionString)` — the generated property becomes `RtSecretValue?`, so this does not compile until changed. Add `OctoSettingsStorage` to the decrypt allowlist. |
| `src/ReportingServices/Services/SettingsService.cs` | `GetRtEntitiesByTypeAsync<RtConnectionInfo>` / `InsertOneRtEntityAsync` | No change. |

Phase: model + code ship together in one service release (phase 3; the code does not compile against
2.2.0). To verify by the owner: whether the Telerik Web Report Designer returns shared connection
strings to the browser through its connections endpoint — if so, that is an existing exposure the
value type does not close; the designer should get connection names only.

## 5. one-time-ticket (Demo.Tickets)

Repo: `/Users/gerald/RiderProjects/meshmakers/main/one-time-ticket` (branch `main`, e3e698b).
The HTTP routes are called anonymously (the BFF `app/server/server.js` forwards without credentials).

### 5.1 Model — phase 3

| File | Current | Required change |
|---|---|---|
| `ck/ConstructionKit/attributes/ticket.yaml` | `TicketSecret: valueType: String` | `valueType: Secret`, `ownership: Secret` |
| `ck/ConstructionKit/types/ticket.yaml` | `Secret` required | **`isOptional: true`** — the redeem flow clears it, and clearing a required secret is refused (message 22) |
| `ck/ConstructionKit/ckModel.yaml` | `Demo.Tickets-1.0.1`, `System-[2.0,3.0)` | `Demo.Tickets-1.1.0`, `System-[2.5,3.0)` |
| `blueprint/OneTimeTicket.Release/blueprint.yaml`, `blueprint/OneTimeTicket.MainLatest/blueprint.yaml` | `1.0.5`, `Demo.Tickets-[1.0.1,2.0.0)` | `1.1.0`, `Demo.Tickets-[1.1.0,2.0.0)` |

### 5.2 Redeem pipeline — phase 3 (same blueprint version)

Files: `blueprint/OneTimeTicket.Release/seed-data/entities.yaml` and
`blueprint/OneTimeTicket.MainLatest/seed-data/entities.yaml` (identical, l. 247–277).

```yaml
# current
                    - type: SetPrimitiveValue@1
                      description: Capture the secret for the response
                      valuePath: $.lookup.Items[0].Attributes.Secret
                      valueType: String
                      targetPath: $.secret
# required
                    - type: RevealSecret@1
                      description: Capture the secret for the response
                      ckTypeId: Demo.Tickets/Ticket
                      rtIdPath: $.lookup.Items[0].RtId
                      attributeName: Secret
                      targetPath: $.secret
                      identity: ServiceAccount

# current (burn)
                        - attributeName: Secret
                          attributeValueType: String
                          value: ""
# required ("" now means "unchanged" - the secret would survive the redeem)
                        - attributeName: Secret
                          attributeValueType: String
                          value: null
```

Note for the owner: `RevealSecret@1` registers the value as a secret, so it is masked in debug
snapshots and execution results, while the HTTP response (`Project@1` → `$.secret`) still carries it
— that is the purpose of the route. Add a pipeline test that the stored value is `null` after a
redeem. The create pipeline (`CreateUpdateInfo@1` with `valuePath: $.body.secret`) and the list
pipeline need no change.

## 6. wwc26-landing-page (Wwc26.Challenge)

Repo: `/Users/gerald/RiderProjects/meshmakers/main/wwc26-landing-page` (branch `main`, 9aeea2d).

`Submission.SecretKey` (`ck/ConstructionKit/attributes/submission.yaml`) is the treasure-hunt answer a
participant submits; the jury reads it: `src/we-are-developers-landing-page/src/app/graphQL/getSubmissions.graphql`
selects `secretKey`, `pages/submissions/submissions.html` shows it, `submissions.ts` has a grid column
and `submissions.util.ts` exports it to CSV. It is not a credential to a system.

**Decided 2026-10-06: not converted** (concept §2.1 item 3). Background: converting makes the value unreadable to the jury
(the UI and CSV would show nothing) and would need a privileged reveal pipeline for a puzzle answer.
Recommendation: keep `String`, remove it from the §5.1 inventory, and if wanted rename it later
(Major) to avoid the misleading name. If the PO decides to convert anyway: `valueType: Secret`,
`ownership: Secret`, `Wwc26.Challenge-1.3.0` with `System-[2.5,3.0)`, and a jury-only HTTP pipeline
(`requiredRoles` = jury) that uses `RevealSecret@1` per submission instead of the GraphQL field;
`data/_general/rt-dataflow-submission.yaml` (create, l. 239–241) needs no change.

## 7. Summary per repo

| Repo | Model bump | Blueprint bump | Pipeline change | Frontend / code change | Phase |
|---|---|---|---|---|---|
| meshmakers-app | Meshmakers.Accounting.Tesla 1.1.0 → 1.2.0 | MeshmakersAccounting.Tesla 1.12.0 → 1.13.0; MeshmakersAccounting: seed placeholders → empty | Tesla fetcher: `RevealSecret@1` for RefreshToken | 3 state checks → `isSet`/`keyMissing` (no string comparison) with fallback; Tesla `secretIsSet`; codegen; `om_set_secrets.ps1` clear semantics | frontend 2, model+pipeline+seed 3 |
| energy-community | EnergyCommunity.Registration 2.4.0 → 2.5.0 | EnergyCommunity.Billing 2.4.2 → 2.5.0 | member-registration: `.isSet` + `RevealSecret@1` | captcha state query → `IS_NOT_NULL` with fallback; codegen | frontend 2, model+pipeline 3 |
| octo-adapter-loxone | Loxone 4.9.0 → 4.10.0 (unused definition) | — | — | `RegisterSecret` in the Loxone nodes; deprecate inline `password` | code 2, model 3 |
| octo-report-services | System.Reporting 2.2.0 → 2.3.0 | — | — | `OctoSettingsStorage` reveal / pending write | 3 (one release) |
| one-time-ticket | Demo.Tickets 1.0.1 → 1.1.0 (`Secret` optional) | OneTimeTicket.* 1.0.5 → 1.1.0 | redeem: `RevealSecret@1`, burn with `null` | — | 3 |
| wwc26-landing-page | none (decided: not converted) | — | — | — | — |
| octo-ai-services | System.Ai 3.12.0 → 3.14.0 (3.13.0 is the System 2.5 re-pin) | — (seeds none) | — | implemented, see §8 | 3 (code tolerates both forms) |

## 8. octo-ai-services — implemented (AB#5541)

Branch `feat/gerald/secret-attribute-type`, commit `ab93f44`:
- System.Ai 3.14.0 (`System-[2.5,3.0)`): `AiTokenLease.AccessToken` / `RefreshToken` /
  `TrustedDeviceToken` (now optional) and `AiCredentialBinding.EncryptedValue` are Secret. No seeds.
- Writes hand the plaintext to the engine; reads use `ISecretAttributeProtector` (enc:v2 and the
  enc:v1 values written before the switch). No second decrypt of ciphertext supplied as text.
- REST responses never contained the values (status DTOs); the GitHub-PAT / env-secret lists still
  show a masked tail computed server-side, only for values of at least 16 characters (review fix 18d1fee); a stored hint without decrypt is an open decision.
- Key ring: `AddRuntimeEngine` binds `SecretEncryption:*`; without an explicit ring the service
  derives `k1` + `LegacyV1Key` from `AiEncryption:InstanceSecretKey` (mirrors the chart fallback).

## 9. Platform findings from this WP

- **Typed point reads do not normalise legacy strings** (engine-mongodb, AB#5533 follow-up):
  `RuntimeRepositoryBase.GetRtEntityByRtIdAsync<T>` reads through `DocumentAsync` without
  `SecretAttributeReadNormalizer`, so a legacy string (clear text or enc:v1) in a Secret slot stays a
  plain `string` in `Attributes`. Writing such an entity back (`CreateUpdate(id, entity)`) makes the
  write path treat it as new input and encrypt the enc:v1 text as plaintext — the value is then lost
  to every reader (the no-double-unwrap rule is correct; the read path is the gap). The query paths
  (`GetRtEntitiesByTypeAsync<T>` etc.) normalise. **Fixed** in engine-mongodb 96e6c9c (all collection
  reads incl. point reads, upsert and change streams normalise). octo-ai-services additionally keeps
  raw legacy strings opaque on write-back (`AiSecretAttributes.PrepareLegacySecretForWriteBack`, 18d1fee);
  enc:v2 text in a legacy slot is never decrypted or promoted (engine cce531b3).
- **octo-adapter-finapi** `FinApiAuthNode` resolves `Password` / `PasswordPath` without `nodeContext.RegisterSecret`; same follow-up as §3 for its owner. It also accepts an
  inline `password`; none of the scanned pipelines use it.

## Version rule after the System 2.5 repins (decided 2026-10-06, G1-a)

Wave 1 repins every model that pinned System 2.4 to `System-[2.5,3.0)` with a **minor** bump (CI `ValidateVersion` rule). Every phase-3 SECRET model change in this document therefore takes the **next minor after the repin version**, e.g. System.Reporting 2.3.0 (repin) → 2.4.0 (Secret `ConnectionString`), Meshmakers.Accounting.Tesla 1.3.0 (repin) → 1.4.0, Demo.Tickets / EnergyCommunity.Registration / Wwc26.Challenge likewise. The repin versions per model are listed in `wave1-push-pr-plan-backend.md` §4.5.
