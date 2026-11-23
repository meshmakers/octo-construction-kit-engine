---
description: Analyzer OM1005: why run-time code must not use generated versioned CK ids, and the version-less alternative.
applies_to: src/ConstructionKit.Analyzers/**
---

# Versioned generated CK ids at run time: analyzer OM1005

Run-time code must not look up CK elements by a generated **versioned** id (`SystemCkIds.CkEntityTypeId`,
a `CkId<T>` bound to the model version the service was compiled against). A tenant may hold a newer
model version than the service embeds, so the versioned id is not in the tenant's CK cache. Use the
version-less constant (`SystemCkIds.RtCkEntityTypeId`) or convert at once with `.ToRtCkId()`. `CkModelId`
(import, "is the model satisfied") is not an element id and stays allowed.

The analyzer `CkVersionedIdAnalyzer` reports **OM1005** for every use of a `…CkIds.Ck<Element>Id` field
of type `CkId<T>`. It ships inside `Meshmakers.Octo.ConstructionKit.Contracts` (`analyzers/dotnet/cs`).
Not analysed: generated code and assemblies whose name ends with `Tests`.

## Severity: suggestion by default, error on request

| Level | How | Effect with `TreatWarningsAsErrors` |
| ----- | --- | ----------------------------------- |
| Info (default) | nothing to do | build is not affected, hint in the IDE |
| Error | `.editorconfig`: `dotnet_diagnostic.OM1005.severity = error`, **or** `Directory.Build.props`: `<WarningsAsErrors>$(WarningsAsErrors);OM1005</WarningsAsErrors>` | build fails |
| Off | `dotnet_diagnostic.OM1005.severity = none` | nothing reported |

A *warning* would break builds as well, so the package ships Info. No consumer csproj has to be touched
before it updates the package.

## Rollout sequence

1. **Audit** the consumers (read-only) on fresh `origin/main`:
   `git grep -nP '[A-Za-z0-9_]+CkIds\.Ck(?!ModelId\b)[A-Za-z0-9_]+' -- '*.cs'`; ignore hits in `*Tests` assemblies
   and lines with `.ToRtCkId()`; fix the others with the `RtCk…` constant.
2. **Release** the package: consumers get the suggestion, nothing breaks.
3. **Raise to error** per repository once it is clean, in the PR that updates the package.

## Audit 2026-10-14 (origin/main of every C# repository of the organisation)

No run-time use remains in the services, adapters (eda, loxone, sap, finapi, weclapp, billbee, mqtt, demos),
`octo-communication-sdk`, `octo-sdk`, plugs (modbus, dilos), apps and samples. Hits are only in assemblies
named `*Tests` (engine, engine-mongodb IntegrationTests), which are not analysed. No test helper project
whose name does not end with `Tests` uses a versioned id, so no further exclusion pattern is needed.
One offender outside the `Tests` rule: `octo-plug-zenon`,
`src/Octo.Edge.Zenon.Importer/ZenonTypes/EquipmentModelGroupExt.cs:16`
(`BasicCkIds.CkTreeNodeTypeId`; fix: the version-less `BasicCkIds.RtCkTreeNodeTypeId`).

## Versioned generated CK ids at run time - guard OM1005

Rule (also in the monorepo instructions): runtime code never looks up CK elements by a generated **versioned** id. The generated
`<Model>CkIds` classes carry both forms per element: `RtCk<X>TypeId` / `RtCk<X>RoleId` / ... (version-less `RtCkId<T>`, what the CK
cache is queried with) and `Ck<X>TypeId` / ... (`CkId<T>` bound to the model version the service was compiled against). A tenant may
hold a **newer** System than the service embeds (the embedded-import downgrade guard keeps it), so the versioned id is not in the
tenant's CK cache - an entity insert of an older service failed that way (AutoIncrement, F1.0). `CkModelId` (the embedded model id
for import / `IsCkModelSatisfiedAsync`, which compares by name and minimum version) is not an element id and stays allowed.

Guard: the Roslyn analyzer `CkVersionedIdAnalyzer` (project `src/ConstructionKit.Analyzers`) reports **OM1005** for every
reference to a `…CkIds.Ck<Element>Id` field of type `CkId<T>`; allowed are the `RtCk…` constants, `CkModelId`, and a versioned id that is
converted at once with `.ToRtCkId()`. Not analysed: generated code and assemblies whose name ends with `Tests`. It ships inside the
`Meshmakers.Octo.ConstructionKit.Contracts` package (`analyzers/dotnet/cs`), so every project that uses `CkId` / `RtCkId` gets it.

**Severity and rollout (review of PR #278).** OM1005 is **Info (suggestion) by default**: the package never breaks a consumer build, not
even with `TreatWarningsAsErrors` (a Warning would break it too, an Error certainly). A repository raises it after it has audited itself:
`.editorconfig` `dotnet_diagnostic.OM1005.severity = error` (or `warning`), or `<WarningsAsErrors>$(WarningsAsErrors);OM1005</WarningsAsErrors>`
in `Directory.Build.props` (verified: both turn the Info diagnostic into a build error, with `TreatWarningsAsErrors` on or off);
`= none` opts out. Sequence: (1) audit the consumers (git grep `\w+CkIds\.Ck(?!ModelId\b)\w+`, see the rollout sequence above), fix
or list the offenders; (2) release the package - consumers see a suggestion only; (3) each repo that is clean sets the severity to `error`
in the same PR as its package update. Tests: `CkVersionedIdAnalyzerTests` (Roslyn `CompilationWithAnalyzers`: reported with the version-less
replacement, every occurrence, allowed forms, test projects and generated code skipped, default severity Info, raised to error and
switched off through the specific diagnostic options). Verified end to end with a real `dotnet build` of a consumer project with
`TreatWarningsAsErrors`: default builds green, `WarningsAsErrors=OM1005` and the `.editorconfig` line fail with OM1005.
