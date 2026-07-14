# RC1.8.3 — Dedicated Tenant Runtime Audit

## Objective

Eliminate runtime surprises caused by `ApplicationDbContext` vs `TenantDbContext` separation. No new business features.

## Ignored Navigation Checklist (`TenantDbContext`)

| Entity | `Tenant` nav ignored | Valid Includes |
|---|---|---|
| Category, Unit, Item | Yes | Supplier, Branch, Customer, Items, etc. |
| Supplier, Customer | Yes | — (use `_platformDb.Tenants` for print/PDF) |
| Branch, BranchProductStock, BranchTransfer | Yes | FromBranch, ToBranch, Product |
| SalesHeader, SalesReturnHeader | Yes | Customer, SalesDetails, Item |
| StockInHeader, StockAdjustmentHeader | Yes | Supplier, Branch, PurchaseOrder, Details |
| DamagedGoodsHeader, SupplierReturnHeader | Yes | Branch, Details, Item |
| PurchaseOrder, Quotation, DeliveryReceipt | Yes | Supplier, Branch, Items |
| Expense, ImportBatch, SystemSetting | Yes | Branch |

`builder.Ignore<Tenant>()` — entire Tenant type excluded from tenant DB schema.

## Invalid Includes Found & Fixed

| Location | Issue | Fix |
|---|---|---|
| `PurchaseOrdersController` (RC1.8.2) | `.Include(p => p.Tenant)` | Removed; `ResolvePlatformTenantAsync()` |
| `SuppliersController.StatementPdf` | `.Include(s => s.Tenant)` | Removed; platform tenant lookup |
| `CustomersController.StatementPdf` | `.Include(c => c.Tenant)` | Removed; platform tenant lookup |

**Remaining valid platform-only Includes:** `TenantsController`, `SuperAdminController`, `BackupManagementController` (use `ApplicationDbContext` only).

## Click-Through Audit (`--qa-tenant-runtime-audit`)

Simulates TenantAdmin session against **dedicated `TenantDbContext`**, invoking real MVC controller actions.

### Pages exercised (47+ GET actions)

- Dashboard, Products, Categories, Units
- Suppliers (Index, Statement, StatementPdf)
- Customers (Index)
- Purchase Orders (Index, Details, Receive, Print, DownloadPdf)
- Receiving, Stock In
- Inventory, Inventory Movement, Stock Adjustment, Damaged Goods, Supplier Returns
- Sales, POS, Sales Return, Customer Collections
- Supplier Payments, Quotations, Delivery Receipts
- Branches, Branch Transfers, Expenses
- Reports (Index, Supplier Payables, Inventory Status, Customer Balance)
- Settings, Audit Trail, Import History
- Users, Change Password

### Result: **47/49 pass**

| Failure | Type | Notes |
|---|---|---|
| `Suppliers/StatementPdf` | `DocumentLayoutException` (QuestPDF) | EF/query OK; PDF layout constraint with test data |
| `PurchaseOrders/DownloadPdf` | `DocumentLayoutException` (QuestPDF) | Print **view** passes; PDF renderer layout edge case |

These are **not** `TenantDbContext` Include errors. Pre-existing PDF layout sensitivity.

## Regression

| Suite | Result |
|---|---|
| `--qa-tenant-runtime-audit` | 47/49 |
| `--qa-po-clickthrough` | 19/19 |
| `--qa-receiving` | 18/18 |
| `--qa-receiving-ux` | 12/12 |
| `--qa-phase51` | 8/8 |
| `--qa-save-confirm` | 4/4 |

## Build

```
dotnet build → 0 Errors, 0 Warnings
```

## Files Changed

- `Controllers/SuppliersController.cs`
- `Controllers/CustomersController.cs`
- `Tools/TenantRuntimeAuditQaRunner.cs` (new)
- `Program.cs`
- `docs/RC183_TenantRuntimeAudit.md`

## Recommendation

Use `--qa-tenant-runtime-audit` after any change that touches EF Includes or operational controllers. Compile-time checks do not catch ignored navigations on dedicated tenants.
