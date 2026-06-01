# PHASE 5.0 PRE-FLIGHT — Database-Per-Tenant Architecture Assessment

**Date**: 2026-06-01  
**System**: HardBuild POS — HardwareManagementSystem SaaS  
**Type**: Read-only architecture audit  
**Scope**: Shared-DB → Database-Per-Tenant migration planning

---

## Executive Summary

The current system uses a **single shared `ApplicationDbContext`** that merges
all SaaS platform tables, ASP.NET Identity tables, and every tenant's operational
data into one SQL Server database.  Migrating to database-per-tenant requires
**splitting the context into two**, routing every request to the correct tenant
database, and stripping `TenantId` column filters from all tenant-scoped queries.

**Complexity score: HIGH** — ~38 controllers, 7+ services, 11 query-scope helpers,
and 12 open transactions all touch the single context.  No raw SQL or
`IDbContextFactory` patterns exist today.  The migration is achievable but
requires 3–4 incremental sub-phases.

**Recommendation: Incremental migration (Option B)** — see §7.

---

## 1 · DbContext Inventory

### Single ApplicationDbContext (current)

```
ApplicationDbContext : IdentityDbContext<ApplicationUser>
```

Inherits all ASP.NET Identity tables automatically:

| Identity Table | Source |
|---|---|
| `AspNetUsers` | `IdentityDbContext<ApplicationUser>` |
| `AspNetRoles` | Implicit |
| `AspNetUserRoles` | Implicit |
| `AspNetUserClaims` | Implicit |
| `AspNetUserLogins` | Implicit |
| `AspNetUserTokens` | Implicit |
| `AspNetRoleClaims` | Implicit |

### Explicit DbSets (35 total)

```
Categories          Units               Suppliers
Customers           Items               StockInHeaders
StockInDetails      SalesHeaders        SalesDetails
RolePermissions     SystemSettings      AuditTrails
StockAdjustmentHeaders  StockAdjustmentDetails
SalesReturnHeaders  SalesReturnDetails  Notifications
CustomerLedgers     SupplierPayments    Expenses
ImportBatches       ImportBatchRows     Branches
BranchProductStocks UserBranches        BranchTransfers
BranchTransferItems Tenants             SubscriptionPlans
Quotations          QuotationItems      DeliveryReceipts
DeliveryReceiptItems PurchaseOrders     PurchaseOrderItems
```

**Total tables (explicit + Identity): 42**

---

## 2 · Table Classification

### PLATFORM Database

Tables that must remain in the shared platform database because they govern
SaaS identity, billing, tenant management, or cross-tenant administration.

| Table | Reason |
|---|---|
| `Tenants` | Tenant registry — routing table, status, subscription |
| `SubscriptionPlans` | Billing plans — platform-level config |
| `AspNetUsers` (\*) | Identity — users belong to a tenant but auth happens platform-wide |
| `AspNetRoles` | Role definitions — shared across all tenants |
| `AspNetUserRoles` | User↔Role assignments |
| `AspNetUserClaims` | User claims |
| `AspNetUserLogins` | External logins |
| `AspNetUserTokens` | Password reset / 2FA tokens |
| `AspNetRoleClaims` | Role claims |
| `RolePermissions` (\*) | Per-role module permissions — tenant-specific but small |
| `AuditTrails` (\*) | Cross-tenant audit log — needed for SuperAdmin forensics |

> **(\*) Identity tables — critical design decision:**  
> `AspNetUsers` contains `TenantId` as a FK.  In DB-per-tenant, either:  
> (a) Users stay in the Platform DB and the application resolves tenant DB after auth, OR  
> (b) Users are duplicated into each tenant DB.  
> **Option (a) is strongly recommended** — it keeps login, password reset, lockout, 
> and session management in one place while tenant data lives separately.

> **RolePermissions:** Small table, tenant-scoped, but queried together with Tenants 
> for seeding.  Can live in either database; simplest to keep in Platform DB.

