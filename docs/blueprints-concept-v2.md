# Blueprints — Konzept v2

Dieses Dokument konsolidiert und aktualisiert den ursprünglichen Plan
(`blueprint-updates-plan.md`) sowie die User-Doku (`blueprints.md`) auf den
Stand der Architektur-Entscheidungen vom 2026-05-14. Es ist die
verbindliche Grundlage für die Fertigstellung der Blueprint-Funktion.

## 1. Status

### Bereits umgesetzt
- Vollständige Contracts in `Runtime.Contracts/Blueprints/` (Service, History,
  Migration, Backup, DTOs).
- `BlueprintService` und `BlueprintMigrationExecutor` in `Runtime.Engine` —
  CK-Model-Loading, Validation, History, Migration-YAML-Parsing.
- MongoDB-Persistenz: `MongoTenantBlueprintHistory`, `MongoBlueprintBackupService`,
  `MongoRuntimeRepositoryProvider`.
- CLI-Tool `octo-bpm` mit allen lokalen Commands (new, validate, pack,
  list, version, catalogs, get, publish, unpublish, config).
- System-Entity-Attribute: `RtBlueprintSource`, `RtBlueprintLocked`,
  `RtBlueprintAppliedAt` sind im SystemCkModel definiert.

### Zu entfernen (Aufräumarbeit Phase 1)
- `BlueprintComposer` und `ComposedBlueprintDto` (Composition-Mechanismus).
- Feld `composedBlueprints` im Blueprint-YAML-Schema.
- Zugehörige Tests (`BlueprintComposerTests`).
- Composition-Kapitel in `blueprints.md`.

### Konkrete Lücken bis Produktivreife
1. `BlueprintService.ApplySeedDataAsync` ruft `IImportRtModelCommand` nicht
   auf — Seed-Data wird gefunden und gezählt, aber nicht geschrieben
   (`BlueprintService.cs:777`).
2. `BlueprintMigrationExecutor` ist in allen Operations-Methoden Stub
   (`ExecuteAdd/Update/Delete/Rename/Transform`, `EvaluateCondition`,
   `RunValidation` — 8 TODOs).
3. Konfliktdetektion zwischen Blueprints (CK-Model-Versionen,
   Entity-Owner) fehlt komplett.
4. Multi-Blueprint-Verwaltung pro Tenant (Liste der aktiven Blueprints,
   Dependency-Auflösung beim Install) existiert nicht.
5. Service-seitige Hosting-Schicht fehlt — es gibt keinen Service, der
   die Engine über HTTP/GraphQL nach außen exponiert.
6. `octo-cli` hat keine Blueprint-Commands.
7. Audit-Events werden nicht publiziert.

## 2. Architektur-Entscheidungen

### 2.1 Versionierung — SemVer
Blueprints folgen SemVer analog zu CK-Modellen: `MyBlueprint-1.2.3`.
Update-Pfade werden über Versionsbereiche (`[1.0,2.0)`) ausgedrückt.
Bei Major-Wechseln ist ein Migration-Script empfohlen, bei Minor/Patch
genügt `Merge`-Modus mit den vorhandenen `rtBlueprintLocked`-Attributen.

### 2.2 Multi-Blueprint pro Tenant — keine Composition
Ein Tenant kann **mehrere Blueprints gleichzeitig** installiert haben.
Blueprints können andere Blueprints als **Dependency** deklarieren.

**Composition wird entfernt** (`composedBlueprints`, `BlueprintComposer`,
`ComposedBlueprintDto`). Begründung: Mit Single-Owner-Tracking (Sec. 4.2)
würden zwei composed Roots auf dieselben Sub-Entities kollidieren.
Dependencies erfüllen alle Wiederverwendungs-Anforderungen sauberer
und alignieren mit etablierten Package-Ecosystem-Patterns. Wenn echte
"interne Bausteine" jemals gebraucht werden, kann `octo-bpm pack`
sie zur Build-Zeit inlinen — Runtime sieht nichts davon.

**Neues YAML-Feld auf Blueprint-Ebene:**
```yaml
blueprintDependencies:
  - BaseEntities-[1.0,)
  - SecurityModel-[2.0,)
```

