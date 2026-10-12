# Construction Kit SemVer Rules and Version Validation

The version of a Construction Kit model lives in the `modelId` suffix of `ckModel.yaml`
(e.g. `modelId: Basic-2.0.2`) and is maintained manually by the developer. The
`ckc ValidateVersion` command enforces that this version is *honest*: it diffs the current model
against its baseline — the newest published version of the same major (see "Baseline" below) —, classifies every change according to the fixed rule set below,
and fails when the declared version does not satisfy the derived minimum bump level.

The command **never writes** `ckModel.yaml` — the developer stays in control of the version; the
tool only checks it. Enforcement happens as a CI gate (PR/main builds).

## The command

```
ckc ValidateVersion -p <ck-folder> [-p <ck-folder2> ...]
                    [-cn <catalogName>] [-o <report.md>]
                    [-rf|--refresh] [-cl|--changelog] [-rmm|--requireMigrationForMajor]
                    [-ap|--apply] [-lce <bool>] [-lcr <path>]
```

| Argument | Description |
| -------- | ----------- |
| `-p, --path` | Root path(s) of Construction Kit model directories. Multiple paths are validated in the given order — pass them in **dependency order** (dependencies first): a later package may depend on a version a sibling package introduces in the same run (see the sibling-resolution note below). |
| `-cn, --catalogName` | Pins **baseline retrieval and the FR-9 dependency-existence check** (`OCTO-CK102`/`OCTO-CK103`) to the named catalog. Without it, all readable catalogs are queried (catalog order: Embedded → LocalFileSystem → PrivateGitHub → PublicGitHub) and the baseline follows the rules in "Baseline" below. **It does not pin the compile-stage dependency *resolution*** — see the note below. |
| `-o, --output` | Additionally writes the report as Markdown (e.g. for PR comments). |
| `-rf, --refresh` | Forces a catalog cache refresh before the baseline is determined. Always use this in CI. |
| `-cl, --changelog` | Writes/updates the `CHANGELOG.md` section of the declared version next to `ckModel.yaml`. Only runs after successful validation; older sections are never rewritten. |
| `-rmm, --requireMigrationForMajor` | Escalates a missing migration for a required major bump from a warning to an error. |
| `-ap, --apply` | AB#4467: writes the **minimum valid version** into `ckModel.yaml` for every package that fails with `OCTO-CK100`, see "Applying the minimum version" below. |
| `-lce, -lcr` | Enable/point the local file system catalog for this invocation (same semantics as `Compile`). |

