# Durable storage for pre-sweep dumps (AB#5539)

Status: concept, 2026-10-06. Scope: where `octo-bot-services` keeps the tenant dump it takes before every
writing secret sweep, and how that generalizes to a platform-level artifact store. Parent concept:
`concept-secret-attribute-type.md` (decision 10: pre-sweep plaintext backups kept 7 days, treated as
secrets, then deleted).

## 1. Current state

**Bot implementation** (`octo-bot-services`, branch `feat/gerald/secret-attribute-type`):

- `SecretSweepCoordinator.TakeBackupAsync` runs `mongodump` in the bot pod (`systemContext.BackupTenantAsync`)
  for every writing mode (`Encrypt`, `Reprotect`, `CleanupUnreadable`) into
  `Bot:SecretSweep:BackupStoragePath/<tenant>/<tenant>-<utc>-<guid>.presweep.tar.gz`.
  If the dump fails, the tenant is skipped (`RequirePreSweepBackup=true`).
- `BackupFileStorageService` creates the directories as `0700` and the file as `0600`, and refuses a
  path inside the tus or dump roots, because the hourly cleanup deletes those after a few hours.
- Retention: `CleanupStaleFilesJob` (hourly) deletes dumps older than `BackupRetentionDays` = 7 and
  writes `deletedAt` into the run history. `DELETE {tenant}/v1/secrets/sweep-runs/{runId}/dump` deletes a
  dump early. A dump can never be downloaded, and **no restore path for a pre-sweep dump exists yet**.
- Default path: `Path.GetTempPath()/octo-bot/secret-backups`. The chart sets no override, so the dump sits
  in the container's writable layer. That means:
  - the dump is **lost** on every restart, reschedule or rollout. The bot uses the `Recreate` strategy, so
    every helm upgrade loses it;
  - the dump uses the **node's disk**. On test-2 the node disks are already at 75–85 %, and parallel image
    pulls have hit ENOSPC there before, so one large tenant dump can push a node into disk pressure;
  - only the pod that wrote the dump can see it. Today that is fine, because the bot runs 1 replica.
- The first `Encrypt` dump of each environment holds **secret plaintext**. Later dumps (`Reprotect`,
  `CleanupUnreadable`) hold `enc:v2` ciphertext plus every other piece of tenant data.

**Tenant dumps and restores share the same weakness.** `DumpRepositoryJob` writes
`.tar.gz` / `.octobak.zip` files to `Bot:DumpStoragePath`, and tus uploads that a restore stages go to
`Bot:TusStoragePath`. Both default to the temp directory, are served or consumed by the same pod, and are
gone after a restart. A download or restore that crosses a pod restart fails.

**Chart** (`octo-helm-core/src/octo-mesh`, `main` and `feat/gerald/secret-key-ring`): `services.bot`
runs `replicaCount: 1` with `recreateStrategy: true` (exclusive RabbitMQ queues). There is no PVC
template and no securityContext; the image runs as root. A generic `pod.volumes` / `pod.volumeMounts`
pass-through exists. There is no env value for the storage paths; the setting would be
`OCTO_BOT__SECRETSWEEP__BACKUPSTORAGEPATH`. test-2 runs three instances (main, dev, release), so it has
three bot deployments.

**Existing DR backups we can use as a template** (`meshmakers-infrastructure`,
`docs/MONGODB-BACKUP-RESTORE.md`, `docs/CONCEPT-CRATEDB-DISASTER-RECOVERY.md`, group_vars, terraform):

| Cluster | MongoDB DR (daily 03:00, keep last 5) | CrateDB DR snapshots | Object storage available | Credentials (Vault) |
| --- | --- | --- | --- | --- |
| test-2 (Proxmox RKE2) | PVC on `smb-hetzner` (Hetzner Storage Box, SMB `vers=2.1`, mount forces `uid=10001`, `file_mode=0664`) | Hetzner Object Storage (S3), bucket `meshmakers`, `cratedb/` | Hetzner Object Storage | `secret/meshmakers/test-2/hetzner-s3` |
| staging-1 (AKS) | Azure File Share `mongodb-backups` (RWX) | Azure Blob `cratedb-backups` | Storage account `mmstaging1backups` (Standard LRS, account key, no lifecycle policy, no workload identity) | `secret/meshmakers/staging-1/azure-storage` |
| prod-2 (AKS) | Azure File Share `mongodb-backups` (RWX) | Azure Blob `cratedb-backups` | Storage account `meshmakersprod2backups` (same set-up as staging-1) | `secret/meshmakers/prod-2/azure-storage` |
| prod-1 (Exoscale SKS) | PVC `exoscale-sbs` (RWO), then upload to SOS `mongodb/` | SOS `cratedb/` (keep 7 daily + 4 weekly) | SOS bucket `prod-1-backups`; the IAM key is scoped to that one bucket | `secret/meshmakers/prod-1/sos` |

