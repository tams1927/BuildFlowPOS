# RC1.8.2 — Purchase Order Details Fix & Workflow Simulation

## Root Cause

`PurchaseOrder` defines a `Tenant` navigation property on the model class, but **`TenantDbContext` explicitly ignores it**:

```csharp
builder.Entity<PurchaseOrder>().Ignore(e => e.Tenant);
```

Dedicated tenant databases (RC1.7 architecture) store only operational data. `Tenant` is platform metadata in the shared `ApplicationDbContext`. When a routed tenant opens PO Details, `_context` is `TenantDbContext`, and `.Include(p => p.Tenant)` throws:

```
InvalidOperationException: The expression 'p.Tenant' is invalid inside an Include operation.
```

### Why the invalid Include existed

`Details`, `Print`, and `DownloadPdf` were written when PO data lived in the shared DB, where `ApplicationDbContext` **does** configure `PurchaseOrder → Tenant`. RC1.8 added `.Include(p => p.Tenant)` to Details without accounting for the dedicated-DB ignore rule already applied in `TenantDbContext`.

## Fix

| Action | Change |
|---|---|
| **Details** | Removed `.Include(p => p.Tenant)` — view never used `Model.Tenant` |
| **Print** | Removed Include; resolve tenant via `ApplicationDbContext.Tenants` |
| **DownloadPdf** | Same as Print |
| **New helper** | `ResolvePlatformTenantAsync()` — platform DB lookup by `TenantId` |

Injected `ApplicationDbContext _platformDb` into `PurchaseOrdersController` (same pattern as `SupplierPaymentsController`, `SalesController`, etc.).

## Audit of PO-related queries

| Location | Include audit |
|---|---|
| `PurchaseOrdersController.Details` | **Fixed** — removed `Tenant` |
| `PurchaseOrdersController.Print/Pdf` | **Fixed** — removed `Tenant` |
| `PurchaseOrdersController.Index/Edit/Receive` | OK — Supplier, Branch, Items only |
| `ReceivingController` | OK — PurchaseOrder (no Tenant include) |
| `HomeController` pending deliveries | OK — Supplier, Items, OrderedUnit |
| `LoadReceivingHistoryAsync` | OK — StockInDetails, DamagedGoods Details |

**Note:** `SuppliersController` and `CustomersController` still `.Include(x => x.Tenant)` for print views — same class of bug, outside RC1.8 scope. Not hit in PO workflow.

## Click-Through Simulation (`--qa-po-clickthrough`)

Exercises **actual MVC controller actions** (not EF-only tests):

1. Create supplier, category, unit, product, PO
2. `PurchaseOrders/Details` ✓ (was failing)
3. `PurchaseOrders/Receive` GET ✓
4. `PurchaseOrders/Edit` GET ✓
5. `PurchaseOrders/Index` ✓
6. `PurchaseOrders/Print` ✓
7. `PurchaseOrders/Receive` POST — partial ✓
8. `Receiving/Details` ✓
9. `Receiving/Index` search ✓
10. `PurchaseOrders/Receive` POST — final ✓
11. `PurchaseOrders/Details` with history ✓
12. Inventory, payables, PO status verified
13. `Home/Index` dashboard KPIs ✓
14. `AuditTrail/Index` search ✓
15. Dedicated DB isolation ✓

**Result: 19/19 pass**

## Regression

| Suite | Result |
|---|---|
| `--qa-po-clickthrough` | 19/19 |
| `--qa-receiving` | 18/18 |
| `--qa-receiving-ux` | 12/12 |
| `--qa-phase51` | 8/8 |

## Build

```
dotnet build → 0 Errors, 0 Warnings
```

## Files Changed

- `Controllers/PurchaseOrdersController.cs`
- `Tools/PoWorkflowClickthroughQaRunner.cs` (new)
- `Program.cs`
- `docs/RC182_PoDetailsFix.md` (this file)

## Recommendation

Replace "run QA" with **click-through simulation of every affected MVC screen** (`--qa-po-clickthrough` pattern). Runtime EF Include errors only surface when controller actions execute against `TenantDbContext`.
