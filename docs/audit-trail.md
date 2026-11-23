---
description: Audit-trail events, sinks and aggregation of repeated audit events in the runtime engine.
applies_to: src/Runtime.Engine/AuditTrails/**, src/Runtime.Contracts/AuditTrails/**
---

# Audit-trail architecture

## Audit-Trail Architecture

Engine code surfaces noteworthy events through typed audit-trail interfaces
(`ICkModelImportAuditTrail`, `IArchiveAuditTrail`, …). All of them route through a single
host-side extensibility point: `IAuditEventSink`.

```
Runtime.Contracts/AuditTrails/
  AuditEvent       — discriminated event record (TenantId, Level, Category, Message, Metadata)
  IAuditEventSink  — single host-replaceable surface

Runtime.Engine/AuditTrails/
  LoggingAuditEventSink   — default; writes structured logs via ILogger
  AggregatingAuditEventSink — decorator for repeated facts (AuditEvent.AggregationKey), AB#7279
  DataPermissionAudit     — builds + counts the data-permission violation events, AB#7279
  NoOpAuditEventSink      — opt-in silence (tests, hosts that explicitly want events dropped)

Runtime.Engine/{CkModelMigrations,StreamData}/
  ForwardingCkModelImportAuditTrail  — implements ICkModelImportAuditTrail
  ForwardingArchiveAuditTrail        — implements IArchiveAuditTrail
  (both translate typed calls into AuditEvent and call IAuditEventSink.PublishAsync)
```

`AddRuntimeEngine` registers:

1. `IAuditEventSink → LoggingAuditEventSink` (TryAddSingleton — hosts can override)
2. `ICkModelImportAuditTrail → ForwardingCkModelImportAuditTrail` (TryAddTransient)
3. `IArchiveAuditTrail → ForwardingArchiveAuditTrail` (AddTransient)

A host that wants events in the platform event log replaces step 1 only — see
`EventRepositoryAuditEventSink` in `octo-common-services`.

**Why this shape (history matters):** WI #3324 originally landed a per-interface bridge
(`EventRepositoryCkModelImportAuditTrail`) in `octo-common-services` that ctor-captured
`IEventRepository`. That closed a DI bootstrap cycle:
`SystemContext.ctor → IDatabaseCkModelRepository → ICkModelImportAuditTrail (bridge)
→ IEventRepository → ISystemContext → …`. Host startup deadlocked in DI's `StackGuard`
(detected via `dotnet-stack` dump on a stuck integration-test agent). Routing every
audit-trail interface through one sink keeps the bridge surface a single point — the sink
implementation in common-services lazy-resolves `IEventRepository` from
`IServiceProvider`, so the bootstrap cycle cannot re-form, even if more audit-trail
interfaces are added later.

**Adding a new audit-trail interface:**

1. Define a focused typed interface in `Runtime.Contracts` (no dependency on
   `Microsoft.Extensions.Logging` or the notifications stack).
2. Add a `Forwarding{Name}AuditTrail` in `Runtime.Engine` that takes `IAuditEventSink` in
   its ctor and translates typed calls into `AuditEvent`s. Pick a stable
   `Category = "{Domain}.{Event}"` and fill the `Metadata` dictionary so structured-log
   consumers can pivot. Render `Message` in the same way as the existing forwarders.
3. Register the forwarder in `AddRuntimeEngine` next to the existing ones.

**Never** add a host-side bridge class that ctor-captures `IEventRepository` or
`ISystemContext` — that re-introduces the WI #3324 cycle. Use the sink.

### Aggregation of repeated audit events (AB#7279)

AuditOnly data-permission violations are published per repository call (prod-1: ~1 million
`[Audit:DataPermissions.ReadViolation]` lines a day from 54 (tenant, subject, type) keys). They are
aggregated instead of published one by one:

- `AuditEvent.AggregationKey` (optional, init-only) opts an event into aggregation. Events without
  a key are always published one by one.
- `AggregatingAuditEventSink` (`Runtime.Engine/AuditTrails`) decorates the host's `IAuditEventSink`
  (log or event store). Per (tenant, key): first occurrence at once, repetitions only counted, at
  the end of the window one summary (same category and level, message suffix
  `[aggregated: N more time(s) from <first> to <last>, window <w>]`, metadata `AggregatedCount`,
  `AggregatedFirstSeen`, `AggregatedLastSeen`, `AggregationWindow`) and a new window. A key without
  repetitions in its window is dropped, so its next occurrence is published at once again.
  Bounded store: at `MaxKeys` the least recently seen key is evicted and its summary published.
  `FlushPendingAsync` publishes every pending summary now (hosts call it when they stop, while the
  inner sink can still write — common-services does so with a hosted service next to the event-store
  sink); `DisposeAsync` (container shutdown) calls it as well.
- Options `AuditEventAggregation` (`AuditEventAggregationOptions`, bound from `IConfiguration` in
  `AddRuntimeEngine`): `Enabled` (default `true`), `Window` (default `00:15:00`; zero disables),
  `MaxKeys` (default 1000). Override per service with the platform prefix, e.g.
  `OCTO_AuditEventAggregation__Window=00:05:00`.
- It is registered as its own singleton, **not** as `IAuditEventSink`: hosts keep their sink type
  (common-services checks `IStrictAuditEventSink` for the CK method audit) and every other audit
  event is untouched. Publishers of repeated facts take it explicitly: `RtDataPermissionEvaluator`
  (DI factory) and the tenant repositories (engine-mongodb `TenantContext` passes it as the
  repository's `auditEventSink`, so the write funnel uses it too).
- `DataPermissionAudit` builds every violation event (category, message, metadata `SubjectId`,
  `CkTypeId`, `Action`, key `category|subject|type|action[|variant]`) and counts it in
  `octo.datapermissions.audit.violations` (meter `Meshmakers.Octo.DataPermissions`, tags `tenant`,
  `subject`, `type`, `action`) — every occurrence, aggregated or not. Actions: `read`, `update`,
  `replace`, `delete` for the read filter (engine-mongodb pre-filters of writes report their real
  action, not "read"), `write`/`delete` for the write funnel. Hosts register the meter
  (`ObservabilityBuilder` in common-services). Enforced policies and permission decisions are not
  affected.
- Tests: `tests/Runtime.Engine.Tests/AuditTrails/AggregatingAuditEventSinkTests.cs`.

`LoggingCkModelImportAuditTrail` and `LoggingArchiveAuditTrail` remain in `Runtime.Engine`
for backwards-compatible direct instantiation (e.g. the manual fallback in Mongo
`TenantContext` when no DI is wired) but are no longer the registered defaults.