Storage classes: test-2 has `proxmox-data-xfs` (RWO, local SSD) and `smb-hetzner` (RWX). AKS has
`default` / managed-csi (RWO, Azure Disk) and azurefile-csi (RWX). prod-1 has `exoscale-sbs` (RWO) only,
with no RWX class. I found no MinIO, Longhorn or NFS on any cluster. Retention in the DR jobs is enforced
by the jobs themselves (count-based); no bucket or container has a lifecycle rule.

## 2. Requirements

1. **Durable for the retention period.** The dump survives pod restarts, reschedules, rollouts and node
   loss for 7 days.
2. **Reachable from every bot replica.** Hangfire can run the sweep, the hourly cleanup and the delete
   endpoint on different servers. One replica works with RWO; more than one needs RWX or object storage.
3. **Confidential.** The dump is secret material. Encryption at rest is required, access is limited to the
   bot's identity, the dump is never downloadable, and it is not copied into other backups (a volume
   snapshot or backup would outlive the retention).
4. **Guaranteed deletion.** The app deletes the dump after 7 days or earlier on request. An independent
   backstop must delete it even when the bot is down or misconfigured.
5. **No node-disk pressure.** Large dumps must not land on the node's root or overlay disk.
6. **Per-environment parity.** One mechanism across test-2, staging-1, prod-1 and prod-2 (S3 or Azure Blob).
7. **Restorable.** An operator can turn a dump back into a tenant (see open question Q1).

## 3. Options per environment

| Option | test-2 | staging-1 / prod-2 | prod-1 | Assessment |
| --- | --- | --- | --- | --- |
| A. Status quo (container temp) | yes | yes | yes | Fails 1, 4 (no backstop) and 5. |
| B. PVC RWO (block) | `proxmox-data-xfs`, not encrypted at rest (local SSD, no LUKS) | managed-csi (Azure Disk, SSE at rest by default) | `exoscale-sbs` (encryption at rest: **verify**) | Meets 1 for a single replica (works with `Recreate`). Fails 2 for more than one replica. A zone-bound disk can block rescheduling. Any volume snapshot must be excluded. No backstop beyond the app. Cost: one small disk, about €1–5/month. |
| C. PVC RWX (file share) | `smb-hetzner`: off-cluster and shared, but SMB 2.1 (no transport encryption), and the mount forces `0664`/`uid 10001`, so `0600` is a no-op | azurefile-csi (SSE at rest, SMB 3 in transit). SMB also ignores `chmod` | not available (would need our own NFS server) | Meets 2, but not on prod-1. File permissions do not protect anything. No lifecycle rules on file shares. |
| D. Object storage | Hetzner Object Storage (S3), already in use | Azure Blob in the existing backup account, new container | Exoscale SOS, new bucket (the existing IAM key covers all of `prod-1-backups`) | Meets 1, 2, 5 and 6. Server-side encryption at rest (Azure SSE always; SOS and Hetzner: **verify**). Lifecycle expiry acts as the deletion backstop (Azure: yes, runs about daily; Hetzner and SOS: **verify** support; if a store lacks it, a CronJob takes over). Cost: dump volume × 7 days at about €0.02/GB-month, i.e. cents to low euros. Hetzner is flat-rate and already paid. |

Cost does not decide between these options; every one stays in the single-digit euros per month.

## 4. Recommendation: a platform artifact store per cluster

Use **option D as a platform service, not as a bot special case.** Each cluster gets one object-storage
target for operational artifacts. It is separate from the DR buckets, because DR and operational
artifacts have different retention, principals and blast radius.