> **AuditTrails:** Currently has a `TenantId` column.  SuperAdmin needs cross-tenant 
> visibility.  Keep in Platform DB, or partition into a platform AuditTrail + per-tenant 
> AuditTrail.  Recommended: Platform DB only.

### TENANT Database (per-tenant isolated)

Every tenant gets their own SQL Server database containing only these tables:

| Table | Business Domain |
|---|---|
| `Categories` | Product master |
| `Units` | Product master |
| `Items` (Products) | Product master |
| `Suppliers` | Supplier management |
| `Customers` | Customer management |
| `CustomerLedgers` | AR / Receivables |
| `StockInHeaders` | Purchasing / Stock receive |
| `StockInDetails` | Purchasing |
| `SalesHeaders` | Point of Sale |
| `SalesDetails` | POS line items |
| `SalesReturnHeaders` | Sales Returns |
| `SalesReturnDetails` | Sales Returns |
| `SalesReturnHeader` | Returns |
| `StockAdjustmentHeaders` | Inventory Adjustments |
| `StockAdjustmentDetails` | Inventory Adjustments |
| `Expenses` | Expense tracking |
| `SupplierPayments` | AP / Payables |
| `Quotations` | Quotations |
| `QuotationItems` | Quotation line items |
| `DeliveryReceipts` | Delivery tracking |
| `DeliveryReceiptItems` | DR line items |
| `PurchaseOrders` | Purchase Orders |
| `PurchaseOrderItems` | PO line items |
| `Branches` | Branch configuration |
| `BranchProductStocks` | Per-branch inventory |
| `UserBranches` | User↔Branch assignments |
| `BranchTransfers` | Stock transfers |
| `BranchTransferItems` | Transfer line items |
| `Notifications` | In-app notifications |
| `ImportBatches` | Excel import history |
| `ImportBatchRows` | Excel import row detail |
| `SystemSettings` | Tenant-level configuration (logo, business name, etc.) |

### AMBIGUOUS Tables (require design decision)

| Table | Issue |
|---|---|
| `AspNetUsers` | Contains `TenantId` — lives in Platform DB but is queried by tenant context resolution |
| `RolePermissions` | Per-tenant role config, but seeded by platform tooling |
| `AuditTrails` | TenantId present — SuperAdmin needs cross-tenant view; tenant users need own-tenant view |
| `SystemSettings` | Tenant-scoped settings — queried from both platform (tenant create) and tenant operations |
| `Notifications` | Tenant-scoped but may include broadcast (TenantId = null) platform notifications |

---

## 3 · Files Requiring Modification

### Controllers (35 files with direct injection + 2 via services = 37 total affected)

35 controllers inject `ApplicationDbContext` directly.  Two do not inject it directly:
- `UsersController` — uses `UserManager<ApplicationUser>` only (still needs platform context via Identity)
- `NotificationsController` — delegates entirely to `NotificationService`

Controllers that currently touch BOTH platform and tenant data (hardest to split):

| Controller | Mixed Context Reason | Complexity |
|---|---|---|
| `TenantsController` | Creates Tenant (platform) + SystemSettings (tenant) + user provisioning | HIGH |
| `SuperAdminController` | Aggregates `Branches`/`Items` counts across ALL tenants — multi-DB fan-out needed | HIGH |
| `HomeController` | Dashboard aggregates Tenants (platform) + all operational tables | HIGH |
| `UsersController` | `AspNetUsers` (platform Identity) + `UserBranches` (tenant) | HIGH |
| `UserBranchesController` | `UserBranches`/`Branches` (tenant) + `UserManager.Users` (platform) | MEDIUM |
| `SettingsController` | `SystemSettings` (tenant) + logo upload | MEDIUM |
| `RolePermissionsController` | `RolePermissions` (platform) + role lookup | MEDIUM |
| `BranchesController` | `Branches` (tenant) + tenant limit check via platform | MEDIUM |
| `AuditTrailController` | `AuditTrails` — destination DB depends on routing decision | MEDIUM |
| All remaining operational controllers (~26) | Pure tenant data — replace `_context` with `_tenantContext` | LOW |

