# Phase 5.0D.2 — Full Dedicated Database Module Cutover

## Goal

Make a **routing-enabled** tenant use its **dedicated `TenantDbContext`** for the *entire*
store system — not just POS/core inventory. SuperAdmin, platform and identity data stay on
the shared `ApplicationDbContext`.

A tenant routes to its dedicated database only when **all** of these are true (unchanged from
5.0D / 5.0D.1, enforced by `TenantDatabaseResolver.IsRoutingActiveAsync`):

- `DatabaseMode == Dedicated`
- `DatabaseProvisionedAtUtc != null`
- `DataMigrated == true`
- `RoutingEnabled == true`

Otherwise every module continues to use the shared database. SuperAdmin (no tenant) always
resolves to shared.

## Architecture (how routing happens)

- Controllers inherit `OperationalDbController`, whose `OnActionExecutionAsync` resolves
  `_context` (an `ITenantOperationalDbContext`) once per request from
  `ITenantOperationalContextProvider`.
- The provider is **scoped** and caches one context per tenant for the request. Therefore the
  controller and every converted service (`BranchService`, `AuditService`,
  `NotificationService`) share the **same** context instance within a request — keeping EF
  change-tracking and ambient transactions (e.g. POS checkout) consistent.
- Platform-only reads that an operational controller still needs (currently only
  `Tenants` for receipt/print headers) go through an explicitly injected
  `ApplicationDbContext _platformDb`.

## Controllers converted (this phase)

| Controller | Platform read kept on `_platformDb` |
|---|---|
| `CustomersController` | — |
| `SuppliersController` | — |
| `ExpensesController` | — |
| `InventoryMovementController` | — |
| `PurchaseOrdersController` | — |
| `SalesReturnController` | — |
| `SupplierPaymentsController` | `Tenants` (print header) |
| `CustomerCollectionsController` | `Tenants` (print header) |
| `QuotationsController` | `Tenants` (print header) |
| `DeliveryReceiptsController` | `Tenants` (print header) |
| `BranchTransfersController` | `Tenants` (print header) |
| `StockAdjustmentController` | `Tenants` (print header) |

Already converted in 5.0D.1 (still routing): `ProductsController`, `CategoriesController`,
`UnitsController`, `InventoryController`, `StockInController`, `POSController`,
`SalesController`, `HomeController` (dashboard), and the **entire `ReportsController`** —
including its `ReportsController.ARAP` (AR/AP), `ReportsController.Inventory` (inventory
intelligence) and `ReportsController.TenantScope` partials, which all share the routed
`_context`. Tasks 13–15 (AR/AP reports, inventory-intelligence reports, remaining report
actions) were therefore already cut over; this phase verifies them end-to-end.

## Services converted

| Service | Treatment |
|---|---|
| `AuditService` | Operational. Audit rows route to the tenant's database (shared when `CurrentTenantId` is null, i.e. all SuperAdmin/platform actions). |
| `NotificationService` | Operational. Notifications route to the tenant's database. |
| `BranchService` | Operational. Branches, `UserBranches`, `BranchProductStock`, and stock mutations route to the tenant's database. Shares the request-scoped context, so its `ExecuteStockMutationAsync` joins the controller's ambient transaction. |
| `TenantLimitGuard` | **Mixed / split.** Limit + plan metadata and **user** counts stay on shared (`ApplicationDbContext` / `UserManager`). **Branch** and **product** counts use `GetContextAsync(tenantId)` so the count is correct for the inspected tenant even when SuperAdmin is the caller. |

No DI cycles were introduced: the provider depends only on `ApplicationDbContext`,
`ITenantContext`, `ITenantDatabaseResolver`, `ITenantDbContextFactory`; none of the converted
services are dependencies of the provider.

## Modules tested (automated, 39/39 steps passed)

Runner: `Tools/Phase50D2QaRunner.cs`, dev-only, launched with
`dotnet run -- --qa-phase50d2` (does **not** start the web server).

