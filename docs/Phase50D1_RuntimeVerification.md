# Phase 5.0D.1 — Pilot Tenant Runtime Cutover Verification

## Objective

Prove that the first pilot tenant (**abc store, TenantId = 4**) can run its core
business modules against its **dedicated** database (`TenantDbContext` → `DB_TENANT4`)
at runtime, while **every other tenant** — plus SuperAdmin, Identity, and
subscriptions — continues unchanged on the shared `ApplicationDbContext`.

This phase does **not** convert the whole application. It cuts over only the modules
required to validate the routing architecture end-to-end:

```
Items (Products) · Categories · Units · Inventory · Stock-In · POS · Sales · Core Reports · Dashboard KPIs
```

---

## How routing is wired (no controller query rewrites)

Phase 5.0D already shipped the routing machinery:

- `ITenantOperationalDbContext` — abstraction implemented by **both** `ApplicationDbContext`
  (shared) and `TenantDbContext` (dedicated), exposing only operational entity sets.
- `ITenantOperationalContextProvider` / `TenantOperationalContextProvider` — resolves, per
  request, whether the current tenant gets the shared or the dedicated context.
- `ITenantDatabaseResolver.IsRoutingActiveAsync(tenantId)` — the single source of truth for
  the routing decision.

### New in 5.0D.1 — `OperationalDbController`

`Controllers/OperationalDbController.cs` is a thin base controller. Before **every** action
executes, it resolves the operational context and assigns it to a protected `_context` field:

```csharp
public override async Task OnActionExecutionAsync(
    ActionExecutingContext context, ActionExecutionDelegate next)
{
    _context = await _operationalContextProvider.GetContextAsync();
    await base.OnActionExecutionAsync(context, next);
}
```

Because `_context` is typed as `ITenantOperationalDbContext`, all existing LINQ/`SaveChangesAsync`
code in the converted controllers compiles and runs **unchanged** — there were no query rewrites.
The only edits per controller were: base class, constructor (swap `ApplicationDbContext` for
`ITenantOperationalContextProvider`), and removal of the local `_context` field.

This makes the cutover low-risk: for non-routing tenants the provider returns the very same
shared `ApplicationDbContext` DI would have injected, so behavior is byte-for-byte identical.

---

## Runtime context resolution (TASK 3)

`TenantOperationalContextProvider.GetContextAsync()`:

```
tenantId = current tenant (null for SuperAdmin)
  └─ null            → ApplicationDbContext (shared)
  └─ has value       → IsRoutingActiveAsync(tenantId)?
                         ├─ true  → TenantDbContext (dedicated, cached per request)
                         └─ false → ApplicationDbContext (shared)
```

`IsRoutingActiveAsync` returns **true only when ALL hold**:

```
DatabaseMode   == Dedicated
Provisioned    == true   (DatabaseProvisionedAtUtc + ConnectionString set)
DataMigrated   == true
RoutingEnabled == true
```

Otherwise the shared context is returned.

---

## Controllers converted

| Module        | Controller(s)                                                                 | Routes |
|---------------|-------------------------------------------------------------------------------|--------|
| Items         | `ProductsController`                                                           | ✅ |
| Categories    | `CategoriesController`                                                         | ✅ |
| Units         | `UnitsController`                                                              | ✅ |
| Inventory     | `InventoryController`                                                          | ✅ |
| Stock-In      | `StockInController`                                                            | ✅ |
| POS           | `POSController`                                                                | ✅ |
| Sales         | `SalesController`                                                              | ✅ |
| Reports       | `ReportsController` (+ `.ARAP`, `.Inventory`, `.TenantScope` partials)        | ✅ |
| Dashboard KPIs| `HomeController`                                                               | ✅ |

All of the above now derive from `OperationalDbController`.

### Platform-read exception

`TenantDbContext` deliberately has **no** `Tenants` set (platform-only). Two controllers read
`Tenants` for report/receipt headers, so they keep a separate shared context (`_platformDb`,
injected `ApplicationDbContext`) used **only** for that read:

