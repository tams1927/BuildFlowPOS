# Phase 5.0D.1 — Full-Cycle Pilot QA Report

- **Test run:** 2026-06-01 12:50:26 (local), id `20260601_125022`
- **Runner:** dev-only `Tools/Phase50D1QaRunner.cs` via `dotnet run -- --qa-phase50d1` (web server NOT started)
- **Environment:** Development
- **Tenant created:** `QA_Tenant_20260601_125022` (TenantId = 5)
- **Dedicated database:** `QA_TenantDb_20260601_125022`
- **Overall result:** ✅ PASS — 20/20 steps passed

## Steps

| # | Step | Result | Detail |
|---|------|--------|--------|
| 1 | Ensure SuperAdmin exists | PASS | superadmin present |
| 2 | Create Tenant | PASS | TenantId=5, Mode=Dedicated, Db=QA_TenantDb_20260601_125022 |
| 3 | Create TenantAdmin | PASS | qa_admin_20260601_125022 (TenantAdmin) |
| 4 | Provision dedicated DB | PASS | Dedicated database 'QA_TenantDb_20260601_125022' provisioned successfully. The tenant continues to use the shared database (routing inactive). (created=True, migrated=True, seeded=True) |
| 5 | Test connection (dedicated) | PASS | Connection successful. |
| 6 | Migrate tenant data | PASS | Migrated 1 rows across 23 tables. All counts validated. (rows=1, tables=23) |
| 7 | Enable routing | PASS | RoutingEnabled=true; resolver cache invalidated |
| 8 | Diagnostics (Provisioned/Migrated/Routing/Runtime=Dedicated) | PASS | Provisioned=True, DataMigrated=True, RoutingEnabled=True, RuntimeDatabase=Dedicated |
| 9 | Operational context routes to TenantDbContext | PASS | Resolved context = TenantDbContext |
| 10 | Create Branch/Category/Unit/Supplier/Customer (dedicated) | PASS | BranchId=1, CategoryId=1, UnitId=1, SupplierId=1, CustomerId=1 |
| 11 | Create Item (dedicated) | PASS | ItemId=1, Code=QAITM20260601_125022 |
| 12 | Create Stock-In header + detail (dedicated) | PASS | StockInId=1, Details=1, Item.CurrentStock=10 |
| 13 | Create POS Sale header + detail (dedicated) | PASS | SaleId=1, Details=1 |
| 14 | Dedicated DB contains all QA operational rows | PASS | Branches=1, Categories=1, Units=1, Items=1, Suppliers=1, Customers=1, StockInHeaders=1, StockInDetails=1, SalesHeaders=1, SalesDetails=1 |
| 15 | Shared DB contains NONE of the QA operational rows | PASS | Branches=0, Categories=0, Units=0, Items=0, Suppliers=0, Customers=0, StockInHeaders=0, SalesHeaders=0 |
| 16 | Report reads see QA item/sale from dedicated DB | PASS | Dashboard/Inventory items=1, Inventory qty=9.000, SalesHistory=1, SalesReport total=80.00 |
| 17 | Rollback: runtime database returns Shared | PASS | RuntimeDatabase=Shared |
| 18 | Rollback: diagnostics show RoutingEnabled=false | PASS | RoutingEnabled=False |
| 19 | Rollback: TenantAdmin can still authenticate | PASS | password check passed, user active |
| 20 | Rollback: provider returns ApplicationDbContext | PASS | Resolved context = ApplicationDbContext |

## Dedicated DB row verification (expected > 0)

| Table | Rows in Dedicated |
|-------|-------------------|
| Branches | 1 |
| Categories | 1 |
| Units | 1 |
| Items | 1 |
| Suppliers | 1 |
| Customers | 1 |
| StockInHeaders | 1 |
| StockInDetails | 1 |
| SalesHeaders | 1 |
| SalesDetails | 1 |

## Shared DB row verification (expected 0 for QA operational rows)

| Table | Rows in Shared |
|-------|----------------|
| Branches | 0 |
| Categories | 0 |
| Units | 0 |
| Items | 0 |
| Suppliers | 0 |
| Customers | 0 |
| StockInHeaders | 0 |
| SalesHeaders | 0 |

> Platform rows (AspNetUsers, Tenants, SubscriptionPlans, RolePermissions) intentionally remain in the shared DB.

## Routing & rollback

- Routing verification: Dedicated confirmed
- Rollback result: ✅ fell back to Shared, no crash, tenant can authenticate, RoutingEnabled=false

## Notes

- Dedicated database was NOT deleted (per spec).
- Cleanup skipped (QA:EnableCleanup not true). Test data retained with QA_ prefix.

## Bugs found & resolution

**BUG-01 (environment) — Pending EF migration not applied to the shared dev database.**

- **Symptom (first run):** `Microsoft.Data.SqlClient.SqlException 207: Invalid column name 'DataMigrated' / 'DataMigratedAtUtc' / 'RoutingEnabled'`.
- **Where:** `ApplicationDbContext.SaveChangesAsync` when inserting the `Tenant` (also affects every read of `Tenants` through the resolver).
- **Root cause:** Phase 5.0D added migration `20260601040730_AddPilotRoutingFlags` (adds 3 columns to `Tenants`), but `dotnet ef database update` had never been run against `HardwareManagementSystemDb`. Model/code expected the columns; the live schema did not have them. This is a deployment/environment gap, **not** a code defect — the migration itself is correct and purely additive.
- **Fix applied:** `dotnet ef database update --context ApplicationDbContext` (additive only: `DataMigrated bit`, `DataMigratedAtUtc datetime2 null`, `RoutingEnabled bit`). Non-destructive; no data loss.
- **Recommendation:** Treat "apply pending migrations" as a required deploy step. Optionally fail-fast on startup when `context.Database.GetPendingMigrations()` is non-empty (log a clear error), so this is caught before runtime rather than on first tenant write.

After applying the migration, the full cycle was re-run and **all 20 steps passed**. No code/logic bugs were found in the routing, provisioning, migration, or rollback paths.

## Cleanup

Cleanup is OFF by default. To remove QA data and DROP the dedicated database, set
`QA:EnableCleanup = true` (e.g. in appsettings.Development.json) and re-run the runner,
or drop manually: `DROP DATABASE [<QA_TenantDb_...>]` and delete the QA tenant/user rows.
