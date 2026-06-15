# Pilot Certification Report — RC1.4 Remediation

**Product:** Hardware Supply POS SaaS  
**Certification date:** 2026-06-05  
**Environment:** Development  
**Certification tenant:** `QA_Tenant_20260605_133843` (TenantId=14)  
**Dedicated database:** `QA_TenantDb_20260605_133843`  
**Final verdict:** **PILOT CERTIFIED**

---

## 1. Root Cause (RC1.3 blockers)

| Blocker | Root cause | Classification |
|---------|------------|----------------|
| Phase50D2 item create FK failure | `Phase50D2QaRunner` / `Phase50D1QaRunner` created `Item` without `BaseUnitId` after Phase 5.1 FK | **QA runner bug** (not application UI bug — `ProductsController` sets `BaseUnitId`) |
| Phase50D2 provision failure (RC1.4 first run) | Shared `RolePermissions` had duplicate `(RoleName, ModuleName)` rows; tenant DB enforces `UX_RolePermissions_Role_Module` | **Provisioning seed bug** — copy did not dedupe |
| Cashier / InventoryStaff unusable | Roles existed but no default `RolePermissions` rows seeded | **Seeder gap** |
| Pilot-critical hardcoded `₱` | Views used literal peso instead of `ViewBag.CurrencySymbol` | **UI gap** (non-architecture) |
| SuperAdmin on dev DB | Pre-existing account with `ForcePasswordChange=false` | **Environment state** — fresh installs correct via DbSeeder |

---

## 2. Fixes Applied

| Area | File(s) | Change |
|------|---------|--------|
| QA item create | `Tools/Phase50D2QaRunner.cs`, `Tools/Phase50D1QaRunner.cs` | Set `BaseUnitId = unit.Id` on QA items |
| Provision seed | `Services/TenantDatabases/TenantDatabaseProvisioningService.cs` | Dedupe `RolePermissions` by role+module when copying to tenant DB |
| Role seed | `Data/Seeders/DbSeeder.cs` | Seed `Cashier` + `InventoryStaff` default permissions; ensure roles exist |
| Currency UI | PO Create/Edit/Receive, Quotations Create/Details/Index, DR Create, Inventory Valuation, Sales Receipt | Use `ViewBag.CurrencySymbol` |
| Security QA | `Tools/Rc12QaRunner.cs` | Verify Cashier/InventoryStaff seeds; ephemeral `ForcePasswordChange` user test |

**No architecture changes. No new features.**

---

## 3. Routing Results

Verified via Phase50D2 steps 47–50:

| Step | Result |
|------|--------|
| Routing ON → `TenantDbContext` | **PASS** |
| Operational modules write to dedicated DB only | **PASS** (50 modules, zero shared leakage) |
| Disable routing → `ApplicationDbContext` (Runtime=Shared) | **PASS** |
| TenantAdmin auth after rollback | **PASS** |
| Re-enable routing → Runtime=Dedicated | **PASS** |
| Provider returns `TenantDbContext` after re-enable | **PASS** |

---

## 4. Currency Results

### Pilot-critical audit

| Screen | Before RC1.4 | After RC1.4 |
|--------|--------------|-------------|
| POS | Dynamic (`ViewBag.CurrencySymbol`) | **Unchanged — OK** |
| Receipt | Broken string fallback | **Fixed** — `settings ?? ViewBag.CurrencySymbol` |
| Quotation Create/Details/Index | Hardcoded `₱` display | **Dynamic** |
| Delivery Receipt Create | Hardcoded `₱` | **Dynamic** |
| PO Create/Edit/Receive | Hardcoded `₱` + JS | **Dynamic** |
| Customer SOA | Already used `currency` variable | **OK** (fallback only) |
| Supplier Statement | Already used `currency` variable | **OK** (fallback only) |
| Inventory Valuation | 6 hardcoded `₱` | **Dynamic** |

### Count (pilot-critical paths)

| Metric | Count |
|--------|-------|
| Hardcoded `₱` display **before** (pilot-critical) | **~27** occurrences across 10 files |
| Hardcoded `₱` display **after** (pilot-critical) | **0** (11 files retain `?? "₱"` fallback only) |

### Deferred (non-critical)