**Installations-Semantik:**
- Beim Install von Blueprint B wird die transitive Dependency-Closure
  berechnet. Fehlende Blueprints werden in topologischer Reihenfolge
  installiert.
- Bereits installierte Dependencies mit kompatibler Version werden
  wiederverwendet.
- Inkompatible Versionen (Dependency verlangt `[2.0,)`, installiert ist
  `1.5`) verhindern die Installation und werden im Preview gemeldet.

**Uninstall-Semantik:**
- Refcount: Blueprint A wird nur dann (auto-)deinstalliert, wenn kein
  anderer installierter Blueprint mehr von A abhängt.
- Default: Dependencies bleiben stehen; explizites `--cascade` löscht
  ungenutzte Dependencies mit.

### 2.3 CK-Model-Auflösung und Konfliktdetektion
**Auto-Install:** CK-Models, die in `ckModelDependencies` deklariert
werden, werden automatisch in den Tenant geladen — analog zum
bestehenden `ICkModelUpgradeService`-Flow.

**Konflikt-Klassen, die im Preview/Validate erkannt werden müssen:**
1. **CK-Model-Versionen:** Blueprint A verlangt `System-[2.0,3.0)`,
   Blueprint B verlangt `System-[3.0,)` → unauflösbarer Schnitt.
2. **Blueprint-Dependency-Versionen:** B verlangt `A-[1.0,2.0)`, ein
   anderer installierter Blueprint hat bereits `A-2.5.0` → Konflikt.
3. **Entity-Ownership:** Zwei Blueprints versuchen, einen Entity mit
   demselben `rtWellKnownName` als `rtBlueprintLocked=true` zu deklarieren.
4. **rtId-Kollision:** Zwei Blueprints liefern verschiedene Entities mit
   identischer `rtId`.

**Verhalten:** Konflikte werden im Preview-Schritt aufgelistet (nie
geräuschlos überschrieben). Der Install/Update bricht ab, bis die
Konflikte über `BlueprintUpdateOptions.ConflictResolutions` oder eine
manuelle Vorbereinigung gelöst sind.

### 2.4 Audit über das Distribution-Event-Hub
Es werden Domain-Events analog zum existierenden Pattern
(`PreCreateTenant`, `EventBase`) publiziert.

**Neue Events** (in `octo-common-services/src/Contracts/DistributionEventHub/Messages/`):
- `BlueprintApplied(TenantId, BlueprintId, ApplicationMode, EntitiesAdded, EntitiesUpdated, EntitiesDeleted, CorrelationId, Timestamp)`
- `BlueprintUpdated(TenantId, BlueprintId, FromVersion, ToVersion, UpdateMode, EntitiesAdded, EntitiesUpdated, EntitiesDeleted, BackupId, CorrelationId, Timestamp)`
- `BlueprintRolledBack(TenantId, BlueprintId, ToVersion, BackupId, CorrelationId, Timestamp)`
- `BlueprintUninstalled(TenantId, BlueprintId, CascadedDependencies, CorrelationId, Timestamp)`
- `BlueprintOperationFailed(TenantId, BlueprintId, Operation, ErrorMessage, CorrelationId, Timestamp)`

**Pattern:** `record BlueprintApplied(...) : EventBase(CorrelationId, Timestamp);`
Publishen via `IDistributionEventHubService.PublishAsync(...)`.

### 2.5 Rollback — voller Backup-basierter Rollback
Vor jedem destruktiven Vorgang (Update, Uninstall) wird über
`ITenantBackupService` ein voller Tenant-Snapshot erstellt
(`BackupType.BlueprintUpdate`). Rollback restauriert den kompletten
Tenant-Stand des Snapshots.

**Bewusst verworfen:** semantisches "Undo der Migration-Steps" — zu
fehleranfällig bei verketteten Operations und User-Modifikationen
zwischen Apply und Rollback.

## 3. Tool-Aufteilung

### 3.1 octo-bpm — Compiler/Authoring-Tool
**Zweck:** Lokale Blueprint-Entwicklung, Paketierung, Veröffentlichung
in Kataloge. Operiert **niemals** gegen einen Tenant-Service.