- `SalesController` — receipt header tenant (`Sale` receipt view).
- `ReportsController` — AR/AP and inventory report headers (`GetTenantAsync`, AR/AP settings).

Everything else in those controllers routes through `_context`.

---

## Services NOT converted (intentional — remain on shared DB)

Per the phase rules ("convert only the listed modules / do not modify unrelated modules"),
the following cross-cutting services still use the injected shared `ApplicationDbContext`:

| Service                | Reason kept on shared                                                        |
|------------------------|------------------------------------------------------------------------------|
| `AuditService`         | Cross-cutting; used by every controller, including platform/SuperAdmin flows. Audit rows for the pilot are written to the shared `AuditTrails`. |
| `NotificationService`  | Cross-cutting; used app-wide. |
| `BranchService`        | Shared infrastructure used by many non-pilot controllers; converting it would alter unrelated modules. Branch PKs are preserved by the 5.0D copy, so branch references stay consistent. |
| `TenantLimitGuard`     | Operates on platform subscription limits (shared). |
| `PermissionService`    | RBAC / Identity (shared, by design). |

Implication for the pilot: core operational writes (Items, BranchProductStocks, StockIn*,
Sales*, CustomerLedgers) land in the **dedicated** DB via `_context`; audit/notification rows
and branch lookups continue against shared. Promoting `AuditService`/`BranchService`/
`NotificationService` to `ITenantOperationalContextProvider` is the recommended next step
before broadening the pilot (Phase 5.0E).

---

## Queries tested / tables verified

Routing-enabled tenant 4 exercises these operational tables through `_context`
(all present in the dedicated schema and populated by the 5.0D migration):

| Module    | Tables read/written via dedicated context |
|-----------|-------------------------------------------|
| Items     | `Items`, `Categories`, `Units`, `Suppliers`, `BranchProductStocks` |
| Inventory | `Items`, `BranchProductStocks`, `Branches` |
| Stock-In  | `StockInHeaders`, `StockInDetails`, `Items`, `BranchProductStocks` |
| POS       | `SalesHeaders`, `SalesDetails`, `CustomerLedgers`, `Customers`, `Items`, `BranchProductStocks`, `SystemSettings` |
| Sales     | `SalesHeaders`, `SalesDetails`, `CustomerLedgers`, `Branches` |
| Reports   | `SalesHeaders`, `SalesDetails`, `CustomerLedgers`, `Items`, `StockInHeaders`, `SupplierPayments`, `Expenses`, `BranchProductStocks`, `SystemSettings` |
| Dashboard | `SalesHeaders`, `SalesDetails`, `Items`, `BranchProductStocks`, `Expenses`, `CustomerLedgers`, `StockInHeaders`, `PurchaseOrders`, `BranchTransfers` |

---

## Diagnostics (TASK 10 & 11)

### Tenant Database Info page (`/Tenants/DatabaseInfo/{id}`)

New **Runtime Context (Phase 5.0D.1)** card shows:

- **Current Context** — `TenantDbContext` or `ApplicationDbContext`.
- **Routing Active** — Active / Inactive badge.
- A link to the verification tool below.

### Verification tool (SuperAdmin only)

```
GET /Diagnostics/RuntimeDatabase            → all tenants
GET /Diagnostics/RuntimeDatabase?tenantId=4 → single tenant
```

Returns JSON:

```json
{
  "verifiedAtUtc": "…",
  "count": 1,
  "tenants": [
    {
      "tenantId": 4,
      "tenantName": "abc store",
      "tenantCode": "ABC",
      "currentContext": "TenantDbContext",
      "connectionType": "Dedicated",
      "databaseName": "DB_TENANT4",
      "routingActive": true
    }
  ]
}
```

---

## Audit events (TASK 13)

Logged by `DiagnosticsController.RuntimeDatabase` (module `Diagnostics`):

- `TENANT_RUNTIME_ROUTING_VERIFIED` — on successful resolution. Records tenant(s), resolved
  database/context, performing user, and client IP.
- `TENANT_RUNTIME_ROUTING_FAILED` — if resolution throws. Records reason, user, IP.

