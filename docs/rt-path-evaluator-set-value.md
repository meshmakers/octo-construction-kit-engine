# `RtPathEvaluator.SetValue`: values, explicit null and missing

`RtPathEvaluator.SetValue` writes a value into an entity or record along an attribute path
(`Designation`, `Amount.Value`, `RecordArrayTests[0].Designation`, `nav->Attr`). This page describes what
a `null` value does (AB#8625).

## Three cases

| Input | Result |
| ----- | ------ |
| a value | the attribute is set; records missing along the path are created (since AB#1988) |
| `null` | an explicit null, see below |
| nothing (the caller does not call `SetValue`) | the attribute stays missing |

A missing attribute and an attribute that holds `null` are different: since AB#8622 an archive write keeps the
stored value of a missing column and clears a column that holds an explicit null.

## Explicit null on an enum attribute

- **Optional enum** (`isOptional: true` on the type or record assignment): `null` sets the attribute to null.
- **Required enum** (`isOptional: false`, the default): `null` is refused with an `InvalidPathException`
  `Path '<path>' cannot be set to null: the enum attribute '<name>' (CkEnumId '<id>') is not optional.`
  It is thrown before anything is changed, also before a missing record would be created.

Before AB#8625 every `null` on an enum failed with `Invalid enum value type ... 'null'`.
Other value types are unchanged: a `null` on them is written whatever `isOptional` says; the entity rule
engine checks required attributes when the entity is saved.

## Explicit null on a sub-path of a record that does not exist yet

`Amount.Value = null` while `Amount` is not set depends on the `RtPathNullHandling` argument:

| `RtPathNullHandling` | Result |
| -------------------- | ------ |
| `KeepMissingRecords` (default; the overloads without the argument) | nothing happens: `Amount` stays missing, so the null arrives as *missing* |
| `CreateMissingRecords` | `Amount` is created (as a value would create it) with `Value` explicitly null; its other members stay missing |

Why the default keeps the record missing: query-row creates and updates of the asset repository
(`QueryMapper`) send a cell for every column, and an empty cell must not create an empty record (AB#1988). A
record with only null members would also fail the required-member check of the rule engine on create.

Why `CreateMissingRecords` exists: a pipeline (`CreateUpdateInfo@1`) that sends an explicit null for
`Amount.Value` must reach the archive as null, not as missing. The caller opts in.

Not changed in either mode: a null on an existing record sets the member to null and keeps the other members;
a null whose missing parent is not a record that `SetValue` creates (a navigation without a target, a missing
record array, `RecordArrayTests[0].Member` or `RecordArrayTests.Member`) is dropped, and so is not refused on a
required enum either; a null on the record
attribute itself (`Amount = null`) sets the record to null.

Tests: `tests/Runtime.Engine.Tests/RtPathEvaluatorTests.cs`, region `SetValue explicit null (AB#8625)`.