**Commands:** `new`, `validate`, `pack`, `list`, `version`, `catalogs`,
`get`, `publish`, `unpublish`, `config`.

`unpublish` ist die destruktive Umkehrung von `publish`: `-r <Version>`
entfernt genau eine Version, ohne `-r` werden **alle** Versionen entfernt;
ohne `-f` nur Vorschau (Dry-Run), mit `-f` wird angewendet. Nur für
beschreibbare Kataloge (LocalFileSystem/PrivateGitHub; Embedded ist read-only).
Auf GitHub-Katalogen werden zusätzlich die drei `catalog.json`-Indexebenen
kaskadierend bereinigt.

**Entfernt; Äquivalent in `octo-cli`:** Die Runtime-Commands `status`,
`preview`, `update`, `history` wurden aus `octo-bpm` entfernt. Sie operieren
gegen einen Tenant und brauchen ein Runtime-Repository samt
`ITenantBackupService` — beides hostet `octo-asset-repo-services`, nicht das
Authoring-Tool. Würden sie hier registriert, zöge `IBlueprintService →
ITenantBackupService` in den DI-Graphen des CLI und ließe den Tool-Start
abstürzen. Die entsprechende Funktionalität bieten die `octo-cli`-Commands
(`ServiceClientOctoCommand`, siehe §3.2): `history` → `GetBlueprintHistory`
(deckt auch die frühere `status`-Ansicht ab), `preview` →
`PreviewBlueprintUpdate`, `update` → `UpdateBlueprint`.

### 3.2 octo-cli — Runtime-Operations gegen Tenant-Services
**Zweck:** Installation, Update, Rollback, Uninstall, Statusabfrage
gegen einen laufenden Tenant.

**Neue Commands** (analog zum bestehenden Library-Command-Set):
- `blueprintInstall -t <tenant> -b <blueprintId>`
- `blueprintList -t <tenant>` — installierte Blueprints
- `blueprintStatus -t <tenant> -b <blueprintId>` — Version + Update-Verfügbarkeit
- `blueprintPreview -t <tenant> -b <blueprintId>` — Diff vor Apply
- `blueprintUpdate -t <tenant> -b <blueprintId> [-m Safe|Merge|Full|Migration]`
- `blueprintUninstall -t <tenant> -b <blueprintId> [--cascade]`
- `blueprintRollback -t <tenant> --backupId <id>`
- `blueprintHistory -t <tenant>`
- `blueprintCatalogs` — verfügbare Kataloge auflisten (Read-only, ruft Service)

Alle als `ServiceClientOctoCommand<IBlueprintServicesClient>`.

### 3.3 Engine als Library, gehostet von octo-asset-repo-services
Die Implementierung in `octo-construction-kit-engine/src/Runtime.Engine/Blueprints/`
ist eine Bibliothek. Sie wird von **`octo-asset-repo-services`** gehostet —
der hostet bereits die Runtime-Repositories und ist der natürliche
Aufrufer von `IImportRtModelCommand`. Außen-API: bestehende
GraphQL-Schicht des Asset-Repo-Service erweitern (analog zu den
bestehenden CK-Model-Library-Operationen).

## 4. Datenmodell-Erweiterungen

### 4.1 Multi-Blueprint-Verwaltung (NEU)
Bisher: `ITenantBlueprintHistory` führt eine flache, append-only Liste.
Das genügt für ein einzelnes Blueprint pro Tenant, nicht für mehrere.

**Erweiterung:** Konzept "Installation" einführen — eine Installation
ist ein aktuell aktiver Blueprint im Tenant. Eine History-Entry ist ein
Snapshot eines Operation-Vorgangs.

