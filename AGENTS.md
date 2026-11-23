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
- `src/Runtime.*`: `Contracts`, `Engine` (repositories' rules, blueprints, security, audit trail, secrets) and
  `Engine.Formulas`. `src/BlueprintManager` holds the blueprint catalog tooling.
- `src/SystemCkModel`, `src/StreamDataCkModel`, `src/SystemFilesCkModel`: the CK models `System`, `System.StreamData`
  (archives, time series) and `System.Files` (platform file system) that this repository owns.
- `tests/`, `samples/`, `scripts/` (CI helpers, the additive-release proof) and `azure-pipelines.yml`.

## Read before you change

<!-- >>> generated: routing -->
| When you change | Read first |
|---|---|
| `src/Runtime.Engine/AuditTrails/**`, `src/Runtime.Contracts/AuditTrails/**` | `docs/audit-trail.md` |
| `src/Runtime.*/Blueprints/**`, `src/Runtime.*/Exchange/**` | `docs/blueprint-engine.md` |
| `src/Runtime.*/Blueprints/**`, `src/BlueprintManager/**`, `src/ConstructionKit.Engine/BlueprintCatalogs/**` | `docs/blueprints.md` |
| `Directory.Build.*`, `src/ConstructionKit.Templates/**`, `src/ConstructionKit.MsBuildTasks/**` | `docs/build-and-packaging.md` |
| `src/ConstructionKit.Contracts/**`, `src/ConstructionKit.Engine/Serialization/**`, `src/ConstructionKit.Engine/Resolvers/**` | `docs/ck-field-touch-points.md` |
| `src/Runtime.Engine/CkModelMigrations/**`, `src/Runtime.Contracts/CkModelMigrations/**` | `docs/ck-model-migrations.md` |
| `azure-pipelines.yml`, `src/ConstructionKit.Engine/ModelCatalogs/**`, `src/ConstructionKit.Engine/ModelRepositories/**` | `docs/ck-publishing.md` |
| `src/ConstructionKit.SchemaProvider/**`, `src/*/Serialization/**`, `src/ConstructionKit.Contracts/RtCkIdJsonShim.cs` | `docs/ck-schema-and-json-serialization.md` |
| `src/ConstructionKit.Engine/SemVer/**` | `docs/ck-semver-rules.md` |
| `src/ConstructionKit.Analyzers/**` | `docs/ck-versioned-id-guard.md` |
| `src/ConstructionKit.Engine/Versioning/**`, `src/ConstructionKit.Engine/Resolvers/**`, `scripts/audit-ck-ranges.sh` | `docs/ck-versioning.md` |
| `src/ConstructionKit.Compiler/**`, `src/ConstructionKit.Engine/Services/**` | `docs/compiler-architecture.md` |
| `src/ConstructionKit.Compiler/**`, `src/ConstructionKit.Engine/Messages/**`, `src/ConstructionKit.Engine/Text.resx` | `docs/compiler-rules.md` |
| `src/ConstructionKit.Contracts/DisplayRules/**`, `src/Runtime.Contracts/DisplayRules/**` | `docs/concept-display-name-rules.md` |
| `src/Runtime.Contracts/DataPermissions/**`, `src/Runtime.Engine/Security/**`, `src/ConstructionKit.Contracts/DataTransferObjects/CkTypeWriteAccessDto.cs` | `docs/data-permissions.md` |
| `src/ConstructionKit.Engine/DependencyGraph/**`, `src/Runtime.Contracts/Repositories/Query/**` | `docs/query-columns.md` |
| `src/*/CkModelMigrations/**`, `src/ConstructionKit.Engine/*Catalogs/**` | `docs/runtime-engine-interfaces.md` |
| `src/Runtime.Contracts/RtPath*.cs`, `src/Runtime.Contracts/RtVersion*.cs`, `src/Runtime.Contracts/Unacknowledged*.cs`, `src/Runtime.Engine.Formulas/**` | `docs/runtime-semantics.md` |
| `src/Runtime.*/Blueprints/**`, `src/Runtime.*/Exchange/**`, `src/ConstructionKit.MsBuildTasks/**` | `docs/runtime-state-preservation.md` |
| `src/StreamDataCkModel/**`, `src/Runtime.Contracts/StreamData/**` | `docs/system-streamdata-model.md` |
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
([concept](docs/concept-secret-attribute-type.md), [app model changes](docs/secret-app-model-changes.md),
[frontend handover](docs/secret-frontend-handover.md), [progress](docs/secret-rollout-progress.md),
[sweep dump storage](docs/secret-sweep-dump-storage.md), [wave 1 plan](docs/wave1-push-pr-plan-backend.md));
the blueprint history ([concept v2](docs/blueprints-concept-v2.md), [first plan](docs/blueprint-updates-plan.md));
[Pascal case validation](docs/concept-pascal-case-validation.md),
[hidden columns of an archive](docs/archive-hidden-columns.md) (read it before a CK change makes an archive column reach
a Hidden attribute), [the models this repository owns](docs/system-ck-models.md) and
[the additive release proof](docs/ck-additive-system-proof.md).

## Build & test

```bash
# Local build against the DebugL package chain (version 999.0.0, packages from ../nuget)
dotnet build --configuration DebugL

# Tests without the system tests; CI runs them too and they need no running services
dotnet test --configuration DebugL --filter "FullyQualifiedName!~SystemTests"
dotnet test --configuration DebugL

# One project, one test
dotnet test tests/ConstructionKit.Engine.Tests/ConstructionKit.Engine.Tests.csproj
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"
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
  `CHANGELOG.md`. A change to one of the three CK models of this repository bumps its version and adds the entry to the
  `CHANGELOG.md` next to its `ckModel.yaml` (rule on versions below).
- A new compiler message is added to `MessageCodes.json` and to the generated `MessageCodes.cs` (T4, not part of the
  build); `MessageCodesSyncTests` fails when they differ ([docs/compiler-rules.md](docs/compiler-rules.md)).
- Run `Test-OctoAgentDocs` (octo-tools) after changing `AGENTS.md` or `docs/`; `Test-OctoAgentDocs -Fix` regenerates the
  routing table. The shim file for Claude Code is generated: edit this file, not the shim.

## Rules

These rules bind new and changed code. Known exceptions in existing code are named in the linked docs.

- **This repository is public.** No work item ids, internal hostnames, customer or tenant names, or secrets in docs and
  in new or changed code comments and test names (work item ids stay in `CHANGELOG.md` entries); remove an existing one
  when you touch its line.
- **A new or changed CK meta-model field passes every touch point.** Schemas, DTOs, compiler copies, graph, resolvers,
  the SemVer diff and classifier rule, source generator, docs generator, the persistence gate in
  `octo-construction-kit-engine-mongodb` and the CK meta in the asset repository
  ([docs/ck-field-touch-points.md](docs/ck-field-touch-points.md)).
- **A changed CK model of this repository bumps its version honestly.** Version in `ckModel.yaml` by the rules
  `octo-ckc ValidateVersion` checks; a published version is never edited; System models publish from `main`
  ([docs/ck-semver-rules.md](docs/ck-semver-rules.md), [docs/ck-publishing.md](docs/ck-publishing.md)).
- **Columns never reach Secret or Hidden attributes.** The query column collector emits none, and no archive column
  reaches a Hidden attribute; an explicit request for such a path stays refused by the consumers' guards
  ([docs/query-columns.md](docs/query-columns.md), [docs/archive-hidden-columns.md](docs/archive-hidden-columns.md)).
- **Access decisions come from the one data-permission rule set.** New read and write paths classify through it and
  callers ask `IRtDataPermissionEvaluator`; an entity the caller may not see answers like a missing one
  ([docs/data-permissions.md](docs/data-permissions.md)).
- **A write never loses a conflict or an unknown outcome silently.** A version mismatch raises
  `RtVersionConflictException`, and a failure that may have taken effect is classified by
  `UnacknowledgedWriteClassifier`, not by a copy per host ([docs/runtime-semantics.md](docs/runtime-semantics.md)).
- **Rt data is serialized only with the canonical options.** Scalars box through `JsonScalar.ToClr`, a raw
  `Utf8JsonWriter` honours the shared encoder, and the Newtonsoft parity tests stay green
  ([docs/ck-schema-and-json-serialization.md](docs/ck-schema-and-json-serialization.md)).
- **Imports and blueprint writes never trample runtime state.** A new import, seed or blueprint write path honours the
  attribute ownership markers, the `replacesGenericCreate` preflight and `access`
  ([docs/runtime-state-preservation.md](docs/runtime-state-preservation.md),
  [docs/blueprint-engine.md](docs/blueprint-engine.md)).
- **Audit-trail interfaces forward through `IAuditEventSink`.** A host never gets a bridge class that captures
  `IEventRepository` or `ISystemContext` in its constructor ([docs/audit-trail.md](docs/audit-trail.md)).
- **Tools branch on message number and parameters, never on message text**
  ([docs/compiler-rules.md](docs/compiler-rules.md)).
- **mXparser stays at 4.4.2.** Never upgrade it or call a `License.iConfirm*` method
  ([docs/runtime-semantics.md](docs/runtime-semantics.md)).
