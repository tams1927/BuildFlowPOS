# Pilot Certification Report — RC1.3

**Product:** Hardware Supply POS SaaS  
**Certification date:** 2026-06-05  
**Environment:** Development (`LAPTOP-GT6DNNRD\MSSQL_SYSDEV`)  
**Certification tenant:** `QA_Tenant_20260605_130715` (TenantId=12)  
**Dedicated database:** `QA_TenantDb_20260605_130715`  
**Method:** Automated QA runners + static code review (no code changes in RC1.3)

---

## Executive Summary

| Metric | Value |
|--------|-------|
| **Verdict** | **PILOT NOT CERTIFIED** |
| **Pilot Readiness Score** | **70 / 100** |
| **Build** | 0 Errors, 0 Warnings |
| **QA suites passed** | 2 of 3 |
| **Blocking issue** | Phase 5.0D.2 full-module cutover **FAIL** at product creation (`FK_Items_Units_BaseUnitId`) |

---

## SECTION A — Architecture Status

| Component | Status | Notes |
|-----------|--------|-------|
| Shared platform DB (`ApplicationDbContext`) | **Ready** | Identity, tenants, subscriptions, role permissions |
| Tenant operational DB (`TenantDbContext`) | **Ready** | Phase51 + RC12 migrations apply via provision |
| Multi-tenant routing | **Partial** | Dedicated routing verified ON; rollback cycle not completed in this run |
| SaaS layer (Tenants, Plans, SuperAdmin) | **Ready** | `[Authorize(Roles = "SuperAdmin")]` on platform controllers |
| Unit conversion foundation (Phase 5.1) | **Ready** | Validated on dedicated routed tenant |
| Cost clarity (RC1.2) | **Ready** | Validated via RC12 QA |

**Architecture unchanged in RC1.3** — validation only.

---

## SECTION B — Dedicated Database Status

Certification tenant created by `--qa-phase50d2`:

| Check | Result | Detail |
|-------|--------|--------|
| Database created | **PASS** | `QA_TenantDb_20260605_130715` |
| Migrations applied | **PASS** | `MigrateAsync` succeeded (incl. Phase51, RC12) |
| Tenant metadata updated | **PASS** | `ConnectionString`, `DatabaseProvisionedAtUtc`, `LastDatabaseMigration` set |
| Connection test | **PASS** | SQL connection successful |
| Data migration (shared → dedicated) | **PASS** | 1 row / 24 tables migrated |
| Routing enabled | **PASS** | `RoutingEnabled=true`, `RuntimeDatabase=Dedicated` |
| Routing disabled | **NOT RUN** | Phase50D2 aborted before rollback steps |
| Routing re-enabled | **NOT RUN** | Phase50D2 aborted before rollback steps |

**Exception (blocking for full cutover):**

```
FK_Items_Units_BaseUnitId — Item insert without BaseUnitId fails on dedicated DB.
```

Phase50D2 QA runner creates `Item` without `BaseUnitId` (post–Phase 5.1 regression in test harness).  
`ProductsController` **does** set `BaseUnitId` in the UI path — production item creation via Products is expected to work.

---

## SECTION C — Routing Status

| Runtime state | Expected context | Verified |
|---------------|------------------|----------|
| Routing OFF | `ApplicationDbContext` | Implicit (default tenants) |
| Routing ON | `TenantDbContext` | **PASS** — `TenantOperationalContextProvider` resolved `TenantDbContext` |

**Leakage checks (Phase51 on routed tenant 12):**

| Entity | Dedicated only | Shared leak |
|--------|------------------|-------------|
| Items / ItemUnitConversions | **PASS** | None detected |
| Stock-In details | **PASS** | Written to dedicated DB |
| Sales (POS) | **PASS** | Written to dedicated DB |
| SystemSettings (currency) | **PASS** | Shared DB not updated while routing ON |

**Not verified in this run (Phase50D2 abort):** PO, Expenses, Audit bulk module writes, rollback routing cycle.

---

## SECTION D — Unit Conversion Status

**Scenario:** QA Nail — base unit **kg**, conversion **1 sack = 25 kg**

| Step | Expected | Result |
|------|----------|--------|
| Stock-In 10 sacks | 250 kg inventory | **PASS** (QA51) |
| Cost 10 sacks @ ₱1,000/sack | ₱40/kg, ₱10,000 total | **PASS** (RC12) |
| POS sale 2.5 kg | 247.5 kg remaining | **PASS** (QA51) |
| StockInDetail fields | Received + base qty stored | **PASS** (QA51) |

**Reports:** Inventory valuation report views still use hardcoded `₱` (see Section E) — numeric stock/cost logic validated at data layer.

---

## SECTION E — Currency Status

