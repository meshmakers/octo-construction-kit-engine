# CK engine version contract (`minEngineVersion`, message 126)

AB#6390 (Feature AB#5686, Epic AB#5584). Source: `src/ConstructionKit.Engine/Versioning/CkEngineVersion.cs`.

## What a model requires

| Compiled model | `minEngineVersion` |
|----------------|--------------------|
| `ckLanguage: 1`, no range retention, v1 dependencies | not written (output byte-identical to before) |
| `ckLanguage: 2` or range-retaining | `3.5.1` (`CkEngineVersion.CkV2MinEngineVersion`) |
| v1 model on a dependency that carries one | the highest value of its resolved dependencies |

`3.5.1` is the first libs release with the full CK v2 Phase 1 reader. The value is a constant, not the compiling
engine's version, so compile output does not depend on the build configuration. A model that was published earlier
keeps the value it was published with (published models are immutable), e.g. `3.4.0`.

## What the running engine reports

The pipeline stamps the AssemblyVersion as `Major.Minor.0.0` (`octo-pipeline-templates`, `set-version.yml`), which
cannot tell 3.5.0 from 3.5.2. The engine therefore reads the first parsable value of

1. `AssemblyFileVersion` (`$(BuildNumberLong)`), e.g. `3.5.2.0` on an `r3.5.2` build,
2. `AssemblyInformationalVersion` with `-slug` and `+sourcelink` stripped,
3. the AssemblyVersion,

and uses major.minor.patch. Nothing throws; a missing or garbled version falls through to the next source.
FileVersion wins because it is purely numeric (the SDK appends `+<commit sha>` to the informational version, test
builds append `-<branch slug>`).

| Build | FileVersion | Running engine |
|-------|-------------|----------------|
| `r3.5.2` release | `3.5.2.0` | 3.5.2 |
| `test/3.5-*` lane | `3.5.<counter>.<rev>` | 3.5.`<counter>` (not a release patch) |
| main (private feed) | `0.1.YYMM.NNNN` | none: check skipped |
| DebugL | `999.0.0.0` | 999.0.0 |

## Message 126

A model whose `minEngineVersion` is above the running engine is refused with message 126, naming the model, the
required version and the running engine as major.minor.patch:

| Running engine | Model requires 3.5.1 |
|----------------|----------------------|
| 3.4.x, 3.5.0 | refused (126) |
| 3.5.1, 3.5.2, 3.6.0 | read |
| 999.0.0 (DebugL) | read (and `1000.0.0` is refused) |
| below 1.0 (main line) | **check skipped** |

**Main line rule.** An engine whose major version is below 1 (private-feed builds `0.1.YYMM.NNNN`) skips the check:
it carries no number comparable with the release line. It is covered by the main-line floor `0.1.2610.9010` in
`octo-mesh-deployment/docs/ck-v2-engine-inventory.md` and by rebuilt images. The rule lives in
`CkEngineVersion.IsSatisfiedBy` and is tested in `CkEngineVersionTests`.

## Tests

Pin the running engine in tests with `CkEngineVersion.OverrideCurrentForTests(new Version(3, 5, 0))` (AB#6274); never
rely on the ambient assembly version.
