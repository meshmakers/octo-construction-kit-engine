# CK v1 golden output (CK v2 Phase 0, AB#5584)

`system-2.5.0/` is a frozen copy of the `System-2.5.0` CK sources (a `ckLanguage: 1` model).
`expected/` holds the compiled YAML and the CK cache JSON produced from these sources by the engine
**before** CK v2 (commit `88236851`, `octo-ckc -c Compile ... --cache`).

`CkV1CompileOutputUnchangedTests` compiles the same sources with the current engine and requires
byte-identical output, proving that models without CK v2 keys are unaffected by the CK v2 members.
Do not regenerate the expected files with a newer engine; the sources are frozen on purpose and do
not follow later System versions.