> **`-cn` pins the baseline, not the compile-stage dependency resolution.** Determining the
> baseline (which version is "the last published one") and the FR-9 dependency-existence check are
> restricted to the named catalog. Compiling the current model in memory, however, resolves its
> dependency closure across **all** readable catalogs — this is by design (the compiler is the same
> one used everywhere), but it means a stale entry in another catalog (typically a leftover local
> `LocalFileSystemCatalog` model with different content) can still poison transitive resolution and
> flip the diff of a *dependent* model to a spurious level, even though `-cn` was given. Combine
> `-cn` with a clean, per-run local catalog to eliminate this.
>
> **Recommended for CI and reproducible local runs:** `-cn <catalog> -rf -lcr <empty-dir>` — pin the
> baseline to one catalog, force a cache refresh, and point the local catalog at a fresh empty
> directory so no stale local content participates in resolution. The shipped CI step in
> `octo-construction-kit` does exactly this via a per-run `$(OctoLocalCatalogRootPath)`.
>
> **Same-run sibling dependency resolution.** A commit that bumps a dependency model and its
> consumer together (e.g. `Basic.Energy` 1.1.4 → 1.2.0 plus `EnergyCommunity` 4.0.0 depending on
> `Basic.Energy-[1.2,2.0)`) declares a range no catalog can satisfy yet — the new dependency
> version is only published *during* the subsequent build. To keep this from dying with a false
> `OCTO-CK103`, every package that validates successfully (including first publications) is
> registered for the rest of the invocation: its declared version satisfies the FR-9
> dependency-existence check of later packages, and its compiled model is published to a
> **per-run working copy** of the local file system catalog (or to the `-lcr` directory when one is
> given, as in CI) so the compile stage of later packages resolves it. Without `-lcr` the command
> copies the local catalog into a temporary directory, registers the siblings there and deletes the copy
> at the end of the run: without `-lcr`, validation does not write into `~/.octo/local-catalog` (AB#5434). The report
> names the write (`Registered X for sibling resolution in …; replaced nothing.`) or states that no
> catalog was written (`-lce false`). This is why the `-p` paths must be in dependency order — a consumer
> validated *before* its same-run dependency still fails with `OCTO-CK103`, honestly.

### Baseline (AB#5450)

One shared, read-only resolver (`ICkBaselineResolver` in `ConstructionKit.Engine/SemVer/`) decides which version a
model is compared against. `ValidateVersion`, the compile gate (AB#6294) and the publish gate (AB#6386, "Publish gate"
below) use the same resolver, so every gate agrees. It queries each catalog separately (never the merged highest
version).

1. **Same major.** The baseline is the newest version within `[major.0.0, major+1.0.0)` of the declared version.
   EnergyCommunity `3.4.0` with `3.3.0` and `4.5.0` published is compared against `3.3.0`; a declared `3.2.0` fails
   with `OCTO-CK101` against `3.3.0`. A maintenance release on an older major line therefore passes the normal gate.
2. **New major.** When the declared major has no version yet, the baseline is the newest version of the highest
   *lower* major: `4.0.0` is still diffed against the newest `3.x`, so the migration check (`OCTO-CK104`) and the
   changelog keep working. A major allows any change; when the changes only need a minor or patch bump, the report
   notes a "valid major bump without structural need". Only a model without any version is a first publication.
3. **Never the model under test (AB#5434, symptom 2).** Entries of the `LocalFileSystemCatalog` at or above the
   declared version are never the baseline: they come from
   earlier local builds, not from a publish. They are listed as ignored in the report. A local entry *below* the
   declared version can be the baseline (marked "local build, not a publish"). A published (remote) entry equal to the
   declared version stays the baseline, so an unchanged version with structural changes still fails with
   `OCTO-CK100`. On a version tie the published entry wins over a local copy. Running `ValidateVersion -rf` twice gives
   the same baseline and verdict.
4. **Read-only.** The resolver never publishes, refreshes or restores. `ValidateVersion` does not write into the
   catalog other builds read either (AB#5434): without `-lcr`, its sibling registration goes to a per-run working copy that
   is removed at the end of the run, so the user's local catalog stays byte-identical (the 2026-09-30 displacement/shadowing
   case). With `-lcr` the registration goes to the directory you named - point it at an empty or throw-away directory (CI does).
5. **Visible.** The report names the baseline version and catalog (e.g. `Published: 3.3.0 (PrivateGitHubCatalog)`),
   marks a local baseline, says when the baseline comes from the previous major line, and lists ignored local entries.
6. **Model names compare case-insensitively (AB#6386).** The catalogs match names case-sensitively, so a model published
   as `System.Bot` used to be invisible to a lookup of `system.bot` and looked like a first publication. The resolver
   discovers the other spellings of the name in each catalog (`GetSpellingsAsync`, one listing per catalog) and queries
   every spelling; the highest hit wins and keeps the spelling it was published under (`System.Bot-3.1.0`). This is the one
   place that does it, so `ValidateVersion`, the compile gate and the publish gate behave alike. A case-only variant is never
   treated as a first publication. (`ValidateCatalog`, AB#4469, reports such pairs.)

When a catalog source is unreachable and no same-major baseline was found, the resolver does not fall back to a
lower major: the result is `OCTO-CK102`, as before.

Blueprints (`octo-bpm validateVersion`) keep their own baseline lookup; they have no structural diff and are immutable
per version (checked for AB#5450, not changed).

### Validation rule

With *published* = the baseline (see above) and *minimum* = published + exactly one bump of the
highest level in the diff:

- Diff empty and declared == published → **valid**.
- Diff empty and declared > published → **valid** (reported as a note: bump without structural
  change, e.g. a semantic change — legitimate).
- Diff non-empty and declared >= minimum → **valid** (higher than required is ok — only the
  *minimum* level is enforced).
- Diff non-empty and declared < minimum (in particular: version left untouched) → **error**.
- Declared < published → **error** (downgrade).

### Error codes

| Code | Meaning | Remediation |
| ---- | ------- | ----------- |
| `OCTO-CK100` | Declared version below the required minimum | Raise the version in `ckModel.yaml` to at least the reported minimum |
| `OCTO-CK101` | Declared version below the published version (downgrade) | Use a version >= the published one |
| `OCTO-CK102` | Catalog source unreachable — baseline (or dependency check) impossible | Check network/VPN and catalog configuration, retry with `--refresh` |
| `OCTO-CK103` | A dependency range is satisfied by no published version and no sibling package validated earlier in the same run | Publish the dependency first, correct the range, or pass the dependency's source path before the consumer's (dependency order) |
| `OCTO-CK104` | Major bump without a matching migration (only with `--requireMigrationForMajor`) | Add a migration with `toVersion` == declared version |

The compile gate (next section) uses the `OCTO-CK2xx` range.

Exit codes: `0` = valid, non-zero = violation or error (CI-friendly). Concretely, a version
violation (any `OCTO-CK1xx` finding, an unknown catalog, or a compile failure) exits with the
process code `-6` (`ModelValidationException`); because process exit codes are unsigned bytes this
surfaces as `250` in PowerShell's `$LASTEXITCODE` and `250` in a POSIX shell's `$?`. **CI must gate
on `-ne 0`, never on `-eq 1`** — the command never exits with `1`.

## Compile gate (AB#6294)

Gate 1 of the CK v2 compatibility concept (§4.3.3). `CkCompile` (the MSBuild task behind `dotnet build`, local
and CI) checks a freshly compiled model against its baseline **after compiling and before any publish** and fails
the build for every change the declared version does not cover. The gate never writes to a catalog; after an
`OCTO-CK2xx` error the model is published nowhere (neither to `OctoPublishCatalog` nor to the local catalog).

The verdict itself (baseline model, diff, classification, validation of the declared version) comes from the engine
service `ICkCompatibilityVerdictService`, which `ValidateVersion` uses as well: for the same model and baseline both
report the same required level and minimum version (test `ValidateVersion_AndCompileGate_AgreeOnTheVerdict`). The
baseline comes from the shared `ICkBaselineResolver` (rules: "Baseline" above); the decision logic is the class
`CkCompileGate`.

| Model | Behaviour |
| ----- | --------- |
| `ckLanguage: 2` | Hard gate: `OCTO-CK200`, `OCTO-CK201`, `OCTO-CK202` fail the build. |
| `ckLanguage` 1 (default) | One informational verdict line, never a warning or an error; a failing check is swallowed into an information line. The compiled JSON is byte-identical to a build without the gate. The CI step `validate-ck-versions` stays the v1 gate. |

### Baseline source: `OctoCkCompatibilityBaseline`

| Value | Catalogs consulted | Default |
| ----- | ------------------ | ------- |
| `Local` | the local file-system catalog plus the cached remote catalogs; needs no network | outside CI (`DebugL`) |
| `Remote` | the remote (GitHub) catalogs only; the local catalog is ignored | when `TF_BUILD` or `ContinuousIntegrationBuild` is true |

The MSBuild property overrides the default (`<OctoCkCompatibilityBaseline>Remote</OctoCkCompatibilityBaseline>`);
any other value fails the build. Feature branches in CI that publish to a per-run local catalog therefore still
diff against GitHub. There is deliberately no switch that turns the gate off for a ckLanguage 2 model; the accepted
bypass is deleting the baseline (a model without a baseline is a first publication).

Never the model under test: in `Local` mode entries of the local catalog at or above the declared version are never
the baseline, because they come from earlier local builds of the same model. Building the same model twice, or for
two target frameworks, yields the same baseline both times, and a breaking edit at an unchanged version fails again
on the next build. A local baseline below the declared version is allowed and is marked "local, not published" in
the verdict line.

### Codes

| Code | Meaning | Remediation |
| ---- | ------- | ----------- |
| `OCTO-CK200` | A change is not covered by the declared version. One error **per uncovered change**: element, change, required level, minimum version | Raise the version in `ckModel.yaml` to at least the reported minimum (`modelId: Name-X.Y.Z`) or revert the change |
| `OCTO-CK201` | The declared version is lower than the baseline of the same major | Use a version >= the baseline version |
| `OCTO-CK202` | The baseline source was unreachable and no baseline is known. An **error** in `Remote`, a high-importance message (not an error) in `Local` | Check network/VPN and the catalog tokens, retry; offline development uses `Local` |

`OCTO-CK203` / `OCTO-CK204` belong to the acknowledge mechanism (next section).

### Accepted limitations of the compile gate

Accepted by the platform owner on 2026-10-10; the guarantee is the catalog gate at publish time (F2.3, AB#6386), not the local build.

1. **Deleting the local baseline** leaves a `Local` gate without a baseline (a model without a baseline is a first publication).
2. **A downgrade below a local copy** is not detected in `Local` mode: local-catalog entries at or above the declared version
   are never the baseline (AB#5434). `OCTO-CK201` needs a published baseline.
3. ~~A model name that differs only in case from a published one has no remote baseline.~~ Closed by AB#6386: the baseline
   lookup is case-insensitive (rule 6 of "Baseline").
4. **CI detection** uses `TF_BUILD` and `ContinuousIntegrationBuild` only; other CI systems pass
   `-p:OctoCkCompatibilityBaseline=Remote`. A project file that sets the property to `Local` switches the Remote gate off, so
   pipelines should pass it as a global property.

Not a limitation: a `ckLanguage` transition (1 to 2 or 2 to 1) is a change like any other (Minor / Major) and keeps the gate
hard; `ValidateCascade` exits non-zero when a model could not be loaded, because an incomplete dry run is not a green one.
The task itself is covered in CI by `CkCompileTaskTests` (the real `CkCompile` MSBuild task).

### Verdict line

Every build with a baseline logs one line (high importance), for example:

```
Compatibility: model 'GateModel', declared 2.5.0, baseline GateModel-2.4.0 (LocalFileSystemCatalog, local, not published), required level MINOR, minimum version 2.5.0: ok.
```

A first publication logs `first publication (no baseline in the Remote baseline source)`.

## Publish gate (AB#6386)

Gate 2 of the CK v2 compatibility concept (§4.3.3). Whoever publishes a model to a **writable catalog other than the
`LocalFileSystemCatalog`** — the private, the public and the dev-lane GitHub catalog — gets the compile gate's verdict,
computed against the catalog being published to. The gate sits in `CatalogService.PublishAsync` (class `CkPublishGate`),
so `octo-ckc -c Publish`, the MSBuild publish of `CkCompile` and every other caller are covered by one implementation;
no pipeline step can forget it and there is no bypass switch (`-r/--replace` only decides whether an existing file may be
overwritten, never whether the gate runs). A refusal throws a `CkPublishRefusedException` (a `ModelValidationException`:
exit code `-6`, the code of every other verdict, MSBuild error) with one message per reason and **writes nothing**. All
reasons of one publish are reported together, see "One refusal" below (AB#6389).

| Aspect | Rule |
| ------ | ---- |
| Verdict | `ICkCompatibilityVerdictService` and `ICkSemVerClassifier`, the same as `ValidateVersion` and the compile gate; codes `OCTO-CK200` to `OCTO-CK202`. ckLanguage 1 and 2 alike (platform owner, 2026-10-10): for a v1 model the gate repeats the verdict CI already enforces with `validate-ck-versions`. |
| Baseline | `ICkBaselineResolver.ResolvePublishBaselineAsync`: **the target catalog only** (the local catalog and a run-isolated catalog are never a baseline), read from the catalog's **source of truth** (AB#6580, see "Fail closed" below), not from the cache. The newest published version of the declared major **other than the declared version**; none in that major: the newest version of the highest lower major. Both catalog roots count: a first ckLanguage 2 release `3.0.0` is diffed against the newest `2.x` under `ck-models/v2/`, `3.1.0` against `3.0.0` under `ck-models/v3/`. A maintenance release `3.4.1` with `3.4.0` and `4.0.0` published is judged against `3.4.0`. |
| Unreachable target | `OCTO-CK202` (also when the catalog is not readable); never treated as a first publication. What counts as unreachable, and what as a genuinely new catalog or model: "Fail closed" below. |
| Same version | The declared version is already published: identical content (compared by the serialized model) is a **no-op** (nothing is written). *Equivalent* content is a no-op too, with a warning naming the difference class and the published file left as it is (AB#6386, `CkPublishEquivalence`): both models are read back and brought into one canonical form, and only what survives is a content change. The allow-list of normalised classes is closed: (1) **restated default ownership** — `ownership: SeedOwned` or `RuntimeState` on an attribute definition (or on an assignment whose definition is in the same model) that equals what the `isRuntimeState` alias resolves to; `TenantOwned` and `Secret` are never normalised, a legacy reader sees them as runtime state; (2) **element order** of the top-level lists; (3) **compiler-stamped metadata** (`minEngineVersion`). Different content is refused with `OCTO-CK205` on the private and the public catalog, also with `--replace` (published versions are immutable). The dev-lane catalog (GitHub repository `octo-catalog-dev`, `IReplaceablePublishCatalog`) keeps its replace behaviour, but the verdict always uses the newest version **strictly below** the declared one, so a replace is never compared with itself; after a refused or replaced publish the next publish gets the same baseline. |
| Race | The catalog is read, the verdict computed, and the catalog read again (forced refresh) right before the write. If the versions of the model changed in between, the verdict is computed again (at most 3 reads, then `OCTO-CK202`). The write never happens on a stale baseline: the GitHub catalogs are read through the Contents API (AB#6580), which is consistent with the repository, so a version published seconds ago is the baseline; an exact same-version race is additionally caught by the contents API at the write (file already exists). |
| Not trusted | The verdict is computed from the model content and the target catalog only. A hand-edited compiled file (lowered version, removed members) gets the verdict of its content; nothing in the file lowers the required level. |
| Acknowledgements | Enforced from the **compiled model alone** (AB#6387, next section "Acknowledgements at publish"): `OCTO-CK203`, `OCTO-CK204` and `OCTO-CK206`. |

| Code | Meaning | Remediation |
| ---- | ------- | ----------- |
| `OCTO-CK205` | The declared version is already published to the catalog with different content | Raise the version in `ckModel.yaml` and publish again; published versions are never replaced (only the dev-lane catalog allows it) |
| `OCTO-CK206` | An entry of `compatibility.acknowledge` in the compiled model violates the entry rules (empty or wildcard or duplicate `change`, empty `reason`, not single-line plain text) | Recompile the model from a valid `ckModel.yaml`; never edit the compiled file by hand |
| `OCTO-CK207` | The version number was unpublished earlier (`Unpublish`, AB#5433) and is burned; reported by the gate (AB#6389) instead of the catalog throwing at write time (the write still checks it) | Publish the next free version number |

### Fail closed (AB#6580, AB#6581)

The publish gate never takes a read that failed for an answer. The GitHub catalogs have two read paths: a cheap one through
the **GitHub Pages mirror** (cache, listing, anonymous readers) and the **Contents API**, authenticated with the token the
publisher holds anyway. The mirror lags the repository by up to a minute after a publish, answers `404` while a Pages
deployment is being replaced and can be throttled. The gate (and the `Unpublish` dependents check) therefore reads the
versions of the model **from the repository** (`IAuthoritativeVersionCatalog.ReadVersionsAsync`: the two root indexes, and
for the matching model(s) the library index and the index of every major - roughly 5 API calls per publish):

| What the read meets | Result |
| ------------------- | ------ |
| Repository **and** branch exist, no index under a root, or the indexes do not list the model | **Genuinely new**: first publication (no baseline). |
| Repository without any commit | Genuinely new, too. |
| Repository or branch not found, token cannot see it, no token | `OCTO-CK202` (nothing could be asked). |
| Transport error (offline, DNS, TLS), timeout, `5xx`, rate limit that outlasted the wrapper's retries | `OCTO-CK202`, the reason (HTTP status, GitHub's text) is part of the message. |
| An index lists a model, major or version but the file it points to is missing, empty or unparseable | `OCTO-CK202` (damaged or half-deployed catalog), never "empty". |
| The mirror answers `404`/`5xx`/times out | No effect on the gate. In the cache refresh it flags the cache `SourceUnreachable` (an index the root lists that the mirror cannot deliver, or a `404` for the root index that the repository contradicts), so a cache answer "model not found" is never read as "model does not exist". |

A model file that is missing from the mirror is read through the Contents API when the reader holds a token (and always
first in the lane catalog, whose versions can be replaced); an anonymous reader has the mirror only. Whatever the gate
reads that fails - also outside the baseline read, e.g. a baseline file the index lists but the repository lacks - ends
as `OCTO-CK202` (exit `-6`), not as an unhandled exception (AB#6582: `octo-ckc -c Publish` offline or against a repository that does not exist ends with `-6` and the reason, formerly `-99`/exit 157; the record of burned numbers is no exception). A transport failure in the middle of the write (after the gate passed) is a `ModelCatalogException` "Publishing model ... failed: <reason>" - nothing half-written is reported as success. Tests: `PublishCommandOfflineTests` (real parser, `Runner` and `GitHubCatalog`), `CkPublishGateOfflineTests`, then `CkPublishGateFailClosedTests`,
`GitHubCatalogMirrorFlagTests`, `GitHubClientWrapperRepositoryStateTests` (the real wrapper against a local HTTP stub of the
GitHub API).

### One refusal (AB#6389)

A publisher who fixes one reason and then meets the next one publishes five times. The gate therefore collects **every**
reason and reports them in one refusal:

- **Same-version refusal continues.** `OCTO-CK205` (the version is published with different content) no longer ends the
  evaluation: the verdict against the baseline strictly below the declared version, the acknowledgement findings and a
  burned number (`OCTO-CK207`) are reported in the same refusal.
- **Order**, most fundamental first: `OCTO-CK207`, `OCTO-CK205` (the version number is unusable), `OCTO-CK202`
  (baseline unknown), `OCTO-CK201`, `OCTO-CK200` (the declared version is too low), `OCTO-CK206`, `OCTO-CK203`,
  `OCTO-CK204` (acknowledgements). Inside one code the order of detection is kept. Repeated `(code, text)` pairs are
  dropped (`CkPublishRefusal.Normalize`).
- **Message contract.** Every reason is one self-contained line:
  `[compat gate] OCTO-CK200 (model 'Name-1.2.3', catalog 'Catalog'): <what is wrong> Next step: <what to do>`.
  The exception text is a one-line summary (`... was NOT published to catalog '...': refused by the compat gate with N
  reason(s) (codes). Nothing was written.`). A compat-gate text never mentions O1.
- **One output for every caller.** `CatalogService.PublishAsync` writes the lines into the `OperationResult` of the call
  and throws `CkPublishRefusedException`. `octo-ckc -c Publish` prints the summary and then each line once
  (`Runner`, exit code `-6`); the MSBuild publish of `CkCompile` logs each line as an MSBuild error (message number
  = the `CK2xx` number). Same lines, same order.
- **Exit codes.** A refusal is a verdict about the model and ends with `-6` (`250` in a POSIX shell), like `ValidateVersion`
  (`OCTO-CK1xx`), `ValidateCascade` and `ValidateCatalog`. `-5` stays for a model that could not be resolved (the publish
  fails before the gate), `-99` for an unexpected exception. Gate on `-ne 0`, never on a single value.

### The two gates in front of a catalog publish (O1 guard and compat gate)

Two independent gates stand in front of a publish. They never waive each other.

| Gate | Where it lives | Question | Output |
| ---- | -------------- | -------- | ------ |
| **O1 guard** (operating rule O1, AB#6277) | Pipeline only: `templates/steps/ck-o1-guard.yml` of `octo-pipeline-templates`, a PowerShell step; the engine does not know it | May a ckLanguage-2 or range-retaining model (or a blueprint on one) go to this target catalog at all? Decided by the model kind and the target catalog; switch `OctoCkO1Lifted`; only `octo-catalog-dev` is the dev exception; an empty or unknown target counts as shared | `##vso[task.logissue type=error]O1: ...` |
| **Compat gate** (this section) | Engine: `CkPublishGate` in `CatalogService.PublishAsync` | Is this version compatible with what the target catalog holds, and are the acknowledgements, the version number and the baseline in order? | `[compat gate] OCTO-CK2xx ...` lines, exit `-6` |

Order in every lane: **O1 guard, ValidateVersion, cascade check (`ValidateCascade`), publish (compat gate inside)**. A
model the O1 guard refuses never reaches the compat gate (no compatibility verdict in that log); a model it lets through
(switch on, or dev-lane target) always passes the other gates. The engine decides from the model and the target catalog
only; the O1 state is not an input, so lifting O1 (AB#6279) or the dev-catalog exception never switches the compat gate off.

| Target catalog | Model | O1 switch | O1 guard | Compat gate |
| -------------- | ----- | --------- | -------- | ----------- |
| private, public | v1 | any | passes | decides (v1 verdict as before) |
| private, public | ckLanguage 2 / range-retaining | off | **refuses** (`O1`), compat gate not reached | not reached |
| private, public | ckLanguage 2 / range-retaining | on | lets through (logs "O1 lifted") | decides: breaking without major = `OCTO-CK200` |
| dev lane (`octo-catalog-dev`) | v1 | any | passes | decides |
| dev lane | ckLanguage 2 / range-retaining | any | lets through (dev exception) | decides: breaking = `OCTO-CK200`, a replace of a published version is judged against the version strictly below |
| empty / unknown target | v1 | any | passes | decides |
| empty / unknown target | ckLanguage 2 / range-retaining | off | **refuses** (counts as shared) | not reached |
| empty / unknown target | ckLanguage 2 / range-retaining | on | lets through | decides |

The engine rows are covered by `CkPublishRefusalTests`, `CkPublishGateRepositoryTests` (private/public layout, dev lane)
and `PublishCommandGateTests`; the O1 rows by the Pester fixtures of `octo-pipeline-templates`
(`tests/ck-o1-guard.Tests.ps1`).

**v1 replay.** Replaying the 152 published versions of the public catalog in release order through the gate (opt-in test
`PublishGateReplayTests`, set `OCTO_PUBLISH_GATE_REPLAY_DIR`) refuses 18 early releases (2.0.x/2.1.x era patch or minor
releases that changed a dependency pin or added elements, e.g. `Basic-2.0.2`, `Industry.Basic-2.1.1`); they predate the
rules the classifier enforces today. The latest release of each of the 12 `octo-construction-kit` models replays clean.

## Acknowledge (AB#6295)

Some changes are accepted only when the publisher says so explicitly. The classifier marks them `requiresAcknowledge`
(F2.1): tightening `access` on a **security-sensitive** attribute (Minor instead of Major, platform-owner decision
2026-10-09) and a **unique index on a stable base** (row B4, Major). The author acknowledges such a change in
`ckModel.yaml` (ckLanguage 2 only; in a ckLanguage 1 model the section is message 90):

```yaml
compatibility:
  acknowledge:
    - change: "RecordAttribute:Login-1/Secret#Modified:access"
      reason: "Close accepted risk R13: password hash readable via GraphQL"
```

| Code | Meaning | Remediation |
| ---- | ------- | ----------- |
| `OCTO-CK203` | A change that needs an acknowledgement has no matching entry. The message prints the **change key** and an example entry | Copy the entry into `compatibility.acknowledge` and give a reason |
| `OCTO-CK204` | An entry matches no change in this release that needs an acknowledgement (stale, or it names an ordinary change) | Remove the entry; an acknowledgement is valid for one release only |

Rules:

- **The change key** is stable, free of model version numbers and exact (`CkChangeKey`): `{ElementKind}:{ElementId}#{ChangeKind}`,
  plus `:{property}` for a modification, for example `RecordAttribute:Login-1/Secret#Modified:access`. The element id is the id
  inside the model. Type indexes have no identity of their own, so their key carries the definition
  (`TypeIndex:Machine-1/index#Added:UniqueNotDeleted on Serial`). The gate prints the key verbatim. Matching is ordinal;
  there are no wildcards.
- **Type indexes** have no identity of their own, so their key carries the definition: `…#Added:<definition>` and, for a
  change, `…#Modified:indexType:<old> -> <new>`; two indexes of a type never share a key.
- **`reason` is mandatory** and, like `change`, a single line of plain text (no line breaks, no `##vso[`). An empty reason or a wildcard key is rejected by the schema (and by the compiler, message 130).
- **An acknowledgement never lowers a level.** A unique index on a stable base still needs a major bump
  (`OCTO-CK200` for a lower version); an ordinary breaking change still fails with `OCTO-CK200` even if it is "acknowledged"
  (and the entry is then stale, `OCTO-CK204`).
- **One release only.** Entries are matched against the diff of exactly this build; on a first publication (no baseline) every
  entry is stale. Nothing carries over to the next release.
- **Visible.** Acknowledged changes appear as "Acknowledged changes" (change, level, reason) in the `ValidateVersion` report, the
  build log of `CkCompile` and the `CHANGELOG.md` section, next to "Behavioural changes". Both gates report the same codes, because
  both use `ICkCompatibilityVerdictService`.
- **Carried for the publish gate.** The compiled model contains `compatibility` (omitted when empty), so the publish gate (AB#6387, "Acknowledgements at publish")
  enforces the same rule without the source. ckLanguage 1 models cannot declare entries; for them "requires acknowledge" stays
  advisory (no `OCTO-CK203`).

### Acknowledgements at publish (AB#6387)

The publish gate (previous section) enforces the acknowledge rule too, from the **compiled model JSON alone**: the publisher
needs no `ckModel.yaml`, the entries travel in `compatibility.acknowledge`. It uses the same code as the compile gate
(`CkAcknowledgementResult`, `CkAcknowledgementFormatter`), not a copy, so `ValidateVersion`, the compile gate and the publish
gate give the same acknowledge verdict for the same model and baseline (pinned by a test over the shared corpus).

| Situation at publish | Result |
| -------------------- | ------ |
| A change that needs an acknowledgement and no entry matches | Refused, `OCTO-CK203` naming the change key; the catalog is unchanged |
| A matching entry (key + reason) | Published; the log lists "Acknowledged changes of model …" with level, change and reason (audit trail without the source repo) |
| An entry that matches no change of this release, names an ordinary change or another element, or is on a first publication | Refused, `OCTO-CK204` |
| An entry for an ordinary breaking change | `OCTO-CK200` stays; the entry is stale (`OCTO-CK204`) |
| An entry for a unique index on a stable base with a lower version | `OCTO-CK200` (major bump required) stays; the entry never waives it |
| Hand-edited or forged entries: empty or blank `reason`, wildcard or empty `change`, duplicate `change`, control characters, `##vso[` | Refused, `OCTO-CK206` (the reader's schema check usually stops the file first; the gate does not rely on it). The rules are the compiler's (`CkAcknowledgeEntryRules`, shared with message 130) |
| ckLanguage 1 on both sides, or no `compatibility` section | No new output, no new failure |

The compiled JSON is untrusted input: the verdict (levels, required major bump) is computed from the model content and the
catalog only; nothing in `compatibility` can lower a level. The same-version no-op (identical or equivalent content already
published) does not evaluate entries; nothing is written.

### Applying the minimum version (`--apply`, AB#4467)

When the compile gate (`OCTO-CK200`) or `ValidateVersion` (`OCTO-CK100`) says the declared version is too low, the author does
not have to work out the number: `octo-ckc -c ValidateVersion -p <model folder> --apply` computes the minimum with the same
verdict service the gates use, replaces **only the version** in the `modelId` line of `ckModel.yaml` (comments, key order,
quoting, line endings and the rest of the file stay byte-identical), prints `old → new` and validates the package again against
the new version. The `OCTO-CK200` message of the compile gate names this command with the model's folder.

- **The build never rewrites `ckModel.yaml`**; applying is an explicit command (a build that edits its own sources is not
  reproducible, and an automatic bump would hide exactly the breaking change the gate surfaces).
- **The file is rewritten byte-faithfully:** only the version of the root-level `modelId` key changes (a look-alike line in a block
  scalar is never touched); a file that is not valid UTF-8 (or is UTF-16) is refused instead of converted; the write goes through a
  temporary file, and a read-only file is reported as an error without a stack trace.
- **Only a version that is merely too low is written.** Nothing is written when the verdict is already valid, on a downgrade
  (`OCTO-CK101`/`OCTO-CK201`), when an acknowledgement is missing or stale (`OCTO-CK203`/`OCTO-CK204`), on a compile error,
  or when the file has no single `modelId: <Name>-<version>` line. A version is never lowered.
- **Dependency order.** With several `-p` paths a dependent is validated after its applied sibling and sees the sibling's new
  version (the sibling registration of "Sibling resolution").
- **Upstream major.** When a dependency's catalog holds a newer version outside the declared range, `--apply` reports
  `not reconciled automatically: range [2.4.0,3.0.0) of System excludes System-3.0.0; update the range of '<model>' and run again.`
  The range is never changed; the author updates it and runs the command again.
- The file is written as soon as the version is merely too low; findings that only appear for the new version (for example a
  missing migration with `-rmm`, `OCTO-CK104`) are reported by the validation that follows.
- No git is involved, and `--apply` writes to no catalog beyond what `ValidateVersion` already does (the validated package is
  registered in the per-run catalog copy for sibling resolution, never in the user's local catalog).

## Cascade dry run (`ValidateCascade`, AB#5437)

`ValidateVersion` only looks at the model itself; it is the **dependents** that break (2026-09-30: nine publications across seven
repositories). `octo-ckc -c ValidateCascade -p <candidate model path>` is the read-only dry run of a base model's next version:

```
octo-ckc -c ValidateCascade -p <candidate model path> [-cn <catalogName>] [-o <report.md>] [-rf] [-sp <targetCatalog>] [-lce <bool>] [-lcr <path>]
```

The candidate is compiled in memory (registered nowhere, no catalog is written; catalog reads may fetch uncached remote models, a refresh only happens with `-rf`). The
dependents are the newest version per major line of every model in the readable catalogs whose dependencies name the candidate,
directly or through another dependent. The command prints one verdict per dependent (console, and Markdown with `-o`) and exits
non-zero when at least one dependent **Breaks**. The same catalog options as `ValidateVersion` apply; it works offline against the
local and cached catalogs.

| Verdict | Meaning |
| ------- | ------- |
| `Compatible` | The dependent keeps working unchanged. Range-retaining: its range admits the candidate, the floor is met, every referenced element (and every `usedSurface` member) exists, is public and is not changed incompatibly. |
| `NeedsRepin` | Exact-pinned dependent (ckLanguage 1): everything it references still binds, but it must be recompiled and republished against the candidate. The required level comes from today's dependency rule (same major: minor, other major: major). |
| `Breaks` | A referenced element is gone or re-identified (`System-2.2.2/Entity-1`), became internal, or changed incompatibly (a Major change of the classifier); the candidate is below the dependent's floor (`floor not met`); or a name collision (below). The finding names the member. |
| `NotInRange` | The dependent's range or pin does not cover the candidate's major, or the candidate is older than the pin. Listed, not counted. |

**Skipping an unchanged published candidate (`-sp <targetCatalog>`, AB#6388).** A build that checks every CK of the repository
would otherwise judge versions that are already published and unchanged, and a break that has been in the catalogs for a long time
(a dependent pinned to an element a published base no longer defines) would turn every later build red. With
`-sp <targetCatalog>` the command first asks whether the candidate's own version is already published to that catalog with
identical or equivalent content (`CkPublishEquivalence`, the publish gate's same-version no-op rule). If so it prints
`Skipped: <model> is already published ... an unchanged published model cannot newly break a dependent`, writes that line as
the Markdown report and exits 0. A missing version, different content or an unreadable published file is checked as usual
(a new version keeps its `Breaks`). Use the catalog's name as the engine knows it (`PrivateGitHubCatalog`).

A model that is listed only because it depends on a dependent (no entry for the candidate in its own dependencies) is `Compatible`
("no direct dependency"): it is affected only through the dependents it depends on, which are checked on their own.

**Name collisions (rows H6 / N2).** The classifier sees two versions of one model, so an addition to a stable base stays Minor.
The dry run, which sees the dependents, reports as `Breaks` an attribute (compile error 13) or method (error 100) the candidate
newly declares on a type when a type of the dependent that derives from it already declares the same name. Row N2: a stable base
that redeclares a method it already inherited from an implemented interface is such an addition for every derived type that also
redeclares it; the finding says so.

**What the dry run cannot prove.** It proves that the referenced surface still binds. It does not prove behavioural changes
(defaults, display rules, non-unique indexes, change streams, method timeouts), data compatibility of existing runtime entities,
or that a dependent that is not in any readable catalog still compiles. Dependents that cannot be loaded are listed as warnings.

The checker is the public engine service `ICkSurfaceSatisfactionChecker` (one dependent against a prepared `CkSurfaceCandidate`),
the discovery and the walk are `ICkCascadeService`; the publish gate (F2.3) and the tenant upgrade guard (F2.5) call them without
the CLI.

Sample report:

```
Candidate: System-2.3.0
Baseline: System-2.2.0 (LocalFileSystemCatalog, local, not published)
Candidate change level: MINOR (minimum version 2.3.0, declared 2.3.0: ok)

  Breaks      PlantE-1.0.0 (exact pins)
      PlantE-1.0.0 uses System-2.2.0/Description-1, which System-2.3.0 does not define (attribute removed or re-identified)
  Compatible  LineR-1.0.0 (range-retaining)
      no references into the candidate

2 dependent(s): 1 Breaks, 0 NeedsRepin, 1 Compatible, 0 NotInRange
```

### First publication vs. unreachable catalogs

"No catalog responds" and "catalogs respond, model unknown" are strictly separated:

- Model unknown in all (reachable) catalogs → **first publication** → valid, any version is
  accepted as the starting point.
- Catalog source unreachable during the last cache refresh → **error** `OCTO-CK102`. A missing
  baseline is never silently interpreted as a first publication.

The report also prints the catalog cache age of the baseline; `--refresh` forces a refresh
(mandatory in CI, where a stale cache would validate against the wrong baseline).

## Catalog validation (`ValidateCatalog`, AB#4469)

`ValidateVersion` proves that each model is individually honest; it does not prove that the **catalog as a whole** still resolves.
`octo-ckc -c ValidateCatalog` is the read-only, standalone check (never part of a plain local build) that loads every model of a
catalog at its newest version and reports, with reasons, which ones no longer resolve:

```
octo-ckc -c ValidateCatalog -cn <catalogName> [-m <model> ...] [-o <report.md>] [-rf] [-lce <bool>] [-lcr <path>]
```

| Aspect | Rule |
| ------ | ---- |
| Scope | The whole catalog; `-m System.Bot` (repeatable, case-insensitive) restricts it to the named models **and their transitive dependents**. An unknown name fails with a message listing the catalog's models. |
| Roots | The listing spans `ck-models/v2/` and `ck-models/v3/`; a v3 model that depends on a v2 model resolves. |
| Exact pins (ckLanguage 1) | Every pinned dependency version exists in the catalog (a pin to an older version is fine; a missing version is reported with model, dependency and the versions that exist) **and its model file loads** (AB#6582): a file deleted by hand while the index entry stays is a defect (`... which the catalog index lists, but the model file could not be loaded`). |
| Range-retaining | The range (with its floor) admits at least one version of each dependency. Against the **newest admitted version** every referenced element and every `usedSurface` member must exist, be public and not be changed incompatibly: the surface checker of the cascade dry run (`ICkSurfaceSatisfactionChecker`), judged against the dependency's predecessor in the catalog, so a catalog check and the dry run of the same version cannot disagree. A newer major outside the range is only noted as `not in range`; no admitted version at all is a defect. |
| Case-only name variants | Models whose names differ only in case (`System.Bot` and `system.bot`, in one root or across `v2`/`v3`) are a defect; the report lists both spellings, the newest version and the catalog path of each (`ck-models/v3/s/system.bot/`). |
| Severity | Follows the catalog: in the Meshmakers-run catalogs (`PrivateGitHubCatalog`, `PublicGitHubCatalog`) a defect is an **error** and the command exits non-zero (`ModelValidationException`, process code `-6`); in any other configured catalog it is listed as a **warning** and the exit code stays 0. |
| Read-only | Reads only (a cache refresh with `-rf` aside); works offline against the local catalog (`-lce`, `-lcr` as in `ValidateVersion`). |

Exact-pinned models cannot be compared one to one with the dry run: a pin to a published version always resolves, while the dry run
of a newer base reports `NeedsRepin`/`Breaks` for the re-pin. For range-retaining models `Breaks` and "does not resolve" agree.

Not covered here: pipeline wiring (the catalogs train and the `octo-construction-kit` catalog lane run the command after publishing,
Platform Ops) and the per-model resolution state in the catalog (AB#4470).

Sample report (`-o`):

```
Catalog: PrivateGitHubCatalog
Severity: Meshmakers-run catalog, defects are errors

  ERROR     PlantR-1.0.0 (range-retaining)
      PlantR-1.0.0 uses System@2/Description-1, which System-2.3.0 does not define (attribute removed or re-identified)
  ERROR     PlantE-1.0.0 (exact pins)
      PlantE-1.0.0 pins System-2.2.0, but the catalog has no version of System
  Resolved  LineR-1.0.0 (range-retaining)
      not in range: PlantR-2.0.0 is outside PlantR [1.0,2.0); checked against PlantR-1.0.0

3 model(s): 1 resolved, 2 do not resolve - defects are ERRORS in this catalog (exit code non-zero)
```

## Withdrawing a published version (`Unpublish`, AB#5433)

Published versions are immutable, but a publish can turn out to be premature (2026-09-30: `System-2.3.0` broke the platform
build and could not be taken back). `octo-ckc -c Unpublish` is the escape hatch; it is the model-side twin of `octo-bpm unpublish`:

```
octo-ckc -c Unpublish -m <model> -ve <version> [-cn <catalogName>] [-f] [-rs <reason>] [-lcr <path>]
```

(`-ve` because `-v` is the global verbosity switch of every `octo-ckc` command.)

- **Dry run by default.** Without `-f` the command prints whether the version is published and which published models depend on it,
  and changes nothing. `-f` applies. The default catalog is `LocalFileSystemCatalog`.
- **Dependents are refused, also with `-f`.** The catalog is scanned: a published model depends on the version when one of its
  resolution ranges (the exact pins of a classic model, the declared range of a range-retaining one) is satisfied by the version and
  by no other remaining version of the model - an exact pin on it, or a range it is the only satisfying version of. The refusal names
  those models; the exit code is non-zero. This keeps the escape hatch from creating dangling references. A range that another
  version still satisfies does not block (the 2026-09-30 `System.Bot` pinned `System-2.2.2`, not 2.3.0).
  The scan lists the catalog from its repository ("Fail closed" above), not from the Pages mirror (AB#6581): a dependent published
  seconds ago blocks the unpublish, a version published seconds ago can be unpublished, and a catalog that cannot be read fails the
  command with `OCTO-CK202` instead of checking a partial list. A model file the mirror lacks is read through the Contents API.
- **The version number is burned.** The removal records the number in `withdrawn.json` next to the model directory
  (`ck-models/v2|v3/<letter>/<Model>/withdrawn.json`: version, time, reason). Every catalog `PublishAsync` - `octo-ckc Publish`, the
  `CkCompile` MSBuild publish, also with `--replace`/force - refuses a burned number, also with identical content, because tenants and
  caches may already hold the old file; the next free number works. The record is a file of its own on purpose: a publish by an older
  engine rewrites only the index files it knows, so it cannot drop the record (an older engine does not enforce the burn, it only
  cannot erase it). Withdrawn versions are not in any index any more, so the baseline resolver (AB#5450) never sees them as a baseline.
- **Index and file order.** The burn record is written first, then the index tree is pruned (per-major index, model index, root index,
  in both roots; an index with nothing left is deleted), then the model file is removed. A failure in between leaves a burned number and
  an orphan file, never an index entry that points to a missing file; running the command again completes it.
- **Idempotent.** Running it again says the version was already unpublished and changes nothing; a version that was never published fails.
- **Public catalog.** Unpublishing from `PublicGitHubCatalog` needs `-f` and a mandatory `-rs <reason>`; the reason is part of the commit
  message of every catalog change and of the withdrawn record. The private and dev catalogs need `-f` only (a reason is recorded when given).
- **Replay of 2026-09-30.** `System-2.3.0` published, nothing depends on it: `octo-ckc -c Unpublish -m System -ve 2.3.0 -cn <catalog> -f`
  withdraws it; `ValidateVersion` of `2.2.3` afterwards uses `2.2.2` as the baseline.

Engine API: `ICatalog.UnpublishAsync` (per catalog: `LocalFileSystemCatalog`, `GitHubCatalog`; the embedded catalog is read-only),
`ICatalogService.PlanUnpublishAsync` (the dry run) and `ICatalogService.UnpublishAsync` (reason policy, dependents check, then the catalog).
A real GitHub catalog is only ever written by an explicit command; the tests run against the local file system catalog and an in-memory
GitHub client.

## The fixed rule set (V1)

The highest applicable level in the overall diff determines the minimum bump level. The rule set
is built-in and not configurable. It is implemented as the central rule table in
`ConstructionKit.Engine/SemVer/CkSemVerClassifier.cs` — keep this page and that table in sync.

Validation always compares the **compiled, canonically sorted** models
(`CkCompiledModelRoot`), never raw source YAMLs. Pure formatting/comment changes in the sources
therefore produce an empty diff and require no bump.

References to elements of the model itself are compared ignoring the model version; references
into other models are compared by name and major version. A dependency switching to a new major
therefore surfaces as a breaking reference change, while minor/revision dependency bumps only
surface in the dependency diff.

### Major (breaking)

| Change | Reasoning |
| ------ | --------- |
| Type, attribute, enum, record, or association role **removed** | Consumers reference the element |
| Attribute assignment removed from a type, record, or association role | Consumers reference the attribute |
| `valueType` of an attribute changed (every change except `String → Secret`, including `Secret → String`) | Data format breaks |
| `valueCkEnumId` / `valueCkRecordId` changed | Reference target breaks |
| Attribute assignment references a different attribute definition (`id` changed) | Value semantics may break (defensive) |
| Type attribute changed from optional to **required** (`isOptional: true → false`) | Existing instances may become invalid |
| New **required** attribute **without** `defaultValues` (or referencing an attribute of another model) | Existing instances become invalid (defensive when not inspectable) |
| `derivedFromCkTypeId` / `derivedFromCkRecordId` changed | Inheritance hierarchy breaks (GraphQL schema, queries) |
| Multiplicity **tightened** (permissiveness One < ZeroOrOne < N decreases) | Existing associations may become invalid |
| `inboundName` / `outboundName` of an association role changed | Navigation/GraphQL breaks |
| Type association removed | Consumers use the navigation |
| Type association added referencing a role with multiplicity **One** (either direction) | New mandatory association — entity creation without it is rejected |
| Type association added referencing a role of **another model** | Multiplicity not inspectable (defensive) |
| `targetCkAttributeIds` of a type association changed | Referential integrity changes (defensive) |
| Enum value **removed** or `key` of an existing `name` changed | Stored values become unreadable |
| `useFlags` changed | Value semantics break |
| `isExtensible: true → false` | Runtime extensions are no longer allowed |
| `isAbstract: false → true` or `isFinal: false → true` | Instantiation/derivation breaks |
| Unique index added (`Unique`, `UniqueNotDeleted`) | Existing data may be invalid |
| Type is no longer a collection root (`isCollectionRoot: true → false`) | Collection semantics break (defensive) |
| Dependency removed | Consumers may rely on the transitively provided model (defensive) |
| Dependency switched to a new **major** version | Transitively breaking |
| CK v2: interface **removed** | Implementing types and interface consumers break |
| CK v2: interface member removed, required member added, member `id` changed, member made required | The contract changed — publish a new interface version (`Named-2`) instead (rows I3–I7) |
| CK v2: `implements` entry removed from a type | Consumers querying the type through the interface break |
| CK v2: interface `extends` entry added or removed | The inherited members change the contract — publish a new interface version instead |
| CK v2: interface association member removed, required one added, `target` or `multiplicity` changed, made required | The contract changed — publish a new interface version instead (rows I3–I7) |
| CK v2: interface method added or removed | The contract changed — publish a new interface version instead (rows I11, M1); field changes follow rows M2–M14 |
| CK v2: type association `targetCkInterfaceId` set or changed | The allowed targets narrow — existing associations may become invalid |
| CK v2: method **removed** | Callers of the method break |
| CK v2: breaking method field change (rows M3–M10, M12) | Callers break — publish a new method version (`ChangePassword-2`) instead |
| CK v2: `ckLanguage` lowered (`2 → 1`) | CK v2 elements may disappear (defensive) |
| CK v2: attribute-assignment `access` tightened (`ReadWrite < ReadOnly < MethodOnly < Hidden`; row T7) | Generic GraphQL clients and dependents lose write/read access |
| CK v2: `visibility` `Public → Internal` (type, record, enum, attribute, association role, interface, method; resolved value, omitted = `Public`) | Other models referencing the element break |
| CK v2: `derivable` `Any → Model` (type, record; resolved value) | Other models deriving from the element break |
| CK v2: `ckLanguage` raised (`1 → 2`) without declaring `derivable: Any` on every type/record | The `derivable` default flips from `Any` to `Model`; reported as a `derivable` change per element |

### Minor (additive)

| Change | Reasoning |
| ------ | --------- |
| New type, attribute, enum, record, association role | Purely additive |
| New **optional** attribute on an existing type/record/association role | Additive |
| New **required** attribute **with** `defaultValues` (same model) | Additive, existing data can be filled |
| New enum value | Additive (precedent: `isExtensible` semantics) |
| `isExtensible: false → true` | Relaxation |
| Attribute changed from required to optional | Relaxation |
| Multiplicity **relaxed** (permissiveness One < ZeroOrOne < N increases) | Relaxation |
| `isAbstract: true → false`, `isFinal: true → false` | Relaxation |
| Non-unique index added, any index removed | Query behavior, no data break |
| `defaultValues` / `autoCompleteValues` / `autoIncrementReference` changed | Behavior of newly created instances changes |
| `isRuntimeState` changed (deprecated alias) | Blueprint re-apply behavior changes |
| Attribute `ownership` changed (resolved value) | Blueprint re-apply and export behavior change |
| Attribute-assignment `ownership` override set, cleared or changed | Re-apply and export behavior change for that assignment |
| `valueType` changed from `String` to `Secret` (AB#5528, decision 2) | Stored values stay readable: readers accept legacy plaintext in a Secret slot until the encrypt sweep has run, and the effective ownership becomes `Secret` (re-apply keeps the value, export drops it). Clients that select the value as a string must switch to the is-set state before the model change (concept phase 2) |
| Record `recordKey` set, cleared or changed (AB#5528) | Only decides how secret sub-values are carried over when a record array is replaced; no data or schema change |
| Attribute `metaData` changed | Metadata only, no data break |
| `enableChangeStreamPreAndPostImages` changed | Change stream behavior, no data break |
| Type becomes a collection root (`isCollectionRoot: false → true`) | Additive |
| New type association referencing a non-mandatory role of the same model (no multiplicity One) | Additive |
| New dependency | Additive |
| Dependency version changed without a major switch | Compatible |
| CK v2: new interface | Purely additive |
| CK v2: new `implements` entry on a type | Additive |
| CK v2: new method | Additive |
| CK v2: attribute-assignment `access` relaxed (resolved value; omitted = `ReadWrite`; row T7) | "access/security" changelog note |
| CK v2: `access` tightened on an attribute that is `securitySensitive` in both versions (row T7 exception) | Accepted security exception: Minor + `requiresAcknowledge` |
| CK v2: attribute `securitySensitive` set or cleared (row A3) | Marker only, no data or API change |
| CK v2: `ckLanguage` raised (`1 → 2`; omitted = 1) | Older engines reject the model with message 91 instead of misreading it. Major instead when it flips a `derivable` default (see Major) |
| CK v2: `visibility` `Internal → Public` | Relaxation |
| CK v2: interface `deprecated` set or cleared | Dependents get (or lose) a compile warning; nothing breaks |
| CK v2: type association `targetCkInterfaceId` cleared | Relaxation |
| CK v2: `derivable` `Model → Any` | Relaxation |

### Patch

| Change | Reasoning |
| ------ | --------- |
| `description` (all element kinds, model meta — including CK v2 interfaces, type/interface methods, method parameters and error codes, row M14) | Purely documentational |
| `displayNameRule` / `displayDescriptionRule` changed on a type | Computed display values change only, no data/schema break |
| Pure formatting/comment changes in the source YAMLs | Compiled model identical → empty diff → no bump required |
| `isRuntimeState: true` rewritten as `ownership: RuntimeState` (or `false` as `SeedOwned`) | Same resolved ownership → empty diff → **no bump required**. Both markers are compared on their resolved value, so migrating a declaration to the enum costs nothing; only a genuine change of owner does. |

### CK v2 rule rows (F2.1)

Every row has an id and a test named after it (`N1_…` in `tests/ConstructionKit.Engine.Tests/SemVer/Rows/`); the
classification guard checks both directions. Rows apply to `ckLanguage: 2` elements (interfaces, methods, visibility,
range retention, `securitySensitive`); no level of a v1 rule changes. Rows T, E, R and A restate the v1 rules for public
types, enums, records and attribute definitions with one test each and add the CK v2 access rule (T7).

**Internal elements (AB#6266).** Other models can never reference an internal element, so it is not part of the
compatibility surface. A change counts as internal when the element — itself or through its owner (type, record,
association role, enum, interface, method) — is `internal` in every version in which it exists.

| Row | Change | Level |
| --- | ------ | ----- |
| N1 | Add, remove or modify an element that is internal in the baseline and in the current version (type, record, enum, attribute, association role, interface, method) | at most Minor (description-only stays Patch), reason "internal element, not part of the compatibility surface" |
| N2 | Any change to a member of an internal owner (type/record/role attribute, type association, index, implemented interface, interface member, method parameter or error) | at most Minor |
| N3 | Element removed that was public in the baseline | Major (unchanged) |
| N4 | `visibility` public → internal / internal → public | Major / Minor |
| N5 | Element made public and changed in the same release | classified by the public rules (no loophole) |

Minor rather than none: a same-version re-import is short-circuited, so a structural change still needs a bump to reach
tenants.

**No internal element is reachable from a public one (AB#6334 / AB#6335, gate findings H1/H3).** The rows above are only
sound when "internal" really means "unreachable from other models". The compiler therefore rejects **inconsistent
visibility** in a `ckLanguage: 2` model with message **129** (`CkInconsistentVisibility`): a public element may only
reference public elements of its own model.

| Public referrer | References that must be public |
| --------------- | ------------------------------ |
| Type | `derivedFromCkTypeId`, `implements`, attribute assignments (attribute definition), association role, association `targetCkTypeId` / `targetCkInterfaceId` / `targetCkAttributeIds` |
| Attribute definition | `valueCkRecordId`, `valueCkEnumId` |
| Record | `derivedFromCkRecordId`, attribute assignments |
| Association role | attribute assignments |
| Interface | `extends`, attribute members, association members (role, targets), **methods** (a public interface may not declare an internal method) |
| Public method of a public type | parameter and result `valueCkRecordId` / `valueCkEnumId` |

A type may also not redeclare a method of a public interface it implements as `internal` (129). Allowed: internal →
public, internal → internal, public → public, internal methods on public types (they may use internal records and
enums). Without the rule, an internal record behind a public attribute could lose a member as a "Minor" change while a
dependent model that uses the public attribute breaks (gate case X1: error 109 downstream). Defence in depth: when the
classifier is handed a model in which a public element still reaches an internal one (compiled by an older ckc), that
internal element — transitively — is treated as public and the cap of N1/N2 does not apply; an internal method of a
public interface follows I11. **Inbound associations (N1, platform-owner decision 2026-10-10):** an association of an internal
type that targets a public type (of this model or a dependency) adds an inbound navigation to that public type in
GraphQL, which derived types in other models inherit; the association and its role (inbound name, multiplicities) are
therefore public surface — removing the association or renaming the role is Major, and so is removing or renaming the internal type that owns such an association (P3-1; an internal type without one stays removable as Minor). The compiler does not forbid such
associations (classifier rule only). `CkVisibilityReferenceCoverageTests` fails when a CK DTO gains an element reference that the
visibility walk does not cover.

**Interfaces (AB#6267).** An interface `X-n` grows by optional members; every change that breaks implementors or
consumers is Major and the reason recommends publishing `X-(n+1)` (e.g. `Named-2`) next to `X-n`. Association members are
keyed by their role, so a changed target is one change (`target`), not remove + add.

| Row | Change | Level |
| --- | ------ | ----- |
| I1 | Optional interface attribute added | Minor when its attribute definition is declared in this model **and** is new in this release or was internal in the baseline; otherwise Major (AB#6337): a type of another model may already assign the existing definition — as `Hidden` (error 99) or under another name (I-3) — and the member binds to it. Remedy: a new attribute definition for the member, or a new interface version |
| I2 | Optional interface association added | Minor (optional association members are never checked against implementors, rule 121, so they cannot collide) |
| I3 | Required attribute or association added | Major |
| I4 | Member removed or renamed (remove + add) | Major |
| I5 | Member's attribute id changed (value type, record or enum change of the member) | Major |
| I6 | Association `multiplicity` or `target` changed | Major |
| I7 | Member `isOptional` true → false / false → true | Major / Minor |
| I8 | `extends` entry added or removed | Major |
| I9 | `deprecated` set or withdrawn | Minor |
| I10 | Interface added / removed | Minor / Major |
| I11 | Method added to an interface | Major (an optional interface method does not exist yet); an `internal` method on a public interface is a compile error (129), so it cannot be added as a capped Minor |
| I12 | Invocation contract of an interface method changed (`kind`; parameter added or removed; parameter `valueType`, record, enum, `isOptional` in either direction, `sensitive`; `result`; error code added or removed) | Major — a type in another model that redeclares the method with the previous contract breaks with error 122; this lifts M2, M5 (required → optional) and M13 on interface methods. Metadata (descriptions, authorization, execution) keeps its M-row level. Type methods are unchanged (M1–M14) |

**Invocation contract (AB#6336, gate finding H2).** A type may redeclare a method of an interface it implements; error
**122** fires only when the redeclaration has another **invocation contract**: `kind`, the parameters as a set keyed
by name (`valueType`, `valueCkRecordId`, `valueCkEnumId`, `isOptional`, `sensitive`), `result` (present or absent and
its type) and the set of error codes. Descriptions, `authorization`, `execution` and the order of parameters and
errors may differ; the redeclaring type's own authorization and execution apply. The definition lives in one place,
`CkMethodContract`, used by error 122 and by row I12.

**Methods (AB#6268).** Type methods and interface methods follow the same rows (for interface methods, I12 lifts every
contract change to Major). The diff emits one change per method
field; parameters (`Method parameter '<owner>/<method>/<name>'`) and error codes (`Method error '<owner>/<method>/<code>'`)
are members of their own. The rendered `signature` is still reported as a readable before/after summary with level
`None`; the level comes from the field changes. A change of several fields takes the highest level.

| Row | Change | Level |
| --- | ------ | ----- |
| M1 | Method added to a public type / removed | Minor / Major (on an interface the addition is row I11) |
| M2 | Optional parameter added | Minor (interface method: Major, row I12) |
| M3 | Required parameter added; parameter removed or renamed | Major |
| M4 | Parameter value type, record id or enum id changed | Major |
| M5 | Parameter optional → required / required → optional | Major / Minor; on an interface method relaxing is Major too (row I12) |
| M6 | Result changed (none ↔ value, other type, record or enum) | Major; widening a result record by an optional attribute is a record change |
| M7 | Error code removed | Major |
| M8 | Error code added | Major (Minor needs `errors: open`, which does not exist yet) |
| M9 | `kind` static ↔ instance | Major |
| M10 | `idempotent` true → false / false → true | Major / Minor |
| M11 | `timeoutSeconds` changed | Minor (behavioural) |
| M12 | Authorization stricter (role removed or roles emptied, **scope added**, `allowSelf` true → false, block removed) / looser (role added — also to an empty list —, **scope removed**, `allowSelf` set, block added that only grants) | Major / Minor; mixed → Major. Roles are any-of and compared **case-insensitively** (like ASP.NET Identity's `NormalizedName`: `Admin` and `admin` are one role, a case-only change is no change; P3-3, 2026-10-10), **scopes all-of** and case-sensitive, and authorization is **default-deny**: an omitted block or empty roles admit administrators only (AB#6338, platform-owner decision 2026-10-10; the F3.4 gateway enforces the same). A block added or removed is one change compared field by field: an omitted block counts as no roles, no scopes and no self-calls — administrators only, the owner is not admitted (strict reading confirmed 2026-10-10). Role and scope names are validated by the compiler (message 104: letters, digits, `_ - . : /`, starting with a letter or digit), because they are compared as sets. Every looser change is also listed under "Behavioural changes" as "security: method access widened" |
| M13 | Parameter `sensitive` changed | Minor (interface method: Major, row I12) |
| M14 | Description of the method, a parameter or an error | Patch |

**Public types, stable bases, enums, records, attribute definitions (AB#6269).** A *stable base* is a type other
models may derive from: in a `ckLanguage: 2` model a public, non-final type with effective `derivable: Any`; in a v1
model only `System/Entity` and `System/Configuration` (derived, no meta-model field). Stable bases are public types,
so T1–T7 apply to them unchanged.

| Row | Change | Level |
| --- | ------ | ----- |
| T1 | Optional attribute added to a public type | Minor |
| T2 | Implemented interface added | Minor |
| T3 | Attribute, association, implemented interface or method removed | Major |
| T4 | Base type changed | Major |
| T5 | `isAbstract` / `isFinal` false → true / true → false | Major / Minor |
| T6 | `derivable` Any → Model / Model → Any | Major / Minor |
| T7 | Attribute `access` stricter (order `ReadWrite < ReadOnly < MethodOnly < Hidden`) / looser | Major / Minor. **Security exception** (platform-owner decision 2026-10-10): tightening an attribute that is `securitySensitive: true` in the baseline and the current version is Minor + `requiresAcknowledge`; every other change of such an attribute follows the normal rules |
| E1 | Enum value added (also to a non-extensible enum) | Minor |
| E2 | Enum value removed or renumbered; `useFlags` changed; `isExtensible` true → false | Major |
| R1 | Optional record attribute added | Minor |
| R2 | Record attribute removed, its attribute id changed, optional → required | Major |
| A1 | Attribute definition `description` / `metaData` | Patch / Minor |
| A2 | Attribute definition value type, record id or enum id (`String → Secret` stays Minor, AB#5528) | Major |
| A3 | Attribute `securitySensitive` set or cleared (`ckLanguage: 2`, resolved value: omitted = false) | Minor |

`securitySensitive: true` marks password hashes, security stamps, tokens and 2FA secrets (System.Identity marks them in
Phase 4, F4.2). It is a stored meta-model field (schema, DTO, graph, Mongo, reflection round-trip gate); a v1 model that
declares it fails with message 90.

**Behavioural changes (AB#6270).** Changes that keep the schema compatible but change runtime behaviour keep their
level and are flagged `IsBehavioural`; the verdict report lists them under "Behavioural changes" (after the change
list) and the changelog moves them from "Added" / "Changed" into a section `### Behavioural changes`. Changes that
require an acknowledge (`RequiresAcknowledge`) are listed there too, with "(requires acknowledge)"; breaking ones stay
under "Breaking" with the same suffix. The acknowledge itself is built in F2.2 / F2.3. **Format change for v1 models:**
a changelog section that contains behavioural changes gets the new heading and those lines move under it; a report
gets the extra list. Without behavioural changes the output is unchanged.

| Row | Change | Level |
| --- | ------ | ----- |
| B1 | Attribute default values, `displayNameRule` / `displayDescriptionRule`, `autoCompleteValues`, `autoIncrementReference`, `enableChangeStreamPreAndPostImages`, method `timeoutSeconds` | unchanged (Minor/Patch), marked behavioural. **Exception (AB#6341, ckLanguage 2):** removing the default values of an attribute definition that is assigned as required anywhere in the model or is public is Major — otherwise "required with default" (Minor) plus "default removed" (Minor) would reach "required without default" (Major) in two minor releases |
| B2 | Non-unique index added or removed (any index removed) | Minor, marked behavioural |
| B3 | Unique index added on a type that is not a stable base | Major, no marker |
| B4 | Unique index added on a stable base | Major + `requiresAcknowledge`; the reason names the impact on derived types in other models |

**Index pairing (AB#6339, ckLanguage 2 models).** Indexes have no id. In a `ckLanguage: 2` model a baseline and a
current index of a type with the same ordered field paths — compared case-insensitively, as the compiler and the
database resolve them — are a pair: a pair that differs only in path case is no change; a pair whose `indexType`
changed is one modification: `Unique` → `UniqueNotDeleted` and unique → non-unique are Minor (behavioural),
`UniqueNotDeleted` → `Unique` and non-unique → unique are Major (B3; on a stable base Major + acknowledge, B4),
non-unique → non-unique is Minor (behavioural). Unpaired indexes keep the added/removed rules above; ckLanguage 1
models keep the v1 comparison.

**Range retention (AB#6271).** For a range-retaining model the declared ranges are compared by dependency name
(`Dependency range '<name>'`). The exact closure (`dependencies`) is still diffed as before; removing the rule "resolved
dependency changed → Minor" is F2.4 (AB#5686).

| Row | Change (range-retaining model) | Level |
| --- | ------------------------------ | ----- |
| D1 | Floor raised within the same major | Minor while the new floor does not exceed the version the previous release resolved; **Major beyond it** (N5, 2026-10-10: the previous resolution is excluded — a tenant that installed the previous release with it cannot import this release without changing the dependency first) |
| D2 | Floor lowered or range widened within the same major | Minor |
| D3 | Upper bound narrowed within the same major | Minor, with a behavioural note (tenants with an excluded newer version cannot import the release) — **Major when the new range or floor excludes the version the previous release resolved** (AB#6340: a tenant that installed the previous release with it would need a dependency downgrade); a resolved dependency version that goes down on a range-retaining model is Major for the same reason (exact pins keep the v1 rule) |
| D4 | Range or floor moves to another major | Major |
| D5 | Range dependency added / removed | Minor / Major (as for exact pins) |
| D6 | Model switches from exact pins to range retention, or back (`rangeRetention`) | Minor, reason points to the one-time re-pin (F2.6); **Major** when the switch back to exact pins pins a version other than the one the previous release resolved (N3, 2026-10-10) |
| D7 | `usedSurface` / `usedSurfaceHash` changed | not classified on its own (derived from the model's own changes); listed in `CkModelDiffService.ExcludedProperties` with that reason (AB#4472) |

### usedSurface (AB#4472)

A range-retaining compiled model records, per declared dependency, which elements and members of that dependency it
uses: `dependencyRanges[].usedSurface` (sorted, de-duplicated, major-qualified, version-less ids) and
`usedSurfaceHash` (`sha256:<hex>` over the list, one id per line). Both are computed by the compiler
(`CkUsedSurfaceCollector`) and stored in the catalog JSON and in the tenant's `CkModel` document.

**What is tracked.**

- Element level, for every reference the compiler resolves: base types and base records, implemented interfaces,
  interface `extends`, reused attribute definitions (type, record, role and interface assignments), records and enums
  used as value types (attributes, method parameters and results), association roles, association target types,
  interfaces and target attributes. Deriving from a type or implementing an interface binds its whole public
  surface, so these are recorded as the element (`System@2/Entity-1`).
- Member level: attribute paths into an inherited dependency type — index fields and `ownerAttributePath` — as
  `System@2/Entity-1.Name` (the nearest base type that declares the attribute).
- Not tracked: references into the model itself; references into transitive dependencies that the model does not
  declare (they have no `dependencyRanges` entry; they are floor-checked); display rules (`displayNameRule`) and
  computed-column formulas, which are not parsed for attribute paths. Internal dependency elements cannot appear,
  the compiler rejects references to them (message 112).

**Which change classes it covers (the research question of AB#4472).** A change of a dependency can be tolerated for a
consumer — even across a major — exactly when it touches nothing listed in that consumer's `usedSurface`: removing or
renaming an element or member, changing its value type, record or enum, its multiplicity, a base type, or tightening
`access` / `visibility` / `derivable` of a listed element. With the F2.1 rules every such change of a *public* element
is Major, so the classifier and `usedSurface` together answer "would break: Industry.Energy uses
System@2/Entity-1.Name" (F2.5, AB#5687, consumes it at tenant import).

**What it cannot prove.** Behavioural and semantic changes keep the surface intact: a changed default value, display
rule, index, method timeout or authorization semantics, a changed meaning of an enum value or attribute, or data a
migration rewrites. These are flagged as behavioural changes (rows B1–B4) but not tracked per consumer. A derive or an
implement binds the whole element, so a consumer counts as affected by every member change of that element, even of
members it never reads.

`usedSurface` is not classified on its own (row D7): it is derived from the model's own references, which are diffed and
classified elsewhere.

### Defensive default

A change without an explicit rule is classified as **Major**. Since only a minimum level is
enforced, an overly strict classification is annoying for the developer but never wrong — an
overly lax one, however, is dangerous.

### Classification guard (AB#6272)

A meta-model field cannot reach main unless it has a diff **and** a classifier rule. The guard is
`tests/ConstructionKit.Engine.Tests/SemVer/CkSemVerClassificationGuardTests.cs`; it fails, naming the item, when

1. a public property of an element DTO (`CkTypeDto`, `CkAttributeDto`, `CkEnumDto`, `CkRecordDto`,
   `CkAssociationRoleDto`, the CK v2 interface and method DTOs and their nested DTOs) is neither in
   `CkModelDiffService.ComparedProperties` nor in `CkModelDiffService.ExcludedProperties` — or a new DTO type is
   neither diffed nor excluded with a reason;
2. a compared property has no probe in the guard test, or its probe produces no diff change, or a change of a
   shape that is missing in `CkModelDiffService.EmittableChanges`;
3. a change emitted by a probe, or a synthetic change of any shape in `EmittableChanges` (element kind, change
   kind, property), reaches the defensive default of `CkSemVerClassifier` (`ClassifyChange`,
   `ClassifyDependencyChange`, `ClassifyAttributeAssignmentChange`);
4. a `CkModelElementKind` value has no rule (no shape, or every shape reaches the default);
5. an exclusion has no written reason (`ExcludedProperties` maps each property to its reason);
6. the rule rows of this page and the tests drift apart: a table row whose first cell is a row id
   (`N1`, `I3`, `M12`, `T7`, `E1`, `R2`, `A1`, `B4`, `D5`, …) needs a test whose name starts with that id and an
   underscore (`N1_InternalElementRemoved_IsMinor`), and a test named like that needs the row.

**How to add a meta-model field:** compare it in `CkModelDiffService` (diff code, `ComparedProperties`, and the
emitted shape in `EmittableChanges`), add a probe for it to `PropertyProbes` in the guard test, add the classifier
rule to `CkSemVerClassifier`, and add the rule row to this page with a row test. If the field is deliberately not
part of the compatibility surface, add it to `ExcludedProperties` with the reason instead.

**Known gaps.** Open items live in one place, `KnownGaps` in the guard test, each with the story that closes it:
the rule rows T1–T7, E1–E2, R1–R2, A1–A2 (AB#6269) and B1–B4 (AB#6270). The list only shrinks — and it must be empty when F2.1 closes (AB#6273).

### Renames

A rename is not structurally detectable and appears as remove+add — which correctly requires a
major bump. The report hints at possible renames when removals and additions of the same element
kind occur in one diff.

## Migration reconciliation

If the diff requires a major bump, the command checks whether the model defines a migration with
`toVersion` equal to the declared version. If not, a warning lists the breaking changes — the
engine's no-migrations bridge allows schema-only majors, so this is not an error by default.
`--requireMigrationForMajor` escalates it to `OCTO-CK104`.

Migration reconciliation is **skipped while the declared version itself is still wrong** (verdict
`VersionTooLow` or a downgrade). In that state `OCTO-CK100`/`OCTO-CK101` already require the
developer to raise the version; reconciling migrations at the same time would name the (too-low)
*declared* version as the missing migration's `toVersion` while `OCTO-CK100` simultaneously demands a
higher minimum — a contradictory hint. Once the version is corrected, the next run reconciles
against the now-correct `toVersion` (the check self-heals). For an already-valid version the
reconciliation runs normally.

## Changelog generation

With `--changelog`, the command writes/updates a `CHANGELOG.md` next to `ckModel.yaml` from the
classified diff: one section per version with date, bump level, and every change including its
classification (`### Breaking` / `### Added` / `### Changed`). Existing sections of older
versions are never rewritten; repeated runs replace only the section of the currently declared
version (idempotent). Generation only runs after successful validation. Without the flag, the
command is fully read-only.

## Known limitations (V1)

- **Parallel branches:** two branches that change the same model and both correctly bump to the
  same version validate green against the same published baseline. Only after merge+publish of
  the first does the second one's validation trip (main build as second gate).
- **Foreign attribute defaults:** whether a *required* attribute referencing an attribute
  definition of another model carries default values cannot be inspected — such additions are
  classified Major defensively.
- **Dependency ranges of exact-pinned (v1) models are classified via their resolved versions** (range-retaining models
  are compared on their declared ranges, rows D1–D7). The compiled baseline model
  persists only the *resolved* dependency versions, not the declared ranges (and the compiled
  model schema is closed, so persisting ranges is a catalog-format evolution). A range edit that
  changes the resolved version is classified (major switch → Major, otherwise → Minor); a range
  edit that leaves the resolved version unchanged (e.g. lowering the floor, or raising the
  ceiling while no matching version is published yet) is not visible to the diff and requires no
  bump. The declared ranges themselves are still validated for satisfiability (`OCTO-CK103`).
  Persisting declared ranges in the compiled model is a follow-up alongside the catalog
  `versionInfo` metadata.
- The rule set is not configurable; catalog metadata (`versionInfo`, `IsBreaking` flags) is a
  follow-up.
