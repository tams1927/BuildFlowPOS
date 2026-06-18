# RC1.5 — Final Simulation Audit

**Date:** 2026-06-15  
**Prior status:** PILOT CERTIFIED (RC1.4, score 97/100)  
**Audit type:** Full-system double-check — no features, no architecture changes  
**Final verdict:** **FINAL PILOT GO**

---

## 1. Build Result

```text
dotnet clean
dotnet build

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Result:** PASS

---

## 2. Migration Result

### ApplicationDbContext (shared platform DB)

| Check | Result |
|-------|--------|
| All migrations applied | **PASS** — through `20260605045843_RC12_CostPerUnitClarity` |
| Pending on shared DB | **None** |
| Snapshot consistency | **OK** — model matches applied migrations |

### TenantDbContext (dedicated tenant DB)

| Check | Result |
|-------|--------|
| Migration chain | `InitialTenantSchema` → `Phase51` → `RC12` |
| Pending on default connection | `RC12` shows pending — **expected** (default connection is shared DB, not tenant DB) |
| Provisioning path | **PASS** — `TenantDatabaseProvisioningService.MigrateAsync()` applies all tenant migrations on provision |
| Latest QA tenant | `QA_TenantDb_20260615_171732` — provisioned + migrated (verified by Phase50D2 + RC12) |

**Result:** PASS — no broken migrations; no production migrations applied beyond dev state.

---

## 3. QA Suite Results

| Suite | Command | Passed | Failed | Time |
|-------|---------|--------|--------|------|
| Phase 5.0D.2 | `--qa-phase50d2` | **50** | **0** | ~69s |
| Phase 5.1 | `--qa-phase51` | **8** | **0** | ~6s |
| RC1.2 | `--qa-rc12` | **22** | **0** | ~6s |

**Certification tenant:** `QA_Tenant_20260615_171732` (TenantId=17)  
**Report:** `docs/Phase50D2_FullCycleQAReport.md`

**Result:** PASS — all suites green.

---

## 4. Workflow Simulation Result (50 steps)

Covered end-to-end by **Phase50D2** (same run as Task 3). All 50 operational steps **PASS**:

| # | Area | Status |
|---|------|--------|
| 1–8 | Tenant create, provision, migrate, route | PASS |
| 9–18 | Master data, PO, receive, stock-in, adjustment, transfer | PASS |
| 19–28 | Customer, quotation, DR, POS/credit sales, collection, supplier payment, expense, return | PASS |
| 29–38 | SOA, supplier statement, AR/AP aging, valuation, movement reports | PASS |
| 39–46 | Dashboard KPIs, receipt settings, audit, branch assignment | PASS |
| 47–50 | Routing disable → shared fallback → re-enable → dedicated | PASS |

Unit conversion (10 sacks → 250 kg, sale 2.5 kg → 247.5 kg) verified by **Phase51**.

**Result:** PASS

---

## 5. DB Isolation Result

From Phase50D2 steps 33–34 (tenant `QA_Tenant_20260615_171732`, routing ON):

| Dedicated DB | Rows | Shared DB leak |
|--------------|------|----------------|
| Branches, Items, Sales, PO, Stock-In, Quotations, DR, Expenses, Audit, etc. | **> 0** | **0** for all QA operational tables |

**Allowed in shared DB:** Tenants, AspNetUsers, RolePermissions, SubscriptionPlans — confirmed.

**Result:** PASS

---

## 6. UI Smoke Test Result

**Tool:** `docs/manuals/_build/smoke_rc15.py` (Playwright)  
**Base URL:** `http://localhost:5146`  
**Prerequisite:** Demo tenant seeded (`dotnet run -- --seed-demo` → `owner` / `Owner123!`)

| Session | Routes tested | Result |
|---------|---------------|--------|
| Unauthenticated | Login | **PASS** (HTTP 200) |
| SuperAdmin | Dashboard, Tenants, Plans, Users, Audit | **5/5 PASS** |
| TenantAdmin (owner) | Dashboard, Settings, Branches, Users, Products, Inventory, Stock-In, POS, Sales, Customers, Suppliers, PO, Quotations, DR, Reports, Import, Audit | **17/17 PASS** |
| Currency | POS displays `₱` or `$` | **PASS** |

