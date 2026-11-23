# AGENTS.md

Instructions for coding agents in this repository. Detail lives in `docs/`; read the matching file before changing the
code it covers.

## What this repo is

The Octo Construction Kit Engine is the .NET core of the OctoMesh data model. It compiles Construction Kit (CK) YAML
models, resolves their dependencies and versions, and provides the runtime contracts and engine, the blueprint
machinery and the base CK models that the services build on. The MongoDB persistence is a separate repository,
`octo-construction-kit-engine-mongodb`, and implements the `Runtime.Contracts` interfaces. The repository is MIT
licensed.

- `src/ConstructionKit.*`: `Contracts` (DTOs, serialization, JSON schemas), `Engine` (resolving, SemVer gates,
  catalogs), `Compiler` (the `octo-ckc` CLI), `SourceGeneration`, `MsBuildTasks`, `Analyzers`, `SchemaProvider`
  (image that serves the JSON schemas) and `Templates` (the `dotnet new ConstructionKit` template).
- `src/Runtime.*`: `Contracts`, `Engine` (repository rules, blueprints, import and export, CK model migrations, stream
  data, security, audit trail, secrets) and `Engine.Formulas`. `src/BlueprintManager` is the `octo-bpm` CLI.
- `src/SystemCkModel`, `src/StreamDataCkModel`, `src/SystemFilesCkModel`: the CK models `System`, `System.StreamData`
  (archives, time series) and `System.Files` (platform file system) that this repository owns.
- `tests/`, `samples/`, `scripts/` (CI helpers, the additive-release proof) and `azure-pipelines.yml`.

## Read before you change

<!-- >>> generated: routing -->
| When you change | Read first |
|---|---|
| `src/Runtime.Engine/AuditTrails/**`, `src/Runtime.Contracts/AuditTrails/**`, `src/Runtime.Engine/StreamData/ForwardingArchiveAuditTrail.cs` | `docs/audit-trail.md` |
| `src/Runtime.*/Blueprints/**`, `src/Runtime.*/Exchange/**` | `docs/blueprint-engine.md` |
| `src/Runtime.*/Blueprints/**`, `src/BlueprintManager/**`, `src/ConstructionKit.Engine/BlueprintCatalogs/**` | `docs/blueprints.md` |
| `Directory.Build.*`, `src/ConstructionKit.Templates/**`, `src/ConstructionKit.MsBuildTasks/**` | `docs/build-and-packaging.md` |
| `src/ConstructionKit.Contracts/**`, `src/ConstructionKit.SourceGeneration/**`, `src/ConstructionKit.Engine/Resolvers/**`, `src/ConstructionKit.Engine/Seri*/**` | `docs/ck-field-touch-points.md` |
| `src/Runtime.Engine/CkModelMigrations/**`, `src/Runtime.Contracts/CkModelMigrations/**` | `docs/ck-model-migrations.md` |
| `azure-pipelines.yml`, `src/ConstructionKit.Engine/ModelCatalogs/**`, `src/ConstructionKit.Engine/ModelRepositories/**` | `docs/ck-publishing.md` |
| `src/ConstructionKit.SchemaProvider/**`, `src/*/Serialization/**`, `src/ConstructionKit.Contracts/RtCkIdJsonShim.cs` | `docs/ck-schema-and-json-serialization.md` |
| `src/ConstructionKit.Engine/SemVer/**` | `docs/ck-semver-rules.md` |
| `src/ConstructionKit.Analyzers/**` | `docs/ck-versioned-id-guard.md` |
| `src/ConstructionKit.Engine/Versioning/**`, `src/ConstructionKit.Engine/Resolvers/**`, `scripts/audit-ck-ranges.sh` | `docs/ck-versioning.md` |
| `src/ConstructionKit.Compiler/**`, `src/ConstructionKit.Engine/Services/**` | `docs/compiler-architecture.md` |
| `src/ConstructionKit.Compiler/**`, `src/ConstructionKit.Engine/Messages/**`, `src/ConstructionKit.Engine/Text.resx` | `docs/compiler-rules.md` |
| `src/ConstructionKit.Contracts/DisplayRules/**`, `src/Runtime.Contracts/DisplayRules/**` | `docs/concept-display-name-rules.md` |
| `src/Runtime.*/Secrets/**` | `docs/concept-secret-attribute-type.md` |
| `src/Runtime.Contracts/DataPermissions/**`, `src/Runtime.Engine/Security/**`, `src/ConstructionKit.Contracts/DataTransferObjects/CkTypeWriteAccessDto.cs` | `docs/data-permissions.md` |
| `src/ConstructionKit.Engine/DependencyGraph/**`, `src/Runtime.Contracts/Repositories/Query/**` | `docs/query-columns.md` |
| `src/*/CkModelMigrations/**`, `src/ConstructionKit.Engine/*Catalogs/**` | `docs/runtime-engine-interfaces.md` |
| `src/Runtime.Contracts/Rt*.cs`, `src/Runtime.Contracts/Unack*.cs`, `src/Runtime.Engine.Formulas/**`, `src/Runtime.Engine/Repos*/**` | `docs/runtime-semantics.md` |
| `src/Runtime.*/Blueprints/**`, `src/Runtime.*/Exchange/**`, `src/ConstructionKit.MsBuildTasks/**` | `docs/runtime-state-preservation.md` |
| `src/SystemCkModel/**`, `src/SystemFilesCkModel/**`, `src/StreamDataCkModel/**`, `src/Runtime.*/StreamData/**` | `docs/system-ck-models.md` |
<!-- <<< end generated: routing -->

