# Backup & Restore Production Setup

Covers everything required to make `/BackupManagement/Index` and all backup/restore actions work on the VPS production server.

---

## 1. Required `appsettings.Production.json` Section

Add or verify the following block in `appsettings.Production.json`:

```json
"BackupSettings": {
  "RootPath": "D:\\HardBuildBackups",
  "RetentionDays": 14,
  "EnableScheduledBackups": false,
  "ScheduledBackupHour": 2,
  "AllowTenantBackupRequest": true,
  "RestoreDataPath": "D:\\SQLData",
  "RestoreLogPath": "D:\\SQLLogs"
}
```

| Key | Purpose | Required? |
|-----|---------|-----------|
| `RootPath` | Root folder for all `.bak` files | **Yes** — page shows a warning if missing |
| `RetentionDays` | Auto-cleanup after N days | No (default: 14) |
| `EnableScheduledBackups` | Turn on nightly auto-backup | No (default: false) |
| `ScheduledBackupHour` | Hour (UTC) for nightly run | No (default: 2) |
| `AllowTenantBackupRequest` | Let tenant admins request a backup | No (default: true) |
| `RestoreDataPath` | Override `.mdf` path for Restore As New | No (falls back to SQL Server default) |
| `RestoreLogPath` | Override `.ldf` path for Restore As New | No (falls back to SQL Server default) |

> **If `RootPath` is empty or missing**, the Backup Management page loads with a warning banner and all backup/restore actions fail gracefully with a clear error message — no crash.

---

## 2. Required Folders on VPS

Create these directories before running backups or restores:

```bat
mkdir D:\HardBuildBackups
mkdir D:\SQLData
mkdir D:\SQLLogs
```

Or with PowerShell:

```powershell
New-Item -ItemType Directory -Force -Path "D:\HardBuildBackups"
New-Item -ItemType Directory -Force -Path "D:\SQLData"
New-Item -ItemType Directory -Force -Path "D:\SQLLogs"
```

> **`D:\SQLData` and `D:\SQLLogs` are only needed if you use the Restore As New feature.** If `RestoreDataPath` / `RestoreLogPath` are left empty, SQL Server uses its default instance paths.

---

## 3. Required Permissions

### SQL Server service account

The SQL Server service account (typically `NT SERVICE\MSSQL$SQLEXPRESS` or `NT SERVICE\MSSQLSERVER`) must have **write access** to:

| Folder | Why |
|--------|-----|
| `D:\HardBuildBackups` | Write `.bak` files during backup; read during restore |
| `D:\SQLData` | Write `.mdf` files during Restore As New |
| `D:\SQLLogs` | Write `.ldf` files during Restore As New |

Grant via PowerShell (run as Administrator):

```powershell
$acl = Get-Acl "D:\HardBuildBackups"
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "NT SERVICE\MSSQL`$SQLEXPRESS", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
$acl.SetAccessRule($rule)
Set-Acl "D:\HardBuildBackups" $acl
```

Repeat for `D:\SQLData` and `D:\SQLLogs`.

### IIS Application Pool

The app pool identity does **not** need write access to the backup folder — SQL Server writes the files directly. The app pool only reads `BackupRecord.BackupPath` values stored in the database.

---

## 4. Required SQL Server Permissions

The SQL login used by the application (`sa` or a dedicated login) must have sufficient privileges to run backup and restore commands.

Minimum grants:

```sql
-- Required for BACKUP DATABASE (backup actions)
GRANT BACKUP DATABASE TO [your_sql_login];

-- Required for CREATE DATABASE and RESTORE (Restore As New)
ALTER SERVER ROLE [dbcreator] ADD MEMBER [your_sql_login];

