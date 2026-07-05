# Backup and Restore Guide

Covers the **shared** platform database and each **dedicated** tenant database, plus the
filesystem assets (tenant logos). Complements `docs/ProductionBackupPlan.md`.

> **Golden rule:** A tenant's data may live in TWO places over its lifetime — the shared DB
> (before cutover / after rollback) and its dedicated DB (while routing is active). Back up **both**
> the shared DB and every provisioned dedicated DB.

---

## What to back up

| Asset | Location | Notes |
|-------|----------|-------|
| Shared database | SQL Server, `DefaultConnection` catalog | Identity, Tenants, Plans, RolePermissions + all shared-mode tenant data |
| Dedicated database(s) | SQL Server, one catalog per routed tenant (`Tenant.DatabaseName`) | Full operational data for routing-enabled tenants |
| Tenant logos | `wwwroot/uploads/logos/tenant-{id}/` | Filesystem; NOT in the database. Back up with the deployment |
| Configuration | environment / token store | `DefaultConnection`; never store secrets in the repo |

PDFs are generated in-memory (QuestPDF) and are **not** persisted to disk — nothing to back up there.

---

## 1. Shared Database Backup

```sql
BACKUP DATABASE [HardBuildShared]
TO DISK = N'C:\Backups\HardBuildShared_FULL.bak'
WITH INIT, COMPRESSION, STATS = 5;
```

Recommended schedule: nightly FULL + (Standard edition) periodic DIFF/LOG. On SQL Server Express,
LOG backups still work in FULL recovery model but SQL Agent is unavailable — schedule via Windows
Task Scheduler + `sqlcmd` (see SQL Server Express Notes).

## 2. Dedicated Database Backup

Repeat for each provisioned tenant database (use the actual `Tenant.DatabaseName`):

```sql
BACKUP DATABASE [HardBuild_Tenant_ABC]
TO DISK = N'C:\Backups\HardBuild_Tenant_ABC_FULL.bak'
WITH INIT, COMPRESSION, STATS = 5;
```

Tip: enumerate dedicated databases from the shared DB:

```sql
SELECT Id, Name, DatabaseName
FROM Tenants
WHERE DatabaseMode = 1            -- Dedicated
  AND DatabaseProvisionedAtUtc IS NOT NULL;
```

Automate by scripting a backup per row of the query above.

## 3. Restore Shared Database

```sql
ALTER DATABASE [HardBuildShared] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;

RESTORE DATABASE [HardBuildShared]
FROM DISK = N'C:\Backups\HardBuildShared_FULL.bak'
WITH REPLACE, RECOVERY, STATS = 5;

ALTER DATABASE [HardBuildShared] SET MULTI_USER;
```

After restore, verify Identity logins, Tenants, and routing flags are intact, then run the app
(applies any pending shared migrations on startup is **not** automatic — apply via
`dotnet ef database update`, see `MigrationManagementGuide.md`).

## 4. Restore Dedicated Database

```sql
ALTER DATABASE [HardBuild_Tenant_ABC] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;

RESTORE DATABASE [HardBuild_Tenant_ABC]
FROM DISK = N'C:\Backups\HardBuild_Tenant_ABC_FULL.bak'
WITH REPLACE, RECOVERY, STATS = 5;

ALTER DATABASE [HardBuild_Tenant_ABC] SET MULTI_USER;
```

Then confirm in the shared `Tenants` row that `ConnectionString` / `DatabaseName` still point at the
restored catalog. If the database name changed, update the tenant record and call
`resolver.Invalidate(tenantId)` (restart the app to clear the 5-minute cache).

---

## Recovery Scenarios

| Scenario | Action |
|----------|--------|
| Shared DB corrupted | Restore shared (§3). Dedicated tenants keep working if their DBs are intact, but routing-flag reads come from shared — restore shared first. |
| One dedicated DB corrupted | Restore that dedicated DB (§4). Other tenants unaffected. As an interim, **Disable Routing** for that tenant to fall back to its last shared data (note: shared data may be stale if it diverged after cutover). |
| Dedicated DB lost, no backup | Disable Routing (tenant uses shared snapshot from cutover time). Data created while routed is lost — reinforces the need for dedicated backups. |
| Wrong data after cutover | Disable Routing to stop writes to dedicated, investigate, re-migrate or fix, then re-enable. |
| Logo files lost | Restore `wwwroot/uploads/logos/` from deployment backup; `LogoPath` in SystemSettings still references the expected path. |

## Disaster Recovery Checklist

- [ ] Latest **shared** backup verified and offsite
- [ ] Latest backup for **each dedicated** tenant DB verified and offsite
- [ ] `wwwroot/uploads/` backed up with the deployment
- [ ] `DefaultConnection` / environment configuration recorded in a secure store
- [ ] Restore rehearsed at least once (shared + one dedicated) on a staging server
- [ ] Documented RTO/RPO acceptable to the pilot business
- [ ] Post-restore validation: SuperAdmin login, Tenants list, routing flags, one routed tenant login

---

## SuperAdmin Restore Feature (Phase 5.2.1)

The BuildFlow platform now includes an in-app SuperAdmin restore feature accessible from **Backup Management → Backup History**.

### Restore As Test DB

Creates a **new** database from a selected backup. Does not affect live tenant routing.

1. In Backup History, find a successful backup row.
2. Click **"Test DB"**.
3. Optionally enter a target database name (auto-generated if blank).
4. Confirm the SweetAlert dialog.

The restored test database is created on the same SQL Server instance as the source.

### Restore Tenant DB (Overwrite)

Overwrites the live tenant database. Available only for **tenant** backups.

1. In Backup History, find a successful tenant backup row.
2. Click **"Restore Tenant DB"**.
3. Type `RESTORE` (uppercase) in the confirmation dialog.
4. Click confirm.

The service:
- Sets the database to `SINGLE_USER` with rollback to terminate active connections.
- Runs `RESTORE DATABASE ... WITH REPLACE`.
- Returns the database to `MULTI_USER`.
- Invalidates the tenant resolver cache.

### Platform Restore

Platform database overwrite restore is **not available in the UI**. See the manual procedure in `docs/Phase521_RestoreManagement.md`.

### Configuration

```json
"BackupSettings": {
  "RootPath": "C:\\HardBuildBackups\\",
  "RestoreDataPath": "",
  "RestoreLogPath": ""
}
```

`RestoreDataPath` and `RestoreLogPath` set override paths for `.mdf` / `.ldf` files when restoring as a new database. Leave empty to use SQL Server defaults.

### Security

- Restore is exclusively SuperAdmin. Controller enforces `[Authorize(Roles = "SuperAdmin")]`.
- All POST actions require `[ValidateAntiForgeryToken]`.
- Backup paths outside `BackupSettings:RootPath` are rejected before any SQL is executed.
- Connection strings are never stored in RestoreRecord fields.

---

## SQL Server Express Notes

- **10 GB per-database limit** — the dedicated-database model keeps each tenant under its own limit;
  consider SQL Server Standard for production scale.
- **No SQL Server Agent** — schedule backups with **Windows Task Scheduler** calling `sqlcmd`:
  ```bat
  sqlcmd -S .\SQLEXPRESS -E -Q "BACKUP DATABASE [HardBuildShared] TO DISK=N'C:\Backups\HardBuildShared_FULL.bak' WITH INIT, COMPRESSION"
  ```
  (COMPRESSION is unavailable on Express — drop that option if it errors.)
- Keep databases in an appropriate recovery model; use SIMPLE if point-in-time recovery is not
  required at pilot scale to avoid log growth.
- Ensure the SQL service account can write to the backup folder.
