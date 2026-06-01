# Phase 5 — Final Release Checklist (Pilot Deployment Preparation)

> **Status:** Release-preparation (no new features, no architecture changes)
> **Build:** `dotnet build` → Build succeeded, 0 Errors, 0 Warnings
> **QA:** Phase 5.0D.2 + 5.0D.4 runner → 50/50 PASS

This is the single-page release reference for HardBuild POS (HardwareManagementSystem).
For step-by-step procedures see the companion guides:
`PilotTenantOperationsGuide.md`, `BackupAndRestoreGuide.md`, `MigrationManagementGuide.md`,
`ReleaseValidationChecklist.md`, `QARunnerGuide.md`, `PilotReadinessReport.md`.

---

## 1. System Overview

HardBuild POS is a multi-tenant SaaS hardware-store management system (ASP.NET Core 9, MVC,
EF Core, SQL Server). It supports inventory, POS, sales, purchasing, AR/AP, branches, imports,
reporting, auditing and notifications. Tenancy runs in two modes simultaneously:

- **Shared mode** (default): all tenants share `ApplicationDbContext` (one database), isolated by `TenantId`.
- **Dedicated mode** (per-tenant database): a routing-enabled tenant performs **all** operational
  work against its own `TenantDbContext` database, while platform data stays shared.

## 2. Platform Components (always shared — never moved)

- Identity: `AspNetUsers`, `AspNetRoles`, user/role management
- `Tenants`, `SubscriptionPlans`, `RolePermissions`
- SuperAdmin dashboard, SaaS administration, billing/subscription data
- SuperAdmin runtime always operates on the shared `ApplicationDbContext`

## 3. Dedicated Tenant Components (routed to `TenantDbContext` when routing is active)

Categories, Units, Items, Suppliers, Customers, Branches, BranchProductStocks, UserBranches,
BranchTransfers (+items), StockInHeaders/Details, PurchaseOrders/Items, SupplierPayments,
Quotations/Items, DeliveryReceipts/Items, SalesHeaders/Details, SalesReturnHeaders/Details,
CustomerLedgers, StockAdjustmentHeaders/Details, Expenses, SystemSettings, ImportBatches/Rows,
Notifications, AuditTrails.

## 4. Shared Components (read from shared even for routed tenants)

- Identity user lookups (`UserManager`) — branch assignments are operational, the users are not
- `Tenants` metadata (e.g. SuperAdmin audit-page tenant name lookup uses shared `_platformDb`)
- Resolver metadata cache (tenant routing flags), read from shared `Tenants`

## 5. Database Architecture

```
                 ┌─────────────────────────────┐
   All requests  │  ITenantOperationalContext  │
   ───────────►  │          Provider           │
                 └──────────────┬──────────────┘
                                │ resolves per request
              routing inactive  │  routing active
                ┌───────────────┴───────────────┐
                ▼                                ▼
   ApplicationDbContext (shared)        TenantDbContext (dedicated)
   - all shared-mode tenants            - one routing-enabled tenant
   - SuperAdmin / platform              - its own SQL database
   - Identity, Tenants, Plans
```

- **Shared DB:** `DefaultConnection` (appsettings token `#{DB_CONNECTION_STRING}#`).
- **Dedicated DB:** built from `DefaultConnection` with `InitialCatalog = <Tenant.DatabaseName>`,
  `TrustServerCertificate=true`, `MultipleActiveResultSets=true`. Stored on `Tenant.ConnectionString`.
- **Migrations:** `ApplicationDbContext` → `Migrations/`; `TenantDbContext` → `Migrations/Tenant/`.

## 6. Dedicated Database Routing Rules

A tenant routes to its dedicated database **only when ALL are true** (`TenantDatabaseResolver`):

1. `DatabaseMode == Dedicated`
2. `DatabaseProvisionedAtUtc != null` (provisioned)
3. `DataMigrated == true`
4. `RoutingEnabled == true`
5. `ConnectionString` is not blank

Otherwise the tenant uses the shared `ApplicationDbContext`. Metadata is cached 5 minutes in
`IMemoryCache`; `resolver.Invalidate(tenantId)` is called after any flag change.

