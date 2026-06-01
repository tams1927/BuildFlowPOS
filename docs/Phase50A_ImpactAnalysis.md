# Phase 5.0A — Impact Analysis

**System**: HardBuild POS — HardwareManagementSystem SaaS  
**Date**: 2026-06-01  
**Phase**: 5.0A (Architecture Foundation — no runtime changes)

---

## Overview

This document catalogs every file, controller, and service that will require
modification when the system migrates from `ApplicationDbContext` to the split
`PlatformDbContext` + `TenantDbContext` architecture (Phase 5.0B onward).

**Phase 5.0A makes ZERO runtime changes.** This document is forward-looking only.

---

## 1. Controllers Affected

### High Complexity (mixed platform + tenant data in same controller)

| Controller | Issue | Estimated Effort |
|---|---|---|
| `TenantsController.cs` | Creates `Tenant` (platform) + `SystemSettings` (tenant) + `UserManager.CreateAsync` (platform Identity). Must orchestrate both contexts. | 6–8 hours |
| `SuperAdminController.cs` | Aggregates `Branches`/`Items` counts across ALL tenants from one DB. Needs per-tenant fan-out or cached metrics table in Platform DB. | 8–12 hours |
| `HomeController.cs` | Dashboard: queries `Tenants` (platform) + all operational tables (tenant) via `TenantLimitGuard`. | 4–6 hours |
| `UsersController.cs` | Writes `AspNetUsers` (platform via `UserManager`) + `UserBranches` (tenant). Must separate context usage. | 4–6 hours |
| `UserBranchesController.cs` | `UserBranches`/`Branches` (tenant) + `UserManager.Users` count (platform). | 3–4 hours |
| `AccountController.cs` | Post-login: loads `Tenant.Status` (platform) + sets up tenant context. | 2–3 hours |

### Medium Complexity (mainly tenant data, with occasional platform lookup for print headers)

| Controller | Platform Table Used | Estimated Effort |
|---|---|---|
| `BranchTransfersController.cs` | `Tenants` (print header) | 2–3 hours |
| `CustomerCollectionsController.cs` | `Tenants` (receipt) | 2–3 hours |
| `DeliveryReceiptsController.cs` | `Tenants` (print) | 2–3 hours |
| `QuotationsController.cs` | `Tenants` (print) | 2–3 hours |
| `SalesController.cs` | `Tenants` (receipt) | 2–3 hours |
| `StockAdjustmentController.cs` | `Tenants` (print) | 2–3 hours |
| `SupplierPaymentsController.cs` | `Tenants` (receipt) | 2–3 hours |
| `AuditTrailController.cs` | `AuditTrails` + `Tenants` (SuperAdmin filter) | 2–3 hours |
| `RolesController.cs` | `RolePermissions` (platform) | 2 hours |
| `RolePermissionsController.cs` | `RolePermissions` (platform) | 1–2 hours |
| `SubscriptionPlansController.cs` | `SubscriptionPlans` + `Tenants` (validate before delete) | 2–3 hours |
| `ReportsController.cs` (4 partials) | `Tenants` (report headers) + all tenant data | 4–6 hours |

### Low Complexity (pure tenant data — swap `_context` for tenant factory)

| Controller | Estimated Effort |
|---|---|
| `BranchesController.cs` | 1–2 hours |
| `CategoriesController.cs` | 1 hour |
| `CustomersController.cs` | 1–2 hours |
| `ExpensesController.cs` | 1 hour |
| `ImportController.cs` | 1–2 hours |
| `InventoryController.cs` | 1–2 hours |
| `InventoryMovementController.cs` | 1 hour |
| `POSController.cs` | 2–3 hours (has `BeginTransactionAsync`) |
| `ProductsController.cs` | 1–2 hours |
| `PurchaseOrdersController.cs` | 2–3 hours |
| `SalesReturnController.cs` | 2 hours (has transaction) |
| `SettingsController.cs` | 1–2 hours |
| `StockInController.cs` | 1–2 hours |
| `SuppliersController.cs` | 1–2 hours |
| `UnitsController.cs` | 1 hour |

### No Direct ApplicationDbContext Injection (indirect change only)

