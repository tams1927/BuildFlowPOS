# Phase 5.0 — Database-Per-Tenant Architecture Plan

**System**: HardBuild POS — HardwareManagementSystem SaaS  
**Date**: 2026-06-01  
**Status**: Architecture design. Phase 5.0A implementation in progress.

---

## 1. Current Architecture

```
Single SQL Server Database
         │
ApplicationDbContext
(IdentityDbContext<ApplicationUser>)
         │
  ┌──────┴──────────────────────────────┐
  │  Platform Tables                    │
  │  • Tenants                          │
  │  • SubscriptionPlans                │
  │  • RolePermissions                  │
  │  • AspNetUsers (TenantId column)    │
  │  • AspNetRoles / Identity tables    │
  ├─────────────────────────────────────┤
  │  Tenant Operational Tables (×31)    │
  │  • Items, Sales, Customers, etc.    │
  │  • Isolated by TenantId column      │
  │  • TenantGuard + ApplyTenantScope   │
  └─────────────────────────────────────┘

Isolation: Row-level (WHERE TenantId = @tenantId)
Risk:      SQL query bug → data leak across tenants
Limit:     SQL Express 10 GB shared by ALL tenants
```

**Current isolation mechanism:**
- `ITenantContext` resolves `CurrentTenantId` from `ApplicationUser.TenantId`
- `TenantGuard` validates per-row tenant ownership on mutations
- `ApplyTenantScope` applies `WHERE TenantId = X OR TenantId IS NULL` on all read queries
- `TenantStatusFilter` blocks suspended/expired tenants at request-time

---

## 2. Target Architecture

```
Platform Database (shared)           Tenant Database (per tenant)
─────────────────────────            ──────────────────────────────
PlatformDbContext                    TenantDbContext
  Tenants ──────────────────────────► ConnectionString field
  SubscriptionPlans                    (resolved by ITenantDatabaseResolver)
  RolePermissions
  AspNetUsers (Identity)              TenantDbContext
  AspNetRoles                           Items, Sales, Customers
  Identity tables                       Suppliers, Branches
  AuditTrail (platform)                 PurchaseOrders, etc.
  Notification (broadcast)              No TenantId filters needed
                                        One DB = One Tenant's data
```

**Request flow (Phase 5.0B+):**

```
HTTP Request
     │
     ▼
Authentication (PlatformDbContext)
     │  user.TenantId
     ▼
ITenantDatabaseResolver
     │  "Server=...; Database=HardBuild_Tenant_42;"
     ▼
TenantDbContextFactory
     │  creates TenantDbContext with tenant connection string
     ▼
Controller / Service
     │  uses TenantDbContext (no WHERE TenantId filters)
     ▼
Tenant SQL Server Database
```

---

## 3. Platform DB Responsibilities

The Platform Database is the source of truth for SaaS management.  
It is **never** handed to a tenant application context.

| Responsibility | Tables / Services |
|---|---|
| Tenant registry | `Tenants` |
| Subscription billing | `SubscriptionPlans`, `Tenant.SubscriptionPlanId` |
| Tenant status management | `Tenant.Status`, `Tenant.ExpirationDate` |
| Tenant limits enforcement | `Tenant.MaxBranches`, `Tenant.MaxUsers`, `Tenant.MaxProducts` |
| Tenant connection routing | `Tenant.ConnectionString` *(added in Phase 5.0B)* |
| User identity + authentication | `AspNetUsers`, all `AspNetNet*` tables |
| Role definitions | `AspNetRoles` |
| Permission matrix | `RolePermissions` |
| Global audit | `AuditTrails` (platform-scope entries) |
| Platform broadcasts | `Notifications` (TenantId = null) |
| SuperAdmin dashboard | Queries Platform DB only |
| Future billing/invoicing | Will extend Platform DB |
| Future payment gateways | Platform DB — never touches tenant data |

---

## 4. Tenant DB Responsibilities

Each tenant database is fully isolated and contains only that tenant's operational data.

