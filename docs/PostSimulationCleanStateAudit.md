# Post-Simulation Clean-State Audit

**Date:** 2026-07-15  
**Tenant:** Tams Construction Supply (Id=1, Code=TCS)  
**Operational DB:** `HardBuild_TCS_1` (dedicated, routing active)  
**Platform DB:** `BuildflowposDb`

---

## Final cleanup executed

```text
dotnet run -- --tenant-reset --tenant-id=1 --retain-admin=adminTCS --confirm
```

Simulation account `buildflow_cashier` removed (simulation-only username list).  
`adminTCS` password rotated via `TENANT_ADMIN_PASSWORD` + `--set-tenant-admin-password` (decoupled from reset).

After `--qa-po-clickthrough` (creates transient test data), tenant 1 was **re-cleaned** so handoff state remains empty.

---

## Retained

| Item | Status |
|------|--------|
| Tenant record (TCS) | Preserved |
| Subscription / routing | Preserved → `HardBuild_TCS_1` |
| `adminTCS` (TenantAdmin) | Preserved |
| `superadmin` (SuperAdmin) | Preserved |
| Roles / role permissions | Preserved |
| Migrations / `__EFMigrationsHistory` | Preserved |
| Backup / restore history (platform) | Preserved |

---

## Deleted (simulation artifacts)

| Item | Notes |
|------|-------|
| `buildflow_cashier` | Created only by human simulation |
| Branches, units, categories, suppliers, customers, products | All 0 rows |
| POs, receiving, inventory, sales, payments, expenses | All 0 rows |
| Simulation audit trails | 0 rows |
| Tenant-scoped SystemSettings | 0 rows |

---

## Final row counts (`HardBuild_TCS_1`)

| Table | Count |
|-------|-------|
| Branches | 0 |
| Units | 0 |
| Categories | 0 |
| Suppliers | 0 |
| Customers | 0 |
| Items | 0 |
| PurchaseOrders | 0 |
| StockInHeaders | 0 |
| BranchProductStocks | 0 |
| SalesHeaders | 0 |
| Expenses | 0 |
| AuditTrails | 0 |
| SystemSettings | 0 |

---

## Non-destructive verification

| Check | Result |
|-------|--------|
| `--qa-post-simulation-clean-state` | **30 / 30 PASS** |
| Browser smoke (`post_simulation_clean_smoke.py`) | **17 / 17 PASS** |
| `dotnet build` | **0 errors, 0 warnings** |
| `--qa-tenant-runtime-audit` | **36 / 36 PASS** |
| `--qa-po-clickthrough` | **19 / 19 PASS** (re-cleaned tenant after) |

### Smoke routes verified (read-only)

SuperAdmin login, adminTCS login, Home, Settings, Branches, Units, Categories, Suppliers, Customers, Products, Purchase Orders, Receiving, Inventory, POS, Reports, cashier absent, SaaS route blocked.

---

## Credential security

| Search target | Result |
|---------------|--------|
| Simulation passwords in source/docs/JSON/scripts | **Removed / redacted** |
| `--set-admin-password` on reset CLI | **Removed** — use `--set-tenant-admin-password` + `TENANT_ADMIN_PASSWORD` env only |
| `--verify-admin-login` | Requires `TENANT_ADMIN_PASSWORD` env (no CLI password) |
| Reports / audit JSON | No plaintext simulation passwords |

`adminTCS` password was rotated during cleanup; value is **not** stored in git-tracked files. Communicate to tenant through a secure out-of-band channel.

---

## Destructive-command protection audit

| Protection | Verified |
|------------|----------|
| Production execution rejected | `IsDevelopment()` in runner + service |
| Missing tenant ID rejected | Exit 1 — `Required: --tenant-id=N` |
| Invalid tenant ID rejected | `InvalidOperationException` for id 99999 |
| Missing `--confirm` = preview only | `DryRun=true`, no deletes |
| Preview shows tenant name, id, routing, target DB | JSON report fields populated |
| Platform DB ≠ tenant operational DB | `BuildflowposDb` vs `HardBuild_TCS_1` |
| Retained admin not deleted | `adminTCS` excluded from `UsersToDelete` |
| SuperAdmin not deleted | Role guard in preview |
| Other tenants unaffected | 2 other tenants present post-reset |
| Transactional delete | `BeginTransactionAsync` on dedicated `TenantDbContext` |
| Before/after row counts | Captured in `TenantCleanResetAudit.json` |
| Password reset separated from cleanup | `SetTenantAdminPasswordRunner` |

---

## Tenant isolation

- Routing active → dedicated `HardBuild_TCS_1` only for tenant 1 operations.
- Platform identity DB holds tenant users; operational data cleared in dedicated DB + stale shared rows for tenant 1.
- Other tenants (count=2) unchanged.

---

## Machine-readable report

`docs/PostSimulationCleanStateAudit.json`