**TenantId references in controllers — 643 confirmed occurrences across 37 files:**

| File | TenantId Count | Complexity |
|---|---|---|
| `UsersController.cs` | 40 | HIGH |
| `PurchaseOrdersController.cs` | 43 | HIGH |
| `ReportsController.Inventory.cs` | 32 | HIGH |
| `QuotationsController.cs` | 32 | HIGH |
| `UserBranchesController.cs` | 33 | HIGH |
| `BranchTransfersController.cs` | 30 | MEDIUM |
| `InventoryController.cs` | 25 | MEDIUM |
| `ProductsController.cs` | 24 | MEDIUM |
| `BranchesController.cs` | 24 | MEDIUM |
| `CustomersController.cs` | 24 | MEDIUM |
| `SuppliersController.cs` | 29 | MEDIUM |
| `DeliveryReceiptsController.cs` | 27 | MEDIUM |
| `ReportsController.TenantScope.cs` | 26 | HIGH (11 helper overloads) |
| `StockInController.cs` | 22 | MEDIUM |
| `StockAdjustmentController.cs` | 22 | MEDIUM |
| `SupplierPaymentsController.cs` | 18 | MEDIUM |
| `HomeController.cs` | 18 | HIGH |
| `SalesReturnController.cs` | 20 | MEDIUM |
| `InventoryMovementController.cs` | 11 | LOW |
| `ImportController.cs` | 11 | LOW |
| (others) | < 10 | LOW |

### Services (8 files — ALL require changes)

| Service | DbContext Usage | Migration Complexity |
|---|---|---|
| `TenantLimitGuard.cs` | Reads `Tenants`+`SubscriptionPlans` (platform) + `Branches`/`Items` (tenant) + `UserManager` | **HIGH** — must hold refs to both contexts |
| `ExcelImportService.cs` | ~27 context calls — pure tenant data (Items, Categories, etc.) | MEDIUM |
| `BranchService.cs` | `Branches`, `BranchProductStocks`, `UserBranches` + `BeginTransactionAsync` (all tenant) | MEDIUM |
| `AuditService.cs` | Writes `AuditTrails` — destination depends on routing decision | MEDIUM |
| `NotificationService.cs` | Reads/writes `Notifications` (tenant, incl. null-TenantId broadcast) | LOW |
| `PermissionService.cs` | Reads `RolePermissions` (platform) | LOW — becomes pure platform |
| `TenantService.cs` | Reads `Tenants` only (platform) | LOW — pure platform service |
| `TenantStatusFilter.cs` | Reads `AspNetUsers`+`Tenants` (both platform) | LOW — becomes pure platform |

### Data Layer (3 files)

| File | Change Required |
|---|---|
| `ApplicationDbContext.cs` | Split into `PlatformDbContext` + `TenantDbContext` |
| `Data/Seeders/DbSeeder.cs` | Update to seed platform DB only; tenant seeding becomes part of tenant provisioning |
| `Data/DevelopmentDataResetService.cs` | **HIGH** — ~80 context calls spanning both platform users and all tenant operational data; needs dual-context overhaul |

### Migrations (current: 17 migrations, single chain)

- All 17 migrations must be **preserved** for existing databases during a migration window.
- New architecture needs **two separate migration chains**:  
  `Migrations/Platform/` and `Migrations/Tenant/`.
- Existing single-database deployments require a **data export/import** step between old and new schemas.

---

## 4 · TenantId Filtering Analysis

### Current ApplyTenantScope Helpers (11 overloads)

All located in `Controllers/ReportsController.TenantScope.cs`:

| Entity | Filter pattern | Notes |
|---|---|---|
| `SalesHeader` | `x.TenantId == tenantId \|\| x.TenantId == null` | Direct column |
| `Item` | Same | Direct column |
| `Expense` | Same | Direct column |
| `StockInHeader` | Same | Direct column |
| `BranchTransfer` | Same | Direct column |
| `BranchProductStock` | Same | Direct column |
| `Customer` (via `ApplyCustomerTenantScope`) | Same | Direct column |
| `CustomerLedger` | Correlated subquery via `Customers.Any(c.TenantId)` | **No direct TenantId column** |
| `SalesReturnHeader` | Correlated via `SalesHeaders.Any(s.TenantId)` | **No direct TenantId column** |
| `SalesDetail` | Correlated via `SalesHeaders.Any(s.TenantId)` | **No direct TenantId column** |
| `SupplierPayment` | Correlated via `StockInHeaders.Any(s.TenantId)` | **No direct TenantId column** |
| `BranchTransferItem` | Correlated via `BranchTransfers.Any(t.TenantId)` | **No direct TenantId column** |

**In DB-per-tenant:**
- All 11 `ApplyTenantScope` overloads can be **deleted entirely** — the tenant database itself is the scope.
- The `GetReportTenantIdAsync()` helper becomes unnecessary.
- `ReportsController.TenantScope.cs` file can be deleted.
- `~65` call sites across ReportsController partials are eliminated.

### Global TenantId Filter Count (verified)

| Location | Confirmed TenantId uses | Removable post-migration? |
|---|---|---|
| Controllers (37 files) | **643** references | **YES** — all column filters on tenant tables |
| Services (8 files) | **90** references | **PARTIAL** — those querying `Tenants`/platform tables stay |
| Data layer | **98** references | **PARTIAL** — indexes/configs in ApplicationDbContext |
| Models (21 entities) | **22** declarations | **KEEP** on Platform tables; **REMOVE** from Tenant tables |
| `ApplyTenantScope` call sites | **~65** | **DELETE** — entire file obsolete |
| Explicit `.Where(TenantId…)` lines | **~88** | **DELETE** — replaced by DB isolation |
| **Total `TenantId ==` comparisons** | **~302** | Most removable after DB-per-tenant cut-over |

### Services That Become Obsolete or Simpler

| Service / Class | Current Role | Post-Migration Status |
|---|---|---|
| `TenantGuard` | Validates TenantId access on every entity | **OBSOLETE** — DB isolation replaces per-row checking |
| `TenantContext` / `ITenantContext` | Resolves current user's TenantId | **TRANSFORMS** → `ITenantDatabaseResolver` (resolves connection string) |
| `TenantStatusFilter` | Checks Tenant.Status per request | **KEPT** — still needs to block suspended tenants at login |
| `ApplyTenantScope` helpers (×11) | Column-level row filtering | **DELETED** |
| `TenantService.GetCurrentTenantAsync()` | Fetches Tenant row by ID | **KEPT** — reads from Platform DB |
| `TenantLimitGuard` | Cross-context limit checks | **REFACTORED** — must hold refs to both PlatformDb + TenantDb |

---

## 5 · Target Architecture Design

### Recommended Context Split

```
PlatformDbContext : IdentityDbContext<ApplicationUser>
│
├─ DbSet<Tenant>
├─ DbSet<SubscriptionPlan>
├─ DbSet<RolePermission>
├─ DbSet<AuditTrail>          ← platform-level audit
├─ DbSet<Notification>        ← broadcast notifications (TenantId = null rows)
│
└─ [All AspNetIdentity tables via IdentityDbContext<ApplicationUser>]


TenantDbContext : DbContext
│
├─ DbSet<Category>
├─ DbSet<Unit>
├─ DbSet<Item>
├─ DbSet<Supplier>
├─ DbSet<Customer>
├─ DbSet<CustomerLedger>
├─ DbSet<StockInHeader> + StockInDetail
├─ DbSet<SalesHeader> + SalesDetail
├─ DbSet<SalesReturnHeader> + SalesReturnDetail
├─ DbSet<StockAdjustmentHeader> + StockAdjustmentDetail
├─ DbSet<Expense>
├─ DbSet<SupplierPayment>
├─ DbSet<Quotation> + QuotationItem
├─ DbSet<DeliveryReceipt> + DeliveryReceiptItem
├─ DbSet<PurchaseOrder> + PurchaseOrderItem
├─ DbSet<Branch>
├─ DbSet<BranchProductStock>
├─ DbSet<UserBranch>
├─ DbSet<BranchTransfer> + BranchTransferItem
├─ DbSet<Notification>        ← tenant-scoped notifications
├─ DbSet<ImportBatch> + ImportBatchRow
├─ DbSet<SystemSetting>
└─ DbSet<AuditTrail>          ← optional: per-tenant audit copy
```

