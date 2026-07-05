# Phase 5.2.1 — SuperAdmin Restore Management

## Overview

Phase 5.2.1 adds a strictly SuperAdmin-only database restore feature connected to the existing Backup Management module (Phase 5.2). SuperAdmins can restore any system-generated backup as a test database, or overwrite an existing tenant database with explicit confirmation. Tenant users are never exposed to restore functionality.

---

## Architecture

```
BackupManagement/Index (SuperAdmin UI)
    ↓
BackupManagementController.RestoreAsNew / RestoreOverwrite
    [Authorize(Roles = "SuperAdmin")] + [ValidateAntiForgeryToken]
    ↓
IRestoreService / RestoreService
    ↓ validates → ApplicationDbContext.BackupRecords
    ↓ executes → SQL Server RESTORE DATABASE
    ↓ records → ApplicationDbContext.RestoreRecords
    ↓ audit   → AuditService
    ↓ cache   → ITenantDatabaseResolver.Invalidate() (overwrite only)
```

### Models

| Model | Location | Context |
|-------|----------|---------|
| `RestoreRecord` | `Models/RestoreRecord.cs` | `ApplicationDbContext` only |
| `BackupRecord` (existing) | `Models/BackupRecord.cs` | `ApplicationDbContext` only |

### Enums

| Enum | Values |
|------|--------|
| `RestoreMode` | `Overwrite`, `RestoreAsNew` |
| `RestoreStatus` | `Pending`, `Running`, `Success`, `Failed`, `Cancelled` |

---

## Restore As New Database Flow

1. SuperAdmin selects a successful backup in the Backup History table.
2. Clicks **"Test DB"** button — a SweetAlert dialog appears.
3. SuperAdmin optionally provides a target database name (auto-generated if blank).
4. Clicks **"Yes, restore test database"**.
5. `RestoreAsNew` POST action fires.
6. `RestoreService.ValidateAsync` runs safety checks.
7. `RESTORE FILELISTONLY` retrieves logical file names from the backup.
8. SQL Server default data/log paths are resolved (or from `BackupSettings:RestoreDataPath/RestoreLogPath`).
9. `RESTORE DATABASE [TargetDb] FROM DISK = ... WITH MOVE ..., STATS = 10` executes.
10. `RestoreRecord` is saved with `Status = Success` or `Failed`.
11. Audit event `DATABASE_RESTORE_TEST_CREATED` is logged.
12. **Live tenant routing is never touched.**

---

## Tenant Overwrite Restore Flow

1. SuperAdmin selects a successful **tenant** backup in Backup History.
2. Clicks **"Restore Tenant DB"** button.
3. SweetAlert danger confirmation appears, requiring the user to type `RESTORE` (uppercase).
4. On confirm, `RestoreOverwrite` POST action fires.
5. `RestoreService.ValidateAsync` runs safety checks.
6. Audit event `DATABASE_RESTORE_STARTED` is logged.
7. `ALTER DATABASE [Target] SET SINGLE_USER WITH ROLLBACK IMMEDIATE` terminates connections.
8. `RESTORE DATABASE [Target] FROM DISK = ... WITH REPLACE, STATS = 10` overwrites the database.
9. `ALTER DATABASE [Target] SET MULTI_USER` restores access.
10. `ITenantDatabaseResolver.Invalidate(tenantId)` clears the 5-minute cache.
11. `RestoreRecord` is saved.
12. Audit event `DATABASE_RESTORE_COMPLETED` or `DATABASE_RESTORE_FAILED` is logged.
13. On failure, best-effort `SET MULTI_USER` recovery is attempted automatically.

---

## Confirmation Requirements

| Action | Confirmation |
|--------|-------------|
| Restore As Test DB | SweetAlert info dialog, "Yes, restore test database" |
| Restore Tenant DB (overwrite) | SweetAlert danger dialog, user must type `RESTORE` |
| Platform overwrite | **Not available in UI** — DBA manual procedure only |

---

## Security Rules

- Controller class decorated with `[Authorize(Roles = "SuperAdmin")]`.
- All POST actions decorated with `[ValidateAntiForgeryToken]`.
- No restore endpoint is accessible to TenantAdmin, BranchManager, Cashier, or InventoryStaff.
- Restore UI buttons are rendered only in `BackupManagement/Index.cshtml`, which is unreachable by non-SuperAdmin users.
- Backup paths are validated to be under `BackupSettings:RootPath` before any restore.
- Connection strings are never printed or logged.

---

## Platform Restore Limitation

Platform database **overwrite** restore is intentionally not exposed in the UI.

Reasons:
- The platform database contains all tenant metadata, identity records, audit logs, and backup records.
- Overwriting it risks losing all tenant routing, user accounts, and restore history.
- This operation must be performed by a DBA following the manual disaster recovery procedure.

**UI behavior:** If a SuperAdmin attempts to trigger an overwrite restore on a platform backup, the controller returns an explicit error message:

> "Platform database overwrite restore must be performed manually by a DBA using the documented recovery procedure."

Platform backups support **Restore As Test DB** only.

---

## SQL Permissions Required

The SQL Server login used by the application must have:

```sql
GRANT BACKUP DATABASE  TO [app_login];
GRANT CREATE DATABASE  TO [app_login];   -- for RestoreAsNew
GRANT ALTER ANY DATABASE TO [app_login]; -- for SET SINGLE_USER / MULTI_USER
```

For minimal privilege, the login should have `dbcreator` server role (for RestoreAsNew) and `sysadmin` or `ALTER ANY DATABASE` for overwrite operations.

---

## Configuration

`appsettings.json`:

```json
"BackupSettings": {
  "RootPath": "C:\\HardBuildBackups\\",
  "RetentionDays": 14,
  "EnableScheduledBackups": false,
  "ScheduledBackupHour": 2,
  "AllowTenantBackupRequest": true,
  "RestoreDataPath": "",
  "RestoreLogPath": ""
}
```

- `RestoreDataPath` / `RestoreLogPath`: Override paths for `.mdf` / `.ldf` files when restoring as a new database. Falls back to SQL Server `InstanceDefaultDataPath` / `InstanceDefaultLogPath` when empty.

---

## Audit Events

| Event | Trigger |
|-------|---------|
| `DATABASE_RESTORE_STARTED` | Before overwrite restore begins |
| `DATABASE_RESTORE_COMPLETED` | After successful overwrite restore |
| `DATABASE_RESTORE_FAILED` | After any restore failure |
| `DATABASE_RESTORE_TEST_CREATED` | After successful RestoreAsNew |

Each audit record includes: BackupRecordId, TenantId, DatabaseName, TargetDatabaseName, RestoreMode, RequestedByUserId, IP address.

---

## QA Runner

Run `--qa-restore` in Development mode:

```bash
dotnet run -- --qa-restore
```

Tests covered:

| # | Test |
|---|------|
| 1 | No Phase521 migration pending |
| 2 | QA backup root resolved |
| 3 | Dedicated tenant found or provisioned |
| 4 | Tenant backup created |
| 5 | `ValidateAsync` accepts valid backup |
| 6 | `ValidateAsync` rejects non-existent backup id |
| 7 | `ValidateAsync` rejects path outside root |
| 8 | `RestoreTenantAsNewAsync` succeeds |
| 9 | `RestoreRecord` saved in ApplicationDbContext |
| 10 | Restored database exists in SQL Server |
| 11 | Restored database schema has tables |
| 12 | Live tenant `DatabaseName` unchanged after restore |
| 13 | `GetHistoryAsync` returns restore records |
| 14 | Restored test database cleaned up |

Destructive overwrite restore is **not** run automatically by the QA runner.

---

## Manual Disaster Recovery Procedure (Platform DB)

In the event the platform database needs to be restored from a backup:

1. Stop the application (IIS application pool stop or `dotnet stop`).
2. Locate the latest platform `.bak` file under `BackupSettings:RootPath\Platform\`.
3. In SQL Server Management Studio or `sqlcmd`, connect as `sa` or a DBA with `sysadmin`.
4. Run:
   ```sql
   USE master;
   ALTER DATABASE [HardwareManagementSystem] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
   RESTORE DATABASE [HardwareManagementSystem]
     FROM DISK = N'C:\HardBuildBackups\Platform\HardwareManagementSystem_20260705_020000.bak'
     WITH REPLACE, STATS = 10;
   ALTER DATABASE [HardwareManagementSystem] SET MULTI_USER;
   ```
5. Verify database integrity: `DBCC CHECKDB([HardwareManagementSystem])`.
6. Restart the application.
7. Log in as SuperAdmin and verify tenant list, backup history, and user accounts.

---

## Files Changed

| File | Change |
|------|--------|
| `Models/RestoreRecord.cs` | New — RestoreRecord model |
| `Configuration/BackupSettings.cs` | Added RestoreDataPath, RestoreLogPath |
| `ViewModels/BackupManagementIndexVm.cs` | Added RestoreHistory list |
| `Data/ApplicationDbContext.cs` | Added DbSet + fluent config for RestoreRecord |
| `Migrations/20260705000001_Phase521_RestoreManagement.cs` | New migration |
| `Migrations/ApplicationDbContextModelSnapshot.cs` | Updated snapshot |
| `Services/Backups/IRestoreService.cs` | New interface |
| `Services/Backups/RestoreService.cs` | New service implementation |
| `Program.cs` | Register RestoreService + --qa-restore |
| `Controllers/BackupManagementController.cs` | Added RestoreAsNew, RestoreOverwrite actions |
| `Views/BackupManagement/Index.cshtml` | Added restore buttons + restore history table |
| `Tools/RestoreQaRunner.cs` | New QA runner |
| `docs/Phase521_RestoreManagement.md` | This file |
| `docs/BackupAndRestoreGuide.md` | Updated |