| Area | Dynamic (`ViewBag.CurrencySymbol` / settings) | Hardcoded `₱` |
|------|-----------------------------------------------|---------------|
| POS | **Yes** | — |
| Settings save (USD → $) | **PASS** (QA51) | — |
| Dedicated isolation | **PASS** — shared DB not updated when routed | — |
| Dashboard / Home KPIs | Partial | Some report KPIs |
| Receipt / Quotation / DR / PO print | Partial | Several print/detail views |
| Inventory Valuation report | **No** | `Views/Reports/InventoryValuationReport.cshtml` |
| AR/AP reports | **No** | CustomerAging, SupplierAging, SupplierPayables, etc. |
| Expenses index | **No** | `Views/Expenses/Index.cshtml` |
| Subscription Plans (SaaS) | **No** | Intentionally platform pricing in ₱ |

**USD test (QA51):** `CurrencyCode=USD`, `CurrencySymbol=$`, `CurrencyName=US Dollar` — saved to dedicated DB, restored to PHP/₱/Philippine Peso.

**Hardcoded `₱` in views (52 `.cshtml` files)** — primarily under `Views/Reports/*`, plus PO Create/Edit JS totals, Expenses, SubscriptionPlans, some statements. Full list in appendix.

---

## SECTION F — Security Status

| Control | Status | Notes |
|---------|--------|-------|
| `SeedSettings:InitialSuperAdminPassword` | **WARN** | Not configured in dev — fallback `SuperAdmin123!` with log warning |
| New SuperAdmin `ForcePasswordChange` | **PASS** | DbSeeder sets `true` on create |
| Live dev SuperAdmin | **WARN** | Pre-existing account: `ForcePasswordChange=false` |
| Login → Change Password redirect | **PASS** | `AccountController` checks flag |
| Tenant isolation | **PASS** | QA51 leak checks; `TenantGuard` on operational controllers |
| Branch isolation | **Designed** | `BranchService` locks BranchManager/Cashier to assigned branch — not re-tested in RC1.3 automation |
| SaaS controllers | **PASS** | `TenantsController`, `SuperAdminController`, `SubscriptionPlansController` — SuperAdmin role only |
| `PermissionAuthorize` on operational modules | **PASS** | 30+ controllers use attribute |
| Uncovered controllers | **INFO** | `AccountController` (auth), `DiagnosticsController` (SuperAdmin), `NotificationsController` (permission-gated) |

---

## SECTION G — Permissions Status

**Roles in system:** `SuperAdmin`, `TenantAdmin`, `BranchManager`, `Cashier`, `InventoryStaff`

> Task spec referenced **Manager** — no `Manager` role exists; closest equivalent is **BranchManager**.

| Role | Tenants / Subscriptions | Users | Reports | Inventory | Settings |
|------|-------------------------|-------|---------|-----------|----------|
| **SuperAdmin** | Full (role gate) | Full | Full | N/A (platform) | Platform |
| **TenantAdmin** | Denied (SaaS modules false) | Full (role + permission) | Full (seeded) | Full (seeded) | Full |
| **BranchManager** | Denied | View/no delete on Users | Full (seeded) | Full (seeded) | View/no delete |
| **Cashier** | Denied | Denied* | Denied* | Denied* | Denied* |
| **InventoryStaff** | Denied | Denied* | Denied* | Denied* | Denied* |

\* **Risk:** `Cashier` and `InventoryStaff` roles are created at startup but **not** seeded with `RolePermissions` rows in `DbSeeder` (only SuperAdmin, TenantAdmin, BranchManager are seeded). Users in these roles are denied all `PermissionAuthorize` modules until permissions are assigned manually via Role Permissions UI or tenant provisioning.

**Enforcement mechanism:** `PermissionAuthorizeAttribute` → `PermissionService.HasPermissionAsync` → redirect to `Account/AccessDenied`.

---

## SECTION H — Inventory Status

| Capability | Status |
|------------|--------|
| Branch-level stock (`BranchProductStock`) | **PASS** (QA51) |
| Unit conversion on stock-in | **PASS** |
| Cost per received unit → base unit | **PASS** (RC12) |
| POS deduction in base units | **PASS** (247.5 kg) |
| Stock adjustment / transfers | **NOT RUN** (Phase50D2 abort) |
| Inventory intelligence reports | **Not validated** in RC1.3 |

---

## SECTION I — Report Status

| Report area | Automated validation |
|-------------|---------------------|
| Inventory valuation (data) | Indirect via stock/cost QA |
| Inventory valuation (UI currency) | **Not certified** — hardcoded ₱ |
| SOA / aging / AR/AP | **Not run** (Phase50D2 abort) |
| Dashboard KPIs | **Not run** |
| PDF exports | **Not run** |

---

## SECTION J — Known Risks

