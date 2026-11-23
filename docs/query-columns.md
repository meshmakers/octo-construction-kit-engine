---
description: CkTypeQueryColumnCollector: association count and exists columns, the MaxColumns and record-cycle guardrails, Secret and Hidden columns.
applies_to: src/ConstructionKit.Engine/DependencyGraph/**, src/Runtime.Contracts/Repositories/Query/**
---

# Query column collector: N:M association columns and guardrails

## N:M Association Query Columns

N:M (many-to-many) associations are exposed as query columns with `totalCount` and `exists` meta-properties. These allow listing and filtering entities based on whether associations exist and how many there are.

### Path Syntax
```
navigationPropertyName.targetTypeName::totalCount    → INTEGER_64 (count of associations)
navigationPropertyName.targetTypeName::exists        → BOOLEAN (true if count > 0)
```

The `::` separator distinguishes association meta-properties from regular attribute navigation (`->`). This avoids collisions with actual attributes named `totalCount` or `exists` on the target type.

### Key Components
- **CkTypeQueryColumnCollector** (`ConstructionKit.Engine/DependencyGraph/`): Generates `totalCount` and `exists` columns for both outbound and inbound N:M associations. One column per navigation property grouping (not per derived type).
- **AssociationCountFilter** (`Runtime.Contracts/Repositories/Query/`): Record type that carries count filter operator and comparison value on a `NavigationPair`.
- **NavigationPair.AssociationCountFilter**: When set, the MongoDB layer generates a count-based aggregation pipeline instead of the standard existence check.

### MongoDB Pipeline
When `AssociationCountFilter` is set on a `NavigationPair`, `SingleOriginRtQuery.CreateAssociationCountNavigation` generates:
1. `$lookup` on the associations collection (matching by role ID and target type)
2. `$addFields` to compute `$size` of the lookup result
3. `$match` to filter by the count comparison (e.g., `>= 1` for exists)
4. `$project` to clean up temporary fields

### Usage in GraphQL
```graphql
# Filter by existence
fieldFilter: [{
  attributePath: "documents.bankTransaction::exists"
  operator: EQUALS
  comparisonValue: true
}]

# Filter by count
fieldFilter: [{
  attributePath: "documents.bankTransaction::totalCount"
  operator: GREATER_THAN
  comparisonValue: 3
}]
```

## Query Column Collector Guardrails

`CkTypeQueryColumnCollector` walks the association graph to build navigation columns
(`a.Type->b`). Two guardrails protect against pathological CK models (root cause of a production-class
incident: a single transient stream-data query on a tenant with a 0..1 self-association on
the root `System/Entity` type allocated >60 GB and killed the asset-repo service):

- **`CkTypeQueryColumnOptions.MaxColumns`** (default `50_000`, `null` = unlimited): hard cap
  on the total number of produced columns. The per-path cycle guard (`ignoredNavigations`)
  only prevents revisiting the same `(targetType, role)` tuple *within one path* — sibling
  branches re-explore the same subgraph, and `GetAllDerivedTypes` fans each 0..1/1 navigation
  out over every derived type of the target, so densely connected models expand
  combinatorially. When the cap is exceeded the collector throws a `DependencyGraphException`
  (fail fast) instead of allocating unbounded memory. Callers that only need physical
  attribute columns (e.g. stream-data archives) should pass
  `IgnoreNavigationProperties = true` instead of relying on the cap.
- **Record cycle detection**: record descent (`Record` / `RecordArray` attributes) tracks the
  records on the current descent path and throws a `DependencyGraphException` on a cycle
  (a record containing itself directly or transitively) instead of recursing to stack
  overflow.
- **No Secret or Hidden columns**: the collector never emits a column for a `Secret` attribute
  (not filterable, sortable or projectable) or for an attribute whose effective `access` is `Hidden`
  (`CkTypeAttributeGraph.IsReadableOutsideMethod == false`). The skip applies on every level - type
  attributes, record and record-array members (a Hidden member drops out of the record's columns, a Hidden
  record attribute drops the whole record) and attributes reached through navigation. `ReadOnly` and
  `MethodOnly` attributes stay default columns (both are readable). This only shapes the **default** column
  set; an explicit request for a Hidden path (filter, sort, column, stored query) is still refused by the
  consumers' access guards (`AccessQueryGuard` in asset-repo, `ReportColumnAccessGuard` in report-services,
  the stored-query guards of bot-services and mesh-adapter). Tests: `CkTypeQueryColumnCollectorTests`
  (`HiddenAndSecret_*`).