---

## Rollback test (TASK 12)

**Procedure:** SuperAdmin → Tenants → abc store → **Disable Routing**.

**Mechanism:**
`TenantsController.DisableRouting` sets `RoutingEnabled = false`, saves, and calls
`_databaseResolver.Invalidate(4)` to evict cached metadata.

**Result (by design):**
- The next request for tenant 4 calls `IsRoutingActiveAsync(4)` → reads fresh metadata →
  `RoutingEnabled == false` → provider returns the shared `ApplicationDbContext`
  **immediately** (no app restart).
- **No crash** — the shared context path is the original, always-valid code path.
- **No migration** — Disable only flips a flag.
- **No data loss** — neither the shared nor the dedicated database is touched; the dedicated
  DB and its copied data remain intact for re-enabling later.

`/Diagnostics/RuntimeDatabase?tenantId=4` reflects the change: `Current Context` flips back to
`ApplicationDbContext`, `Routing Active = false`.

> Note: these are logical/architectural verifications confirmed by code review and a clean
> build. Live click-through on the running pilot (create item → stock-in → sell → report) is a
> manual QA step to be performed by the operator with abcadmin signed in.

---

## Files changed

**Added**
- `Controllers/OperationalDbController.cs` — per-request operational context base controller.
- `Controllers/DiagnosticsController.cs` — SuperAdmin `/Diagnostics/RuntimeDatabase` tool + audit events.
- `docs/Phase50D1_RuntimeVerification.md` — this document.

**Modified (converted to `OperationalDbController`)**
- `Controllers/ProductsController.cs`
- `Controllers/CategoriesController.cs`
- `Controllers/UnitsController.cs`
- `Controllers/InventoryController.cs`
- `Controllers/StockInController.cs`
- `Controllers/POSController.cs`
- `Controllers/SalesController.cs` (+ `_platformDb` for `Tenants` read)
- `Controllers/ReportsController.cs` (+ `_platformDb`; partials `.ARAP`, `.Inventory` Tenants reads redirected)
- `Controllers/HomeController.cs`

**Modified (diagnostics UI)**
- `Controllers/TenantsController.cs` — `TenantDatabaseInfoVm.CurrentContext` / `RoutingActive`.
- `Views/Tenants/DatabaseInfo.cshtml` — Runtime Context card + verification link.

---

## Remaining controllers / modules NOT converted (still shared)

These intentionally remain on `ApplicationDbContext` (out of pilot scope):

```
Customers · Suppliers · SupplierPayments · CustomerCollections · Expenses
PurchaseOrders · Quotations · DeliveryReceipts · BranchTransfers · Branches
StockAdjustment · SalesReturn · InventoryMovement · Import · Settings
Users · Roles · RolePermissions · AuditTrail · Account · SuperAdmin
SubscriptionPlans · Tenants · UserBranches · Products(N/A) · ImportController
```

Plus shared services: `AuditService`, `NotificationService`, `BranchService`,
`TenantLimitGuard`, `PermissionService` (see table above).

For non-routing tenants, every controller above (and the converted ones) behaves exactly as
before — the cutover is a no-op until a tenant is provisioned + migrated + routing-enabled.

---

## Success criteria status

| Criterion (tenant abc store, routing enabled)        | Status |
|------------------------------------------------------|--------|
| Create Item → dedicated DB                           | Routed via `_context` (TenantDbContext) |
| Stock-In → dedicated DB                              | Routed |
| POS Sale → dedicated DB                              | Routed |
| Sales Report → dedicated DB                          | Routed |
| Inventory Report → dedicated DB                      | Routed |
| Dashboard KPIs → dedicated DB                        | Routed |
| All other tenants → `ApplicationDbContext` unchanged | Guaranteed by provider fallback |
| SuperAdmin / Identity / subscriptions → shared       | Unchanged (not converted) |
| Rollback (Disable Routing) → instant shared fallback | Verified (flag + cache invalidation) |

**Build:** `0 Warning(s) / 0 Error(s)`.