-- Required for RESTORE ... WITH REPLACE and ALTER DATABASE (Restore Overwrite)
ALTER SERVER ROLE [sysadmin] ADD MEMBER [your_sql_login];
-- OR (less privileged):
GRANT ALTER ANY DATABASE TO [your_sql_login];
```

> The production connection string uses `sa` which already has `sysadmin`. No additional grants needed if `sa` is used.

---

## 5. Required Database Migrations

Phase 5.2 and 5.2.1 must be applied before using Backup Management.

### Check pending migrations

```sql
-- Check tables exist
SELECT name FROM sys.tables WHERE name IN ('BackupRecords', 'RestoreRecords');
```

Expected output: 2 rows. If 0 or 1 rows are returned, migrations are pending.

### Apply migrations on VPS

**Option A — dotnet CLI (from app folder on VPS):**

```powershell
cd C:\inetpub\wwwroot\HardwareManagementSystem
dotnet ef database update --context ApplicationDbContext
```

**Option B — SQL script (no dotnet tools required):**

Generate the SQL migration script on the dev machine and run it on the VPS:

```powershell
# On dev machine:
dotnet ef migrations script --context ApplicationDbContext --idempotent --output migration.sql
```

Then run `migration.sql` in SSMS on the VPS server against `HardwareManagementSystemDb`.

**Option C — Use IIS startup migration (already configured):**

The application calls `context.Database.Migrate()` at startup (if configured). Restart the application pool after deploying and verify the tables exist.

> **Do NOT run migrations in production code at runtime for each request.** Use one of the above one-time methods.

---

## 6. Migration Applied Verification Checklist

After applying migrations, verify:

```sql
USE HardwareManagementSystemDb;

-- Phase 5.2
SELECT TOP 1 * FROM BackupRecords;   -- should not error

-- Phase 5.2.1
SELECT TOP 1 * FROM RestoreRecords;  -- should not error
```

---

## 7. How to Test Production Setup

### Step 1 — Open the page

Navigate to: `https://your-vps-domain/BackupManagement/Index`

**Expected:** Page loads with the Backup & Restore Management header.
- If warnings appear: follow the instructions in the yellow banner.
- If "Something went wrong": check IIS logs for the exception — most likely a missing table or folder.

### Step 2 — Platform Backup

1. Click **Backup Platform**.
2. Confirm the SweetAlert.
3. Expect: green success banner, new row in Backup History.
4. Check the file exists: `D:\HardBuildBackups\Platform\*.bak`.

### Step 3 — Tenant Backup (if provisioned tenants exist)

1. Find a tenant with a dedicated database in the Tenant Databases table.
2. Click **Backup** for that tenant.
3. Expect: success banner, new row in Backup History.

### Step 4 — Restore As Test DB

1. In Backup History, find a **Success** backup row.
2. Click **Test DB**.
3. Accept the SweetAlert (optional: enter custom target DB name).
4. Expect: success banner. Verify in SSMS:
   ```sql
   SELECT name FROM sys.databases WHERE name LIKE '%RESTORE_TEST%';
   ```

### Step 5 — Clean up test database

```sql
DROP DATABASE [DB_NAME_RESTORE_TEST_20260705...];
```

---

## 8. Common Production Errors

| Error | Cause | Fix |
|-------|-------|-----|
| "Something went wrong" on `/BackupManagement/Index` | Missing `BackupSettings` section **or** `BackupRecords`/`RestoreRecords` table missing | Add config section + apply migrations |
| "Cannot open backup device... Access is denied" | SQL Server service account lacks write permission to backup folder | Grant folder write access to SQL service account |
| "RESTORE ... WITH MOVE: file already exists" | Target database already exists | Choose a different target name, or drop existing test DB first |
| "The database ... is currently in use" | Active connections during overwrite restore | `SINGLE_USER WITH ROLLBACK IMMEDIATE` is issued automatically; if it fails, manually kill connections in SSMS |
| "Invalid object name 'RestoreRecords'" | Phase521 migration not applied | Run `dotnet ef database update --context ApplicationDbContext` |

---

## 9. Migration Commands Summary

```powershell
# Apply all pending migrations (dev or staging)
dotnet ef database update --context ApplicationDbContext

# Generate idempotent SQL script for production (run on dev, apply script on VPS)
dotnet ef migrations script --context ApplicationDbContext --idempotent --output migration.sql

# List pending migrations
dotnet ef migrations list --context ApplicationDbContext
```