~40+ occurrences remain in general reports (CustomerAging, VatTaxSummary, Expenses, SubscriptionPlans, etc.) — acceptable for pilot per RC1.4 scope.

---

## 5. Role Results

### Cashier (default seeded)

| Module | View | Create | Edit | Delete | Print |
|--------|------|--------|------|--------|-------|
| POS | ✓ | ✓ | — | — | ✓ |
| Sales | ✓ | — | — | — | ✓ |
| Customers | ✓ | — | — | — | — |
| SalesReturn | ✓ | ✓ | — | — | ✓ |
| All other modules | — | — | — | — | — |

### InventoryStaff (default seeded)

| Module | Access |
|--------|--------|
| Products, Inventory, StockIn, StockAdjustment, PurchaseOrders, Suppliers, Categories, Units, Import, InventoryMovement, BranchTransfers | View + Create + Edit + Print (no Delete) |
| Inventory intelligence reports | View + Export only |
| SaaS modules | Denied |

**RC12 verification:** Cashier POS View+Create ✓, InventoryStaff StockIn View+Create ✓

No manual Role Permissions configuration required for pilot tenant onboarding.

---

## 6. Security Results

| Control | Result |
|---------|--------|
| `SeedSettings:InitialSuperAdminPassword` | Config key present; set before production |
| New SuperAdmin `ForcePasswordChange` | **PASS** — DbSeeder |
| Ephemeral fresh user test | **PASS** — `ForcePasswordChange=true` created and verified |
| `AccountController` redirect on flag | Existing — unchanged |
| Dev `superadmin` legacy account | **INFO** — `ForcePasswordChange=false`; rotate before go-live |
| Tenant / branch isolation | **PASS** — Phase50D2 leakage checks |

---

## 7. QA Results

| Suite | Command | Passed | Failed | Time |
|-------|---------|--------|--------|------|
| Phase 5.0D.2 | `--qa-phase50d2` | **50** | **0** | ~15.3s |
| Phase 5.1 | `--qa-phase51` | **8** | **0** | ~4s |
| RC1.2 | `--qa-rc12` | **22** | **0** | ~4s |

### Phase50D2 full workflow (all PASS)

Supplier → Customer → Item → PO → Receive → Stock-In → Quotation → DR → Sale (POS + credit) → Collection → Supplier Payment → Expense → Reports (SOA, statements, aging, valuation, dashboard) → Routing rollback cycle.

Detailed step log: `docs/Phase50D2_FullCycleQAReport.md`

### Build

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## 8. Remaining Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| Dev SuperAdmin not forced to change password | Medium | Set `InitialSuperAdminPassword` + rotate on production deploy |
| Non-pilot report views still hardcode `₱` | Low | Accept for pilot or fix in post-pilot polish |
| Shared DB `RolePermissions` may contain historical duplicates | Low | Provisioning now dedupes on copy; optional shared DB cleanup |
| QA test tenants/databases retained | Low | Enable `QA:EnableCleanup` or manual DROP of `QA_TenantDb_*` |
| PDF services may still label "Unit Cost" | Low | Display values correct; label cosmetic |

---

## 9. Pilot Readiness Score

| Category | Weight | Score |
|----------|--------|-------|
| QA automation | 25% | 25 |
| Dedicated DB / provision | 20% | 20 |
| Runtime routing | 15% | 15 |
| Unit conversion | 15% | 15 |
| Multi-currency (pilot-critical) | 10% | 9 |
| Security | 10% | 8 |
| Permissions | 5% | 5 |
| **Total** | **100%** | **97** |

---

## 10. GO / NO-GO

### **GO — PILOT CERTIFIED**

All success criteria met:

- [x] Phase50D2 = **PASS** (50/50)
- [x] Phase51 = **PASS** (8/8)
- [x] RC12 = **PASS** (22/22)
- [x] Build = **0 Errors, 0 Warnings**
- [x] Routing cycle verified
- [x] Pilot-critical currency dynamic
- [x] Cashier / InventoryStaff ready without manual permission setup

**Pre-go-live checklist:**

1. Set `SeedSettings:InitialSuperAdminPassword` in production configuration.
2. Rotate existing dev/staging SuperAdmin password.
3. Provision first real tenant dedicated DB and enable routing.
4. Confirm tenant currency in Settings before UAT.

---

**Final Verdict: PILOT CERTIFIED**
