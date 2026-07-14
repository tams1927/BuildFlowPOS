# Novice Human Simulation Results

**Date:** 2026-07-15 (gap-closure)  
**Tenant:** Tams Construction Supply (Id=1, `HardBuild_TCS_1`)  
**Persona:** Ana  
**Automation:** `docs/manuals/_build/novice_human_simulation.py` + `novice_gap_phases.py`  
**Evidence:** `docs/manuals/_assets/novice_human_results.json`

## Summary

| Metric | Count |
|--------|------:|
| Total scenarios | 81 |
| Passed | 79 |
| Failed | 0 |
| Usability issues | 2 |
| Blockers | 0 |
| Not applicable | 0 |
| Not executed | 0 |

---

## Gap-closure phases (newly automated)

| Phase | Scenarios | Result |
|-------|-------------|--------|
| NH1 — Duplicate unit modal | 4 | 4 PASS |
| NH2 — Supplier email validation | 6 | 6 PASS |
| P12 — Credit sale novice | 4 | 4 PASS |
| P13 — Stock adjustment novice | 4 | 4 PASS |
| P14 — Report empty/invalid | 18 | 16 PASS, 2 USABILITY |
| P16 — General chaos | 2 | 2 PASS |

---

## NH-001 retest (duplicate unit)

| ID | Action | Result |
|----|--------|--------|
| NH1-032 | Create Piece/pc | PASS |
| NH1-033 | Duplicate Piece/pc in modal | PASS |
| NH1-034 | Case variation piece/PC | PASS |
| NH1-035 | DB unit row count = 1 | PASS |

---

## NH-002 retest (supplier email)

| ID | Email input | Result |
|----|-------------|--------|
| NH2-036 | abc | PASS |
| NH2-037 | abc@ | PASS |
| NH2-038 | @example.com | PASS |
| NH2-039 | supplier@example | PASS |
| NH2-040 | supplier example.com | PASS |
| NH2-041 | valid@supplier.ph | PASS |
| P5-045 | bad-email (P5 phase) | PASS |

---

## Credit sale (P12)

| ID | Action | Result |
|----|--------|--------|
| P12-051 | Credit + walk-in customer | PASS (blocked) |
| P12-052 | Tender disabled in Credit mode | PASS |
| P12-053 | Valid credit sale + DB (1 sale, 1 ledger) | PASS |
| P12-054 | Re-open POS — no duplicate sale | PASS |

---

## Stock adjustment (P13)

| ID | Action | Result |
|----|--------|--------|
| P13-057 | Submit without reason | PASS |
| P13-058 | Zero quantity | PASS |
| P13-059 | Decrease > available | PASS |
| P13-060 | Valid increase adjustment | PASS |

---

## Reports (P14)

All nine report types opened without HTTP 500. Empty-database states verified for Sales, Inventory, Valuation, Movement, AR/AP Aging, Expenses. Customer and Supplier statement empty states flagged as sparse (low usability).

---

## Data integrity

| Area | Finding | Result |
|------|---------|--------|
| Units duplicate | One Piece row; no duplicate on retry | PASS |
| Credit sale | SalesHeaders +1, CustomerLedgers +1 | PASS |
| Stock adjustment | One movement per valid save | PASS |
| PO → Receive | Stock increased once | PASS |
| POS checkout | No duplicate sale on double-click | PASS |
| Tenant isolation | `--qa-po-clickthrough` zero cross-tenant leakage | PASS |
| Post-reset | `--qa-post-simulation-clean-state` 30/30 after reset | PASS |

---

## Usability warnings (open, low)

| ID | Issue |
|----|-------|
| P14-070 | Customer statement empty state sparse |
| P14-074 | Supplier statement empty state sparse |