| Responsibility | Tables |
|---|---|
| Product catalog | `Categories`, `Units`, `Items` |
| Supplier management | `Suppliers` |
| Purchasing | `StockInHeaders`, `StockInDetails`, `PurchaseOrders`, `PurchaseOrderItems`, `SupplierPayments` |
| Customer management | `Customers`, `CustomerLedgers` |
| Sales / POS | `SalesHeaders`, `SalesDetails`, `SalesReturnHeaders`, `SalesReturnDetails` |
| Quotations | `Quotations`, `QuotationItems`, `DeliveryReceipts`, `DeliveryReceiptItems` |
| Inventory adjustments | `StockAdjustmentHeaders`, `StockAdjustmentDetails` |
| Branch management | `Branches`, `BranchProductStocks`, `UserBranches`, `BranchTransfers`, `BranchTransferItems` |
| Expenses | `Expenses` |
| Tenant branding/settings | `SystemSettings` |
| Data import history | `ImportBatches`, `ImportBatchRows` |
| Operational notifications | `Notifications` (TenantId set) |
| Tenant-level audit | `AuditTrails` (TenantId set) |

---

## 5. Identity Strategy

**Decision: Centralized Identity (Platform DB)**

ASP.NET Identity remains in `PlatformDbContext` for Phase 5.0A through Phase 5.0C.

**Rationale:**
- Login, password reset, lockout, and 2FA tokens all need a single source of truth
- Splitting Identity requires replicating users into each tenant DB — complex + risky
- `ApplicationUser.TenantId` is used after authentication to route to the correct tenant DB

**Cross-DB relationship:**  
`UserBranch.UserId` → `AspNetUsers.Id` becomes **application-enforced** in Phase 5.0C.  
EF Core cannot enforce FKs across databases. The `TenantDbContext.UserBranches` DbSet  
documents this via XML comments. Validation happens in `UsersController` / `UserBranchesController`.

**Future (Phase 5.0D+):**  
Optionally replicate tenant users into tenant DBs if tenants want full data portability.  
This is explicitly deferred and not required for multi-DB functionality.

---

## 6. Tenant Provisioning Strategy

**When a new tenant signs up (Phase 5.0B+):**

```
1. SuperAdmin creates Tenant in Platform DB (TenantsController.Create)
       ↓
2. Platform creates a new SQL Server database
   e.g.: HardBuild_Tenant_{tenantId}
       ↓
3. Apply TenantDbContext migrations to the new database
   (EF Core: context.Database.MigrateAsync())
       ↓
4. Seed default tenant data:
   - Default SystemSettings row
   - Optional: starter Categories, Units
       ↓
5. Store connection string on Tenant.ConnectionString (Platform DB)
       ↓
6. Create TenantAdmin user in AspNetUsers (Platform DB)
       ↓
7. Seed RolePermissions for this tenant (Platform DB or replicated to Tenant DB)
```

**Database naming convention:**
```
HardBuild_Platform           (shared — always exists)
HardBuild_Tenant_1           (Tenant ID 1)
HardBuild_Tenant_2           (Tenant ID 2)
HardBuild_Tenant_{N}         (Tenant ID N)
```

---

## 7. Migration Strategy

### Current state
- Single `ApplicationDbContext` migration chain with 17 migrations
- All migrations must be preserved for existing shared-DB deployments

### Future state (two migration chains)
```
Migrations/
├── Platform/              ← PlatformDbContext migrations
│   └── 20260601000001_InitialPlatform.cs
│   └── __PlatformMigrationsHistory
└── Tenant/                ← TenantDbContext migrations
    └── 20260601000001_InitialTenant.cs
    └── __TenantMigrationsHistory
```

### Migration phases

| Phase | Migration Action |
|---|---|
| 5.0A | No migration changes. ApplicationDbContext chain preserved. |
| 5.0B | Add `ConnectionString` column to `Tenants` table (single migration on existing chain or on new PlatformDbContext chain) |
| 5.0C | Create TenantDbContext migration chain starting from current schema |
| 5.0D | Create PlatformDbContext migration chain starting from current schema |
| 5.0E | Data cut-over; after migration, drop TenantId columns from tenant tables (tenant DB only) |

### Existing tenant data migration script (Phase 5.0E)
```sql
-- Per-tenant migration script (run once per tenant)
-- 1. Create HardBuild_Tenant_{id} database
-- 2. Apply TenantDbContext migrations
-- 3. INSERT INTO HardBuild_Tenant_{id}.[dbo].[Items]
--    SELECT * FROM HardBuild_Shared.[dbo].[Items]
--    WHERE TenantId = {id}
-- 4. Repeat for all 31 tenant tables
-- 5. Verify counts
-- 6. Update Tenant.ConnectionString
-- 7. Validate application smoke test
-- 8. Delete tenant rows from shared DB (or keep read-only for 30 days)
```

