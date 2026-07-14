# Novice Human Simulation — Issues

**Date:** 2026-07-15  
**Tenant:** Tams Construction Supply (Id=1)  
**Persona:** Ana  
**Run:** `docs/manuals/_assets/novice_human_results.json` (81 scenarios, 79 PASS, 0 FAIL, 2 USABILITY, 0 BLOCKER)

---

## Resolved usability issues (gap-closure)

### NH-001 — Duplicate unit feedback in modal ✅ FIXED

| Field | Detail |
|-------|--------|
| **Scenarios** | NH1-033, NH1-034, NH1-035 |
| **What Ana did** | Created Piece/pc, retried Piece/pc, then piece/PC |
| **Fix** | `UnitsController.CheckDuplicate` + AJAX modal submit with inline field errors; modal stays open; values retained |
| **Retest** | PASS — messages: “A unit named 'Piece' already exists.” / “The abbreviation 'pc' is already in use.” |
| **DB** | Exactly one Piece unit row |

### NH-002 — Supplier email validation ✅ FIXED

| Field | Detail |
|-------|--------|
| **Scenarios** | NH2-036–040, P5-045 |
| **What Ana did** | Entered abc, abc@, @example.com, supplier@example, supplier example.com |
| **Fix** | Client regex + server `EmailAddressAttribute` when non-empty; inline `#addSupplierEmailError` in modal |
| **Retest** | PASS — “Enter a valid email address or leave the field blank.”; valid email saves |
| **DB** | No supplier created for invalid submissions |

---

## Open usability issues (low severity)

### NH-003 — Customer statement empty state sparse

| Field | Detail |
|-------|--------|
| **Scenario** | P14-070 |
| **Severity** | Low |
| **What happened** | Page loads without 500 but empty-state copy is sparse for a novice |
| **Status** | Deferred — non-blocking for onboarding |

### NH-004 — Supplier statement empty state sparse

| Field | Detail |
|-------|--------|
| **Scenario** | P14-074 |
| **Severity** | Low |
| **What happened** | Same as NH-003 for supplier statement report |
| **Status** | Deferred — non-blocking for onboarding |

---

## Issues fixed during simulation (prior passes)

| ID | Issue | Fix |
|----|-------|-----|
| NH-005 | Cashier could not access POS (missing permissions) | `DbSeeder.RepairPilotRolePermissionsAsync` dedupes Cashier permissions |
| NH-006 | Expired tenant POST to POS returned unclear result | Subscription middleware returns 401 for API POST |
| NH-007 | PO receive harness clicked wrong button | Send PO → Receive Delivery flow corrected in automation |
| NH-008 | SuperAdmin cookie caused false tenant-leak in P1 | `context.clear_cookies()` before direct URL tests |

---

## Failure evidence

No new failures in gap-closure run. Prior failure screenshots (if any) remain under `docs/manuals/_assets/novice_human_failures/`.
