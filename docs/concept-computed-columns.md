# Concept: Computed Columns via Formula (Raw & Time-Range Archives)

Status: **Draft** — concept agreed in chat on 2026-06-28, implementation pending.
Work item: **AB#4189** (parent epic AB#4073 — VOEST M1: EDA energy data to data lake).

See also:
- [concept-rollup-archives.md](./concept-rollup-archives.md) — the derived-rollup design this concept consumes.
- [concept-time-range-archives.md](./concept-time-range-archives.md) — the `(window_start, window_end)` storage shape shared by rollup and time-range archives.
- AB#4184 — Recompute rollup archives when raw data changes (the optimistic / atomic-swap model this concept **builds on**).
- AB#4188 — Multiple aggregations per rollup archive (computed columns compose with multi-aggregate).
- AB#4190 — Timestamp-derived columns (compatibility for any time-derived formula).

## §1 Overview

A **computed column** is an archive column whose value is **derived by a formula** from one
or more other columns of the **same row**, rather than supplied by the producer at ingest.
Example: an `EnergyMeter` raw archive ingests `activePower` and `apparentPower`; a computed
column `powerFactor = activePower / apparentPower` is materialised on every row.

Computed columns are supported on **raw archives**, **time-range archives**, and —
per the design decision in §11 — **inside rollup archives** themselves (a formula over the
rollup's aggregated columns, e.g. `powerFactor = activeAvg / apparentAvg`).

The hard requirement is that a computed column can be **added to or changed on an *active*
archive** without breaking ingest or readers, with the same optimistic / atomic semantics
established by AB#4184: while the backfill runs, consumers keep seeing the previous archive
state and switch to the new values atomically when it completes; a failed backfill leaves
the previous state intact.

### Design decisions (agreed 2026-06-28)

| # | Decision | Consequence |
|---|----------|-------------|
| D1 | **Stored, evaluated in .NET via mXparser at ingest.** Reuse the existing `Runtime.Engine.MongoDb.Formulas` engine. | One formula semantics across the whole platform (adapter `DataPointMapping` and archive computed columns share the identical mXparser dialect). The computed value is a real, typed CrateDB column → natively aggregatable by rollups and visible to direct SQL consumers (Grafana datasource). Cost: backfill of past rows is a read→compute→write batch in .NET, not a single SQL `UPDATE`. |
| D2 | **Rollup-internal computed columns are in scope** for AB#4189. | A `RollupArchive` may carry its own computed columns over its aggregated output columns. One mechanism, applied at orchestrator write time. |
| D3 | **AB#4184 is sequenced first.** AB#4189 reuses its backfill orchestration + atomic-swap + observability. | AB#4189 does **not** re-invent the atomic swap. The CK-model + ingest-time evaluation + validation (§3–§7) can land independently; the *active-archive backfill* acceptance criteria block on AB#4184. |

### Goals

- A column on a raw / time-range / rollup archive can be declared *computed* with a formula
  referencing one or more existing columns of the same archive by name.
- New rows automatically carry the computed value on ingest — across **every** producer path
  (pipeline node, REST `insertTimeRange`, SDK `InsertAsync`), because evaluation lives in the
  server-side repository, not in any single producer.
- Adding / changing a computed column works on an active archive via a backfill with
  optimistic / atomic semantics (AB#4184).
- Rollups aggregate computed columns the same way as normal columns (single- and
  multi-aggregate per AB#4188).
- Reuse the existing mXparser engine, its `now` / `startOfDay` functions, its `null`
  convention, and the existing syntax-validation service.

### Non-Goals (MVP)

- **String / non-scalar results.** mXparser computes a `double` internally, but the result is
  **cast back to the column's declared type** — exactly as the runtime-query path already does
  (`FieldFilterResolver.ResolveSearchAttributeValue`). So `Boolean`, `Int`, `Int64`, `Double`
  and `DateTime` (ticks) are all supported result types. **`String` is not reachable** this way
  (no string concatenation / text formatting), and non-scalar types (records, arrays) are out of
  scope. Documented as the hard limit; revisit string support only if a concrete need appears.
- **Cross-row / window functions** (lag, moving average) — separate concern.
- **Cross-archive formulas** — a computed column references only columns of the same row.
- **User-defined functions via plugins / code** — built-in mXparser dialect only.
- **Required computed columns.** Computed columns are inherently nullable (formula NULL/NaN
  → SQL NULL); they are never part of the producer's required-attribute contract.

## §2 Where evaluation lives

```
Producer (pipeline node / REST / SDK)
        │  ingested attribute dict  (activePower, apparentPower, …)
        ▼
CrateDbStreamDataRepository.InsertAsync / InsertTimeRangeAsync     ← evaluation hook (NEW)
        │  + computed columns (powerFactor = activePower/apparentPower)
        ▼
multi-row INSERT … ON CONFLICT …  (existing upsert)
```

Evaluation is performed **server-side in the CrateDB repository layer**
(`Runtime.Engine.CrateDb`, same assembly that already references mXparser). This is the
single choke point every ingest path flows through, so the computed value is identical
regardless of producer. The mesh-adapter `SaveStreamDataInArchive` / `SaveTimeRangeStream
DataInArchive` nodes stay **unchanged**.

For rollups, the equivalent hook is in the orchestrator's bucket-write path
(`RollupAggregationSqlBuilder` / `AggregateBucketAsync`): after the aggregates are computed,
the rollup-internal formulas are evaluated over the aggregated values before the bucket row
is written (§11).

## §3 CK Model changes (`System.StreamData`)

The `CkArchiveColumn` record (today: `Path`, `Required`, `Indexed`) is extended so a column
can be a computed column. A computed column has **no source attribute path**; it has an
output name and a formula.

```yaml
ckRecord: CkArchiveColumn
  attributes:
    - Path                 # existing — attribute path for ingested columns; null for computed
    - Required             # existing — must be false/absent for computed columns
    - Indexed              # existing
    - Name                 # NEW (optional) — explicit output column name; for computed columns this
                           #   is the formula-referenceable identifier. For ingested columns it stays
                           #   derived from Path (unchanged behaviour).
    - Formula              # NEW (optional) — mXparser expression. When set → the column is COMPUTED.
                           #   Mutually exclusive with Path.
    - ResultType           # NEW (required for computed) — declared output type the double result is
                           #   cast back to: Boolean | Int | Int64 | Double | DateTime. No String.
    - ComputedState        # NEW (optional) — CkComputedColumnState (see below); managed by the engine,
                           #   not the author.

ckEnum: CkComputedColumnState { Pending, Backfilling, Active, Failed }
```

- `Formula != null` is the marker that a column is computed. The validation hook (§9) rejects
  a column that sets both `Path` and `Formula`, or sets `Required: true` together with
  `Formula`.
- **`ResultType` is declared explicitly** (not inferred) and drives both the physical CrateDB
  column type and the cast-back step (§5). Allowed: `Boolean`, `Int`, `Int64`, `Double`,
  `DateTime`. `String` and non-scalar types are rejected.
- `ComputedState` is engine-managed bookkeeping for the active-archive lifecycle (§8). On a
  freshly created (not-yet-activated) archive, computed columns activate together with the
  table and start life `Active` (no backfill needed — there are no past rows).

A computed column references other columns **by their logical column name** — the camelCase
name `ColumnNameMapper.PathToColumnName(path)` produces for ingested columns, or the `Name`
of another computed column. mXparser binds each referenced name as an `Argument`.

## §4 Storage layout

- Each computed column → one physical CrateDB column, **nullable**, typed from `ResultType`:

  | `ResultType` | CrateDB column type |
  |---|---|
  | `Boolean` | `BOOLEAN` |
  | `Int` | `INTEGER` |
  | `Int64` | `BIGINT` |
  | `Double` | `DOUBLE PRECISION` |
  | `DateTime` | `TIMESTAMP WITH TIME ZONE` |

- Indexed per the column's `Indexed` flag (default on), same rules as ingested columns.
- On an active archive, the physical column is added with `ALTER TABLE … ADD COLUMN` — a
  metadata-only operation on CrateDB.
- **Atomic swap (from AB#4184).** For backfills that must preserve old values during a
  formula change, the column is written under a **versioned physical name**
  (`{base} → {base}__v2`) and the logical→physical mapping is flipped atomically on
  completion, the old physical column dropped afterwards. This is AB#4184 infrastructure;
  AB#4189 reuses it and does not define it here. For a brand-new computed column (never
  visible) the degenerate case is simply: column absent from the *logical* schema until the
  backfill completes, then a single `Pending → Active` flip exposes it.

## §5 Ingest-time evaluation

In `CrateDbStreamDataRepository.InsertAsync` / `InsertTimeRangeAsync`, after the producer's
attributes are flattened into the per-row column dictionary and before the INSERT:

1. For each `Active` computed column of the archive (in dependency order — see §9):
   - Build an `OctoExpression(ConvertTernaryToIf(formula))`.
   - For each referenced column name, `addArguments(new Argument(name, value))`, where `value`
     is the row's value for that column. A missing / NULL operand binds the `null` constant
     (mXparser `-Infinity`), matching the existing engine convention.
   - `calculate()` returns a `double`. **Cast back to `ResultType`** — the identical pattern
     the runtime-query path uses (`FieldFilterResolver.ResolveSearchAttributeValue`):

     | Result | Cast-back |
     |---|---|
     | `double.IsNegativeInfinity` (the `null` constant) | SQL `NULL` |
     | `double.IsNaN` | SQL `NULL` + audit warning |
     | `Double` | as-is |
     | `Int` / `Int64` | `(long)` / `(int)` cast |
     | `Boolean` | `result != 0` |
     | `DateTime` | `new DateTime((long)result)` — ticks; `now()` / `startOfDay()` already return ticks |

     A parse/eval exception → store `NULL` + emit an audit warning (the insert is **not**
     failed — computed columns are best-effort and nullable, mirroring
     `ApplyDataPointMappingsNode`'s fall-back philosophy).
2. Add the computed value to the row's column list. The existing multi-row
   `INSERT … ON CONFLICT DO UPDATE` handles the rest unchanged.

This reuses, verbatim, the null/NaN handling, the cast-back ladder, and the
`ConvertTernaryToIf` translation already proven in `FieldFilterResolver`,
`ApplyDataPointMappingsNode`, and `ExpressionValidationService`.

## §6 Formula language (documented subset)

The mXparser dialect already in field use (`OctoExpression`):

| Category | Allowed |
|---|---|
| Arithmetic | `+ - * / ^ %`, parentheses |
| Comparison / logic | `< <= > >= == !=`, `&&`, `\|\|`, mXparser boolean operators |
| Conditional | `if(cond, a, b)`; C-style `cond ? a : b` sugar (translated to `if`) |
| Built-in functions | mXparser math functions, plus Octo extensions `now(addMinutes)`, `startOfDay(dayCount)` |
| Null | the `null` constant (`-Infinity`); referenced NULL operands bind to it; NaN result → SQL NULL |
| Operands | bound arguments by column name; numeric, boolean (0/1), and DateTime (ticks) columns can all feed a formula — only `String` columns cannot be referenced |
| Result | `double` internally, **cast back to the declared `ResultType`** (`Boolean` / `Int` / `Int64` / `Double` / `DateTime`); stored nullable. No `String` result. |

Start small, documented exactly as above; anything outside is a syntax error caught by §9.

Because both operands and result span boolean / integer / double / DateTime (everything that
round-trips through a `double` — booleans as 0/1, DateTimes as ticks), the only CK primitive a
computed column cannot read or produce is `String`.

## §7 Shared formula engine (refactor — **done**, Phase 0)

The mXparser glue used to be split and partly duplicated:
- `Runtime.Engine.MongoDb/Formulas/OctoExpression.cs` — the engine (+ `NowFunction`, `StartOfDayFunction`),
  living in the **MongoDB** assembly only because `FieldFilterResolver` was its first consumer.
- `ApplyDataPointMappingsNode` (mesh-adapter) and `ExpressionValidationService` (communication
  controller) each carried their **own copy** of `ConvertTernaryToIf` + null/NaN handling.

AB#4189 consolidated this into a single reusable surface so any layer (incl. the asset-repo
GraphQL layer) can evaluate / validate formulas without a direct mXparser dependency:

- **`IFormulaEngine`** + `FormulaResultType` + `FormulaSyntaxResult` in `Runtime.Contracts/Formulas`
  (no mXparser dependency). One method set: `NormalizeTernary`, `Validate(expression, arguments)`,
  `EvaluateRaw(expression, arguments)`, `Evaluate(expression, arguments, resultType)` (the cast-back
  ladder of §5/§6).
- **New project `Runtime.Engine.Formulas`** (net10.0, `PackageId
  Meshmakers.Octo.Runtime.Engine.Formulas`) holds `OctoExpression` + the `internal` mXparser
  function extensions + the `FormulaEngine` implementation + the `AddFormulaEngine()` DI extension.
  It is a **dedicated net10.0 package** rather than part of `Runtime.Engine` because mXparser ships
  `netstandard2.1` but **not** `netstandard2.0`, and `Runtime.Engine` must keep its `netstandard2.0`
  target for the compiler / source-generation tooling.
- `OctoExpression` moved out of the MongoDB assembly; `FieldFilterResolver` / `RtFieldFilterResolver`
  now reference the new package. `ConvertTernaryToIf` lives in `FormulaEngine` only; the adapter node
  and the communication controller's `ExpressionValidationService` are thin callers of `IFormulaEngine`
  (duplication removed). `AddFormulaEngine()` is invoked from `AddMongoDbRuntimeRepository()`, so every
  host that wires the runtime engine gets `IFormulaEngine` registered.

## §8 Active-archive lifecycle & backfill (builds on AB#4184)

Adding a computed column, changing its formula, or removing it on an `Activated` archive:

```
add / change formula
        │
        ▼
ComputedState = Pending → ALTER TABLE ADD COLUMN (versioned physical name)
        │
        ▼
Backfill job  (AB#4184 orchestration):
   page through existing rows → read referenced columns →
   evaluate in .NET (mXparser) → write computed value into the new physical column
        │
        ▼
ComputedState = Active  +  atomic logical→physical pointer flip (AB#4184)
        │
   on failure ▼
ComputedState = Failed  +  drop the new physical column → readers never saw partial data
```

- **Triggers** are AB#4184's: implicit (column added / formula changed), periodic, manual
  (API + Studio), optional event-driven. Concurrency / coalescing / idempotency semantics are
  inherited from AB#4184.
- **Reader contract during backfill** (AB#4184): a query returns either the previous state
  (new column absent, or previous formula's values) or the new state — never a half-populated
  mix. New-column case: the column is simply not in the logical projection until `Active`.
- **Removing** a computed column: drop from the logical schema, then drop the physical
  column. Dependent rollups (§10) must be revalidated.

## §9 Validation

On create / edit of a computed column (reusing the shared validator from §7, which already
does `checkSyntax()` + NaN test-eval):

1. **Syntax** — `OctoExpression.checkSyntax()` against a test binding.
2. **Reference resolution** — every name referenced by the formula must be an existing column
   of the **same** archive (an ingested column, or another computed column).
3. **Acyclic dependency** — computed columns may reference other computed columns, but the
   reference graph must be a DAG. The engine computes a topological evaluation order (used in
   §5). A cycle is rejected at edit time.
4. **Mutual exclusivity** — reject `Path` + `Formula` together, and `Required: true` +
   `Formula`.
5. **Result type** — `ResultType` must be one of `Boolean` / `Int` / `Int64` / `Double` /
   `DateTime`; `String` and non-scalar types are rejected.

The Studio's column editor calls the same validator live so the operator sees errors before
saving, and offers the archive's existing column names as formula operands.

## §10 Rollup consumption of source computed columns

Because a computed column is a **real stored `DOUBLE`**, a rollup over a source archive
aggregates it natively — `CkRollupAggregation.SourcePath` targets the computed column's name
and `RollupAggregationSqlBuilder` emits `SUM/MIN/MAX/AVG/COUNT(...)` over it with no special
casing in the SQL.

One wiring change: rollup `SourcePath` resolution currently assumes a **CK attribute path**
(`ArchivePathTypeResolver` walks the CK type). A computed column is archive-only, not a CK
attribute — so source-path resolution must also accept the source archive's computed-column
names, the same way `RollupLogicalPathResolver` already handles rollup storage-column names
for chained rollup-of-rollup. Recompute propagation when the source computed column is rebuilt
is AB#4184's job (the rollup's watermark range is recomputed downstream).

## §11 Rollup-internal computed columns (D2 — in scope)

A `RollupArchive` may declare its own computed columns whose formula references the rollup's
**aggregated output columns** (e.g. `powerFactor = activeAvg / apparentAvg`).

- The rollup's `Columns[]` is generated from `Aggregations[]` by `RollupColumnGenerator`. We
  extend it to append the declared computed columns **after** the aggregation columns.
- Evaluation happens in the orchestrator's bucket-write path: after the aggregates for a
  bucket are computed, the rollup-internal formulas are evaluated in .NET over the aggregated
  values, then included in the bucket INSERT.
- **Avg operand subtlety.** `Avg` is stored as `{base}_sum` + `{base}_count` and read as
  `sum/count`. A rollup-internal formula referencing an avg column binds to the **logical**
  avg value (`sum/count`), not the raw storage columns — consistent with how
  `RollupLogicalPathResolver` already reverses storage names to logical paths.
- Recompute / rewind of a bucket re-evaluates the rollup-internal formulas atomically with the
  aggregates (AB#4184).

## §12 API surface

Auto-generated CRUD on the archive carries the new `CkArchiveColumn` fields. Because
mutating an *active* archive is special, add explicit mutations alongside the existing
`activate / freeze / rewind` family in `StreamDataMutation` (and REST equivalents under
`/api/v1/streamData`):

| Mutation | Effect |
|---|---|
| `addComputedColumn(archiveRtId, name, formula, indexed)` | Validate, `ALTER ADD COLUMN`, start backfill, `Pending → Active`. |
| `updateComputedColumnFormula(archiveRtId, name, formula)` | Validate, backfill new physical column, atomic swap. |
| `removeComputedColumn(archiveRtId, name)` | Drop from logical schema + physical column; revalidate dependent rollups. |

- A path-enumeration extension lets the Studio list the archive's existing columns as
  candidate operands and validate a draft formula live.
- `octo-cli` commands mirror the mutations (`AddComputedColumn`, `UpdateComputedColumnFormula`,
  `RemoveComputedColumn`).
- All require `StreamDataAdmin` (same guard as the other lifecycle mutations).

## §13 Immutability rule

Computed columns are the **one** schema element that may change after activation. The archive
validation hook keeps ingested `Path` columns immutable post-activation (unchanged
`ArchiveSchemaImmutableException` behaviour) but permits add / edit / remove of computed
columns, routing each through the backfill lifecycle (§8).

## §14 Failure modes

| Situation | Behaviour |
|---|---|
| Per-row formula error / NaN at ingest | Store `NULL` for that cell, emit audit warning. Insert succeeds. |
| Backfill fails mid-run | `ComputedState = Failed`, new physical column dropped, readers keep previous state. |
| Formula references a removed column | Caught at edit time (§9); a column removal revalidates dependents. |
| Cyclic dependency | Rejected at edit time (§9). |
| Source computed column recomputed | Dependent rollups recomputed downstream (AB#4184). |

## §15 Observability

Reuse archive metrics; fold computed-column backfill into AB#4184's recompute observability:
per archive — last successful backfill, in-progress flag, last failure (timestamp + reason);
metrics — backfill duration, rows processed, per-row eval-failure count. Audit: who added /
changed / removed a computed column, with the formula text.

## §16 Test plan (maps to AB#4189 acceptance criteria)

1. Ingest with a computed column — new rows carry the computed value (raw + time-range).
2. Add a computed column to a live archive → backfill → atomic switch; reader sees previous
   state during backfill, new column atomically afterwards.
3. Change a formula on a live archive → backfill with old-values-until-swap semantics.
4. Mid-backfill failure → previous state intact, no partial computed data.
5. Rollup single- and multi-aggregate (AB#4188) over a source computed column.
6. Recompute propagation when the source computed column is rebuilt (AB#4184).
7. Rollup-internal computed column (`powerFactor = activeAvg / apparentAvg`) on ingest and on
   recompute/rewind.
8. Validation: syntax error, unknown operand, cyclic dependency, `Path`+`Formula`, `Required`+
   `Formula` — all rejected.
9. Null/NaN handling: NULL operand → defined result; NaN result → SQL NULL.

## §17 Sequencing & dependencies

| Dependency | Relationship |
|---|---|
| **AB#4184** (first) | Provides backfill orchestration, atomic swap, recompute triggers, observability. The active-archive ACs of AB#4189 block on it. The CK-model + ingest-time evaluation + validation + the shared evaluator refactor (§3–§7, §9) can be implemented in parallel / ahead. |
| **AB#4188** | Multi-aggregate composes trivially — a computed source column is just another aggregatable column. |
| **AB#4190** | Timestamp-derived columns: `now` / `startOfDay` already cover the time-function need; compatibility noted. |
| **AB#4186** | VOEST App must tolerate active-archive backfills without hard errors — same optimistic contract. |

## §18 Open items to confirm during implementation

- **Computed-referencing-computed**: confirmed allowed as a DAG (§9). If we want to keep v1
  minimal we could restrict references to ingested/aggregated columns only; current concept
  allows the DAG.
- **Null binding semantics**: the existing engine maps `null` to `-Infinity`. For
  archive computed columns we may prefer a stricter "any NULL operand → NULL result" rule
  (short-circuit before mXparser) to avoid surprising arithmetic with `-Infinity`. Decide at
  implementation; default recommendation: short-circuit NULL→NULL for archive formulas, keep
  the `null` constant available for explicit use.
- **Indexing default for computed columns**: default `Indexed: true` like ingested columns, or
  opt-in only (computed columns are often derived ratios that are filtered less often)? Lean
  default-on for consistency.