### ITenantDatabaseResolver

```csharp
public interface ITenantDatabaseResolver
{
    /// <summary>
    /// Returns the connection string for the current tenant's database.
    /// Called once per HTTP request after authentication.
    /// </summary>
    Task<string> ResolveConnectionStringAsync(int tenantId);
}
```

**Implementation options:**

| Option | Mechanism | Notes |
|---|---|---|
| A. Config-file map | `appsettings.json` keyed by tenantId | Simple; requires config redeploy to add tenants |
| B. Platform DB lookup | `SELECT ConnectionString FROM Tenants WHERE Id = @id` | Dynamic; `Tenants` row stores per-tenant connection string |
| C. Convention-based | `Server=...; Database=Tenant_{tenantId};` | Simplest; requires all DBs on same SQL Server instance |

**Recommendation: Option B** for production flexibility.

> **Important:** The current `Tenant` model has **no `ConnectionString` field**. Adding  
> `public string? ConnectionString { get; set; }` to `Models/Tenant.cs` is a  
> mandatory prerequisite for Phase 5.0-B.

### TenantDbContextFactory

```csharp
public class TenantDbContextFactory
{
    private readonly ITenantDatabaseResolver _resolver;
    private readonly ITenantContext _tenantContext;

    public async Task<TenantDbContext> CreateAsync()
    {
        var tenantId = _tenantContext.CurrentTenantId
            ?? throw new InvalidOperationException("No tenant context.");

        var connStr = await _resolver.ResolveConnectionStringAsync(tenantId);

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer(connStr)
            .Options;

        return new TenantDbContext(options);
    }
}
```

### DI Registration Pattern

```csharp
// Platform context — registered normally (connection string from config)
builder.Services.AddDbContext<PlatformDbContext>(opts =>
    opts.UseSqlServer(config["ConnectionStrings:Platform"]));

// Tenant context — resolved dynamically per request
builder.Services.AddScoped<TenantDbContextFactory>();
builder.Services.AddScoped<ITenantDatabaseResolver, SqlTenantDatabaseResolver>();

// Identity uses PlatformDbContext
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<PlatformDbContext>();
```

### Tenant Provisioning Flow (automated)

```
SuperAdmin: Create Tenant
↓
1. Insert Tenant row in Platform DB
2. Create new SQL Server database (e.g. "HardBuild_Tenant_42")
3. Apply TenantDbContext migrations
4. Seed default data (SystemSettings, demo categories/units, etc.)
5. Update Tenant.ConnectionString in Platform DB
6. Create TenantAdmin user in Platform.AspNetUsers
7. Seed RolePermissions for new tenant
```

---

## 6 · Migration Complexity Estimate

### Overall Score: **HIGH**

| Dimension | Score | Reasoning |
|---|---|---|
| Code volume | HIGH | ~38 controllers, 7 services, all touch single context |
| Data migration | HIGH | All existing tenant data must be split into separate DBs |
| Transaction isolation | MEDIUM | All current transactions are within tenant data only (no cross-tenant transactions found) — safe to move |
| Identity complexity | HIGH | `AspNetUsers.TenantId` creates a cross-DB FK conceptually |
| EF Core migrations | HIGH | Two separate migration chains needed; existing chain cannot be re-used |
| Testing surface | HIGH | Every controller action needs regression testing |

### Files Affected Summary

