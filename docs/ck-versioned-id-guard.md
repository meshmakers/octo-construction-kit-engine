# Versioned generated CK ids at run time: analyzer OM1005 (AB#6673)

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