Background, no path trigger: the design records of the stream-data archives
([time range](docs/concept-time-range-archives.md), [rollups](docs/concept-rollup-archives.md),
[multi-source rollups](docs/concept-multi-source-rollups.md), [recompute](docs/concept-rollup-recompute.md),
[threshold reset](docs/concept-rollup-threshold-reset.md),
[time-weighted aggregation](docs/concept-time-weighted-aggregation.md),
[resolution-aware queries](docs/concept-resolution-aware-series-queries.md),
[downsampling](docs/concept-simple-downsampling.md), [timezones](docs/concept-timezone-aware-queries.md),
[computed columns](docs/concept-computed-columns.md), [export and import](docs/concept-archive-data-export-import.md),
[tenant backup](docs/concept-tenant-backup-archive-data.md),
[native dump](docs/concept-tenant-backup-native-archive-dump.md)); the SECRET value type
([app model changes](docs/secret-app-model-changes.md),
[frontend handover](docs/secret-frontend-handover.md),
[sweep dump storage](docs/secret-sweep-dump-storage.md)); the [blueprint concept v2](docs/blueprints-concept-v2.md);
[Pascal case validation](docs/concept-pascal-case-validation.md),
[hidden columns of an archive](docs/archive-hidden-columns.md) (read it before a CK change makes an archive column reach
a Hidden attribute) and [the additive release proof](docs/ck-additive-system-proof.md).

## Build & test

```bash
# Local build against the DebugL package chain (version 999.0.0, packages from ../nuget)
dotnet build --configuration DebugL

# Tests without the system tests
dotnet test --configuration DebugL --filter "FullyQualifiedName!~SystemTests"

# All tests; CI runs the system tests too and they need no running services
dotnet test --configuration DebugL

# One project, one test
dotnet test --configuration DebugL tests/ConstructionKit.Engine.Tests/ConstructionKit.Engine.Tests.csproj
dotnet test --configuration DebugL --filter "FullyQualifiedName~TestClassName.TestMethodName"
```

- The version comes only from the pipeline: a Debug or Release build with neither `-p:OctoVersion=X.Y.Z` nor
  `-p:OctoNugetPrivateServer=<feed>` fails fast with MSBuild error `OCTO0001`; use `-c DebugL` locally. Release
  against a published version: `dotnet build --configuration Release -p:OctoVersion=X.Y.Z`.