| Category | File Count | Avg Effort per File |
|---|---|---|
| Controllers (pure tenant — LOW) | ~26 | 2–3 hours — swap `_context` with tenant factory |
| Controllers (platform-mixed — HIGH) | ~9 | 4–8 hours — split context usage, handle dual injection |
| Services (8 files) | 8 | 1–6 hours each |
| DbContext split + new infrastructure | 3 new files (Platform/TenantDbContext + Resolver) | 8–16 hours |
| `ApplicationDbContext.cs` refactor | 1 | 4–6 hours |
| Migrations (two new chains) | 2 new chains | 4–8 hours |
| `DevelopmentDataResetService.cs` | 1 | 4–6 hours |
| DbSeeder + Program.cs DI | 2 | 4–6 hours |
| Add `Tenant.ConnectionString` + migration | 1 | 1–2 hours |
| **Total confirmed files** | **47 + 3 new** | **~120–200 hours** |

### Risk Areas

| Risk | Severity | Notes |
|---|---|---|
| `TenantLimitGuard` cross-context queries | HIGH | Must count Branches/Items (tenant) AND check SubscriptionPlan (platform) in same service call |
| `AspNetUsers.TenantId` FK becomes logical-only | HIGH | EF FK cannot span databases; no DB-enforced FK possible after split |
| `UserBranch` → `AspNetUsers` relationship | HIGH | FK from tenant DB to platform DB — becomes application-enforced only |
| `SuperAdminController` cross-tenant aggregation | HIGH | Currently queries ALL tenants' `Branches`/`Items` from one DB — needs per-DB fan-out or cached metrics |
| `Tenant` model missing `ConnectionString` field | HIGH | Must be added before routing is possible — schema + migration required |
| `AuditTrail` + `Notification` have no `HasOne<Tenant>()` FK | MEDIUM | Currently TenantId is index-only; routing decision needed for these tables |
| Notification broadcast (`TenantId = null` rows) | MEDIUM | NULL-tenant broadcast rows need to live in platform DB; tenant reads must still see them |
| `DevelopmentDataResetService` spans both contexts | MEDIUM | ~80 context calls; resets both platform users and tenant data — needs dual-context refactor |
| SQL Express 10 GB limit per DB | MEDIUM | This is the primary motivation; each tenant now gets own 10 GB envelope |
| Transaction scope across platform + tenant | LOW | **No such transactions exist today** — safe to split |
| Raw SQL | LOW | **None found** — clean LINQ throughout; zero migration risk |

### Estimated Phases

| Phase | Duration | Work |
|---|---|---|
| 5.0-A: Context split (no DI change) | 2–3 weeks | Create `PlatformDbContext` + `TenantDbContext` as code-only; both target same DB initially |
| 5.0-B: Factory + resolver | 1–2 weeks | Implement `ITenantDatabaseResolver`, `TenantDbContextFactory`, per-tenant DB provisioning |
| 5.0-C: Controller migration | 3–4 weeks | Replace `_context` with correct context in all 38 controllers; remove `ApplyTenantScope` helpers |
| 5.0-D: Service migration | 1–2 weeks | Refactor `TenantLimitGuard`, `BranchService`, `AuditService`, `NotificationService` |
| 5.0-E: Data migration | 1–2 weeks | Script to extract each tenant's data from shared DB into individual tenant DBs |
| 5.0-F: Migration chain split | 1 week | Create separate migration histories; update CI/CD |
| **Total estimate** | **9–14 weeks** | With 1 senior developer |

---

## 7 · Recommended Migration Path

### ✅ Option B — Incremental Migration (RECOMMENDED)

**Reasoning against Option A (Big-Bang):**
- 50 files, 670+ TenantId references, 37 DbSets cannot be safely migrated atomically
- Big-bang requires all tests to pass at once before any deployment
- A single rollback reverts everything — very high risk for live pilot tenants

**Incremental Migration Plan:**

