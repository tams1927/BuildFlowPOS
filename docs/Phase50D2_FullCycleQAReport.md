# Phase 5.0D.2 + 5.0D.4 — Full & Final Module Cutover QA Report

- **Test run:** 2026-07-15 05:04:31 (local), id `20260715_050421`
- **Runner:** dev-only `Tools/Phase50D2QaRunner.cs` via `dotnet run -- --qa-phase50d2` (web server NOT started)
- **Environment:** Development
- **Tenant created:** `QA_Tenant_20260715_050421` (TenantId = 4)
- **Dedicated database:** `QA_TenantDb_20260715_050421`
- **Overall result:** FAIL — 5/6 steps passed

## Steps

| # | Step | Result | Detail |
|---|------|--------|--------|
| 1 | Ensure SuperAdmin exists | PASS | superadmin present |
| 2 | Create Tenant (Dedicated) | PASS | TenantId=4, Db=QA_TenantDb_20260715_050421 |
| 3 | Create TenantAdmin | PASS | qa_admin_20260715_050421 |
| 4 | Provision dedicated DB | PASS | Dedicated database 'QA_TenantDb_20260715_050421' provisioned successfully. The tenant continues to use the shared database (routing inactive). (created=True, migrated=True, seeded=True) |
| 5 | Test connection (dedicated) | PASS | Connection successful. |
| 6 | Migrate tenant data | FAIL | Validation failed — row counts differ: SystemSettings (1≠2). Migration rolled back. (rows=2, tables=37) |

## Dedicated DB row verification (expected > 0)

| Table | Rows in Dedicated |
|-------|-------------------|

## Shared DB row verification (expected 0 for QA operational rows)

| Table | Rows in Shared |
|-------|----------------|

> Platform rows (AspNetUsers, AspNetRoles, Tenants, SubscriptionPlans, RolePermissions) intentionally remain in the shared DB.

## Routing & rollback

- Rollback result: see steps

## Notes

- STOPPED: Migration failed.
- Cleanup skipped (QA:EnableCleanup not true). Test data retained with QA_ prefix.
