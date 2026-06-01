# Phase 5.0D.4 — Final Operational Split-Brain Elimination

This phase converts the last tenant-operational modules that were still reading and writing
through the shared `ApplicationDbContext`, so that a routing-enabled tenant performs **all**
operational work against its dedicated `TenantDbContext`. Platform data (Identity, Tenants,
SubscriptionPlans, RolePermissions, SaaS administration) remains shared by design.

## Controllers converted

| Controller | Before | After | Platform reads kept on shared |
|------------|--------|-------|-------------------------------|
| `BranchesController` | `ApplicationDbContext` | `OperationalDbController` (routed `_context`) | none — all sets (`Branches`, `BranchProductStocks`, `StockInHeaders`, `SalesHeaders`, `Expenses`) are operational |
| `UserBranchesController` | `ApplicationDbContext` | `OperationalDbController` (routed `_context`) | Identity users via `UserManager` (shared) |
| `ImportController` | `ApplicationDbContext` | `OperationalDbController` (routed `_context`) | none — only `ImportBatches` / `ImportBatchRows` |
| `AuditTrailController` | `ApplicationDbContext` | `OperationalDbController` (routed `_context`) + injected `ApplicationDbContext _platformDb` | `Tenants` lookup (SuperAdmin) via `_platformDb` |

### Branches
`Create`, `Edit`, `Delete`/deactivate, `Activate`, `Deactivate`, `SetMainBranch`, `SwitchBranch`
and `Details` now operate on the routed context. For a routing-enabled tenant they persist to
the dedicated database; for every other tenant / SuperAdmin they use the shared context exactly
as before.

### User Branches
Branch **assignments** (`UserBranches`) and the branch dropdown (`Branches`) are operational →
routed. Identity **users** stay platform-level and are still read through `UserManager`. There is
**no cross-database FK assumption**: the user list is materialized first (`userIds.ToList()`) and
that in-memory list is passed into the dedicated query, and `UserBranch.UserId` is a plain string
(no FK to `AspNetUsers` in the dedicated DB).

### Import
The controller's batch bookkeeping (`ImportBatches` / `ImportBatchRows`) is routed. Because
`ITenantOperationalContextProvider` is scoped and caches one context per request, the controller
and `ExcelImportService` share the **same** context instance within a request — a batch created
by the controller is immediately visible to the service, and confirmed rows are written to the
same (dedicated) database.

### Audit Trail (read-side)
`AuditTrails` are operational and read through the routed context:
- **Routing-enabled tenant user** → sees their **dedicated** audit records.
- **Normal tenant / SuperAdmin** → shared `ApplicationDbContext` (platform-level audit), which is
  the runtime database for those principals.

The SuperAdmin `Tenants` name lookup is platform metadata and always reads from `_platformDb`
(shared).

> **Known cross-database limitation (documented, accepted):** When SuperAdmin filters the audit
> page by a *routing-enabled* tenant, SuperAdmin runs on the shared context and therefore sees
> only that tenant's shared/platform-level audit rows, not the operational audit rows that now
> live in the tenant's dedicated database. This matches the stated requirement ("SuperAdmin must
> still see platform-level audit data") and is consistent with the database-per-tenant model. A
> future cross-database audit aggregator would be a separate feature, not a split-brain bug.

## Services converted

| Service | Change |
|---------|--------|
| `ExcelImportService` | Replaced injected `ApplicationDbContext` with `ITenantOperationalContextProvider`. Every DB method (`BuildPreviewAsync`, `SavePreviewRowsAsync`, `ConfirmImportAsync`, `ImportProductsAsync`, `ImportOpeningStockAsync`, `ImportCustomersAsync`, `ImportSuppliersAsync`) resolves the routed context (`await provider.GetContextAsync()`). Catalog validation reads (Units/Categories/Suppliers/Items/Customers) and entity writes (Items/Customers/Suppliers) now route to the dedicated DB. `ReadHeaders` and `GenerateTemplate` touch no database. |
| `BranchService` | No change required — already fully routed in Phase 5.0D.2 (`GetCurrentBranchAsync`, `GetAllActiveBranchesAsync`, `GetAssignedBranchesAsync`, `SetCurrentBranchAsync`, stock mutations). |

## Import audit findings (Task 4)

`ExcelImportService` previously wrote `Items`, `Customers`, `Suppliers` and `ImportBatchRows`
directly to `ApplicationDbContext`. **All direct `ApplicationDbContext` access has been removed**;
the service now talks only to `ITenantOperationalDbContext` via the provider. No platform tables
are written by the import service.

Note on import types: the system's import types are `Products`, `OpeningStock`, `Customers`,
`Suppliers`. There are no standalone "Categories"/"Units" import types — categories and units are
referenced by `Products` import and must already exist (created via their own modules, which are
already routed). The QA exercises the real, supported import types end-to-end.

## Branch selector verification (Task 6)

`Views/Shared/_Layout.cshtml` resolves the branch selector entirely through `BranchService`
(`GetCurrentBranchAsync`, `GetAllActiveBranchesAsync`, `GetAssignedBranchesAsync`), which is
routed. There are **no stale shared-db branch reads** in the layout. For a routing-enabled tenant
the current branch name, assigned-branch dropdown, and newly created/assigned branches all come
from the dedicated database immediately. No `_Layout.cshtml` or `BranchService` change was needed.

## QA results

Runner: `Tools/Phase50D2QaRunner.cs` (extended), dev-only via `dotnet run -- --qa-phase50d2`
(web server not started). Latest run: **OVERALL PASS — 50/50 steps**.

New Phase 5.0D.4 steps added and passed:
- Set Main Branch (single-main invariant enforced through routed context)
- Assign User Branch (operational assignment; user stays platform-level)
- Write tenant audit record to dedicated context
- Import Suppliers / Customers / Items (Products) through the **real** `ExcelImportService`,
  routed by a simulated TenantAdmin principal (proves ambient-context routing, not just the
  forced `GetContextAsync(tenantId)` overload)
- Read Audit Entries from dedicated (not shared)
- Branch selector reads dedicated assignments

### Dedicated DB verification (expected > 0)
All new entities present in the dedicated DB: `Branches=2`, `UserBranches=1`, `AuditTrails(QA)=1`,
`Imported Supplier=1`, `Imported Customer=1`, `Imported Item=1` (alongside the full 5.0D.2
operational set).

### Shared DB verification (expected 0)
None of the QA operational rows leaked into the shared DB, including `UserBranches=0`,
`AuditTrails(QA)=0`, `Imported Supplier=0`, `Imported Customer=0`, `Imported Item=0`.

### Rollback / re-enable
- Disable routing → runtime returns **Shared**, TenantAdmin still authenticates, no crash, no data
  loss (dedicated DB not deleted).
- Re-enable routing → runtime returns **Dedicated**, provider returns `TenantDbContext`,
  branches/assignments/imported records visible again.

## Audit events (Task 12)

`TENANT_FINAL_CUTOVER_VERIFIED` / `TENANT_FINAL_CUTOVER_FAILED` are written (platform-level,
shared) by the QA runner, including `TenantId`, `TenantName`, runtime database, performer
("QA Runner (Phase 5.0D.4)") and IP.

## Remaining shared-by-design components

- Identity: `AspNetUsers`, `AspNetRoles`, role/user management
- `Tenants`, `SubscriptionPlans`, `RolePermissions`
- SuperAdmin dashboard, SaaS administration, billing/subscription data
- SuperAdmin runtime context (SuperAdmin always operates on the shared `ApplicationDbContext`)

## Build

`dotnet build` → **Build succeeded. 0 Warning(s) 0 Error(s)**.