1. **Phase50D2 full cutover FAIL** — QA harness incompatible with Phase 5.1 `BaseUnitId` FK; blocks automated certification of full 12-step workflow.
2. **Routing rollback not exercised** in this certification run.
3. **SuperAdmin default credentials** on dev DB — must set `InitialSuperAdminPassword` and rotate before production.
4. **52 views** with hardcoded `₱` — multi-currency display incomplete on reports/statements.
5. **Cashier / InventoryStaff** permissions not auto-seeded — first real tenant must configure role permissions.
6. **Phase50D2 test data retained** (`QA:EnableCleanup` not true) — manual cleanup of `QA_TenantDb_20260605_130715` optional.

---

## SECTION K — GO / NO-GO Recommendation

### **NO-GO for first real tenant onboarding**

**Rationale:**

- Full end-to-end cutover suite (**Phase50D2**) **failed** before completing PO → receive → expenses → collections → supplier payments → reports → routing rollback.
- Routing disable/re-enable cycle **not validated** in this run.
- SuperAdmin security **not production-ready** on current dev instance.
- Multi-currency **settings work** but **report/UI layer** still largely PHP-hardcoded.

**Conditions to reach GO:**

1. Fix or update Phase50D2 QA runner (`BaseUnitId` on item create) and achieve **OVERALL PASS**.
2. Complete routing rollback/re-enable verification.
3. Set production `SeedSettings:InitialSuperAdminPassword`; confirm fresh SuperAdmin forced password change.
4. Seed or document Cashier/InventoryStaff default permissions for pilot tenant.
5. Accept or remediate hardcoded `₱` on pilot-critical reports (valuation, AR/AP).

---

## TASK 1 — QA Results Summary

| Suite | Command | Passed | Failed | Warnings | Exceptions | Time |
|-------|---------|--------|--------|----------|------------|------|
| Phase 5.0D.2 | `--qa-phase50d2` | 9 steps | **1 (overall)** | DbSeeder ×2 (SuperAdmin password) | `DbUpdateException` FK_Items_Units_BaseUnitId | **~6.0s** |
| Phase 5.1 | `--qa-phase51` | **8** | 0 | — | — | **~3.7s** |
| RC1.2 | `--qa-rc12` | **18** | 0 | EF `FirstOrDefault` without OrderBy ×4 | — | **~3.3s** |

**Phase50D2 detail:** Steps 1–9 passed (tenant create → provision → migrate → route → master data). Failed at step 10 (Create Product). Report: `docs/Phase50D2_FullCycleQAReport.md`.

---

## TASK 8 — Manual Workflow Validation

| # | Workflow step | RC1.3 status |
|---|---------------|--------------|
| 1 | Create Supplier | **PASS** (Phase50D2 step 9) |
| 2 | Create Customer | **PASS** (Phase50D2 step 9) |
| 3 | Create Item | **FAIL** (Phase50D2 — BaseUnitId FK) / **PASS** via Products UI path (code review) |
| 4 | Create Purchase Order | **NOT RUN** |
| 5 | Receive Stock | **NOT RUN** (PO path) / **PASS** stock-in (QA51) |
| 6 | Create Sale | **PASS** (QA51 POS) |
| 7 | Print Receipt | **NOT RUN** |
| 8 | Create Quotation | **NOT RUN** |
| 9 | Create Delivery Receipt | **NOT RUN** |
| 10 | Record Collection | **NOT RUN** |
| 11 | Record Supplier Payment | **NOT RUN** |
| 12 | View Reports | **NOT RUN** |

**Interactive browser manual test:** Not performed in RC1.3 (automation-only certification).

---

## TASK 9 — Build Validation

```
dotnet build
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Pilot Readiness Score Breakdown

| Category | Weight | Score | Max |
|----------|--------|-------|-----|
| QA automation | 25% | 17 | 25 |
| Dedicated DB / provision | 20% | 14 | 20 |
| Runtime routing | 15% | 11 | 15 |
| Unit conversion | 15% | 15 | 15 |
| Multi-currency | 10% | 5 | 10 |
| Security | 10% | 4 | 10 |
| Permissions | 5% | 4 | 5 |
| **Total** | **100%** | **70** | **100** |

---

## Appendix — Hardcoded `₱` view files (sample)

Reports (primary gap): `InventoryValuationReport`, `CustomerAging`, `SupplierAging`, `VatTaxSummary`, `ProfitReport`, `ExpenseVsProfit`, `AgingReceivables`, `CashierPerformance`, `Analytics`, `Index`, `Print`, and others under `Views/Reports/`.

Operational: `Expenses/Index`, `PurchaseOrders/Create|Edit` (JS grand total), `Inventory/Index`, `Customers/Index`, `Quotations/Create`.

SaaS (acceptable): `SubscriptionPlans/*`, `SuperAdmin/Dashboard`.

---

**Final Verdict: PILOT NOT CERTIFIED**