## 7. Rollback Rules

- Setting `RoutingEnabled = false` (Disable Routing) makes the tenant fall back to the shared
  database **immediately** (after cache invalidation). No data is deleted; the dedicated database
  is retained.
- Re-enabling routing returns the tenant to the dedicated database.
- Rollback is reversible and non-destructive. Verified by QA (disable→Shared→auth OK→re-enable→Dedicated).

> **Operational caution:** Operational writes made while routing was active live in the dedicated
> DB; writes made while rolled back to shared live in the shared DB. Re-migration/reconciliation is
> required if you write to both during the same window. Treat rollback as an emergency fallback,
> not a routine toggle.

## 8. Known Accepted Risks

See `docs/AcceptedRisks.md` for full entries. Release-relevant summary:

- **AR-SEC-01 (NEW):** The seeded SuperAdmin (`superadmin` / `SuperAdmin123!` in `DbSeeder`) is a
  well-known default. **Must be rotated on first login before pilot go-live.**
- **AR-001:** Shared-scope helpers include `TenantId == null` legacy rows (widens visibility within
  the shared DB only; not applicable inside a dedicated DB which holds one tenant's rows).
- **AR-002:** `CustomerLedger` has no direct `TenantId`; scoped via parent `Customer`.
- **AR-AUDIT-01 (5.0D.4):** SuperAdmin filtering the audit page by a routing-enabled tenant sees
  only shared/platform-level audit rows for that tenant; the tenant's operational audit lives in
  its dedicated DB (consistent with database-per-tenant; cross-DB aggregation is a future feature).
- **CSP:** `Content-Security-Policy` uses `'unsafe-inline'` for Bootstrap/SweetAlert (planned nonce).
- **SQL Server Express 10 GB/db limit:** mitigated per-tenant by dedicated databases; use Standard
  edition for scale.

## 9. Deployment Requirements

- .NET 9 runtime / ASP.NET Core Hosting Bundle on the host
- SQL Server (Express OK for pilot; Standard recommended for growth)
- `DefaultConnection` provided via environment/token replacement (never hardcoded)
- HTTPS certificate (HSTS + secure cookies enforced in non-Development)
- IIS `maxAllowedContentLength >= 15728640` (matches 15 MB Kestrel limit) — see `IISDeploymentChecklist.md`
- Writable `wwwroot/uploads/logos/` for tenant logos (must persist across deploys & be backed up)
- The SQL login must have permission to `CREATE DATABASE` and run migrations if dedicated tenants
  are provisioned from the app

## 10. Pilot Requirements

- One pilot tenant (known, trusted business) onboarded per `PilotTenantOperationsGuide.md`
- Dedicated database provisioned, data migrated, routing enabled, diagnostics = Dedicated
- Backups configured for shared DB **and** the pilot's dedicated DB (`BackupAndRestoreGuide.md`)
- Rollback procedure tested and understood by the operator

## 11. Go-Live Checklist

- [ ] `dotnet build` → 0 Errors / 0 Warnings
- [ ] QA runners pass (`--qa-phase50d1`, `--qa-phase50d2`) in a dev/staging environment
- [ ] `DefaultConnection` set via environment; no secrets committed
- [ ] `ASPNETCORE_ENVIRONMENT=Production`; `SeedSettings:EnableDemoDataCleanup` **false**; `QA:EnableCleanup` **false/absent**
- [ ] SuperAdmin default password rotated
- [ ] HTTPS + HSTS verified; security headers present
- [ ] Shared DB migrations applied (`dotnet ef database update`)
- [ ] Backups scheduled (shared + dedicated) and a test restore performed
- [ ] `wwwroot/uploads` writable and included in backup/deploy retention
- [ ] Pilot tenant provisioned, migrated, routed; diagnostics show `Runtime Database: Dedicated`
- [ ] `ReleaseValidationChecklist.md` executed and signed off
- [ ] Rollback (Disable Routing) tested on the pilot tenant
- [ ] `/health` endpoint returns healthy