- Warnings are errors and nullable reference types are on. xUnit v3 is the test framework.
- The compiler CLI, the MSBuild properties of the CK build and the template pack are in
  [docs/build-and-packaging.md](docs/build-and-packaging.md).

## Before you commit

- The solution builds in `DebugL` without warnings and the tests above pass.
- A change that model authors, blueprint authors or service owners must know about gets an entry under "Unreleased" in
  `CHANGELOG.md`. A change to `System` or `System.StreamData` bumps its version and adds the entry to the `CHANGELOG.md`
  next to its `ckModel.yaml`; `System.Files` has no such file (rule on versions below).
- A new message is added to `MessageCodes.json` and to the generated `MessageCodes.cs` (T4, not part of the build) in
  `src/ConstructionKit.Engine/Messages/` or `src/Runtime.Engine/Messages/`; `MessageCodesSyncTests` checks only the
  Engine pair. A reason that tools must tell apart gets its own placeholder, because tools branch on the message
  number and parameters, never on the text ([docs/compiler-rules.md](docs/compiler-rules.md)).
- Run `Test-OctoAgentDocs` (octo-tools) after changing `AGENTS.md` or `docs/`; `Test-OctoAgentDocs -Fix` regenerates the
  routing table. The shim file for Claude Code is generated: edit this file, not the shim.

## Rules

These rules bind new and changed code. Known exceptions in existing code are named in the linked docs.

- **This repository is public.** No work item ids, internal hostnames, customer or tenant names, or secrets in docs and
  in new or changed code comments and test names (work item ids stay in `CHANGELOG.md` entries); remove an existing one
  when you touch its line.
- **A new or changed CK meta-model field passes every touch point** of
  [docs/ck-field-touch-points.md](docs/ck-field-touch-points.md), also those in `octo-construction-kit-engine-mongodb`
  and the asset repository; `CkSemVerClassificationGuardTests` checks only the SemVer row.
- **Versions are honest.** A change to one of the three CK models bumps its `ckModel.yaml` version
  (`octo-ckc ValidateVersion`); a published version is never edited; a test-only CK model project sets
  `OctoPublishCkModelToRemoteCatalog=false` ([docs/ck-semver-rules.md](docs/ck-semver-rules.md),
  [docs/ck-publishing.md](docs/ck-publishing.md)).
- **A new or changed attribute of the three CK models declares its `ownership`.** Only System.StreamData runs the
  `OCTO-CK001` lint ([docs/runtime-state-preservation.md](docs/runtime-state-preservation.md), "Attribute Ownership").
- **Secret values and Hidden attributes stay inside the engine.** No query or archive column, export, diff, log line or
  exception text carries a Secret value (plaintext or envelope) or reaches a Hidden attribute
  ([docs/query-columns.md](docs/query-columns.md), [docs/archive-hidden-columns.md](docs/archive-hidden-columns.md),
  [docs/concept-secret-attribute-type.md](docs/concept-secret-attribute-type.md)).
- **Access decisions come from the one data-permission rule set.** New read and write paths classify through it and
  callers ask `IRtDataPermissionEvaluator`; an entity the caller may not see answers like a missing one
  ([docs/data-permissions.md](docs/data-permissions.md)).
- **Imports and blueprint writes never trample runtime state.** A new import, seed or blueprint write path honours the
  attribute ownership markers, the `replacesGenericCreate` preflight and `access`
  ([docs/runtime-state-preservation.md](docs/runtime-state-preservation.md),
  [docs/blueprint-engine.md](docs/blueprint-engine.md)).
- **Rt attribute values box only through `JsonScalar.ToClr`**, and a change to the Rt JSON converters keeps Newtonsoft
  parity; the oracle, `Sdk.Common.PipelineParityTests`, runs in `octo-communication-sdk`, not here
  ([docs/ck-schema-and-json-serialization.md](docs/ck-schema-and-json-serialization.md)).
- **mXparser stays at 4.4.2**, the last BSD-licensed version: never upgrade it, and never raise the version that
  `MxParser_IsTheLastBsdLicensedVersion` expects ([docs/runtime-semantics.md](docs/runtime-semantics.md)).
