# Pilot Readiness Report

> **Date:** 2026-06-01
> **Scope:** Pilot deployment of HardBuild POS (database-per-tenant, single pilot tenant)
> **Prepared by:** Release preparation phase (Phase 5 Final Release Checklist)

---

## 1. Architecture Status — ✅ Complete

- Dual-context architecture in place: shared `ApplicationDbContext` + per-tenant `TenantDbContext`,
  abstracted by `ITenantOperationalDbContext` and resolved per request by
  `ITenantOperationalContextProvider`.
- Provisioning, data migration (with count validation), resolver, and routing flags implemented.
- Full operational cutover complete through Phase 5.0D.4 — all tenant operational controllers and
  services (incl. Branches, UserBranches, Import/ExcelImportService, AuditTrail, Settings,
  AuditService, NotificationService, BranchService) route to the dedicated DB when active.
- Platform data (Identity, Tenants, SubscriptionPlans, RolePermissions, SaaS admin) remains shared
  by design.

## 2. QA Status — ✅ Pass

- Phase 5.0D.2 + 5.0D.4 automated runner: **50 / 50 steps PASS**.
- Phase 5.0D.1 pilot core-cycle runner: pass.
- Verified: dedicated rows present, zero operational leakage to shared, reports read dedicated,
  imports via the real `ExcelImportService`, audit read-side, rollback + re-enable.
- Build: `dotnet build` → **0 Errors / 0 Warnings**.

## 3. Routing Status — ✅ Working

- Routing activates only when all five preconditions hold (Dedicated + Provisioned + DataMigrated +
  RoutingEnabled + ConnectionString). Otherwise shared.
- Resolver caches metadata 5 minutes; invalidated on flag changes.
- SuperAdmin and all non-routed tenants remain on shared.

## 4. Rollback Status — ✅ Working

- Disable Routing → immediate fallback to shared, non-destructive, dedicated DB retained.
- Re-enable Routing → returns to dedicated; previously created/imported records visible.
- Verified end-to-end by QA.

## 5. Operational Coverage — ✅ Complete

Inventory, Stock-In, POS, Sales (cash/credit/returns/void), Purchase Orders (+receive), Quotations
(+convert), Delivery Receipts, Branch Transfers, Stock Adjustments, Customers/Suppliers, AR
collections / AP supplier payments, Expenses, Imports (Suppliers/Customers/Products/Opening Stock),
Branches, User-Branch assignments, Settings (incl. logo), Reports (dashboard KPIs, sales, profit,
inventory valuation/intelligence, AR/AP aging, VAT), Audit, Notifications.

## 6. Remaining Accepted Risks

| ID | Risk | Mitigation / status |
|----|------|---------------------|
| AR-SEC-01 | Default SuperAdmin `superadmin` / `SuperAdmin123!` seeded | **Rotate password before go-live** (blocking checklist item) |
| AR-AUDIT-01 | SuperAdmin sees only shared/platform audit for a routed tenant | By design for database-per-tenant; cross-DB audit aggregation is future work |
| AR-001 | Shared-scope helpers include `TenantId == null` rows | Shared DB only; not applicable inside a dedicated DB; DBA back-fill planned |
| AR-002 | `CustomerLedger` lacks direct `TenantId` | Scoped via parent `Customer`; planned column add |
| CSP-01 | CSP uses `'unsafe-inline'` | Required by Bootstrap/SweetAlert; nonce strategy planned |
| INFRA-01 | SQL Express 10 GB/db | Mitigated per-tenant by dedicated DBs; Standard edition recommended for scale |
| OPS-01 | Writes can split across shared/dedicated during rollback window | Treat rollback as emergency; reconcile before re-enable (documented) |

## 7. Readiness Score

| Dimension | Weight | Score |
|-----------|--------|-------|
| Architecture completeness | 20 | 20 |
| QA / automated verification | 20 | 20 |
| Routing correctness | 15 | 15 |
| Rollback safety | 15 | 15 |
| Operational coverage | 10 | 10 |
| Backup / DR documentation | 10 | 9 (procedures documented; live restore rehearsal pending) |
| Security / configuration | 10 | 7 (HTTPS/headers/rate-limit OK; default SuperAdmin password rotation pending) |
| **Total** | **100** | **96 / 100** |

## 8. Recommendation

The system is **technically ready** for a controlled single-tenant pilot. Before go-live, complete
the two non-code operational items:

1. **Rotate the SuperAdmin default password** (and confirm it cannot be the seeded default).
2. **Rehearse a restore** of the shared DB and one dedicated DB on staging, and schedule backups for
   both per `BackupAndRestoreGuide.md`.

These are configuration/operational actions, not code changes.

## 9. GO / NO-GO Decision

**GO (conditional).** Proceed to pilot once the SuperAdmin password is rotated and backups +
restore rehearsal are confirmed. All code, architecture, routing, rollback, and QA gates are met
(50/50, build 0/0). No further development is required for the pilot.
