# Tenant Clean Reset Audit

**Date:** 2026-07-15  
**Environment:** Development only  
**Command:** `dotnet run -- --tenant-reset --tenant-id=1 --retain-admin=adminTCS --confirm`

Password rotation is **decoupled** — use `TENANT_ADMIN_PASSWORD` env + `--set-tenant-admin-password` (never `--set-admin-password` on reset).

---

## Selected Tenant

| Field | Value |
|-------|-------|
| Tenant ID | 1 |
| Code | TCS |
| Name | Tams Construction Supply |
| Database Mode | Dedicated |
| Routing Enabled | true |
| Routing Active | true |
| Target Database | `HardBuild_TCS_1` |
| Masked Connection | `Server=.\MSSQL_SYSDEV; Database=HardBuild_TCS_1; (credentials hidden)` |

---

## Preserved Accounts

| Account | Role | Notes |
|---------|------|-------|
| `superadmin` | SuperAdmin | Platform DB — never touched |
| `adminTCS` | TenantAdmin | Retained explicitly via `--retain-admin` |
| Dev password | — | Set separately via `TENANT_ADMIN_PASSWORD` + `--set-tenant-admin-password` (Development only) |

---

## Safety Gates Verified

| Gate | Result |
|------|--------|
| `IsDevelopment()` | Required — refuses Production |
| `--confirm` | Required for destructive execution |
| Preview default | Dry-run without `--confirm` |
| SuperAdmin protection | Never deleted |
| Retained admin protection | Never deleted |
| Tenant record preserved | Yes |
| Subscription / routing preserved | Yes |
| EF migration history preserved | Yes |
| `BackupRecords` / `RestoreRecords` | **Preserved** — platform metadata; not auto-deleted |

---

## Operational Database Cleared (FK-safe order)

All tables in dedicated DB `HardBuild_TCS_1` cleared via routed `TenantDbContext`:

**Transactions:** DeliveryReceiptItems, DeliveryReceipts, QuotationItems, Quotations, SalesReturnDetails, SalesReturnHeaders, SalesDetails, SalesHeaders, SupplierPayments, CustomerLedgers, Expenses, StockInDetails, StockInHeaders, PurchaseOrderItems, PurchaseOrders, StockAdjustmentDetails, StockAdjustmentHeaders, BranchTransferItems, BranchTransfers, SupplierReturnDetails, SupplierReturnHeaders, DamagedGoodsDetails, DamagedGoodsHeaders, ImportBatchRows, ImportBatches, Notifications, AuditTrails

**Master/setup:** BranchProductStocks, ItemUnitConversions, Items, Customers, Suppliers, Categories, Units, UserBranches, Branches, tenant-scoped SystemSettings

**Stale shared-DB rows** for TenantId=1 also cleared from `ApplicationDbContext` when routing is dedicated.

---

## Intentionally Preserved

| Item | Location |
|------|----------|
| Tenant record | Platform `Tenants` |
| Subscription | Platform |
| Routing flags / connection string | Platform |
| AspNetUsers (retained admin) | Platform |
| Roles / RolePermissions | Platform + replicated in tenant DB |
| SubscriptionPlans (replica) | Tenant DB |
| RolePermissions (replica) | Tenant DB |
| `__EFMigrationsHistory` | Both databases |
| BackupRecords / RestoreRecords | Platform |

---

## Before / After Row Counts (Tenant DB)

| Table | Before | After |
|-------|--------|-------|
| Branches | 1 | 0 |
| Items | 19 | 0 |
| Categories | 2 | 0 |
| Units | 4 | 0 |
| Customers | 0 | 0 |
| Suppliers | 13 | 0 |
| SalesHeaders | 4 | 0 |
| PurchaseOrders | 13 | 0 |
| StockInHeaders | 28 | 0 |
| SystemSettings | 1 | 0 |
| AuditTrails | 38 | 0 |

*(Post-reset execution on clean DB showed zeros across all operational tables.)*

---

## Isolation Checks

- Other tenants in platform DB unchanged
- Dedicated DB scoped to tenant 1 only
- No global `DevelopmentDataResetService` execution (that service deletes **all** tenants/users)

---

## Why Not Use `DevelopmentDataResetService` As-Is

The legacy service:
- Deletes **all** tenant records
- Deletes **all** tenant-scoped users
- Clears **entire** shared operational tables globally
- Omits RC18 tables (PurchaseOrders, DamagedGoods, etc.) when run against outdated code paths

**New service:** `TenantOperationalResetService` + `--tenant-reset` CLI.

---

## Machine-readable report

`docs/TenantCleanResetAudit.json`
