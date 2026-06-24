# Phase 5.2 — Backup Management

> **Date:** 2026-06-23  
> **Status:** Implemented and QA-verified

## Backup architecture

- **Platform-owned metadata** — `BackupRecord` entities live only in `ApplicationDbContext` (shared platform DB).
- **SQL Server native backups** — `BACKUP DATABASE ... TO DISK` with `COPY_ONLY`, `INIT`.
- **Paths** — Configured via `BackupSettings:RootPath`:
  - `{RootPath}/Platform/` — shared platform database
  - `{RootPath}/Tenants/{TenantCode}/` — dedicated tenant databases
- **No restore in app** — Phase 5.2 records and creates backups only; restore remains a SuperAdmin/DBA operational procedure.

## SuperAdmin backup flow

1. Navigate **SaaS Management → Backup Management** (`/BackupManagement/Index`).
2. View platform status, per-tenant last backup, and history table.
3. Actions (SweetAlert confirmed):
   - **Backup Platform** — backs up `DefaultConnection` database
   - **Backup Tenant** — backs up provisioned dedicated tenant DB
   - **Backup All** — platform + all provisioned dedicated tenants (non-suspended)
   - **Run Retention Cleanup** — deletes expired successful backup files under root only
4. Results appear via layout toast; history table shows status, size, errors.

**Authorization:** `[Authorize(Roles = "SuperAdmin")]` on `BackupManagementController`.

## Tenant backup status / request flow

1. TenantAdmin opens **Settings** (`/Settings/Index`).
2. **Data Protection** card shows:
   - Last backup date, status, backup type
   - Shared-mode note when tenant uses platform DB
3. **Request Backup** (when `AllowTenantBackupRequest = true`):
   - Dedicated tenant → immediate tenant DB backup (`BackupType = Requested`)
   - Shared tenant → platform backup with tenant note (covers tenant data on shared DB)
4. TenantAdmin **cannot** download, restore, or delete backup files.

**Authorization:** `PermissionAuthorize("Settings", "View")` for display; `Settings/Edit` for request.

## Configuration (`appsettings.json`)

```json
"BackupSettings": {
  "RootPath": "C:\\HardBuildBackups\\",
  "RetentionDays": 14,
  "EnableScheduledBackups": false,
  "ScheduledBackupHour": 2,
  "AllowTenantBackupRequest": true
}
```

| Setting | Default | Notes |
|---------|---------|-------|
| `RootPath` | (required) | Must be writable by **SQL Server service account** |
| `RetentionDays` | 14 | Files older than this (successful only) eligible for cleanup |
| `EnableScheduledBackups` | false | Must be explicitly enabled for automatic runs |
| `ScheduledBackupHour` | 2 | Local hour (0–23) for daily scheduled batch |
| `AllowTenantBackupRequest` | true | TenantAdmin Request Backup button |

## Scheduled backup behavior

- `ScheduledBackupBackgroundService` registered as `IHostedService`.
- Runs **only** when `EnableScheduledBackups = true`.
- Daily check every 15 minutes; runs once per calendar day after `ScheduledBackupHour`.
- Executes `BackupAllDatabasesAsync(Scheduled)` then retention cleanup.
- Logs results via `ILogger`.

## Retention policy

- Deletes files where:
  - `BackupRecord.Status = Success`
  - `CompletedAtUtc` older than `RetentionDays`
  - File path is **under** `BackupSettings:RootPath` (path traversal guard)
- Removes matching `BackupRecord` rows after file delete.
- Skips paths outside root; logs warning.

## Restore limitation

- **Not implemented in Phase 5.2** — intentional.
- Restore via SQL Server Management Studio or `RESTORE DATABASE` scripts using `.bak` files on the backup server.
- Document restore runbook separately (`docs/ProductionBackupPlan.md`).

## Security notes

- Connection strings never shown in backup UI or `BackupRecord` fields exposed to tenants.
- Tenant backup request audited via `AuditService`.
- SuperAdmin actions audited (`BACKUP_PLATFORM`, `BACKUP_TENANT`, `BACKUP_ALL`, `BACKUP_RETENTION_CLEANUP`).
- Backup files stored on disk — restrict OS folder permissions to SQL Server + administrators.

## QA results

| Suite | Result |
|-------|--------|
| `dotnet build` | **0 errors, 0 warnings** |
| `--qa-backup` | **10 / 10 PASS** |
| `--qa-phase50d2` | **50 / 50 PASS** |
| `--qa-phase51` | **8 / 8 PASS** |
| `--qa-rc12` | **22 / 22 PASS** |

### `--qa-backup` coverage

1. Test backup folder resolution  
2. Platform database backup + `.bak` file  
3. Dedicated tenant database backup + `.bak` file  
4. `BackupRecord` persistence  
5. SuperAdmin history query  
6. Tenant latest-backup query  
7. No connection string leakage in records  
8. Retention cleanup skips files outside backup root  

## Operational prerequisites

1. Create `BackupSettings:RootPath` on the server.
2. Grant **SQL Server service account** read/write to that folder (OS error 5 = access denied).
3. Apply migration `Phase52_BackupManagement` on platform DB.
4. Set production `RootPath` in environment-specific config (not committed secrets).

## Files added / changed

| Area | Path |
|------|------|
| Model | `Models/BackupRecord.cs` |
| Config | `Configuration/BackupSettings.cs` |
| Service | `Services/Backups/IBackupService.cs`, `BackupService.cs`, `ScheduledBackupBackgroundService.cs` |
| Controller | `Controllers/BackupManagementController.cs` |
| Settings | `Controllers/SettingsController.cs` (RequestBackup, Data Protection) |
| Views | `Views/BackupManagement/Index.cshtml`, `Views/Settings/Index.cshtml` |
| Migration | `Migrations/20260623040436_Phase52_BackupManagement.cs` |
| QA | `Tools/BackupQaRunner.cs` |
| Docs | `docs/UI_GridPaginationAudit.md` |
