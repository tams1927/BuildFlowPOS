# Phase 5.0D — Pilot Tenant Runtime Routing

> **Goal:** one pilot tenant (e.g. *abc store*) runs on its **dedicated** database at
> runtime, while every other tenant — and SuperAdmin, Identity, and subscriptions —
> stays on the **shared** database. Routing is opt-in per tenant and instantly
> reversible.

---

## 1. Routing Rules

A tenant's runtime database is **Dedicated** only when **all** of the following hold
(`Tenant.RoutingActive`):

| Condition | Field |
|-----------|-------|
| Dedicated mode | `DatabaseMode == Dedicated` |
| Provisioned | `DatabaseProvisionedAtUtc != null` and `ConnectionString` set |
| Data migrated + validated | `DataMigrated == true` |
| Routing switch on | `RoutingEnabled == true` |

If any condition is false → runtime database is **Shared** (`ApplicationDbContext`).

`ITenantDatabaseResolver`:
- `IsRoutingActiveAsync(tenantId)` → the gate above.
- `GetRuntimeDatabaseAsync(tenantId)` → `"Dedicated"` or `"Shared"`.
- `Invalidate(tenantId)` → clears the 5-minute metadata cache after any flag change.

---

## 2. Migration Flow

`ITenantDataMigrationService.MigrateAsync(tenantId)`:

1. **Validate** — tenant exists, Dedicated mode, provisioned, not already migrated.
2. **Load** the tenant's operational rows from the shared database in dependency order.
3. **Clear** the destination operational tables (children → parents) so the copy is repeatable.
4. **Bulk copy** each table using `SqlBulkCopy` with `SqlBulkCopyOptions.KeepIdentity`
   inside a single transaction — **primary keys, document numbers, dates and status are
   preserved exactly**.
5. **Validate** row counts per table (shared vs dedicated).
6. **Commit** only if every count matches; otherwise **roll back** entirely.
7. On success: set `DataMigrated = true`, `DataMigratedAtUtc = now`, invalidate cache.

> The shared database is **never modified**. Migration is a copy.

### Tables copied (dependency order)

`SystemSettings → Branches → Categories → Units → Suppliers → Customers → Items →
BranchProductStocks → UserBranches → SalesHeaders → SalesDetails → CustomerLedgers →
StockInHeaders → StockInDetails → Expenses → PurchaseOrders → PurchaseOrderItems →
Quotations → QuotationItems → DeliveryReceipts → DeliveryReceiptItems → Notifications →
AuditTrails`

Tenant scoping: tables with a direct `TenantId` are filtered by it; child tables
without one (`SalesDetails`, `StockInDetails`, `CustomerLedgers`, `*Items`, `UserBranches`)
are filtered by their parent's id set.

> **Scope note (for 5.0D.1 review):** this copy set matches the Phase 5.0D spec exactly.
> It does **not** yet include `SalesReturns`, `StockAdjustments`, `BranchTransfers`,
> `SupplierPayments`, or `Import*`. Decide in the verification sprint whether the pilot
> tenant needs those before enabling routing in production.

---

## 3. Validation

After the copy, the service compares row counts for every table:

```
Branches    Shared: 3    Dedicated: 3   ✔
Items       Shared: 128  Dedicated: 128 ✔
Customers   Shared: 55   Dedicated: 55  ✔
```

Any mismatch → the transaction is rolled back, `DataMigrated` stays `false`, and a
`TENANT_DATA_MIGRATION_FAILED` audit entry is written. The per-table comparison is
returned in `TenantDataMigrationResultVm.Tables` and surfaced via toast.

---

## 4. Dual-Context Safety (opt-in abstraction)

`ITenantOperationalDbContext` is implemented by **both** `ApplicationDbContext` (shared)
and `TenantDbContext` (dedicated). It exposes only operational entity sets +
`SaveChangesAsync` + `Database`.

`ITenantOperationalContextProvider` resolves the right context for the current tenant:

```
provider.GetContextAsync()
   → routing active?  yes → dedicated TenantDbContext (created via factory, owned + disposed per request)
                       no → shared ApplicationDbContext
```

Existing controllers/services that inject `ApplicationDbContext` directly are **unchanged
and never break** — they always use the shared database. A service starts routing to the
dedicated database for routing-enabled tenants only after it is migrated to depend on
`ITenantOperationalContextProvider`. That incremental cutover of business operations is
the subject of the **5.0D.1 verification sprint**.

---

## 5. Rollback Process

`Disable Routing` sets `RoutingEnabled = false`, invalidates the cache, and audits
`TENANT_ROUTING_DISABLED`. The tenant **instantly** falls back to the shared database on
the next request. **No data is deleted** from either database. Re-enabling is a single
click (data is still present and validated).

---

## 6. Pilot Tenant Procedure (SuperAdmin)

1. **Edit tenant** → set Database Mode = **Dedicated**, set a Database Name.
2. **Provision Database** (Phase 5.0C) → creates + migrates + seeds the dedicated DB.
3. **Migrate Data** → copies + validates operational rows.
   - SweetAlert: *“Copy tenant data to dedicated database? No shared data will be removed. Proceed?”*
4. **Enable Routing** → switches the tenant to its dedicated database.
   - SweetAlert: *“Enable dedicated database routing? Only this tenant will use the dedicated database. Rollback remains available. Proceed?”*
5. **Verify** via Diagnostics → `Current Runtime Database = Dedicated`.
6. **Rollback** anytime via **Disable Routing**.

---

## 7. Diagnostics

`Tenants/DatabaseInfo/{id}` now shows: Provisioned, **Data Migrated**, **Routing Enabled**,
**Current Runtime Database** (Dedicated/Shared), plus the existing routing/connection info.

---

## 8. Audit Events

| Event | When |
|-------|------|
| `TENANT_DATA_MIGRATION_STARTED` | Migrate Data invoked |
| `TENANT_DATA_MIGRATION_COMPLETED` | Copy validated + committed (includes per-table counts) |
| `TENANT_DATA_MIGRATION_FAILED` | Validation/runtime failure (rolled back) |
| `TENANT_ROUTING_ENABLED` | Routing switched on |
| `TENANT_ROUTING_DISABLED` | Rollback to shared |

Each records Tenant Id, Tenant Name, Database Name, Performed By, and IP address.

---

## 9. Success Criteria

For pilot tenant **abc store**: `Provisioned = Yes`, `Data Migrated = Yes`,
`Routing Enabled = Yes`, `Runtime Database = Dedicated`.
For every other tenant: `Runtime Database = Shared`. SuperAdmin stays on
`ApplicationDbContext`; Identity and subscriptions remain shared.

---

## 10. Recommended next step — 5.0D.1 verification sprint

Routing is the biggest architectural change in the project. Before expanding beyond the
pilot, prove end-to-end that the pilot tenant can — entirely from the dedicated database —
create items, stock-in, sell, report, and log audits. That requires migrating the relevant
business services/controllers to `ITenantOperationalContextProvider` and exercising them
against a routing-enabled tenant.