| Controller | Current Mechanism | Change Required |
|---|---|---|
| `UsersController.cs` | `UserManager<ApplicationUser>` only | Update DI to ensure UserManager uses PlatformDbContext Identity stores |
| `NotificationsController.cs` | Delegates to `NotificationService` | No controller change; update NotificationService |

---

## 2. Services Affected

| Service | Current Usage | Complexity | Change Required |
|---|---|---|---|
| `TenantLimitGuard.cs` | Reads `Tenants`+`SubscriptionPlans` (platform) AND `Branches`/`Items` (tenant) AND `_userManager.Users` | **HIGH** | Must accept `PlatformDbContext` + `TenantDbContextFactory`; resolve counts from tenant DB |
| `ExcelImportService.cs` | ~27 `_context` calls — all tenant data | MEDIUM | Swap to `TenantDbContext` |
| `BranchService.cs` | `Branches`, `BranchProductStocks`, `UserBranches` (tenant) + `BeginTransactionAsync` | MEDIUM | Swap to `TenantDbContext` |
| `AuditService.cs` | Writes `AuditTrails` | MEDIUM | Route to Platform or Tenant DB depending on context |
| `NotificationService.cs` | `Notifications` (tenant + broadcast) | LOW–MEDIUM | Route based on `TenantId` value |
| `PermissionService.cs` | `RolePermissions` (platform) | LOW | Swap to `PlatformDbContext` |
| `TenantService.cs` | `Tenants` (platform) only | LOW | Swap to `PlatformDbContext` |
| `TenantStatusFilter.cs` | `AspNetUsers`+`Tenants` (both platform) | LOW | Swap to `PlatformDbContext` |

---

## 3. Data Layer Affected

| File | Change Required | Complexity |
|---|---|---|
| `ApplicationDbContext.cs` | **Decommission** — replaced by `PlatformDbContext` + `TenantDbContext` in Phase 5.0E | HIGH |
| `Data/Seeders/DbSeeder.cs` | Platform seed → `PlatformDbContext`; tenant seed → part of `TenantDatabaseProvisioningService` | MEDIUM |
| `Data/DevelopmentDataResetService.cs` | ~80 context calls spanning platform users and all tenant operational data; must split by context | HIGH |
| `Program.cs` | Replace `AddDbContext<ApplicationDbContext>` with `AddDbContext<PlatformDbContext>` + tenant factory registration; point Identity at `PlatformDbContext` | MEDIUM |

---

## 4. TenantId Filtering Locations (to be removed in Phase 5.0E)

| Location | Count | Removable After Migration? |
|---|---|---|
| `Controllers/*.cs` — explicit `.Where(TenantId…)` | ~88 lines | YES |
| `Controllers/*.cs` — total `TenantId ==` comparisons | ~643 refs | YES (tenant tables) / Keep for platform queries |
| `Services/*.cs` — TenantId references | ~90 | PARTIAL |
| `Data/ApplicationDbContext.cs` — index definitions | ~30 indexes | Move to TenantDbContext (already done); drop from ApplicationDbContext |
| `ReportsController.TenantScope.cs` — `ApplyTenantScope` helpers | 11 overloads, ~65 call sites | **DELETE ENTIRE FILE** in Phase 5.0E |
| `TenantGuard.CanAccessAsync` per-row checks | ~150 references | **DELETE** — replaced by DB isolation |

---

## 5. New Infrastructure Files Required (Phase 5.0B+)

| File | Phase | Purpose |
|---|---|---|
| `Services/ITenantDatabaseResolver.cs` | 5.0B | Interface for resolving tenant connection strings |
| `Services/SqlTenantDatabaseResolver.cs` | 5.0B | Platform DB-backed connection string resolver with caching |
| `Services/TenantDbContextFactory.cs` | 5.0B | Creates `TenantDbContext` instances per request |
| `Services/TenantDatabaseProvisioningService.cs` | 5.0D | Automates new tenant database creation and migration |
| `Migrations/Platform/` | 5.0B | New PlatformDbContext migration chain |
| `Migrations/Tenant/` | 5.0C | New TenantDbContext migration chain |

