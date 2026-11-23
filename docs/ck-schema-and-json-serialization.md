---
description: How a CK meta-schema change reaches model authors, and the Newtonsoft parity rules of the Rt JSON serializers and RtCkIdJsonShim.
applies_to: src/ConstructionKit.SchemaProvider/**, src/ConstructionKit.Contracts/Serialization/Schema/**, src/Runtime.Contracts/Serialization/**, src/ConstructionKit.Contracts/RtCkIdJsonShim.cs
---

# JSON schema publish path and Rt JSON serialization parity

## Working with Construction Kit Models

When modifying CK models:
1. YAML files in `ConstructionKit/` folders define the model structure
2. Models must follow the schema at `https://schemas.meshmakers.cloud/construction-kit-meta.schema.json`
3. After changes, recompile the model using the compiler
4. Generated code will be created based on the model definitions

### JSON schema publish path (AB#8522)

The JSON schemas a model's `$schema` points to (`https://schemas.meshmakers.cloud/<file>`) are served from the files in
`src/ConstructionKit.Contracts/Serialization/Schema/`, `src/ConstructionKit.Contracts/BlueprintCatalogs/Serialization/Schema/`
and `src/Runtime.Contracts/Serialization/Schema/`. A schema change reaches model authors only along this path:

1. **Image** - this repository's CI builds `meshmakers/octo-schema-provider` (nginx, `src/ConstructionKit.SchemaProvider/Dockerfile`
   copies the three schema folders flat into the web root) and tags it with the libs version (`3.5.x.0`).
2. **helm-core train** - `start-helm-core-release-train` (def 193, `octo-mesh-deployment/pipelines/release-helm-core-train.yml`)
   packages the chart `octo-mesh-schema-provider` (repo `octo-helm-core`) with `AppVersion` = libs tier + `.0`, i.e. the image above.
3. **CD prod-1** - `octo-mesh-schema-provider-CD` (def 213, `octo-mesh-deployment/pipelines/deploy-octo-mesh-schema-provider.yml`)
   runs after every train run on `main`, waits for the approval on the environment `prod-1-schema-provider` and deploys the single
   global instance on prod-1. Its smoke step fails unless every `*.json` file of the running image answers 200 at the public host,
   byte-identical.

So a new schema key (a new meta-model field) is usable in catalog models only after a libs release **and** the next helm-core train
with an approved CD run. Until then the served schema rejects the key (`additionalProperties: false`). Before 2026-11-09 the CD trigger
filtered `refs/tags/r*`, which no train run matches, so schemas.meshmakers.cloud stayed on the 2026-06-28 deployment (AB#8522).

## JSON Serialization & Newtonsoft Parity

The Rt-model serialization rules are split across two canonical options bundles, both in
`Runtime.Contracts/Serialization/`:

- `RtNewtonsoftSerializer.DefaultSerializer` — pre-migration Newtonsoft setup (used by tests and
  legacy callers). `RtNewtonsoftAttributesConverter` preserves source CLR types through the
  in-memory `JObject.FromObject` / `JToken.ToObject` round-trip (e.g. `int 1` stays `Int32` in
  `JValue.Value`).
- `RtSystemTextJsonSerializer.Default` — STJ counterpart. Has `RtAttributesConverter` for the
  attribute dict, the CK / Rt ID converters, and the **`NewtonsoftParityDoubleConverter` /
  `…SingleConverter` / `…DecimalConverter`** that emit `.0` for whole-number reals (mirroring
  `JsonConvert.ToString`). Without these, `double 0.0` would serialize as `0` and re-deserialize
  as `long` via `JsonScalar.ToClr` — observable as `BsonInt64` regressions in MongoDB.
  It also sets **`Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`** so serialized output
  matches Newtonsoft byte-for-byte on non-ASCII (umlauts/ß) and HTML chars (`< > &`); STJ's default
  `JavaScriptEncoder` would `\uXXXX`-escape those and diverge from the legacy wire form (breaking
  hash/HMAC parity over serialized bytes). This encoder is the single source of truth and flows into
  `SystemTextJsonOptions.Default` (octo-sdk) and every wire node. **Any consumer that serializes Rt
  data with a raw `Utf8JsonWriter` (e.g. SDK `IDataContext.WriteJsonTo`) must honour it** — pass
  `new JsonWriterOptions { Encoder = SystemTextJsonOptions.Default.Encoder }`.

`JsonScalar.ToClr` is the single source of scalar boxing (`Runtime.Contracts/Serialization/`).
Rules:
- Integers prefer `Int32` (fits in 32 bits), then `Int64`. Matches Newtonsoft's
  `JObject.FromObject(int)` → `JValue.Value=Int32`.
- Reals box to `double`.
- ISO-8601 strings parse to `DateTime` when `parseDateStrings=true`.

The parity contract is enforced by `Sdk.Common.PipelineParityTests` (in octo-sdk) using
Newtonsoft as the oracle. Irreducible divergences (float vs double, decimal vs double,
DateTimeOffset vs DateTime — JSON has no source-CLR-type marker) are listed by name in
`AttributeValueParityCorpus.IrreducibleDivergences`. Consumers needing exact source-CLR-type
preservation must use typed accessors (`GetAttributeValue<decimal>`, typed properties on DTOs).

**RtCkId virtual-property shim.** On the wire an `RtCkId<T>` serialises to a bare
`SemanticVersionedFullName` string, but legacy pipeline YAML drills `.SemanticVersionedFullName` /
`.FullName` expecting the historical object shape. The rule that those two property names on a JSON
string are virtual self-aliases is centralised in **`RtCkIdJsonShim`**
(`ConstructionKit.Contracts/RtCkIdJsonShim.cs`), next to `RtCkId<T>` where the wire shape is decided.
It replaces magic strings formerly duplicated across the SDK walkers (`JsonPathWalker`, `DataOverlay`,
`LayeredSource`), `RtNewtonsoftAttributesConverter`, and the mesh-adapter `CreateUpdateInfoNode`.
