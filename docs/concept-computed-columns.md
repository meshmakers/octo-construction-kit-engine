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

> **Status: implemented in System.StreamData 1.5.0 (Phase 1).** Additive bump (no migration
> script — the engine's no-migrations bridge synthesises the path). The descriptor version flows
> automatically through the generated `SystemStreamDataCkIds.CkModelId`, so every host promotes to
> 1.5.0 on the next tenant resolve. The snapshot (`CkArchiveColumnSpec` + the new
> `ComputedColumnState` contracts enum) and the Mongo mapping (`MongoArchiveRuntimeStore.MapColumnSpecs`)
> carry the fields through; DDL / ingest consumption follows in Phase 2 / 3.
>
> **Two implementation details worth knowing:**
> - `CkComputedColumnResultType` key values are **aligned with the `FormulaResultType` code enum**
>   (`Boolean=0, Int=1, Int64=2, Double=3, DateTime=4`) so the snapshot maps by a direct cast.
> - The generated runtime enum properties are **non-nullable**, so `ResultType` / `ComputedState`
>   cannot be "null" on the runtime record. **`Formula` (a nullable string) is the authoritative
>   computed-vs-ingested discriminator** (`IsComputed => Formula is not null`); the spec only
>   surfaces `ResultType` / `ComputedState` when the column is computed.

The `CkArchiveColumn` record (was: `Path`, `Required`, `Indexed`) is extended so a column
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

> **Status: DDL implemented (Phase 2).** `ArchiveColumnDdl` gained an explicit `ColumnName`;
> `ArchivePathTypeResolver` resolves computed columns (type from `ResultType`, name from `Name`
> lower-cased via `ColumnNameMapper`, always nullable) and skips CK-type resolution for a
> computed-only archive; `ArchiveDdlGenerator` emits them in both the raw and windowed
> `CREATE TABLE`, and `GenerateAddColumn` provides the `ALTER TABLE … ADD COLUMN` statement for
> Phase 7. Ingest-time evaluation is Phase 3.

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

> **Status: implemented (Phase 3).** `CrateDbStreamDataRepository` takes `IFormulaEngine` and
> evaluates computed columns on every ingest path (single / batch / time-range) via
> `BuildComputedPlan` → `ApplyComputedColumns`, writing the result into the row under the computed
> column's physical name. Per-row failures (exception / NaN / null sentinel) store `NULL` and log a
> warning — the insert never fails. Plan building topologically sorts computed-vs-computed
> dependencies (`TopologicalSort`); the common no-computed-column archive pays nothing (empty plan).
>
> **Binding-name decision (important):** a formula binds to columns by their **physical CrateDB
> column name** — the lower-cased, dot-stripped form `ColumnNameMapper.PathToColumnName` produces
> (e.g. CK path `ActivePower` → `activepower`). The row dictionary is already keyed this way, so
> binding is direct and deterministic. Only columns with a numeric reading (number, bool→0/1,
> DateTime→ticks, numeric string) are bound; a formula referencing a non-numeric / absent column
> yields NaN → `NULL`. Standard columns (`timestamp`, `rtid`, …) are **not** bound today — a
> timestamp-derived computed column (#4190) would add `timestamp` to the argument set later.
> Translating user-facing **logical** names → physical names at create time is **Phase 4** (the
> stored formula already uses physical names by then).

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

## §8 Active-archive lifecycle & backfill (Phase 7 — reuses AB#4184 orchestration, **not** its row-generation pointer)

> **Status: planned (Phase 7), AB#4184 complete (2026-06-29) → unblocked.** Design concretised
> below after mapping the AB#4184 implementation.

Adding a computed column, changing its formula, or removing it on an `Activated` archive:

```
add / change formula
        │
        ▼
ComputedState = Pending → ALTER TABLE ADD COLUMN (new / versioned physical name)
        │
        ▼
ComputedState = Backfilling → backfill job:
   page through existing rows → read referenced columns →
   evaluate in .NET (mXparser) → write computed value into the new physical column
        │
        ▼
ComputedState = Active  +  atomic column-pointer flip (single Mongo write:
   active physical name + ComputedState) → drop the superseded physical column
        │
   on failure ▼
ComputedState = Failed  +  drop the new physical column → readers never saw partial data
```

### §8.1 Two different swap models — the key design decision

AB#4189 reuses AB#4184's **orchestration** (job model, coalescing, observability, audit, the
periodic hosted-service tick) but deliberately **does not** reuse its row-level generation
pointer. The two swap units are different granularities:

| | AB#4184 (rollup recompute) | AB#4189 Phase 7 (computed column) |
|---|---|---|
| Swap unit | a **row/window range** | a whole **column** |
| Atomic commit | `archive_<rtId>__genmap` side-table pointer per `(range_start, range_end, rtid_scope)` | logical→physical **column-name pointer** in the archive's Mongo runtime-state |
| Storage cost | `generation` column keyed into the rollup PK | none — raw / time-range tables gain **no** `generation` column |
| Read-path gate | `generation = CASE WHEN <range> THEN <gen> … ELSE 0 END` (exists) | computed columns whose `ComputedState != Active` are **excluded from the SELECT projection**; the active physical name is resolved per column (**new**) |

The generation pointer is window-granular and would force a `generation` column onto raw /
time-range tables that have none — the wrong granularity for a column-scoped change. So the
computed-column path is a **column-projection swap**, exactly as already sketched in §4: a brand
-new computed column is simply absent from the logical projection until `Active`; a formula
change writes a **versioned physical column** (`{base} → {base}__v2`) and flips the
logical→physical name pointer atomically on completion.

### §8.2 Design decisions (Phase 7)

- **D-7.1 — Column pointer in Mongo runtime-state.** The active physical column name and
  `ComputedState` per computed column live in the archive entity's runtime-state. Backfill
  writes the new physical column; completion is **one** Mongo write (name pointer +
  `ComputedState = Active`), after which the superseded physical column is dropped. Single-store
  flip, no CrateDB side-table for the column case.
- **D-7.2 — Read-path projection gating (new work).** The logical→physical column resolver that
  builds the `SELECT` projection (`CrateQueryCompiler` / column resolver) must exclude computed
  columns with `ComputedState != Active` and resolve the active physical name. This is **not**
  covered by AB#4184's generation-CASE injection.
- **D-7.3 — Dual-write during a formula-change backfill.** While `Backfilling` a formula change,
  ingest must populate **both** physical columns — the old formula into the active column (so
  current readers stay correct) and the new formula into `{base}__v2` (so recent rows are
  already consistent at swap time). `BuildComputedPlan` (§5) is extended to carry the pending
  column. A brand-new computed column needs **no** dual-write — readers don't see it until
  `Active`.
- **D-7.4 — Dedicated backfill executor, shared orchestration.** A new
  `IArchiveColumnBackfillExecutor` (CrateDB layer) pages all rows of one archive, evaluates one
  column via the existing `ApplyComputedColumns`, and writes it back. It reuses AB#4184's
  `IRecomputeJobStore`, `IArchiveRecomputeStateStore`, the hosted-service tick, the audit trail
  and the StreamData metrics — but **not** the `genmap` range logic.
- **D-7.5 — Dependent rollups go through AB#4184.** After a raw / time-range computed-column
  swap completes, rollups that aggregate it (§10) are marked dirty in the AB#4184
  `DirtyWindows` / `PendingRecomputeRanges` ledger, so AB#4184's orchestrator recomputes them
  downstream. This is the one place Phase 7 consumes AB#4184's recompute path directly.

### §8.3 Triggers, reader contract, removal

- **Triggers:** implicit (column added / formula changed), manual (API + Studio), periodic
  (the AB#4184 hosted-service tick also drains pending computed-column backfills). Concurrency /
  coalescing / idempotency semantics follow AB#4184's job model.
- **Reader contract during backfill:** a query returns either the previous state (new column
  absent from the projection, or the previous formula's values via the still-active physical
  column) or the new state — never a half-populated mix. The gate is `ComputedState` +
  active-physical-name (D-7.2), not the generation pointer.
- **Removing** a computed column: drop from the logical projection (flip first), then drop the
  physical column. Dependent rollups (§10) must be revalidated.

### §8.4 Open items to verify during implementation

1. Exact location of the logical→physical column projection on the read path (D-7.2).
2. Whether `CkArchiveColumnSpec` / the archive runtime-state already carries an "active physical
   name" field, or whether an additive `System.StreamData` 1.5.x bump is needed (D-7.1).
3. The REST recompute-endpoint shape as the template for the computed-column REST mutations.

## §9 Validation

> **Status: implemented (Phase 4).** `ComputedColumnValidator.Validate` (in the CrateDB layer,
> where `ColumnNameMapper` lives) runs at activation — `CrateDbStreamDataRepository.EnsureArchiveCreatedAsync`
> calls it before provisioning, for raw / time-range archives (rollups are Phase 6). It throws
> `ComputedColumnInvalidException` (a `StreamDataException`, so it surfaces as a stable GraphQL error
> code) on: `Path` + `Formula` together, a `Required` computed column, a missing `Name` / `ResultType`,
> a syntactically invalid formula or one referencing an unknown column, or a cyclic reference between
> computed columns. Syntax + reference resolution use a new **`IFormulaEngine.CheckSyntax`** primitive
> that validates *without evaluating* — so a formula that merely divides by zero at probe values is
> not a false-positive (unlike `Validate`). References resolve against **physical** column names (§5).
>
> *Deferred (UX surface, not correctness):* a GraphQL operand-enumeration query for the Studio picker
> and an optional user-facing logical→physical name translation at create time.

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

> **Status: implemented (Phase 5).** The only change needed: `RollupValidator.ValidateForActivation`
> now builds its accepted-source-name set from **both** ingested column `Path`s and computed column
> `Name`s, so `CkRollupAggregation.SourcePath` may target a source computed column. No SQL change —
> `RollupAggregationColumns.Resolve` already maps `SourcePath` through `ColumnNameMapper.PathToColumnName`,
> so `AVG("powerfactor")` aggregates the stored computed column exactly like an ingested one.
> Recompute propagation when the source computed column is rebuilt remains AB#4184's job.

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
**aggregated output columns** by their **physical name** (e.g.
`(active_avg_sum / active_avg_count) / (apparent_avg_sum / apparent_avg_count)`).

> **Decision — physical-name model (consistent with §5).** Rollup-internal formulas reference the
> aggregate output columns by their **physical** storage name (`active_avg_sum`, `total_sum`, …),
> exactly like raw computed columns reference physical ingested columns. This lets the whole
> rollup-internal path **reuse** the raw machinery unchanged — `ComputedColumnValidator` (the
> aggregate columns' Paths are already the physical names), `BuildComputedPlan` / `ApplyComputedColumns`
> for evaluation, and the same `TryToDouble` binding. The ergonomic `Avg` shorthand
> (`activeAvg` ⇒ `sum/count`) is deferred as a UX nicety alongside the §5 logical→physical
> translation; for now the author writes the `sum`/`count` arithmetic explicitly.
>
> **Status — declaration done (Phase 6 part 1):**
> - **Snapshot:** `MongoArchiveRuntimeStore` appends a rollup's stored computed columns to the
>   generated aggregate columns.
> - **DDL:** `RollupColumnTypeResolver` types a rollup computed column from its `ResultType` (shared
>   `ComputedColumnDdl.Build`, also used by the raw resolver), nullable.
> - **Validation:** `ComputedColumnValidator` now runs for every archive shape (rollups included),
>   resolving references against the aggregate columns' physical names + the computed names.
>
> **Status — evaluation done (Phase 6 part 2, approach a):** `AggregateBucketAsync` now runs a
> `.NET` pass after the aggregate `INSERT` — `EvaluateRollupComputedColumnsAsync` loads the rollup's
> archive snapshot, builds the same `BuildComputedPlan`, reads the just-written bucket rows back via
> `StreamRawRowsAsync` (`RollupComputedColumnSqlBuilder.BuildSelect`), runs `ApplyComputedColumns`
> over each, and `UPDATE`s the computed cells (`RollupComputedColumnSqlBuilder.BuildUpdate`).
> Rollups without computed columns short-circuit (no read-back). The INSERT→SELECT→UPDATE steps are
> not yet one transaction; a reader in the gap sees aggregates populated and computed columns still
> NULL — the optimistic / atomic guarantee is AB#4184 (Phase 7). Recompute / rewind of a bucket
> re-evaluates via the same path.

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