```csharp
public interface ITenantBlueprintInstallations  // NEU
{
    Task<IReadOnlyList<BlueprintInstallation>> GetInstalledAsync(string tenantId, CancellationToken ct);
    Task<BlueprintInstallation?> GetByBlueprintNameAsync(string tenantId, string blueprintName, CancellationToken ct);
    Task UpsertAsync(string tenantId, BlueprintInstallation installation, CancellationToken ct);
    Task RemoveAsync(string tenantId, string blueprintName, CancellationToken ct);
}

public class BlueprintInstallation
{
    public required BlueprintId BlueprintId { get; set; }
    public required DateTime InstalledAt { get; set; }
    public required DateTime LastUpdatedAt { get; set; }
    public string? SeedDataChecksum { get; set; }
    public List<BlueprintId> ResolvedDependencies { get; set; } = [];
    public bool IsDependency { get; set; }  // true wenn auto-installiert als Dep
}
```

`ITenantBlueprintHistory` bleibt bestehen für Audit/Forensik, aber als
reines Operations-Log (Install, Update, Rollback, Uninstall jeweils mit
Zeitstempel, Modus, Counts).

### 4.2 rtBlueprintSource — genau ein Owner
`rtBlueprintSource` bleibt ein String und identifiziert **genau einen
Owner-Blueprint pro Entity**. Andere Blueprints dürfen den Entity
referenzieren (z. B. in Associations), aber nicht claimen.

Konsequenz für Apply/Update:
- Wenn ein zweiter Blueprint versucht, einen Entity mit `rtWellKnownName`
  oder `rtId` zu schreiben, der bereits einen `rtBlueprintSource != null`
  und `rtBlueprintLocked = true` hat → **Entity-Ownership-Konflikt**,
  Install bricht im Preview ab.
- Wird ein Blueprint deinstalliert, werden alle Entities mit passendem
  `rtBlueprintSource` mitgelöscht (außer sie sind `rtBlueprintLocked = false`
  und wurden vom User danach umfunktioniert).

### 4.3 Conflict-Detection — Original-Werte aus dem Catalog
Für die Erkennung von User-Modifikationen an Locked-Entities lädt der
Diff-Algorithmus die Original-Werte **zur Diff-Zeit aus dem versionierten
Catalog neu** (Blueprint-ID + Version sind im `BlueprintInstallation`
und auf der Entity selbst hinterlegt). Es wird kein separater Snapshot
gespeichert. Vorteil: kein zusätzliches Storage, keine Drift zwischen
Snapshot und Catalog-Wahrheit. Voraussetzung: Catalogs sind immutable
pro Version (existierende Annahme).

## 5. Workflow-Skizzen

### 5.1 Install
```
octo-cli blueprintInstall -t T1 -b ECommerce-2.0.0 [--force]
│
├─ 1. Service: Resolve transitive Blueprint-Dependencies
│       → [BaseEntities-1.0.0, SecurityModel-2.1.0, ECommerce-2.0.0]
├─ 2. Service: Konflikt-Check (CK-Versionen, Entity-Owner, rtIds)
│       → BlueprintApplicationResult.Conflicts ggf. → Abbruch
├─ 3. Idempotenz-Check pro Blueprint:
│       ├─ schon installiert in gleicher Version → No-Op
│       ├─ --force gesetzt → Re-Apply (Upsert über bestehende Entities)
│       └─ andere Version → Update-Pfad (siehe 5.2)
├─ 4. Service: pro Blueprint in topologischer Reihenfolge
│       ├─ CK-Models installieren/upgraden
│       ├─ Seed-Data via IImportRtModelCommand (Upsert)
│       ├─ Entities mit rtBlueprintSource/Locked/AppliedAt taggen
│       ├─ BlueprintInstallation persistieren
│       └─ Event BlueprintApplied publishen
└─ 5. Service: History-Entry schreiben, Rückgabe an CLI
```

**Idempotenz-Regel:** Identischer Re-Install ist No-Op (kein Event,
kein History-Entry). Re-Apply nach Storage-Korruption: explizit
`--force`, schreibt Seed-Data per Upsert über bestehende Entities,
publisht `BlueprintApplied` mit `ApplicationMode = ReApply`.

