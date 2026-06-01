# Phase 5.0C — Dedicated Database Provisioning

> **Status:** Complete. Dedicated databases can be **created, migrated, tested, and seeded**.
> **Routing is still INACTIVE.** Every tenant continues to run on the shared database via `ApplicationDbContext`.

---

## 1. Goal

Create physical dedicated databases for tenants **safely**, while the live application
keeps using `ApplicationDbContext` for everything. After provisioning, the dedicated
database exists and is schema-complete, but it remains **unused** at runtime — it is
*prepared only*, ready for Phase 5.0D (data migration + request routing).

---

## 2. Architecture

```
                ┌─────────────────────────────────────────────┐
                │              SuperAdmin (UI)                  │
                │   Tenants / Details  →  Provision / Test      │
                └───────────────┬───────────────────────────────┘
                                │
                                ▼
        ┌───────────────────────────────────────────────────────┐
        │      ITenantDatabaseProvisioningService                 │
        │      (TenantDatabaseProvisioningService)                │
        │   • validate tenant + mode + name                       │
        │   • build connection string (current SQL instance)      │
        │   • CREATE DATABASE if missing                          │
        │   • MigrateAsync (TenantDbContext)                      │
        │   • seed minimal reference data                         │
        │   • persist routing metadata + audit                    │
        └───────────────┬───────────────────────────┬────────────┘
                        │                           │
                        ▼                           ▼
        ┌───────────────────────┐      ┌──────────────────────────────┐
        │ ITenantDbContextFactory│     │ ITenantDatabaseResolver        │
        │ CreateForConnection()  │     │ (metadata + connection string) │
        └───────────┬───────────┘      └──────────────────────────────┘
                    │
                    ▼
        ┌───────────────────────────────┐         ┌───────────────────────────┐
        │  Dedicated DB  (TenantDbContext)│  ⟵ NEW │  Shared DB (ApplicationDb)  │ ⟵ LIVE
        │  Prepared only — NOT routed     │         │  Powers ALL runtime traffic │
        └───────────────────────────────┘         └───────────────────────────┘
```

### Components

| Component | Responsibility |
|-----------|----------------|
| **Shared DB** (`ApplicationDbContext`) | The live multi-tenant database. Powers **all** runtime operations: login, POS, inventory, reports, SaaS subscriptions. Unchanged. |
| **Dedicated DB** (`TenantDbContext`) | One isolated database per tenant. Created and migrated by the provisioning service. Holds operational tables + a seeded `SystemSetting` row and replicated `RolePermissions` / `SubscriptionPlans`. **Not used at runtime in 5.0C.** |
| **`ITenantDatabaseResolver`** | Returns per-tenant routing metadata (mode, database name, connection string), masked for display. Cached in memory. |
| **`ITenantDbContextFactory`** | Builds a `TenantDbContext` from a resolved tenant id, or from an explicit connection string (`CreateForConnection`) during provisioning. |
| **`ITenantDatabaseProvisioningService`** | Orchestrates create → migrate → seed → persist metadata. The Phase 5.0C addition. |

---

## 3. Provisioning Flow (`ProvisionAsync(int tenantId)`)

1. **Load + validate tenant**
   - Tenant must exist.
   - `DatabaseMode == Dedicated` (else: *“Tenant must be switched to Dedicated mode first.”*)
   - `DatabaseName` must be set (else: *“Database name is required before provisioning.”*)
   - Must **not** already be provisioned (else: *“Database already provisioned.”* — no Force in 5.0C).
   - `DatabaseName` is validated against an identifier allow-list (letters/digits/`_`/`-`).