```
Phase 5.0-A  ──  Create both contexts (dual-target same DB)
                 Zero user-facing change; purely structural
                 Run existing EF migrations against both contexts (both see same DB)
                 Full regression testing with no behavioral change

Phase 5.0-B  ──  Implement ITenantDatabaseResolver + TenantDbContextFactory
                 Default resolver returns shared DB connection string (no change yet)
                 Tenant provisioning creates individual DBs in background
                 New tenants get own DB; existing tenants stay on shared DB

Phase 5.0-C  ──  Migrate controllers in batches (5–7 per sprint)
                 Start with pure-tenant controllers (easiest: Categories, Units, Expenses)
                 End with mixed-context controllers (Users, Home, Tenants)
                 Each controller batch can be deployed and tested independently

Phase 5.0-D  ──  Migrate existing tenant data
                 Per-tenant: export all tenant rows → import into dedicated DB → verify → cut over
                 Can be done tenant-by-tenant (zero downtime per tenant with brief maintenance window)
                 Fallback: keep shared DB as read replica for 30 days post-migration

Phase 5.0-E  ──  Remove TenantId columns from TenantDbContext tables
                 Add NOT NULL constraints and remove legacy || TenantId == null filters
                 Final cleanup of TenantGuard, ApplyTenantScope helpers
```

**Key safety property of Option B:**
During Phases 5.0-A through 5.0-C, the system remains fully functional on the
shared database.  The context split is a code change only.  Individual tenants
can be migrated to dedicated databases one at a time in Phase 5.0-D without
affecting other tenants.

---

## 8 · Output Summary

### 1. Platform Tables (11)
`Tenants`, `SubscriptionPlans`, `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`,
`AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims`,
`RolePermissions`, `AuditTrails`

### 2. Tenant Tables (30)
`Categories`, `Units`, `Items`, `Suppliers`, `Customers`, `CustomerLedgers`,
`StockInHeaders`, `StockInDetails`, `SalesHeaders`, `SalesDetails`,
`SalesReturnHeaders`, `SalesReturnDetails`, `StockAdjustmentHeaders`,
`StockAdjustmentDetails`, `Expenses`, `SupplierPayments`, `Quotations`,
`QuotationItems`, `DeliveryReceipts`, `DeliveryReceiptItems`, `PurchaseOrders`,
`PurchaseOrderItems`, `Branches`, `BranchProductStocks`, `UserBranches`,
`BranchTransfers`, `BranchTransferItems`, `Notifications`, `ImportBatches`,
`ImportBatchRows`, `SystemSettings`

### 3. Files Impacted
- **Controllers**: 35 files with direct injection + 2 via services = 37 affected
- **Services**: 8 files (all, confirmed — includes `ExcelImportService`)
- **Data layer**: 4 files (`ApplicationDbContext.cs`, `DbSeeder.cs`, `DevelopmentDataResetService.cs`, `Program.cs`)
- **Migrations**: New dual migration chains required + `Tenant.ConnectionString` migration
- **Total confirmed**: 47 existing files + 3 new infrastructure files

### 4. Architectural Risks

| Risk | Severity |
|---|---|
| `UserBranch` cross-DB FK (tenant↔platform user) | HIGH |
| `TenantLimitGuard` dual-context queries | HIGH |
| `SuperAdminController` cross-tenant aggregation → multi-DB fan-out | HIGH |
| `Tenant` model missing `ConnectionString` field (blocker for routing) | HIGH |
| Data migration window for existing pilot tenants | HIGH |
| `DevelopmentDataResetService` spans both contexts | MEDIUM |
| AuditTrail routing decision | MEDIUM |
| `AspNetUsers.TenantId` becomes application-enforced only | MEDIUM |
| Notification TenantId=null broadcast rows | MEDIUM |

### 5. Complexity Score: **HIGH**
~50 files, 120–200 developer-hours, 9–14 week timeline with 1 senior developer.

### 6. Recommended Migration Path: **Option B — Incremental**
Five ordered sub-phases.  Zero downtime per tenant.  Reversible at any stage
before Phase 5.0-D (data cut-over).

---

*End of Phase 5.0 Pre-Flight Architecture Assessment*