### 5.2 Update
```
octo-cli blueprintUpdate -t T1 -b ECommerce-2.1.0 -m Merge
│
├─ 1. Tenant-Backup anlegen (ITenantBackupService)
├─ 2. Diff zwischen installierter und Ziel-Version berechnen
├─ 3. Konflikt-Detektion gegen rtBlueprintLocked
├─ 4. Migration-Pfad finden (analog ck-model-migrations Auto-Bridge)
├─ 5. Operations ausführen (Merge: nur Locked-Entities updaten)
├─ 6. BlueprintInstallation.LastUpdatedAt aktualisieren
└─ 7. BlueprintUpdated-Event publishen
```

### 5.3 Rollback
```
octo-cli blueprintRollback -t T1 --backupId <id>
│
├─ 1. ITenantBackupService.RestoreBackupAsync
├─ 2. BlueprintInstallations aus Backup wiederherstellen
└─ 3. BlueprintRolledBack-Event publishen
```

### 5.4 Uninstall
```
octo-cli blueprintUninstall -t T1 -b ECommerce-2.0.0 [--cascade]
│
├─ 1. Backup anlegen
├─ 2. Reverse-Refcount: andere Blueprints prüfen
├─ 3. Entities mit rtBlueprintSource == ECommerce-2.0.0 löschen (Locked)
├─ 4. ggf. ungenutzte Dependencies löschen (mit --cascade)
└─ 5. BlueprintUninstalled-Event publishen
```

## 6. Integrationspunkte (konkret)

| Was | Wo | Zweck |
|---|---|---|
| `IImportRtModelCommand` | `Runtime.Contracts.Exchange` | Seed-Data + Migration `Add`-Operationen |
| `IDistributionEventHubService.PublishAsync` | `Services.Contracts.DistributionEventHub` | Audit-Events |
| `ICkModelUpgradeService` | `Runtime.Contracts.CkModelMigrations` | CK-Model auto-install/upgrade beim Apply |
| `ServiceClientOctoCommand<T>` | `octo-cli/Commands` | Basis für neue Blueprint-CLI-Commands |
| `EventBase` Pattern | `octo-common-services` | Record-Definition für Blueprint-Events |
| `RtBlueprintSource/Locked/AppliedAt` | `SystemCkModel/entity.yaml` | bereits da, nutzen |
| `ITenantBackupService` | `Runtime.Contracts.Blueprints` | Pre-Operation-Snapshots |

## 7. Implementierungsreihenfolge

> Status-Legende: ✅ = umgesetzt und gemerged, ⏳ = offen, 🔄 = teilweise

### Phase 1: Seed-Import + Single-Blueprint produktiv ✅
- ✅ Composition entfernt (`BlueprintComposer`, `ComposedBlueprintDto`,
  `composedBlueprints`-Feld, Tests, Doku).
- ✅ `BlueprintApplicationMode.ReApply` für `--force`.
- ✅ Seed-Import an `IImportRtModelCommand` gekoppelt.
- ✅ Audit-Events `BlueprintApplied` / `BlueprintOperationFailed`.
- ✅ `octo-asset-repo-services` Blueprint-API (`BlueprintsController` mit
  Tenant-API-Policies) + DI-Wiring `.AddMongoBlueprintSupport()`.
- ✅ `octo-cli`: `InstallBlueprint` (`-f`), `ListBlueprints`, `GetBlueprintHistory`.

