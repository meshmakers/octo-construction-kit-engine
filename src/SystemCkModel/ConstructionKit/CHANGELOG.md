# Changelog

## 2.8.0 - 2026-10-30

_Required bump level: minor._

### Added

- Enum value 'AggregationTypes-1/Integral' added _(minor — purely additive enum value)_

### Compatibility

- Additive, no data migration: existing entities (e.g. persisted `System/AggregationSdQuery` with
  aggregation types 0-6) are unchanged; dependent models need no re-pin (`System-[2.x,3.0)` ranges
  resolve 2.8.0).
- A service built before 2.8.0 cannot map the stored value 7: do not create a query entity with
  `Integral` before every service of the installation runs a build with System 2.8.0. Roll out like
  every System minor (release train, dependency check, `FixAll`/`ClearCache` per tenant).
- `Integral` (AB#7918): step integral over the query window in value unit x hours, LOCF, rtIds summed,
  raw event archives; rollup support follows in AB#7920.