---

## 6. Estimated Complexity

### Overall Score: HIGH

| Dimension | Score | Reasoning |
|---|---|---|
| Code volume | HIGH | 47 files, 670+ TenantId references |
| Data migration | HIGH | All existing tenant data must move to individual DBs |
| Identity FK cross-DB | HIGH | UserBranch.UserId cannot have a DB-level FK to Platform DB |
| EF migration chain split | HIGH | Two new migration chains; existing chain preserved for shared-DB deployments |
| Transaction integrity | LOW | All existing transactions are within tenant data only — no cross-context transactions |
| Raw SQL risk | NONE | Zero raw SQL in codebase |

### Effort Estimate

| Category | Files | Est. Hours |
|---|---|---|
| High complexity controllers/services | 10 | 50–80 hours |
| Medium complexity controllers | 12 | 24–36 hours |
| Low complexity controllers | 15 | 15–30 hours |
| Service layer | 8 | 16–24 hours |
| Data layer + Program.cs | 4 | 16–24 hours |
| New infrastructure (resolver, factory, provisioning) | 6 (new files) | 20–30 hours |
| Migrations + chain split | — | 8–16 hours |
| Testing + regression | — | 20–40 hours |
| **Total** | **~55 files** | **~169–280 hours** |

---

## 7. Phase 5.0B Recommendations

**Immediate prerequisites before starting Phase 5.0B:**

1. **Add `ConnectionString` field to `Tenant` model**  
   `public string? ConnectionString { get; set; }`  
   This field is the core of connection routing. Without it, `ITenantDatabaseResolver` cannot function.

2. **Register `PlatformDbContext` with same connection string as `ApplicationDbContext`**  
   This allows `TenantService`, `TenantStatusFilter`, and `PermissionService` to be migrated  
   to `PlatformDbContext` without any behavior change — they still hit the same shared DB.

3. **Implement `ITenantDatabaseResolver` with null fallback**  
   When `Tenant.ConnectionString` is null, fall back to the shared database connection string.  
   This makes Phase 5.0B fully backward-compatible — no tenants are affected until explicitly migrated.

4. **Update `TenantLimitGuard` first**  
   It is the highest-risk service (dual-context queries). Getting it right validates the pattern  
   for all other dual-context callers.

5. **Migrate pure-platform services first** (`TenantService`, `TenantStatusFilter`, `PermissionService`)  
   These are LOW complexity and establish the `PlatformDbContext` injection pattern.

6. **Migrate one pure-tenant controller as proof-of-concept** (recommended: `CategoriesController`)  
   Verify the `TenantDbContextFactory` pattern works end-to-end before scaling to all controllers.

**Phase 5.0B success criteria:**
- All tenants still use the shared database (via null fallback)
- `PlatformDbContext` is registered and used by platform services
- `TenantDbContextFactory` resolves correctly
- Build passes with 0 errors, 0 warnings
- All regression tests pass

---

## 8. Runtime Verification Checklist (Phase 5.0A)

| Check | Status | Notes |
|---|---|---|
| `ApplicationDbContext` is the runtime DbContext | ✅ PASS | `Program.cs` unchanged — only `ApplicationDbContext` registered |
| `PlatformDbContext` is NOT registered in DI | ✅ PASS | `PlatformDbContext.cs` exists but not added to `Program.cs` |
| `TenantDbContext` is NOT registered in DI | ✅ PASS | `TenantDbContext.cs` exists but not added to `Program.cs` |
| Login behavior unchanged | ✅ PASS | No Identity or auth changes |
| Tenant isolation unchanged | ✅ PASS | TenantGuard + ApplyTenantScope unchanged |
| Existing migrations valid | ✅ PASS | No new migrations; no schema changes |
| No route changes | ✅ PASS | No controller modifications |
| No DI changes | ✅ PASS | `Program.cs` not modified |
| No data movement | ✅ PASS | Architecture preparation only |
| No tenant database creation | ✅ PASS | Not in scope for Phase 5.0A |
| Build succeeds | Verified after `dotnet build` |

---

*Phase 5.0A — Architecture foundation complete. No runtime behavior changed.*