- **Target per cluster:** test-2 → Hetzner Object Storage bucket `test-2-octo-artifacts`.
  staging-1 / prod-2 → container `octo-artifacts` in `mmstaging1backups` / `meshmakersprod2backups`
  (blob versioning and soft delete off for this container's account scope, see §5). prod-1 → SOS bucket
  `prod-1-octo-artifacts` with its own IAM role and key limited to that bucket.
- **Layout:** `<instancePrefix>/<kind>/<tenantId>/<file>`, where kind is `presweep`, `tenant-dumps` or
  `restore-staging`. The instance prefix keeps test-2 main, dev and release apart. The tenant id is
  lower-cased and validated as today (`ResolveTenantDirectory` rules).
- **Lifecycle rules per prefix (backstop, not primary):** `*/presweep/` expires after 8 days (retention +
  1 day, because lifecycle runs once a day). `*/tenant-dumps/` and `*/restore-staging/` expire after
  1 day, matching `FileRetentionHours`. For S3 versioned buckets, add a noncurrent-version expiry or keep
  versioning off.
- **Credentials:** phase 1 uses static, bucket-scoped keys from Vault
  (`secret/meshmakers/<cluster>/octo-artifacts`), rendered by the chart into the backend secret. This is
  the same pattern the DR jobs use. Later, AKS workload identity (needs `oidc_issuer_enabled` and
  `workload_identity_enabled` in terraform, neither is set today) and an Exoscale IAM key per service.
- **Bot code:** an `IArtifactStore` abstraction (`LocalFileSystem` for dev and the PVC fallback, `S3` for
  Hetzner and SOS, `AzureBlob`) behind `IBackupFileStorageService`. `mongodump` keeps writing to a local
  scratch volume (`emptyDir` with `sizeLimit`, ideally a generic ephemeral volume, so the dump never
  touches the node's root disk). The bot encrypts the file, uploads it, and deletes the scratch copy.
  Cleanup, run-history `Exists`/`SizeBytes` and the delete endpoint work against the store. Streaming
  `mongodump --archive` straight into a multipart upload is a later optimization.
- **Same store for tenant dumps and restores:** the job result keeps a store key instead of a pod-local
  path. Downloads stream from the store, and tus uploads are moved into `restore-staging/` when they
  complete. Downloads and restores then survive restarts and any number of replicas.

**Why not a PVC per service:** a PVC per service and cluster means 4 storage-class variants, no RWX on
prod-1, permissions that SMB ignores, no lifecycle backstop, and a separate answer for tenant dumps.
Object storage already exists on every cluster and is already wired to Vault.

**Interim, if the first `Encrypt` sweep has to run before the store exists:** a single RWO PVC for the bot
(chart value `services.bot.persistence`, see the work items), mounted at `/data/octo-bot` and with
`OCTO_BOT__SECRETSWEEP__BACKUPSTORAGEPATH=/data/octo-bot/secret-backups`, plus dump-file encryption
(§5). This is acceptable because the bot stays at 1 replica with `Recreate`. Exclude the PVC from any
volume-snapshot or backup tooling.

## 5. Security

**Encrypt the dump itself, in addition to encryption at rest. Recommended for every option.**

- Format `*.presweep.octoenc`, envelope encryption:
  - a random 256-bit data key per dump;
  - the body in chunks (e.g. 1 MiB), each sealed with AES-256-GCM: nonce = random prefix ‖ chunk counter,
    a last-chunk flag and the header bound as AAD (the standard streaming-AEAD construction; a single
    GCM call cannot cover multi-GB dumps);
  - the data key wrapped with AES-256-GCM under a key-encryption key derived as
    `HKDF-SHA256(ring[kid], info="octo/dump/v1")`, so a ring key is never used directly for a second
    purpose;
  - the header records magic, version, `kid`, wrapped key and chunk size.
- The bot already holds the key ring (`ISecretAttributeProtector`, active kid). The header names the
  `kid` that encrypted the dump.
- Effect: storage, SMB permissions and the object store's own encryption no longer have to be trusted.
  A leaked bucket key or file-share mount does not expose secrets. For the first `Encrypt` dump, the
  plaintext secrets are protected by the same key that protects them in the database, so the dump is
  never weaker than the live data.
- **Key loss = dump unreadable. That is acceptable** given 7-day retention. Rule for rotation: do not
  remove a `kid` from the ring until its newest dump has expired (≥ 7 days after the `Reprotect` run).
  This tightens the "remove the source key again" step in concept §6.
- If the ring is not configured, the sweep cannot write anyway (`Encrypt` needs a key). Writing modes
  therefore always have a key for the dump.

**Access:** only the bot identity holds store credentials, scoped to the artifact bucket or container.
The DR keys stay separate. There is no download endpoint. The store must not be public, and Azure
containers use `private` access. Avoid SAS URLs.

**Guaranteed deletion:** the app's hourly cleanup is primary. The lifecycle rule (8 days) is the
backstop. Blob soft delete, versioning and immutability (WORM) policies must be **off** for this target,
because they would keep "deleted" dumps or block the early-delete endpoint. Dumps must not be included in
volume snapshots, Velero or DR backups.

**Related finding (out of scope, follow-up):** for the days after the `Encrypt` sweep, the daily MongoDB
DR backups (keep last 5, locally and on SOS) still hold plaintext secrets from before the sweep. That
satisfies decision 10 only if the operator accepts "≤ 5 days" for DR copies, or trims them after the
sweep.

## 6. Migration / rollout

1. **Bot:** dump-file encryption (`.octoenc`), plus a restore path for a pre-sweep dump (Q1), behind the
   current local-file storage. Can ship without infrastructure.
2. **Chart:** a `services.bot.persistence` (PVC) option and a `services.bot.artifactStore` block (type
   `s3`/`azureBlob`, endpoint, bucket or container, secret refs, instance prefix), plus a scratch
   `emptyDir` with `sizeLimit`. Both default to off, so the chart behaves as today.
3. **Infrastructure per cluster:** bucket or container, scoped credentials in Vault, lifecycle rules,
   no versioning or soft delete. Order: test-2 pilot → staging-1 → prod-1 → prod-2. Each step is
   verified with a test tenant: run `Encrypt`, restart the bot pod, the dump still exists, early delete
   works, and the lifecycle rule is visible.
4. **Bot:** the `IArtifactStore` implementations and moving tenant dumps and restore staging onto the store.
5. **Run the environment's first `Encrypt` sweep only after step 1, plus step 3 or the interim PVC, is
   live on that cluster.**

## 7. Open questions

- **Q1 Restore of a pre-sweep dump:** a bot admin job (decrypt as a stream into `mongorestore --archive`,
  role `SecretManagement`, `confirm=true`, same tenant only), or an ops tool (octo-cli / Semaphore)? The
  bot job is preferred because the key never leaves the bot.
- **Q2:** Do Hetzner Object Storage and Exoscale SOS support lifecycle expiration and server-side
  encryption at rest? Is `exoscale-sbs` encrypted at rest? If a store has no lifecycle support, a small
  CronJob takes over the backstop.
- **Q3:** Doc and terraform disagree on names: SOS bucket `prod-1-backups` (terraform and group_vars) vs
  `meshmakers-prod-1-backups` (MongoDB doc), and staging account `mmstaging1backups` (terraform) vs
  `meshmakersstaging1backups` (CrateDB doc). The live names need confirming.
- **Q4:** Are the existing backup storage accounts acceptable for the new container (account-wide soft
  delete and versioning settings), or does each AKS cluster need a dedicated account?
- **Q5:** Workload identity on AKS: when? Static keys are phase 1.
- **Q6:** Plaintext in DR backups after the `Encrypt` sweep (§5): accept, or trim?

## Work items (created 2026-10-06)

| WI | Scope | Epic |
|---|---|---|
| AB#5559 | Bot: encrypt pre-sweep dumps with the key ring, restore job | 4969 |
| AB#5560 | octo-mesh chart: bot persistence and artifact-store values | 2157 |
| AB#5561 | Platform artifact store (pre-sweep dumps, tenant dumps, restore staging) | 2157 |
| AB#5562 | test-2: artifact bucket + bot configuration (pilot) | 2157 |
| AB#5563 | staging-1: artifact container + bot configuration | 2157 |
| AB#5564 | prod-1: SOS artifact bucket + bot configuration | 2157 |
| AB#5565 | prod-2: artifact container + bot configuration (last; gate for the first Encrypt sweep) | 2157 |
| AB#5566 | Plaintext in MongoDB DR backups after the Encrypt sweep — **decided 2026-10-06 (user): existing DR backups are kept; the residual risk of legacy plaintext in DR backups for up to their retention after the Encrypt sweep is accepted.** Closed. | 4969 |

## Decision 2026-10-06 — DR backups

Existing MongoDB DR backups are kept unchanged. Backups taken before an environment's Encrypt sweep still contain legacy plaintext secrets until they age out under the normal DR retention; this residual risk is accepted (AB#5566, closed). No trimming of DR backups.

## Provider capabilities (verified 2026-10-06)

Answers Q2 and Q3 and part of Q4. Sources are official vendor docs; all were accessed on 2026-10-06.
Bucket and account names come from a read-only look at the local infra repos (`meshmakers-infrastructure`
at `b6a17b9`; `infrastructure-helm-charts`, `maintenance-deployment` and `octo-mesh-deployment` define no
object storage). No live system was queried, so live names still need one `aws s3 ls` / `az storage account
show` check during AB#5562–5565.

### Summary

| Capability | Exoscale SOS (prod-1) | Hetzner Object Storage (test-2) | Azure Blob (staging-1, prod-2) |
| --- | --- | --- | --- |
| Lifecycle expiry by prefix | **Yes** (`Expiration.Days` + `Filter.Prefix`, runs daily at 00:00 UTC). One contradicting doc page, see notes | **Yes** (`Expiration.Days` + prefix filter, via `put-bucket-lifecycle-configuration`) | **Yes** (`prefixMatch` + `delete.daysAfterCreationGreaterThan` / `daysAfterModificationGreaterThan`) |
| Encryption at rest by default | **Yes**: SSE-SOS (AES-256, Exoscale-managed keys) is on by default for **new** buckets. Older buckets need `put-bucket-encryption` | **No**: "no default data-at-rest encryption". Only SSE-C (customer key on every request) | **Yes**: always on, cannot be disabled, AES-256, Microsoft-managed keys by default |
| Credentials limited to a single bucket/container | **Yes**: IAM v3 role with `parameters.bucket == '<bucket>'` plus an API key bound to that role (prod-1 already does this for `prod-1-backups`) | **Only through a bucket policy**: keys are valid for every bucket in the project. A bucket policy that denies everyone except the key's ARN restricts the bucket, but the key itself still reaches all other buckets in the project | **Not with account keys** (full access to the whole account). **Yes** with Entra ID: an RBAC role (`Storage Blob Data Contributor`) at container scope, used by workload identity, or a user-delegation SAS |
| Versioning default | Off (un-versioned until enabled) | Off, but forced on and permanent for buckets created with Object Lock | Off; account-level setting. The portal does not turn it on by default |
| Soft delete / WORM default | No soft delete; legal hold only on request | Object Lock only if chosen at bucket creation | Blob soft delete is **on by default when the account is created in the portal**, off when created with CLI, PowerShell or ARM/Terraform without `deleteRetentionPolicy`. Account-wide, not per container |

### Exoscale SOS (prod-1)

- **Lifecycle:** supports expiration after N days, prefix (and object-size) filters,
  `NoncurrentVersionExpiration`, `AbortIncompleteMultipartUpload` and `ExpiredObjectDeleteMarker`. Storage-class
  transitions are not supported. Rules are set with the S3 `put-bucket-lifecycle-configuration` call or
  `exo storage bucket lifecycle set`, and every rule needs a `Filter` (it may be empty). "Bucket Lifecycle
  evaluation runs every day at midnight UTC. There might be some delay between evaluating a bucket's
  configuration and deleting the respective objects." The page was updated 2026-10-05.
  https://community.exoscale.com/product/storage/object-storage/how-to/bucketlifecycle/ ·
  https://community.exoscale.com/reference/cli/exo/storage/bucket/lifecycle/set/
  - **Contradiction:** the "Limits and Quotas" page (updated 2026-09-14) still lists "Object Lifecycle
    Management" under functionality that "cannot be used on Exoscale", and a search snippet called the
    feature "Early Access". Treat lifecycle as supported but **verify on the new bucket**: run
    `get-bucket-lifecycle-configuration` after setting it, and check that a test object under the prefix
    disappears. The app-side cleanup stays primary anyway.
    https://community.exoscale.com/product/storage/object-storage/service-boundaries/limits-and-quotas/
- **Encryption at rest:** "All new buckets are created with SSE-SOS encryption enabled by default"
  (AES256, Exoscale-managed keys). Existing buckets have to be switched on with `put-bucket-encryption`, and the
  setting cannot be turned off afterwards. SSE-C is available but blocked by default per bucket.
  A new `prod-1-octo-artifacts` bucket therefore gets SSE-SOS automatically, so the client does not need to send
  `x-amz-server-side-encryption`.
  https://community.exoscale.com/product/storage/object-storage/how-to/encryption/ ·
  https://www.exoscale.com/compliance/encryption/
- **Versioning:** "By default buckets are un-versioned and versioning needs to be activated." Object lock
  exists only as a legal hold, applied explicitly.
  https://community.exoscale.com/product/storage/object-storage/how-to/versioning/
- **IAM v3 bucket scoping:** policy expressions on the `sos` service can use `parameters.bucket`.
  `list-buckets` has no parameters, so it cannot be limited to one bucket. That is why the existing prod-1
  role also allows `operation in ['list-sos-buckets-usage', 'list-buckets']`. A key only needs to *see*
  other bucket names; it cannot read them. Lifecycle operations are `put-bucket-lifecycle` and
  `delete-bucket-lifecycle`. Leave them out of the bot's role so the bot cannot remove its own backstop;
  terraform sets the rule with an operator key.
  https://community.exoscale.com/reference/iam/sos/
- **Limits:** multipart uploads allow at most 10,000 parts of 5 MiB (except the last part) to 5 GiB each, and
  objects up to 4 TiB. **At most 20 buckets per account** (quota 100), so check the current bucket count before
  adding `prod-1-octo-artifacts`. At most 1,000 versions per object.
  https://community.exoscale.com/product/storage/object-storage/service-boundaries/limits-and-quotas/

### Hetzner Object Storage (test-2)

- **Lifecycle:** supports `Expiration` (days, or a fixed date), a prefix filter, `NoncurrentVersionExpiration`
  (only `NoncurrentDays`), `AbortIncompleteMultipartUpload` and `ExpiredObjectDeleteMarker`. Rules are applied
  with `aws s3api put-bucket-lifecycle-configuration` or `mc ilm rule import`. The docs say nothing about
  timing. Object lock overrides expiry.
  https://docs.hetzner.com/storage/object-storage/howto-protect-objects/manage-lifecycle/
- **Encryption at rest: none by default.** "There is no default data-at-rest encryption of objects, but
  you can encrypt your data during the upload using SSE-C." SSE-C is the only server-side encryption type
  ("Only this encryption type: SSE-C"). The server does not store the key, metadata is not encrypted, and
  SSE-C objects cannot be copied.
  https://docs.hetzner.com/storage/object-storage/faq/general/ ·
  https://docs.hetzner.com/storage/object-storage/supported-actions/ ·
  https://docs.hetzner.com/storage/object-storage/howto-protect-objects/encrypt-with-sse-c/
  - **Consequence:** on test-2 the `.octoenc` envelope (§5) is the **only** encryption at rest, so it is
    mandatory for every category on this provider, not optional. `S3:ServerSideEncryption` (AES256 /
    `x-amz-server-side-encryption`) must stay **unset** for Hetzner, because only SSE-C is supported and an
    SSE-S3 header is unsupported. SSE-C would add a second key to manage and gives nothing that `.octoenc`
    does not already give.
- **Versioning / object lock:** versioning has to be enabled explicitly, and the bucket FAQ gives no default.
  Object Lock can only be enabled at bucket creation. "Buckets with Object Lock always have versioning
  enabled and it is not possible to disable it." Buckets are private by default.
  https://docs.hetzner.com/storage/object-storage/howto-protect-objects/protect-versioning/ ·
  https://docs.hetzner.com/storage/object-storage/faq/buckets-objects/
  → Create `test-2-octo-artifacts` **without** Object Lock and leave versioning off.
- **Credential scoping:** "each key pair is automatically valid for every Bucket within the same
  project", with read and write access. To restrict a bucket, a bucket policy denies all principals except
  `arn:aws:iam:::user/p<project_id>:<access_key>`. After that, Hetzner Console can no longer list the bucket.
  https://docs.hetzner.com/storage/object-storage/faq/s3-credentials/
  - **Consequence:** a bucket policy protects the *artifact bucket* from other keys, but the bot's key
    can still read the DR bucket `meshmakers` (CrateDB snapshots) in the same project. True isolation needs a
    **separate Hetzner project** for the artifact bucket, or acceptance that keys are project-wide on test-2.
    New decision point **Q7**.
- **Limits:** the docs recommend multipart uploads above 100 MB. The FAQ pages state no part or object size
  limits.

### Azure Blob Storage (staging-1, prod-2)

- **Lifecycle:** a policy holds up to 100 rules. Filters are `blobTypes` (required, `blockBlob`) and
  `prefixMatch`, with up to 10 case-sensitive prefixes per rule. Each "prefix string must start with a
  container name", e.g. `octo-artifacts/main/presweep/`, and wildcards are not supported. The `delete` action
  on `baseBlob` takes `daysAfterModificationGreaterThan` or `daysAfterCreationGreaterThan`.
  "When you add or edit the rules of a lifecycle policy, it can take up to 24 hours for changes to go into
  effect and for the first execution to start." A policy is always replaced as a whole. Delete does not work
  in immutable containers, and with soft delete enabled a lifecycle delete only soft-deletes.
  https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-overview ·
  https://learn.microsoft.com/en-us/azure/storage/blobs/lifecycle-management-policy-structure
  - **Consequence for the layout:** an instance prefix `<instance>/presweep/` cannot be matched with a wildcard
    such as `*/presweep/`. Write one `prefixMatch` per instance prefix (`octo-artifacts/<instance>/presweep/`),
    which allows up to 10 per rule; that is enough for one instance per AKS cluster. The S3 providers accept
    exactly one prefix per rule, so the same applies there: one rule per instance and category. test-2 has 3
    instances × 3 categories = 9 rules.
- **Encryption at rest:** "Azure Storage encryption is enabled for all storage accounts. You can't disable
  Azure Storage encryption." It uses AES-256 (GCM for objects), and "Data in a new storage account is
  encrypted with Microsoft-managed keys by default."
  https://learn.microsoft.com/en-us/azure/storage/common/storage-service-encryption
- **Soft delete / versioning defaults:** "Blob soft delete is enabled by default when you create a new
  storage account with the Azure portal". It is *not* enabled when the account is created with PowerShell or
  Azure CLI. It is configured per storage account (`blobServices/default.deleteRetentionPolicy`), with a
  retention of 1–365 days. Versioning is also an account-level setting and is off unless enabled.
  https://learn.microsoft.com/en-us/azure/storage/blobs/soft-delete-blob-enable ·
  https://learn.microsoft.com/en-us/azure/storage/blobs/soft-delete-blob-overview ·
  https://learn.microsoft.com/en-us/azure/storage/blobs/versioning-overview
  - `mmstaging1backups` and `meshmakersprod2backups` are created by terraform (`azurerm_storage_account`
    without a `blob_properties` block). That points to soft delete and versioning being **off**, but the
    live value must be checked with `az storage account blob-service-properties show`. Because the setting
    is account-wide, keeping it off for `octo-artifacts` means keeping it off for the DR containers
    (`mongodb-backups`, `cratedb-backups`) as well. That matches today's terraform. If someone later wants
    soft delete for DR, `octo-artifacts` needs its own account (Q4).
- **Credentials:** "Storage account access keys provide full access to the storage account data". An account
  key from Vault (`secret/meshmakers/<cluster>/azure-storage`) would therefore give the bot the DR containers
  too. The right scope is the RBAC role `Storage Blob Data Contributor` on
  `/subscriptions/…/storageAccounts/<account>/blobServices/default/containers/octo-artifacts`.
  https://learn.microsoft.com/en-us/azure/storage/common/storage-account-keys-manage ·
  https://learn.microsoft.com/en-us/azure/storage/blobs/assign-azure-role-data-access
- **Workload identity (AKS):** the cluster needs the OIDC issuer and `--enable-workload-identity`; the
  terraform for both AKS clusters sets neither today. Then create a user-assigned managed identity (or an app
  registration) with a federated credential for the bot's service account, at most 20 per identity. The
  service account gets the annotation `azure.workload.identity/client-id`, and the pod needs the label
  `azure.workload.identity/use: "true"`. The webhook injects `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and
  `AZURE_FEDERATED_TOKEN_FILE`. `DefaultAzureCredential` (Azure.Identity ≥ 1.9.0) picks it up through
  `WorkloadIdentityCredential`.
  https://learn.microsoft.com/en-us/azure/aks/workload-identity-overview
  - **Consequence:** with account keys (phase 1), "scoped to the container" is **not achievable** on Azure.
    The options are: (a) accept account-key scope for phase 1; (b) a dedicated storage account just for
    `octo-artifacts`, so the key's blast radius equals the container; or (c) workload identity with a
    container-scoped role. (b) is the cheapest way to get real isolation without changing AKS; (c) is the
    target state. Both are recorded under Q4/Q5.

### Exoscale Block Storage (`exoscale-sbs`, interim PVC on prod-1)

- "Every new Block Storage volume is encrypted at rest by default" with AES-256-XTS at the hypervisor
  layer, using a unique key per volume that is destroyed when the volume is deleted. The compliance page adds:
  "Default for new volumes. Existing volumes are not re-encrypted." A new interim PVC from the `exoscale-sbs`
  StorageClass (CSI add-on) is therefore encrypted at rest.
  https://www.exoscale.com/compliance/encryption/ ·
  https://community.exoscale.com/product/storage/block-storage/how-to/per-vol/
  → This closes the "**verify**" in §3 option B for prod-1.

### Resolved names per cluster (Q3)

| Cluster | Provider | Existing DR target (source of truth) | Doc mismatch | Proposed artifact target |
| --- | --- | --- | --- | --- |
| test-2 | Hetzner Object Storage | Bucket and endpoint live **only in Vault** (`secret/meshmakers/test-2/hetzner-s3`, keys `bucket`, `endpoint`). `docs/CLUSTER-SERVICES-OVERVIEW.md` names bucket `meshmakers` (prefix `cratedb/`); `test_2_infrastructure.yml` has no bucket name. The location is not in the repo | none, but unconfirmed | `test-2-octo-artifacts` (same project unless Q7 decides otherwise) |
| staging-1 | Azure Blob | Account **`mmstaging1backups`** (terraform `variables.tf` default and `staging_1_infrastructure.yml`), westeurope, Standard LRS; containers `mongodb-backups`, `cratedb-backups` (private) | `docs/CONCEPT-CRATEDB-DISASTER-RECOVERY.md` says `meshmakersstaging1backups`, which is **stale** | container `octo-artifacts` in `mmstaging1backups` (or a dedicated account, Q4) |
| prod-2 | Azure Blob | Account **`meshmakersprod2backups`** (terraform and `prod_2_infrastructure.yml`), westeurope, Standard LRS; the same two containers | none | container `octo-artifacts` in `meshmakersprod2backups` (or a dedicated account) |
| prod-1 | Exoscale SOS | Bucket **`prod-1-backups`** (`aws_s3_bucket "${var.cluster_name}-backups"`, `cluster_name = "prod-1"`; `exoscale_sos.bucket` in `prod_1_infrastructure.yml`), endpoint `https://sos-at-vie-1.exo.io`, zone `at-vie-1`; prefixes `mongodb/`, `cratedb/`, `signal-bridge/`. IAM role/key `prod-1-sos-backup` is limited to that bucket | `docs/MONGODB-BACKUP-RESTORE.md` says `meshmakers-prod-1-backups`, which is **stale** | bucket `prod-1-octo-artifacts`, its own IAM role/key `prod-1-octo-artifacts` (bucket count < 20 to be checked) |

Vault paths (names only): `secret/meshmakers/test-2/hetzner-s3`, `secret/meshmakers/staging-1/azure-storage`,
`secret/meshmakers/prod-2/azure-storage`, `secret/meshmakers/prod-1/sos`.

### Effect on the design

1. A lifecycle backstop is available on all three providers, so no CronJob fallback is needed. On SOS, check
   once that the rule is actually honored, because of the contradicting limits page.
2. Encryption at rest: Azure and SOS (new bucket) encrypt by default; **Hetzner does not**. The
   `.octoenc` envelope is therefore mandatory on test-2 and recommended everywhere. Keep
   `S3:ServerSideEncryption` unset on Hetzner. On SOS it is redundant, because the bucket default applies.
3. Single-bucket credentials: only SOS gives them natively. Hetzner needs a bucket policy plus a decision
   on a separate project (Q7). Azure needs a dedicated account or workload identity (Q4/Q5).
4. Lifecycle prefixes cannot contain wildcards on any provider. Write one rule per `<instancePrefix>/<category>/`
   from the infra config, rather than a single `*/presweep/` rule.

**Q7 (new):** test-2: put the artifact bucket in its own Hetzner project (true key isolation from the DR bucket),
or accept project-wide keys plus a bucket policy?
