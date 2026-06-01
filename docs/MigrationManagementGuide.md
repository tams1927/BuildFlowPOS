# Migration Management Guide

The solution has **two** EF Core contexts, each with its own migrations history:

| Context | Purpose | Migrations folder | History table |
|---------|---------|-------------------|---------------|
| `ApplicationDbContext` | Shared platform + shared-mode tenant data | `Migrations/` | `__EFMigrationsHistory` (shared DB) |
| `TenantDbContext` | Dedicated per-tenant operational schema | `Migrations/Tenant/` | `__EFMigrationsHistory` (each dedicated DB) |

`TenantDbContext` is built at design time by `Data/TenantDbContextDesignTimeFactory.cs` (a
placeholder connection — never connected to during `migrations add`). At runtime, dedicated
databases are migrated automatically by the provisioning service (`MigrateAsync`).

> Prerequisite: `dotnet ef` tooling. Install/update once: `dotnet tool install --global dotnet-ef`
> (or `dotnet tool update --global dotnet-ef`). Run all commands from the project directory.

---

## How to create migrations

### ApplicationDbContext (shared)

```bash
dotnet ef migrations add <Name> --context ApplicationDbContext --output-dir Migrations
```

### TenantDbContext (dedicated)

```bash
dotnet ef migrations add <Name> --context TenantDbContext --output-dir Migrations/Tenant
```

Always pass `--context` because the project has multiple `DbContext` types. Keep the two schemas in
sync for operational tables: a change to an operational entity usually needs a migration in **both**
contexts (shared and tenant).

## How to apply migrations

### ApplicationDbContext (shared DB)

```bash
dotnet ef database update --context ApplicationDbContext
```

Uses `DefaultConnection`. Apply this on deploy whenever shared migrations are pending.

### TenantDbContext (dedicated DBs)

Dedicated databases are normally migrated **automatically**:

- **At provisioning** — `TenantDatabaseProvisioningService.ProvisionAsync` calls
  `tenantContext.Database.MigrateAsync()` and records `LastDatabaseMigration` on the tenant.
- **To upgrade an existing dedicated DB to a new tenant migration**, apply against that tenant's
  connection. Example using a known connection string:

  ```bash
  dotnet ef database update --context TenantDbContext --connection "Server=.;Database=HardBuild_Tenant_ABC;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
  ```

  Repeat for each dedicated database (enumerate via the `Tenants` table — see
  `BackupAndRestoreGuide.md`). For app-driven upgrades, re-running provisioning logic or a
  maintenance routine that calls `MigrateAsync` per tenant is the supported path.

## How to verify pending migrations

```bash
# List migrations and whether they are applied (against the target connection)
dotnet ef migrations list --context ApplicationDbContext
dotnet ef migrations list --context TenantDbContext --connection "<dedicated-connection>"
```

```sql
-- In any database, see what has been applied
SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory ORDER BY MigrationId;
```

The latest tenant baseline is `Migrations/Tenant/..._InitialTenantSchema`. For a routed tenant,
`Tenants.LastDatabaseMigration` records the last applied tenant migration name.

## How to generate a SQL script (preferred for production)

For controlled environments, script the migration instead of running `database update` live:

```bash
dotnet ef migrations script --idempotent --context ApplicationDbContext -o shared_migration.sql
dotnet ef migrations script --idempotent --context TenantDbContext   -o tenant_migration.sql
```

Review and run the idempotent script during a maintenance window.

## How to handle migration failures

1. **Read the exact error.** Multiple-cascade-path FK errors and duplicate-object errors are the
   most common. (The known `BranchTransfers` dual-branch FK cascade issue was fixed in Phase 5.0C
   Hotfix by configuring both FKs `OnDelete(Restrict)`.)
2. **Do not** delete `__EFMigrationsHistory` rows by hand on a production DB.
3. **Shared DB failure:** restore from backup (`BackupAndRestoreGuide.md`), fix the migration in a
   dev branch, regenerate, retest, then re-deploy.
4. **Dedicated DB provisioning failure:** the provisioning service logs the error and returns
   `Success = false` with `TENANT_DATABASE_PROVISION_FAILED` audited. The tenant remains on the
   shared database (safe). Drop the partially-created dedicated database if needed and re-provision
   after fixing the migration:
   ```sql
   IF DB_ID(N'HardBuild_Tenant_ABC') IS NOT NULL
   BEGIN
       ALTER DATABASE [HardBuild_Tenant_ABC] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
       DROP DATABASE [HardBuild_Tenant_ABC];
   END
   ```
   (Re-provision is blocked while `DatabaseProvisionedAtUtc` is set; clear it / drop the DB first.
   There is intentionally no automatic "Force Reprovision" in the current release.)
5. **Always** test migrations on staging with a copy of production data before applying to the live
   shared database or any dedicated database.

## Safety rules

- Always specify `--context`.
- Never edit an already-applied migration; add a new one.
- Keep shared and tenant operational schemas consistent.
- Back up before applying any migration to a production database.
