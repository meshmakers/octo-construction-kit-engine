# CK v1 golden output (CK v2 Phase 0, AB#5584)

Fixtures (frozen sources, `ckLanguage: 1`):

- `system-2.5.0/` — copy of the `System-2.5.0` CK sources (no dependencies).
- `dependent-1.0.0/` — `GoldenDependent-1.0.0`, depends on `System-[2.5,3.0)`: derives from
  `System/Entity` and `System/Configuration`, assigns `System/Name` (type and record), uses the
  `System/TimeRange` record, an own enum and the `System/ParentChild` association, has a display rule.
  It exercises the dependency resolvers and reference handling changed by CK v2 (review L18).

`expected/` holds, per fixture, the compiled YAML, the CK cache JSON (`octo-ckc -c compile ... -c <dir>`) and
under `expected/catalog/` the catalog JSON written by `octo-ckc -c publish -c LocalFileSystemCatalog`.
**All files were regenerated on 2026-10-07 with octo-ckc built from `main` (6189ef1d, engine before
CK v2)** — not from a feature commit. The System files were byte-identical to the earlier goldens.
Re-verified on 2026-10-08 against octo-ckc built from `origin/main` 01fb187 (Phase 1 base): all six files
byte-identical, no regeneration needed.

Regenerate (only if `main` itself changes the v1 output on purpose):

```bash
git worktree add /tmp/eng-main main && dotnet build /tmp/eng-main/src/ConstructionKit.Compiler -c Debug
CKC=/tmp/eng-main/bin/Debug/net10.0/octo-ckc.dll; CAT=/tmp/golden-catalog
dotnet $CKC -c compile -p system-2.5.0 -o out/sys -c out/sys -lce true -lcr $CAT -v none -cr
dotnet $CKC -c publish -f out/sys/ck-system-2.yaml -c LocalFileSystemCatalog -r -lcr $CAT
dotnet $CKC -c compile -p dependent-1.0.0 -o out/dep -c out/dep -lce true -lcr $CAT -v none -cr
dotnet $CKC -c publish -f out/dep/ck-goldendependent.yaml -c LocalFileSystemCatalog -r -lcr $CAT
```

`CkV1CompileOutputUnchangedTests` compiles and publishes the same sources with the current engine (flag off)
and requires byte-identical output.
