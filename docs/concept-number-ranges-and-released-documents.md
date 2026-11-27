---
description: System CK 2.9.0 schema for number ranges per fiscal year and released-document locks (numbering on release, lock, storno, audit, gap report) - elements, decisions, acceptance-criteria mapping and compatibility.
---

# Number ranges and released documents (System 2.9.0)

System 2.9.0 carries the whole schema of two Features in one System bump (AB#9752):

- **AB#8675** - document numbers are allocated atomically from ranges per document type and fiscal year
  (AB#9753 atomic allocation, AB#9754 fiscal-year ranges and format tokens, AB#9755 start value and company tenants);
- **AB#8676** - released documents keep their number for ever (AB#9758 number on release, AB#9759 lock,
  AB#9760 same error on every API, AB#9761 storno, AB#9762 audit, AB#9763 gap report).

The schema is final for both Features: their stories change code, not the System model. This page is the design
note those stories refer to; the behaviour itself is built by them.

## 1. Elements

All new attributes are optional. The attributes on `System/AutoIncrement` are `TenantOwned` like `End`, `Format` and
`CurrentValue` (AB#5359): the finance admin configures the range on the running tenant, a blueprint seed only
initialises it, and a re-apply neither resets a counter nor undoes the tenant's configuration.

### 1.1 `System/AutoIncrement` (existing type, new optional attributes)

| Attribute | Type | Meaning | Absent |
|-----------|------|---------|--------|
| `End` | Int | upper limit (existing); applies to every period | - (required as before) |
| `Format` | String | number format (existing): legacy `string.Format` (`{0:D4}`) or tokens (1.4) | the plain number |
| `CurrentValue` | Int | last number handed out (existing); the counter of a range without periods | - (required as before) |
| `StartValue` | Int | first number of the range and of every new period | continue from `CurrentValue` |
| `PeriodKind` | Enum `AutoIncrementPeriodKinds` (`None` = 0, `FiscalYear` = 1) | whether the counter restarts per fiscal year | `None` |
| `FiscalYearStartMonth` | Int 1-12 | month in which the fiscal year starts | 1 (calendar year) |
| `ReferenceTimeZone` | String, IANA id | zone in which the allocation or release time is read to decide the fiscal year | allowed only without a period reset; with `PeriodKind = FiscalYear` the range is refused at configuration time (D2) |
| `Periods` | RecordArray of `AutoIncrementPeriod` | the per-fiscal-year counters of a `FiscalYear` range | no periods |
| `Release` | Record `NumberRangeRelease` | numbering on release and lock of released documents | numbering on insert, no lock |

### 1.2 Record `System/AutoIncrementPeriod`

| Attribute | Type | Meaning |
|-----------|------|---------|
| `FiscalYear` | Int | the fiscal year, named by the calendar year in which it starts |
| `CurrentValue` | Int | last number handed out in this fiscal year |

One entry per fiscal year, so that one atomic `findOneAndUpdate` advances exactly one fiscal year's counter
(`$inc` on `Periods.$[p].CurrentValue` with an array filter on `FiscalYear`). A missing year is added together with its
first block by one conditional update that matches only while no entry of that year exists, so two writers never both
create it; the loser increments the entry the winner added (AB#9754). `CurrentValue` stays the counter of ranges
without periods.

### 1.3 Record `System/NumberRangeRelease`

| Attribute | Type | Meaning |
|-----------|------|---------|
| `StateAttribute` | String (required) | name of the state attribute of the document type, e.g. `DocumentState` |
| `ReleasedValues` | StringArray (required) | state values that mean released: an enum value by its name, or a string value; compared case-sensitively. A document in one of them gets its number and is locked |
| `ChangeableAttributes` | StringArray | attributes that may still change on a released document (e.g. a payment status); absent = none |
| `StornoCkTypeId` | String | CK type id of the storno document of this document type; absent = no storno configured |

### 1.4 Format tokens

`Format` stays one string; no schema element is added for prefixes. A format that contains one of the tokens
`{yyyy}`, `{yy}` or `{counter}` / `{counter:D<n>}` is a token format (e.g. `RE-{yyyy}-{counter:D5}`):
`{yyyy}` and `{yy}` are the fiscal year (`AutoIncrementPeriod.FiscalYear`, four and two digits), `{counter:D5}`
the zero-padded counter, everything else is literal text (the prefix). Any other format is passed to
`string.Format` with the counter as `{0}`, exactly as today.

### 1.5 Type `System/DocumentAuditEntry` (new, final, derived from `Entity`)

| Attribute | Type | Meaning |
|-----------|------|---------|
| `DocumentRtId` | String (required) | the document |
| `DocumentCkTypeId` | String (required) | its CK type |
| `Action` | Enum `DocumentAuditActions` (`Release` = 0, `Storno` = 1, `Denied` = 2) (required) | what happened |
| `Timestamp` | DateTime UTC (required) | when |
| `SubjectId` | String | user id of the caller |
| `ClientId` | String | OAuth client id of the caller |
| `DocumentNumber` | String | the formatted number |
| `FiscalYear` | Int | fiscal year of the number |
| `SequenceValue` | Int | the unformatted counter value |
| `ReferenceRtId` | String | storno: on the original the storno, on the storno the original |
| `Source` | String | denied: GraphQL, REST path or pipeline, if known |
| `AttributeNames` | StringArray | denied: the attributes the refused write tried to change |
| `Message` | String | detail, e.g. the error code of a refused write |

Index `(DocumentRtId, Timestamp)` for "the audit trail of one document in time order". "Released at / released by"
is the `Release` entry of the document; no attribute is added to the document types.

### 1.6 Association role `System/Cancels`

Outbound `Cancels` (One) from the storno to the original, inbound `CancelledBy` (N). System declares only the role;
the document model declares it on its storno type (e.g. Basic `Document`, in a Basic minor version). The inbound
side is N in the schema; a second released storno of one original is refused in code (1.7, decision D6).

## 2. Decisions

| # | Decision | Reason |
|---|----------|--------|
| D1 | **The release configuration lives on the number range** (`AutoIncrement.Release`), not on the CK type attribute and not through AB#5983 (decided by Odin, comment on AB#9752). The lock finds it through the type attribute's `autoIncrementReference`. If AB#5983 (state-conditional data policies) starts first, the lock of AB#8676 uses its mechanism. | No meta-schema change, tenants configure it at run time, one System bump. |
| D2 | **The fiscal year is decided by the release date in the range's `ReferenceTimeZone`**, not by UTC (Odin). A range without release configuration uses the allocation (insert) time. The zone is **required** for a range with `PeriodKind = FiscalYear`: a missing or unknown IANA id is a validation error when the range is configured, never a silent UTC default; only a range without a period reset may leave it empty. No tenant-level time zone in this bump (Odin, 2026-11-24). | The invoice date is the release date; a release at 00:30 local time on 1 January belongs to the new year, and a missing zone must not quietly put it into the old one. A tenant time zone would be a System-wide concept beyond this Feature. |
| D3 | **A batch that changes a released document is refused as a whole**, with a forbidden error naming the document (Odin). | No partial write next to a locked document; matches the all-or-nothing rule of AB#8674. |
| D4 | **A fiscal year is named by the calendar year in which it starts** (`FiscalYearStartMonth` 7: July 2027 - June 2028 is 2027; `{yyyy}` shows 2027). | One integer key per period; with start month 1 it equals the calendar year. |
| D5 | **`StartValue` is a floor, never a reset**: the next number is `max(counter, StartValue - 1) + 1`. A start value set below a counter already in use is ignored, so it never produces a duplicate. Every new fiscal year starts at `StartValue` (or 1). | Continuing an external range (1235 after 1234) and protecting numbers already issued, without a separate validation path. |
| D6 | **A second storno of an original that already has a released storno is refused.** | A cancelled document is cancelled once; a correction of the correction is a new document. |
| D7 | **A storno of a draft is refused** (drafts are edited or deleted). | Only a released document needs a storno. |
| D8 | **Locked state = the document's state value is in `ReleasedValues`** (a cancelled original is listed there too, so it stays locked). | One rule for the lock and the numbering. |
| D9 | **Audit entries are append-only**: the engine refuses every update and delete of a `DocumentAuditEntry` (AB#9762); they are readable only by callers who may read the document. | Audit evidence. |
| D10 | **Restore and import meet the lock as follows.** A tenant dump restore (mongorestore into an unregistered database, then attach) is outside the lock by design: it restores documents, counters, periods and audit entries 1:1 to a consistent prior state; there is no named bypass switch. An RT import of a released document that does not exist yet in the tenant is allowed and keeps the number from the file (no renumbering). An RT import with Upsert, a blueprint seed install or the nightly re-apply onto an existing released document is refused with the same forbidden error as the API and writes a `Denied` audit entry; carrying identical content (the usual nightly re-apply) is a no-op without a write and without an audit entry - only a change is refused. The lock check sits where both write paths reach it - the repository write methods, or one shared check that `BulkRtMutation` and the import (before `BulkInsertRtEntitiesAsync`) both call - not only in `BulkRtMutation`, because the import writes through `BulkInsertRtEntitiesAsync` and skips the mutation hooks. | The import path is the admin bypass the lock must not have; a restore needs no bypass because it never passes the engine. |

## 3. Acceptance criteria mapped to the schema

| Story | Criterion | Schema element |
|-------|-----------|----------------|
| AB#9753 | 20 parallel creations get n+1..n+20; one `findOneAndUpdate` `$inc`; retry gives one number; `End` raises `AutoIncrementEndReached`; rollback with the transaction | `AutoIncrement.CurrentValue`, `End` (no new element) |
| AB#9754 | `RE-{yyyy}-{counter:D5}`, restart on 1 January | `Format` tokens (1.4), `PeriodKind = FiscalYear`, `Periods` |
| AB#9754 | fiscal year starting 1 July, `{yyyy}` shows the fiscal year | `FiscalYearStartMonth`, D4 |
| AB#9754 | two document types number independently | one `AutoIncrement` per type attribute (`autoIncrementReference`, existing) |
| AB#9754 | legacy `{0:D4}` without period unchanged | absent `PeriodKind` = `None`, legacy `Format` |
| AB#9754 | parallel creations across the year boundary distinct | `Periods` entry per year, atomic update (1.2) |
| AB#9755 | start value 1235 continues 1234 | `StartValue`, D5 |
| AB#9755 | start value below current: never a duplicate | D5 |
| AB#9755 | two company tenants number independently | the counter is a tenant entity (existing) |
| AB#9755 | blueprint re-apply keeps the counter | `TenantOwned` on all `AutoIncrement` attributes incl. `Periods` |
| AB#9758 | draft gets no number; release numbers it in the same write; insert in released state numbers it | `Release.StateAttribute`, `Release.ReleasedValues` |
| AB#9758 | fiscal year of the release date | `ReferenceTimeZone`, `FiscalYearStartMonth`, D2 |
| AB#9758 | deleted draft leaves no gap; re-update of a released document keeps its number | `Release` (number only on the transition into a released state) |
| AB#9758 | range without release configuration numbers on insert | absent `Release` |
| AB#9759 | every write to a released document refused, stored document unchanged | `Release.ReleasedValues` (D8) |
| AB#9759 | mixed bulk write | D3 |
| AB#9759 | attribute changeable after release | `Release.ChangeableAttributes` |
| AB#9759 | drafts unchanged; no admin bypass | `Release` (state not in `ReleasedValues`) |
| AB#9760 | GraphQL, REST, pipeline callers get the same error | no schema element (engine error code) |
| AB#9761 | storno gets its own number, original unchanged | `Release.StornoCkTypeId`; the storno type's own `AutoIncrement` range |
| AB#9761 | storno of a draft refused; second storno refused | D7, D6 |
| AB#9761 | original finds its storno through the association | role `Cancels` / `CancelledBy` (1.6) |
| AB#9762 | release entry (document, action, identity, UTC time, number) | `DocumentAuditEntry` with `Action = Release`, `SubjectId`, `ClientId`, `Timestamp`, `DocumentNumber` |
| AB#9762 | storno entry for the original and for the storno | `Action = Storno`, `ReferenceRtId` |
| AB#9762 | denied entry (identity, time, path, attribute names) | `Action = Denied`, `Source`, `AttributeNames`, `Message` |
| AB#9762 | audit entries cannot be changed or deleted; trail by document in time order, readable only with the document | D9, index `(DocumentRtId, Timestamp)` |
| AB#9763 | missing and duplicate numbers per range and fiscal year; drafts not counted | the documents' number attribute (index in the document model) parsed with `Format`; `DocumentAuditEntry.FiscalYear` / `SequenceValue` as cross-check |

## 4. Compatibility per element

| Element | Impact |
|---------|--------|
| New optional `AutoIncrement` attributes | Minor. Existing counters (Basic `BillingDocumentDocumentNumber`, EnergyCommunity `CustomerNumber`, the energy-community seed counters with `End`, `Format`, `CurrentValue`) validate and number unchanged; absent = today's behaviour. |
| Records `AutoIncrementPeriod`, `NumberRangeRelease` | Minor, new elements, used only through the optional attributes. |
| Enums `AutoIncrementPeriodKinds`, `DocumentAuditActions` | Minor, new elements. |
| Type `DocumentAuditEntry` | Minor, new final type; a new collection appears only when the first entry is written. |
| Association role `Cancels` | Minor, new role; no existing type declares it, so no existing entity or GraphQL type changes until a document model opts in. |
| Version | `System-2.8.0` -> `System-2.9.0`; no CK migration script; dependents with `System-[2.x,3.0)` need no re-pin. Verified by `System29_AgainstSystem280_IsAMinor_WithOnlyAddedElements` (`tests/ConstructionKit.Compiler.Tests/SystemInterfacesTests.cs`). |

AB#9754 implements `PeriodKind`, `FiscalYearStartMonth`, `ReferenceTimeZone`, `Periods`, the format tokens and the
first number of a new fiscal year (`StartValue`, or 1) in engine-mongodb (`AutoIncrementModifier`, rules in
`AutoIncrementRangeSettings`): the fiscal year is read from the time the document is numbered - its creation time on a
range without `Release`, its release time on a range with `Release` (AB#9758, D2). AB#9755 adds the `StartValue` floor of D5: every allocation (with and without periods)
takes its block from `max(counter, StartValue - 1) + 1` on, in the same single atomic statement (a pipeline update), so
a start value continues an external range and one set below a counter in use is ignored. The range rules also refuse a
`StartValue` below 1 or above `End`, and they are checked for partial updates of a range too (engine hook
`IPreDocumentUpdateValidation`, called by `BulkRtMutation` before anything of the batch is written): an update that makes
the stored range invalid is refused up front, not only when the next number is taken. Every update through
`BulkRtMutation` is checked, the repository's `UpdateOneRtEntity*` and `UpdateManyRtEntity*` methods included; a direct
collection write or a mutation with `DisablePreDocumentModifications` is not, and the allocation checks the rules again.
Counters are entities of the tenant database, so company tenants under one group tenant number independently;
`CurrentValue`, `Periods` and the configuration attributes are `TenantOwned`, so a seed re-apply keeps them. A service
built before AB#9754 ignores the new attributes and numbers a fiscal-year range like a legacy counter, so configure them
only on an installation whose services contain the implementation.

AB#9758 implements the numbering on release. The engine reads the release configuration with `NumberRangeRelease`
(`Runtime.Contracts`, the one rule of D8 for the numbering and the lock: a state value is released when it is in
`ReleasedValues`, an enum value by the name of its CK enum value). In engine-mongodb `AutoIncrementModifier` numbers a
document of a range with `Release` only in the write that puts it into a released state: an insert directly in that
state, a replace, or a partial update that sets the state attribute (engine hook `IPreDocumentUpdateModification`,
called by `BulkRtMutation` after the update validations; the number is written in the same update statement and, in a
transaction, together with the counter). A draft is written without a number and leaves the counter alone, so a deleted
draft leaves no gap; a document that already has a number keeps it. A range without `Release` keeps its single
allocation statement (it now also matches only ranges without `Release`), so its documents are numbered on insert as
before. The range rules refuse a `Release` without `StateAttribute` or `ReleasedValues` when the range is configured, and
a `StateAttribute` the document type does not have when a document is numbered.

The lock of released documents (D3, D8, D10) is enforced by the engine itself, not by a hook, and has no switch.
`ReleasedDocumentLock` (`Runtime.Engine/Repositories`) judges a write against the stored document: a document is
locked when its stored state value is in the `ReleasedValues` of a number range one of its attributes is numbered from.
It is called where every write path passes:

| Write path | Check |
|------------|-------|
| `BulkRtMutation.ApplyChangesAsync`: every insert, update, replace, delete and archive of the repository by id, by filter (engine-mongodb `Mutation`) and in `ApplyChanges`, and the association changes of the batch (the cascade of a deleted entity included) | `CheckMutationAsync`, after the rule validation and the not-found answer of the write funnel, before anything of the batch is written; whatever `BulkRtMutationOptions` say (also `DisablePreDocumentModifications`) and whatever the identity (also a System session) |
| `RuntimeRepositoryBase.BulkInsertRtEntitiesAsync`: RT import (`importrt`), blueprint seed install, update and nightly re-apply | `FilterImportAsync`, for the whole import batch before its first write |
| `RuntimeRepositoryBase.BulkRtAssociationsAsync`: the associations of an import | `FilterAssociationImportAsync` |
| `RuntimeRepositoryBase.DeleteRtAssociationsByIdAsync`: the to-one replacement of an import | `CheckAssociationDeleteAsync` |

Rules: a released document changes no attribute, not its number (never, also when `ChangeableAttributes` lists it),
not its state out of `ReleasedValues` (from one released value to another only when the state attribute is listed
there), and no outbound association (the document is the origin; associations to it, such as a storno's `Cancels`,
stay possible); it is never deleted or archived. Only `ChangeableAttributes` may change, and a write that changes a
changeable and another attribute is refused as a whole. A write is judged by its values: an attribute set to its stored
value is no change (strict comparison: strings ordinal, numbers by value, times by UTC ticks, records by type and
members, lists in order; a written linked binary is always a change), so a form that saves the unchanged values with a
changeable one passes. A replace without the number keeps the stored one (the number modifier carries it over); with
`DisablePreDocumentModifications` it would clear it and is refused. An import that carries the stored content is a
no-op: the entity is not written and no audit entry is made. An import of a document the tenant does not hold yet is
written with the number of the file; the counter is not touched. The blueprint bookkeeping attributes of
`System/Entity` (`RtBlueprintSource`, `RtBlueprintLocked`, `RtBlueprintAppliedAt`) are not business data: a re-apply
that only stamps a released document anew is that no-op. A range whose `Release` cannot be read locks every stored
document of its types (fail closed).

A refused batch is refused as a whole (D3) with one `ReleasedDocumentLockedException` (`Runtime.Contracts.Repositories`):
engine message 168 `RtReleasedDocumentLocked` per refused document (document, type, state, range, operation, what it
would change, the changeable attributes) and the structured refusals. The RT import passes it on unwrapped. For each
refused document the repository writes one `DocumentAuditEntry` with `Action = Denied` (document, UTC time, the caller's
subject, the write channel as `Source`, `AttributeNames`, the message number) in a session of its own, outside the
refused write's transaction, so the entry stays when the caller aborts it. The lock reads only for writes that target an
entity of a type with an auto-increment attribute: one read of the stored documents per type and batch and, only when
one of them is stored, one read of the ranges, in the session of the write; a batch of new documents reads no range.

Outside the lock, by design: the tenant dump restore (mongorestore into an unregistered database and attach, D10)
restores documents, counters, periods and audit entries 1:1, and numbering goes on from the restored counter; the CK
model migration writes of engine-mongodb (`*ForMigrationAsync`), which rewrite stored documents for a schema change; and
the display rule sweeper, which writes only `rtDisplayName` and `rtDisplayDescription`.

## 5. Outside System

- The storno type of a document model declares the `Cancels` association (Basic `Document` minor version).
- The gap report (AB#9763) needs an index on the number attribute of the document type, in the document model.
