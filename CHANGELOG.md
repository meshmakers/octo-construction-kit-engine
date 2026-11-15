# Changelog

Changes of the engine libraries (`Meshmakers.Octo.ConstructionKit.*`, `Meshmakers.Octo.Runtime.*`) that blueprint and model
authors or service owners need to know about. The CK models of this repository keep their own changelogs
(`src/SystemCkModel/ConstructionKit/CHANGELOG.md`, `src/StreamDataCkModel/ConstructionKit/CHANGELOG.md`). Versions are the libs
release tags (`r<X.Y.Z>`); entries without a release are listed under "Unreleased".

## Unreleased

### Added

- **Blueprint seeds: escape for runtime placeholders (AB#8995).** A seed value with `$${name}` is stored as the literal
  `${name}`, without a variable lookup and without the "Unknown blueprint variable" warning (message 27). Use it for
  placeholders that are filled at run time, such as notification templates (`${count}`) or KPI formulas. It applies to
  `rtWellKnownName`, string attributes, string lists and records / record arrays at any depth, also when the tenant has no
  blueprint variables. An unescaped unknown `${name}` still warns and stays unchanged. Rules: `docs/blueprints.md`,
  "Keeping a runtime placeholder".
  - **Minimum engine version:** the first libs release after r3.5.75 that contains AB#8995. Older engines store `$${name}`
    verbatim, so a blueprint may use the escape only once every cluster that installs it runs that release or later.
  - **Behaviour change:** a seed string that already contained `$${` was written as `$` followed by the substituted variable
    (or with a warning). No blueprint in the monorepo or the public blueprint catalog contained `$${` on 2026-11-15.