---

## 8. Backup Strategy

### Platform DB
- Full backup: daily
- Differential: every 4 hours
- Transaction log: every 15 minutes
- Retention: 30 days
- Hosting: Separate SQL Server instance or Azure SQL

### Tenant DB (per tenant)
- Full backup: daily
- Differential: every 6 hours
- Transaction log: every 30 minutes
- Retention: configurable per plan (e.g., Basic: 7 days, Pro: 30 days)
- Automation: SQL Server Agent job per tenant database

### SQL Express Note
SQL Server Express has a **10 GB per database limit**.  
In the shared-DB model, **all tenants share one 10 GB envelope**.  
In database-per-tenant, **each tenant gets their own 10 GB**.  
A 50-tenant installation currently limited to 10 GB total becomes 500 GB total capacity.  
This is the primary technical justification for the migration.

---

## 9. Restore Strategy

### Platform DB restore
- Restore to point-in-time from transaction logs
- Critical: restoring Platform DB restores all tenant routing data
- Tenant databases remain intact during a Platform-only restore

### Tenant DB restore
- Restore individual tenant database without affecting other tenants
- Point-in-time restore for compliance/dispute resolution
- Tenant-initiated: via SuperAdmin portal (future feature)

### Partial restore (row-level)
- For accidental data deletion: restore to staging, extract affected rows, apply to production
- Enabled by per-tenant isolation — impossible to do in shared-DB model

---

## 10. Connection Resolver Concept

The `ITenantDatabaseResolver` is the heart of the database-per-tenant architecture.  
It runs on every request after authentication.

```csharp
// Defined in Phase 5.0B: Services/ITenantDatabaseResolver.cs
public interface ITenantDatabaseResolver
{
    Task<string> ResolveConnectionStringAsync(int tenantId);
}

// Implementation: reads Tenant.ConnectionString from Platform DB
public class SqlTenantDatabaseResolver : ITenantDatabaseResolver
{
    private readonly PlatformDbContext _platformContext;
    private readonly IMemoryCache _cache;

    public async Task<string> ResolveConnectionStringAsync(int tenantId)
    {
        // Cache for 5 minutes — avoids Platform DB query on every request
        return await _cache.GetOrCreateAsync(
            $"tenant_conn_{tenantId}",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                var tenant = await _platformContext.Tenants
                    .AsNoTracking()
                    .Where(t => t.Id == tenantId)
                    .Select(t => t.ConnectionString)
                    .FirstOrDefaultAsync();

                return tenant ?? throw new InvalidOperationException(
                    $"No connection string found for tenant {tenantId}.");
            });
    }
}

// TenantDbContextFactory — creates a scoped TenantDbContext per request
public class TenantDbContextFactory
{
    private readonly ITenantDatabaseResolver _resolver;
    private readonly ITenantContext _tenantContext;

    public async Task<TenantDbContext> CreateAsync()
    {
        var tenantId = _tenantContext.CurrentTenantId
            ?? throw new InvalidOperationException("No tenant context for this request.");

        var connStr = await _resolver.ResolveConnectionStringAsync(tenantId);

        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlServer(connStr)
            .Options;

        return new TenantDbContext(options);
    }
}
```

**Caching strategy:**  
Connection strings are cached per tenant for 5 minutes. Invalidated when Tenant record is updated.  
This prevents a Platform DB roundtrip on every HTTP request.

---

## 11. SQL Express 10 GB Justification

| Model | Capacity | Growth |
|---|---|---|
| Shared DB (current) | 10 GB total across ALL tenants | Hard ceiling — affects all tenants |
| Database-Per-Tenant | 10 GB per tenant | Each new tenant adds 10 GB capacity |
| SQL Server Standard | Unlimited | Used when any single tenant exceeds 10 GB |

**SQL Express is viable per tenant** for small hardware POS businesses:
- A typical retail location generates ~5–50 MB of transaction data per month
- A 100-transaction/day store generates ~18–180 MB/year
- At that rate, SQL Express gives 5–10 years of headroom per tenant