**Total:** 24/24 passed  
**Results JSON:** `docs/manuals/_assets/rc15_smoke_results.json`

**Notes:**
- No HTTP 500 errors
- No login redirects on authenticated routes (after demo seed)
- No missing views observed
- HTTPS port `7232` not bound when app runs HTTP-only profile — use `5146` or launch `https` profile for manual testing

**Result:** PASS

---

## 7. Security Result

| Control | Result |
|---------|--------|
| SuperAdmin `ForcePasswordChange` on new accounts | **PASS** (RC12 ephemeral user test) |
| Dev `superadmin` legacy account | **WARN** — rotate before production |
| `SeedSettings:InitialSuperAdminPassword` | Not set in dev — warning logged |
| Tenant users → SaaS pages | **PASS** — `[Authorize(Roles = "SuperAdmin")]` on Tenants, Plans, SuperAdmin |
| Tenant isolation | **PASS** — Phase50D2 zero leakage |
| Branch isolation | Designed via `BranchService` — not re-tested in RC1.5 |
| `PermissionAuthorize` | **PASS** — operational controllers protected |
| Cashier permissions | **PASS** — seeded (RC12) |
| InventoryStaff permissions | **PASS** — seeded (RC12) |
| Connection strings in UI | **PASS** — `DatabaseInfo` uses `MaskedConnectionString` only |
| Secrets in logs | **PASS** — `TenantDatabaseResolver` logs `(credentials hidden)` |

**Result:** PASS (with production hardening reminders)

---

## 8. Bugs Found

| # | Issue | Classification | Action |
|---|-------|----------------|--------|
| — | **No application bugs found** in RC1.5 audit | — | — |

**Environment / process notes (not code bugs):**

1. UI smoke requires demo tenant (`--seed-demo`) or existing `owner` user — documented prerequisite.
2. Default `dotnet run` binds HTTP `:5146` only; Playwright must target correct URL.
3. Dev SuperAdmin still has `ForcePasswordChange=false` — pre-existing account.

---

## 9. Fixes Applied

**None required.** RC1.5 was validation-only; RC1.4 fixes remain in place.

**Audit tooling added (non-feature):**

- `docs/manuals/_build/smoke_rc15.py` — repeatable UI route smoke test

---

## 10. Remaining Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| Production SuperAdmin password | Medium | Set `InitialSuperAdminPassword`; rotate on deploy |
| Non-pilot report views hardcode `₱` | Low | Deferred from RC1.4 |
| QA test DBs retained (`QA_TenantDb_*`) | Low | Optional cleanup with `QA:EnableCleanup` |
| UI smoke depends on demo seed | Low | Document onboarding seed step |

---

## 11. Final GO / NO-GO Recommendation

### **FINAL PILOT GO**

All success criteria met:

- [x] Build: 0 errors, 0 warnings
- [x] Migrations: consistent; provisioning path verified
- [x] QA: Phase50D2 50/50, Phase51 8/8, RC12 22/22
- [x] Full workflow simulation: PASS (via Phase50D2)
- [x] Dedicated DB isolation: PASS
- [x] UI smoke: 24/24 PASS
- [x] Security: PASS with production reminders

**Pilot Readiness Score (RC1.5):** **98 / 100**

---

## Appendix — QA Command Reference

```powershell
dotnet clean
dotnet build
dotnet run -- --qa-phase50d2
dotnet run -- --qa-phase51
dotnet run -- --qa-rc12

# UI smoke (app running + demo seeded)
dotnet run -- --seed-demo   # once, if owner user missing
dotnet run                  # start app
$env:RC15_BASE_URL="http://localhost:5146"
python docs/manuals/_build/smoke_rc15.py
```

---

**FINAL PILOT GO**