Create Branch (×2) · Category · Unit · Product · Supplier · Purchase Order · Receive PO ·
Stock-In · Stock Adjustment · Branch Transfer · Customer · Quotation · Convert Quotation to
Sale · Delivery Receipt · POS Sale · Credit Sale · Customer Collection · Sales Return ·
Supplier Payment · Expense · Customer SOA · Supplier Statement · Customer Aging · Supplier
Aging · Inventory Valuation · Fast/Slow/Dead-moving · Reorder Suggestions · ABC Analysis ·
Dashboard KPIs.

## Tables verified in the dedicated DB (and absent from shared)

`Branches`, `Categories`, `Units`, `Items`, `Suppliers`, `Customers`, `PurchaseOrders`,
`PurchaseOrderItems`, `StockInHeaders`, `StockInDetails`, `StockAdjustmentHeaders`,
`StockAdjustmentDetails`, `BranchTransfers`, `BranchTransferItems`, `Quotations`,
`QuotationItems`, `DeliveryReceipts`, `DeliveryReceiptItems`, `SalesHeaders`, `SalesDetails`,
`CustomerLedgers`, `SalesReturnHeaders`, `SalesReturnDetails`, `SupplierPayments`, `Expenses`.

Every QA row was found in the dedicated DB; **none** leaked into the shared DB (see
`docs/Phase50D2_FullCycleQAReport.md` for the exact counts).

## Remaining shared tables (by design)

- `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, and the rest of ASP.NET Identity
- `Tenants`
- `SubscriptionPlans`
- `RolePermissions` (platform permission catalog)
- SuperAdmin dashboard / SaaS management / billing & subscription data

## Phase 5.0D.3 — Settings cutover (follow-up patch, completed)

`SettingsController` now inherits `OperationalDbController`, so tenant `SystemSettings`
reads/writes route to the dedicated database for routing-enabled tenants. `_Layout.cshtml`
(theme/business profile) was switched from `ApplicationDbContext` to the operational provider
as well, and receipt/PDF services already receive the routed `SystemSetting` from their
controllers. Logo upload (file system under `wwwroot/uploads/logos/tenant-{id}`) is unchanged.
Audit events: `SETTINGS_UPDATED` (profile/VAT/TIN/footer) and `COMPANY_LOGO_UPDATED`.

The QA runner now also edits `BusinessName`/`TIN`/`VAT`/`ReceiptFooter` through the routed
context and verifies the dedicated `SystemSettings` row is updated, the shared row is not, and
the receipt/PDF header reads the dedicated values — **41/41 steps pass**.

## Known risks / remaining work

- **`SettingsController` converted in Phase 5.0D.3** (above) — no longer a split-brain risk.
- `Users`, `Roles`, `RolePermissions`, `Import` and `AuditTrail` (read) remain shared/platform
  by design.
- **Cross-database FKs are intentionally absent.** Platform rows (Tenant, Identity user) are
  referenced by string/int values only; no FK spans the shared↔dedicated boundary.
- **Audit of platform actions stays shared.** Because routing keys off `CurrentTenantId`,
  SuperAdmin/platform audit entries (tenant create, provision, migrate, enable/disable
  routing) are written to the shared DB — intended.

## Rollback result

`Disable Routing` → resolver returns **Shared**, the operational provider hands back
`ApplicationDbContext`, and the TenantAdmin can still authenticate — no crash, no data loss,
dedicated DB left intact. `Enable Routing` again → resolver returns **Dedicated** and the
provider hands back `TenantDbContext`. Both verified by the runner.

## Audit events

- `TENANT_FULL_CUTOVER_VERIFIED` — written (shared `AuditTrails`) when the full module cycle
  passes.
- `TENANT_FULL_CUTOVER_FAILED` — written when it fails.

Both include TenantId, tenant name, dedicated database name, and the performing actor.

## QA results

- **Overall: PASS — 39/39 steps.**
- Dedicated DB contained all 25 verified operational tables with QA rows; shared DB contained
  none of them.
- Operational reports (SOA, statements, aging, valuation, movement intelligence, reorder, ABC,
  dashboard KPIs) all read the QA data from the dedicated DB.
- Rollback + re-enable verified.

See `docs/Phase50D2_FullCycleQAReport.md` for the full per-step table and row counts.
