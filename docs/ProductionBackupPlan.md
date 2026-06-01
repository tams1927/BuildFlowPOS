# HardBuild POS — Production Backup Plan

**Project:** HardwareManagementSystem / HardBuild POS  
**Database:** SQL Server (on-premises or hosted)  
**Last Updated:** 2026-05-29

---

## 1. Backup Schedule

### 1.1 Full Backup
| Schedule | Frequency | Retention |
|----------|-----------|-----------|
| Full database backup | Daily — recommended at low-traffic hours (e.g. 01:00 AM) | Retain 7 daily backups |
| Weekly full backup (archive) | Every Sunday | Retain 4 weekly backups (≈1 month) |
| Monthly full backup (archive) | 1st of each month | Retain 12 monthly backups (≈1 year) |

**SQL Server Agent Job (example name):** `HardBuild_Full_Daily`

```sql
-- Example: Full backup (run via SQL Server Agent or a scheduled task)
BACKUP DATABASE [HardwareManagementDB]
TO DISK = N'D:\Backups\HardBuild_Full_$(date).bak'
WITH COMPRESSION, CHECKSUM, STATS = 10;
```

> ⚠️ Replace path and database name with your actual values. Never hard-code credentials.

---

### 1.2 Transaction Log Backup (Point-in-Time Recovery)
> Requires the database recovery model to be set to **FULL** (not SIMPLE).

| Schedule | Frequency | Retention |
|----------|-----------|-----------|
| Transaction log backup | Every 15–30 minutes during business hours | Retain 48 hours of log backups |

```sql
BACKUP LOG [HardwareManagementDB]
TO DISK = N'D:\Backups\HardBuild_Log_$(datetime).trn'
WITH COMPRESSION, CHECKSUM;
```

---

### 1.3 Differential Backup
| Schedule | Frequency | Retention |
|----------|-----------|-----------|
| Differential backup | Every 6 hours (mid-day, evening, midnight, pre-dawn) | Retain 2 days |

A differential backup captures changes since the last full backup and is faster to restore than replaying all log files.

---

## 2. Restore Test Checklist

Perform a restore test on a **non-production** server at least once per month.

- [ ] Copy the latest `.bak` and `.trn` files to the test server
- [ ] Restore the full backup with `NORECOVERY`
- [ ] Apply differential backup (if used) with `NORECOVERY`
- [ ] Apply transaction log backups in sequence with `RECOVERY` on the last one
- [ ] Verify database comes online with `ONLINE` status
- [ ] Log in to the HardBuild POS test environment and confirm:
  - [ ] Dashboard loads with correct data
  - [ ] Recent transactions appear correctly
  - [ ] Inventory counts match expectations
- [ ] Record restore completion time (RTO target: < 30 minutes for a typical backup set)
- [ ] Document test result in the ops log

---

## 3. Pre-Migration Backup Rule

> **MANDATORY**: Before running any EF Core migration (`dotnet ef database update`), a manual full backup must be taken.

Checklist before every migration:
- [ ] Notify active users that a brief maintenance window is starting
- [ ] Take a full database backup manually (or trigger the SQL Agent job)
- [ ] Confirm the `.bak` file is written and readable
- [ ] Run the migration: `dotnet ef database update --project HardwareManagementSystem`
- [ ] Verify the app starts and the `/health` endpoint returns `Healthy`
- [ ] If the migration fails: restore from the pre-migration backup immediately

---

## 4. Retention Policy Summary

| Backup Type | Retention Period |
|-------------|-----------------|
| Daily full backup | 7 days |
| Weekly full backup | 4 weeks |
| Monthly full backup | 12 months |
| Transaction log | 48 hours |
| Differential | 2 days |
| Pre-migration backup | Keep until next migration is verified stable (minimum 30 days) |

---

## 5. Storage Recommendations

- Store backups on a **separate physical drive or NAS** from the database server.
- For offsite protection, copy backups to a secondary location (network share, cloud storage, or tape) daily.
- Encrypt backup files at rest if they contain tenant or customer PII.
- Verify backup file integrity with `RESTORE VERIFYONLY` regularly.

```sql
RESTORE VERIFYONLY
FROM DISK = N'D:\Backups\HardBuild_Full_latest.bak'
WITH CHECKSUM;
```

---

## 6. Local Development Backup Note

> Local developer machines do **not** need production-grade backup schedules.

Recommended minimum for local dev:
- Keep a local `.bak` of the dev database before applying new migrations.
- Command to take a quick backup from SSMS or SQLCMD:

```sql
BACKUP DATABASE [HardwareManagementDB_Dev]
TO DISK = N'C:\Dev\Backups\HardBuild_Dev_before_migration.bak'
WITH COMPRESSION;
```

- Seed data is managed by `DbSeeder.cs` — a clean database can always be re-seeded.
- Never use production backup files to seed local development databases.

---

## 7. Contacts & Escalation

| Role | Responsibility |
|------|---------------|
| DB Administrator | Owns backup schedule, monitors job failures, performs restore tests |
| Lead Developer | Owns pre-migration backup rule, notifies DBA before migrations |
| System Owner | Approves extended maintenance windows for large migrations |

---

*This document should be reviewed whenever the database schema changes significantly or the hosting environment changes.*