### Phase 2: Update + Rollback ohne Migration-Scripts ✅
- ✅ Phase 2a/b/c: Diff/Preview-Logik (Safe / Merge / Full gegen
  `rtBlueprintLocked`), Mode-spezifischer Apply, KeepBlueprint-Promotion in-call
  (Phase 2d, PRs #172 / #173).
- ✅ `octo-cli`: `PreviewBlueprintUpdate`, `UpdateBlueprint`,
  `ListBlueprintBackups`, `RollbackBlueprint`.
- ✅ Events `BlueprintUpdated`, `BlueprintRolledBack`.

### Phase 3: Multi-Blueprint + Dependencies + Konfliktdetektion ✅
- ✅ Phase 3a: `ITenantBlueprintInstallations` + Mongo-Persistenz.
- ✅ Phase 3b: Dependency-Resolver mit Topo-Sort.
- ✅ Phase 3c: Multi-Install Apply-Pfad.
- ✅ Phase 3d: Refcounted Uninstall + `--cascade`.
- ✅ Phase 3e: REST/SDK/CLI für Uninstall (`UninstallBlueprint`,
  `ListBlueprintInstallations`).

### Phase 4: Migration-Executor produktiv ✅
- ✅ Alle 5 Operations (Add / Update / Delete / Rename / Transform),
  Conditions, Validations.
- ✅ Type-aware Attribute-Writes über `ICkCacheService` +
  `AttributeValueConverter` (PR #171); record-typed Skalar-Updates werden mit
  klarer Fehlermeldung abgewiesen.

### Phase 5: Kundenfähig 🔄
- ✅ Permissions: Status quo via generischer
  `TenantAssetApiReadOnlyPolicy` / `TenantAssetApiReadWritePolicy` (Open
  Question G geschlossen — keine eigenen Blueprint-Permissions ohne
  Kundenanforderung).
- 🔄 Public Catalogs:
  - ✅ `composedBlueprints`-Schema-Drift im
    `blueprint-libraries-build`-Repo behoben.
  - ⏳ GitHub Pages ist auf `blueprint-libraries-build` deaktiviert; Catalog ist
    aktuell write-only. Entscheidung: Pages aktivieren *oder* Inhalt nach
    `meshmakers.github.io/blueprints/` migrieren.
- ✅ Doku-Endspurt: `blueprints.md` neu geschrieben; `blueprints-concept-v2.md`
  mit Status-Markern.

## 8. Entschieden (2026-05-14)

| # | Frage | Entscheidung |
|---|---|---|
| 1 | Versionierung | SemVer analog CK-Modelle |
| 2 | Multi-Blueprint | Ja, mit `blueprintDependencies`; Composition entfernt |
| 3 | CK-Model-Auflösung | Auto-Install + Konfliktdetektion vor Install |
| 4 | Audit-Trail | `IDistributionEventHubService` mit Record-Events |
| 5 | Rollback | Voll, Backup-basiert |
| 6 | Tool-Schnitt | `octo-cli` führt Runtime-Ops (Install/Update/Rollback/Uninstall). Authoring-CLI (`octo-bpm`) ist implementiert (new, validate, pack, list, version, catalogs, get, publish, unpublish, config) und operiert nie gegen einen Tenant-Service. |
| 7 | Hosting | `octo-asset-repo-services` (GraphQL-API) |
| 8 | Entity-Ownership | Ein Owner pro Entity |
| 9 | Re-Apply | `--force` flag, neuer Mode `ReApply` |
| 10 | Conflict-Detection-Quelle | Original-Werte beim Diff aus Catalog laden |

## 9. Verbleibende Open Questions (nicht Phase-1-blockierend)

**B. CK-Model-Konflikt-Auflösung — melden oder selbst lösen?**
Wenn Blueprint A `System-[2.0,3.0)` und Blueprint B `System-[3.0,)`
verlangt: nur melden (User muss eingreifen) oder Engine fällt
automatisch auf den höchsten kompatiblen Schnitt? Mein Vorschlag:
nur melden — Engine darf CK-Model-Versionen nie eigenmächtig wählen.
Phase 3.

**G. Berechtigungen (Phase 5) — geschlossen**
Entscheidung: Blueprint-Operationen folgen dem bestehenden Auth-Pattern
des Asset-Repo-Service (`TenantAssetApiReadOnlyPolicy` für Reads,
`TenantAssetApiReadWritePolicy` für Mutationen). Eigene Permissions wie
`blueprint:install` / `blueprint:uninstall` werden erst eingeführt,
wenn eine konkrete Kundenanforderung sie rechtfertigt — bis dahin
YAGNI.

**H. Migration-Scripts und Multi-Blueprint-Ownership**
Migration-Script eines Blueprints A kann Entities tangieren, die
einem anderen Blueprint B "gehören". Validation: verbieten (sauberer)
oder warnen (flexibler)? Phase 4. Mein Vorschlag: verbieten in der
Validation, mit Override-Flag `--ignore-foreign-ownership` für
Edge-Cases.