**When to upgrade a tenant from SQL Express to SQL Standard:**
- Database size approaches 8 GB (80% threshold)
- `TenantLimitGuard` can surface this as a warning
- Upgrade path: detach Express DB, attach to Standard instance — zero data loss

---

## 12. Future SaaS Growth Plan

| Growth Phase | Architecture |
|---|---|
| 1–20 tenants | All databases on one SQL Server Express instance |
| 20–100 tenants | Migrate busy tenants to SQL Server Standard; keep small tenants on Express |
| 100–500 tenants | Dedicated SQL Server per customer tier; load balancing |
| 500+ tenants | Azure SQL Elastic Pools; auto-scaling per tenant |
| Enterprise | Database sharding; read replicas; geo-replication |

**Platform DB always remains a single high-availability instance** (read replicas for reporting).

---

## 13. Risks

| Risk | Severity | Description |
|---|---|---|
| Connection string exposure | HIGH | Connection strings must never be logged or returned to the client |
| Tenant DB misconfiguration | HIGH | Wrong connection string routes a user to wrong tenant's data |
| `UserBranch.UserId` cross-DB | HIGH | No DB-enforced FK — must validate in application code |
| Platform DB single point of failure | HIGH | All logins and tenant routing fail if Platform DB is unavailable |
| `TenantLimitGuard` dual-context | HIGH | Must query both Platform (limits) and Tenant (counts) DBs — needs careful design |
| SuperAdmin cross-tenant reports | HIGH | SuperAdmin dashboard currently runs a single cross-tenant query — must be rebuilt as fan-out or use cached metrics |
| Data migration window | MEDIUM | Existing tenants require a brief maintenance window per-tenant |
| TenantId = null legacy rows | MEDIUM | Must be cleaned up before dropping TenantId column filters |
| AuditTrail routing | MEDIUM | Decide: platform-only, tenant-only, or dual-write |
| Notification broadcast rows | MEDIUM | TenantId = null rows must be split to Platform DB before tenant DB migration |

---

## 14. Mitigation Plan

| Risk | Mitigation |
|---|---|
| Connection string exposure | Store in `Tenant.ConnectionString` (encrypted column). Never expose via API. |
| Wrong connection string | Validate tenant ID in TenantDbContextFactory. Log every connection resolution. |
| `UserBranch` orphaned users | Add application-level validation in UsersController + UserBranchesController |
| Platform DB availability | SQL Always On / Azure SQL zone redundancy for Platform DB |
| `TenantLimitGuard` dual-context | Refactor to accept `PlatformDbContext` + `TenantDbContext` separately |
| SuperAdmin cross-tenant | Build SuperAdmin metrics as cached aggregates updated asynchronously per tenant |
| Data migration window | Per-tenant blue/green migration; keep shared DB as read replica for 30 days |
| TenantId = null rows | Add validation script in Phase 5.0E to reject null-TenantId rows before cut-over |
| AuditTrail routing | Default to dual-write (platform + tenant) during transition; clean up post-migration |
| Notification broadcast | Keep in Platform DB; push to tenant DBs via background service or SignalR |

---

## Phase Roadmap

### Phase 5.0A — Architecture Foundation (CURRENT)
**Goal:** Prepare codebase without runtime changes.  
**Deliverables:**
- `IPlatformEntity`, `ITenantEntity`, `IBranchEntity` marker interfaces
- `PlatformDbContext` (compilation only)
- `TenantDbContext` (compilation only)
- Table classification documentation
- Architecture plan documentation
- Impact analysis

**Constraints:** Zero runtime changes. Zero schema changes. Build must pass.

---

### Phase 5.0B — Connection Resolver Layer ✅ COMPLETE (2026-06-01)

**Goal:** Implement the infrastructure for per-tenant database connection routing.  
**No tenant databases created yet. All tenants still use shared DB.**

