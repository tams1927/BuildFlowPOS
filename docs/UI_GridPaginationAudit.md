# UI Grid / Pagination / Confirmation Audit (Recommendation v1)

> **Date:** 2026-06-23  
> **Scope:** List/table modules for pilot UI consistency  
> **Legend:** Falcon styling = `hb-card`, `table-light`, `badge-soft-*`, sidebar layout patterns

| Module | Route | Pagination | Search | Sorting | Export | SweetAlert | Toast | Falcon | Recommended action |
|--------|-------|------------|--------|---------|--------|------------|-------|--------|-------------------|
| Branches | `/Branches/Index` | Yes | Yes | No | No | Partial (forms) | Layout | Good | Add column sort later; destructive delete uses Swal where present |
| UserBranches | `/UserBranches/Index` | Yes | Yes | No | No | Partial | Layout | Good | OK for pilot |
| Users | `/Users/Index` | Yes | Yes | No | No | Partial | Layout | Good | OK for pilot |
| Roles | `/Roles/Index` | Yes | Yes | No | No | Partial | Layout | Good | OK for pilot |
| RolePermissions | `/RolePermissions/Index` | Yes | Yes | No | No | Partial | Layout | Good | Matrix view; OK for pilot |
| Products | `/Products/Index` | Yes | Yes | No | No | Yes (delete) | Layout | Good | OK for pilot |
| Categories | `/Categories/Index` | Yes | Yes | No | No | Partial | Layout | Good | OK for pilot |
| Units | `/Units/Index` | Yes | Yes | No | No | Partial | Layout | Good | OK for pilot |
| Suppliers | `/Suppliers/Index` | Yes | Yes | No | No | Partial | Layout | Good | OK for pilot |
| Customers | `/Customers/Index` | Yes | Yes | No | No | Partial | Layout | Good | OK for pilot |
| Purchase Orders | `/PurchaseOrders/Index` | Yes | Yes | No | No | Yes (create/receive/cancel) | Layout | Good | OK for pilot |
| Stock In | `/StockIn/Index` | Yes | Yes | No | No | Yes (create) | Layout | Good | OK for pilot |
| Stock Adjustments | `/StockAdjustment/Index` | Yes | Yes | No | No | Partial | Layout | Good | OK for pilot |
| Branch Transfers | `/BranchTransfers/Index` | Yes | Yes | No | No | Partial | Layout | Good | OK for pilot |
| Quotations | `/Quotations/Index` | Yes | No | No | PDF | Yes (details — Phase 5.2) | Layout | Good | Add Index search filter post-pilot |
| Delivery Receipts | `/DeliveryReceipts/Index` | Yes | No | No | PDF | Yes (cancel — Phase 5.2) | Layout | Good | Add Index search post-pilot |
| Sales | `/Sales/Index` | Yes | Yes | No | Receipt/PDF | Partial | Layout | Good | OK for pilot |
| Sales Returns | `/SalesReturn/Index` | Yes | Yes | No | No | Partial | Layout | Good | OK for pilot |
| Customer Collections | `/CustomerCollections/Index` | Yes | No | No | No | Partial | Layout | Good | Add search on ledger list post-pilot |
| Supplier Payments | `/SupplierPayments/Pay` | No (workflow) | No | No | No | Partial | Layout | Fair | Entry from reports; not a grid module |
| Expenses | `/Expenses/Index` | Yes | Yes | No | No | Partial | Layout | Good | OK for pilot |
| Inventory Movements | `/InventoryMovement/Index` | Yes | Yes | No | No | Partial | Layout | Good | OK for pilot |
| Audit Trail | `/AuditTrail/Index` | Yes | Yes | No | No | No | Layout | Good | Read-only; OK for pilot |
| Reports | `/Reports/Index` + sub-reports | Mixed | Mixed | No | Excel/PDF | Partial | Layout | Fair | Many report views; currency hardcoding remains in KPI views |
| Tenants | `/Tenants/Index` | No | No | No | No | Yes | Layout | Good | **Post-pilot:** add pagination when tenant count grows |
| Subscription Plans | `/SubscriptionPlans/Index` | No | No | No | No | Yes (delete — Phase 5.2) | Layout | Good | Small list; pagination optional |
| Import | `/Import/Index` + history | Yes | No | No | No | Partial | Layout | Good | OK for pilot |

## Phase 5.2 UI fixes applied

| File | Change |
|------|--------|
| `Views/DeliveryReceipts/Details.cshtml` | Replaced `confirm()` with SweetAlert for Cancel DR |
| `Views/Quotations/Details.cshtml` | SweetAlert for Convert to Sale and Void |
| `Views/SubscriptionPlans/Index.cshtml` | SweetAlert for Delete; removed duplicate Bootstrap alerts (layout toast) |
| `Views/SuperAdmin/Dashboard.cshtml` | SweetAlert for Suspend tenant |
| `Views/Settings/Index.cshtml` | Data Protection section + Request Backup SweetAlert |

## Cross-cutting observations

1. **Pagination pattern** — Most operational lists use `PagedResult<T>` with page sizes 10/25/50/100.
2. **Sorting** — No server-side column sorting in list modules; acceptable for pilot scale.
3. **Toasts** — Controllers use `TempData["SuccessMessage"]` / `ErrorMessage`; `_Layout.cshtml` renders SweetAlert toasts globally.
4. **Destructive actions** — Tenants, Products, POs, and POS use SweetAlert; remaining `confirm()` usages in less-critical paths can be migrated post-pilot.
5. **Tenants / Subscription Plans** — Unbounded lists acceptable at pilot tenant count; monitor before fleet scale.

## Recommended post-pilot UI work

- Add search to Quotations, Delivery Receipts, and Customer Collections index pages.
- Add pagination to Tenants index when tenant count > 25.
- Continue replacing residual `confirm()` in detail views.
- Standardize report KPI currency via `ViewBag.CurrencySymbol` (see SAAS-AUDIT-1).