2. **Build connection strings** from the current SQL Server instance using
   `DefaultConnection` as the template (preserves the deployment's server + auth),
   swapping the catalog to the tenant database and forcing
   `TrustServerCertificate=true; MultipleActiveResultSets=true`.
3. **Create database if missing** — connect to `master`, `SELECT DB_ID(@name)`,
   and `CREATE DATABASE [name]` when absent (brackets escaped).
4. **Apply migrations** — `await context.Database.MigrateAsync()` on `TenantDbContext`
   (migrations in `Migrations/Tenant`, initial migration `InitialTenantSchema`).
5. **Seed minimal data** (see §4).
6. **Persist metadata** on the tenant row: `ConnectionString`, `DatabaseServer`,
   `DatabaseProvisionedAtUtc`, `LastDatabaseMigration`.
7. **Audit** `TENANT_DATABASE_PROVISIONED` (or `TENANT_DATABASE_PROVISION_FAILED`).
8. Return a `TenantDatabaseProvisionResultVm`.

`TenantDatabaseProvisionResultVm`: `Success`, `Message`, `DatabaseCreated`,
`MigrationsApplied`, `SeedCompleted`, `DatabaseName`, `ProvisionedAtUtc`.

---

## 4. Seed Content (minimal)

A freshly provisioned dedicated database is **empty of business data**. Only the
following reference data is seeded (idempotent — skipped if rows already exist):

| Seeded | Detail |
|--------|--------|
| **`SystemSetting`** | One tenant-owned row: `BusinessName = tenant.Name`, `CurrencySymbol = ₱`, `DefaultVatPercent = 12`, `TaxMode = VAT`. |
| **`RolePermissions`** | Replicated copy of the shared permission matrix so authorization works locally. |
| **`SubscriptionPlans`** | Read-only replicated copy of the plan catalog for reference. |

**NOT seeded:** Branches, Items, Customers, Suppliers, Sales, Stock, Expenses, Users.
The dedicated database starts empty of operational/business data.

---

## 5. SuperAdmin UI

**Tenant → Details → “Dedicated Database” card** shows:
- Database Mode (Shared / Dedicated)
- Database Name
- Provisioned (status + timestamp)
- Last Migration

and provides two actions:
- **Provision Database** — guarded by a **SweetAlert** confirmation (no browser `confirm()`):
  > *Provision Dedicated Database?*
  > *This will create a dedicated SQL database for this tenant. The tenant will continue using the shared database. No data will be moved. Proceed?*
  > Buttons: **Yes, Provision** / **Cancel**
  Only enabled when the tenant is Dedicated, has a name, and is not yet provisioned.
- **Test Connection** — posts to `TestDatabaseConnection`; result is shown as a toast.

**Tenant → DatabaseInfo (Diagnostics)** shows Current Routing, Provision Status,
Database Exists (Yes/No), Connection Test (Passed/Failed), Last Migration, masked
connection string, and resolver/phase status.

---

## 6. Test Connection behaviour (`TestDatabaseConnection`)

| Tenant state | Result (toast) |
|--------------|----------------|
| Shared mode | *Using shared database.* |
| Dedicated + provisioned | Opens a real connection → *Connection successful.* (or failure detail) |
| Dedicated + not provisioned | *Database not provisioned.* |

Audited as `TENANT_DATABASE_CONNECTION_TESTED`.

---

## 7. Safety Guards

- **Wrong mode:** provisioning refused unless `DatabaseMode == Dedicated`.
- **No name:** provisioning refused unless `DatabaseName` is set and valid.
- **Re-provisioning:** refused once `DatabaseProvisionedAtUtc` is set.
  **Force Reprovision is intentionally NOT implemented in Phase 5.0C.**
- **Injection:** database name allow-list + bracket escaping on `CREATE DATABASE`.
- **Secrets:** raw connection strings are never shown — only masked.

---

## 8. Audit Events

| Event | When |
|-------|------|
| `TENANT_DATABASE_PROVISIONED` | Successful provisioning |
| `TENANT_DATABASE_PROVISION_FAILED` | Provisioning failed (validation or runtime) |
| `TENANT_DATABASE_CONNECTION_TESTED` | Test Connection invoked |

Each entry records Tenant Id, Tenant Name, Database Name, Performed By (current user),
and IP Address.

---

## 9. Current Status

- ✅ Dedicated database can be **created**.
- ✅ Dedicated database can be **migrated** (`TenantDbContext` → `InitialTenantSchema`).
- ✅ Dedicated database can be **tested**.
- ✅ Minimal reference data is **seeded**.
- ⛔ **Routing still inactive.**

---

## 10. ⚠️ Explicit Warning

> **Phase 5.0C DOES NOT move tenant data.**
> **Phase 5.0C DOES NOT route requests.**
> **All runtime operations still use `ApplicationDbContext`.**

The dedicated database is **prepared only**. Activating per-tenant routing and migrating
data is deferred to **Phase 5.0D**.

---

## 11. Files (Phase 5.0C)

**Added**
- `Services/TenantDatabases/ITenantDatabaseProvisioningService.cs`
- `Services/TenantDatabases/TenantDatabaseProvisioningService.cs`
- `ViewModels/TenantDatabaseProvisionResultVm.cs`
- `Data/TenantDbContextDesignTimeFactory.cs`
- `Migrations/Tenant/*_InitialTenantSchema.cs` (+ designer + model snapshot)
- `docs/Phase50C_DatabaseProvisioning.md`

**Modified**
- `Data/TenantDbContext.cs` — added `RolePermissions` / `SubscriptionPlans` DbSets; ignored the `Tenant` type/navigations so a clean tenant-only schema is generated; **added explicit relationship delete-behavior config (hotfix — see §12)**.

---

## 12. Hotfix — Multiple Cascade Path Failure

**Root cause:** `TenantDbContext` left foreign-key delete behavior to EF conventions.
EF makes **non-nullable** FKs `Cascade` by default. `BranchTransfer` has two
non-nullable FKs to `Branches` (`FromBranchId`, `ToBranchId`), producing two cascade
paths to the same table — which SQL Server rejects
(*"may cause cycles or multiple cascade paths"*). A second latent conflict existed on
`SalesReturnDetail`, reachable by cascade from both `SalesReturnHeaders` (via
`SalesReturnHeader`) and `SalesDetails` (via `SalesHeader → SalesDetail`).

**Fix:** `TenantDbContext.OnModelCreating` now mirrors the proven delete behaviors of
`ApplicationDbContext`:

| Relationship | Delete behavior |
|--------------|-----------------|
| `BranchTransfer.FromBranch` / `.ToBranch` → Branch | **Restrict** (both) |
| `SalesReturnDetail` → SalesReturnHeader / SalesDetail / Item | Restrict |
| `SalesReturnHeader` → SalesHeader | Restrict |
| `SupplierPayment` → StockInHeader / Supplier | NoAction |
| Optional branch FKs (StockIn/Sales/Expense/StockAdjustment/Quotation/DR/PO) | NoAction |
| Quotation/DR → Customer, DR → SalesHeader, QuotationItem/DRItem → Item | SetNull |
| `*Item` → Header (BranchTransfer/Quotation/DR/PO) | Cascade (single path) |
| Detail → Item (Sales/StockIn/StockAdjustment/SalesReturn/BTItem/PO) | Restrict |
| CustomerLedger → Customer, PurchaseOrder → Supplier | Restrict |

No `Cascade` is used on any dual-path or self/duplicate-principal relationship.

**Other multi-path risks audited:** `UserBranches` (single Branch FK → safe),
`DeliveryReceipts` / `PurchaseOrders` (multiple distinct principals, all NoAction/SetNull),
`AuditTrails` (no FKs in tenant DB; `Tenant` ignored). No further conflicts.

**Migration:** `InitialTenantSchema` was removed and regenerated
(`20260601034922_InitialTenantSchema`). Verified: both `FK_BranchTransfers_Branches_*`
and all `FK_SalesReturnDetails_*` constraints emit `onDelete: ReferentialAction.Restrict`.

**Provisioning test:** the regenerated migration was applied to a fresh empty database
(`HardBuild_Tenant_FKTest`) via `dotnet ef database update` — **applied successfully with
no FK / cascade-path errors** — then the test database was dropped.
- `Services/TenantDatabases/ITenantDbContextFactory.cs` / `TenantDbContextFactory.cs` — added `CreateForConnection`.
- `Controllers/TenantsController.cs` — `Provision`, `TestDatabaseConnection`, enhanced `DatabaseInfo`; new diagnostic VM fields.
- `Views/Tenants/Details.cshtml` — “Dedicated Database” card + Provision/Test buttons (SweetAlert).
- `Views/Tenants/DatabaseInfo.cshtml` — Live Status section.
- `Program.cs` — registered `ITenantDatabaseProvisioningService`.