**Completed work:**
1. ✅ Added 6 database-routing fields to `Tenant` model: `DatabaseMode`, `DatabaseName`, `ConnectionString`, `DatabaseServer`, `LastDatabaseMigration`, `DatabaseProvisionedAtUtc`
2. ✅ Created `TenantDatabaseMode` enum (`Shared` = 0 default, `Dedicated` = 1)
3. ✅ Created `Services/TenantDatabases/ITenantDatabaseResolver.cs` with `GetConnectionStringAsync`, `GetDatabaseNameAsync`, `UsesDedicatedDatabaseAsync`
4. ✅ Created `Services/TenantDatabases/TenantDatabaseResolver.cs` — reads `ApplicationDbContext`, 5-min `IMemoryCache`, connection-string masking, `DefaultConnection` fallback for Shared mode
5. ✅ Created `Services/TenantDatabases/ITenantDbContextFactory.cs` + `TenantDbContextFactory.cs`
6. ✅ Registered both services as scoped in `Program.cs` (did NOT replace `ApplicationDbContext`)
7. ✅ SuperAdmin UI: Details DB card, Edit advanced section (mode + name only), `/Tenants/DatabaseInfo/{id}` diagnostic
8. ✅ Migration `AddTenantDatabaseRouting` — existing tenants default to Shared
9. ✅ Build: 0 errors, 0 warnings. Runtime behaviour unchanged.

**Key outcomes:**
- `ApplicationDbContext` remains the only runtime context.
- The resolver returns `DefaultConnection` for all (Shared) tenants — no behaviour change.
- `ConnectionString` is backend-only; never bound from UI, never logged in full.

See `docs/Phase50B_ConnectionResolver.md` for full details.

**Note:** The original 5.0B sketch suggested registering `PlatformDbContext` and refactoring
`TenantLimitGuard`. These were intentionally deferred — Phase 5.0B added the resolver layer only,
keeping `ApplicationDbContext` authoritative to guarantee zero runtime change. PlatformDbContext
registration and `TenantLimitGuard` dual-context refactoring move to Phase 5.0C.

---

### Phase 5.0C — First Tenant Dedicated Database

**Goal:** Migrate one tenant to their own dedicated database. Validate the full stack.

**Tasks:**
1. Manually create `HardBuild_Tenant_{id}` database for pilot tenant
2. Apply `TenantDbContext` migrations to the new database
3. Run per-tenant data migration script for all 31 tenant tables
4. Set `Tenant.ConnectionString` for the pilot tenant
5. Verify all SaaS modules work correctly for that tenant
6. Verify all other tenants are unaffected (still using shared DB)
7. Verify SuperAdmin can still see all tenants
8. Run full regression test
9. Monitor for 1 week; roll back by clearing `Tenant.ConnectionString` if needed

**Rollback procedure:** Set `Tenant.ConnectionString = null` → tenant immediately routes back to shared DB.

---

### Phase 5.0D — Automatic Tenant Database Provisioning

**Goal:** New tenants automatically get their own database. Zero manual steps.

**Tasks:**
1. Add `TenantDatabaseProvisioningService`
   - Creates SQL Server database
   - Applies migrations
   - Seeds default data
   - Updates `Tenant.ConnectionString`
2. Hook into `TenantsController.Create` (call provisioning after tenant row created)
3. Add SuperAdmin UI for manual provisioning (for existing tenants)
4. Add background migration service for bulk-migrating remaining shared-DB tenants
5. Add monitoring: database size per tenant, connection health checks
6. Add `TenantLimitGuard` warning when tenant DB approaches 8 GB

**After Phase 5.0D:** All new tenants get dedicated databases automatically.

---

### Phase 5.0E — Shared DB Decommission (Future)

**Goal:** Migrate all remaining tenants off the shared database. Clean up TenantId filters.

**Tasks:**
1. Migrate all existing tenants to dedicated databases (using Phase 5.0D tooling)
2. Keep shared DB as read-only replica for 30 days post-migration
3. Remove `ApplyTenantScope` helpers (11 overloads → delete `ReportsController.TenantScope.cs`)
4. Remove all inline `.Where(x => x.TenantId == tenantId || x.TenantId == null)` filters
5. Retire `TenantGuard.CanAccessAsync` (replaced by DB isolation)
6. Optionally drop `TenantId` columns from all tenant DB tables (simplifies schema)
7. Decomission shared ApplicationDbContext — fully replaced by PlatformDbContext + TenantDbContext
8. Update EF migration chains
9. Final regression test

---

*End of Phase 5.0 Architecture Plan*
