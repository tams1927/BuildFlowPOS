# Novice Human Simulation Plan

**Date:** 2026-07-15  
**Persona:** Ana — small hardware store owner, limited computer experience  
**Tenant:** Tams Construction Supply (TCS, Id=1) — disposable dev/QA only  
**Admin:** `adminTCS`  
**Automation:** `docs/manuals/_build/novice_human_simulation.py`

---

## Test philosophy

Simulate **wrong-order**, **invalid-input**, **duplicate-click**, and **browser-navigation** chaos — not happy-path only. A step passes only when a novice receives **clear, safe behavior** (block, warn, or guide) without technical errors or silent data corruption.

---

## Environment setup

1. `dotnet run -- --tenant-reset --tenant-id=1 --retain-admin=adminTCS --confirm`
2. Set `TENANT_ADMIN_PASSWORD`, `SUPERADMIN_PASSWORD` (env only — not in repo)
3. Start app: `dotnet run --urls "https://localhost:7232"`
4. Run: `python docs/manuals/_build/novice_human_simulation.py`

**Retained:** tenant, subscription, routing, `adminTCS`, SuperAdmin, roles  
**Cleared:** all operational/setup data

---

## Risk areas

| Area | Novice risk |
|------|-------------|
| Subscription | Login after expiry, direct URLs, renewal mistakes |
| Setup order | POS/PO before branch, products before units |
| Duplicate submit | Double-click Save/Pay/Receive |
| Browser nav | Back/refresh after POST |
| Stock integrity | Oversell, over-receive, duplicate checkout |
| Permissions | Cashier direct URL bypass |
| Empty states | Dashboard/reports with no data |

---

## Phases (16)

1. Subscription access chaos  
2. First login — no store setup  
3. Settings novice tests  
4. Branch setup + prerequisite checks  
5. Units and categories  
6. Suppliers and customers  
7. Products out-of-order  
8. POS before inventory  
9. Purchase order novice  
10. Receiving novice  
11. POS after inventory  
12. Credit sale novice  
13. Inventory adjustment  
14. Reports empty filters  
15. Cashier permissions  
16. General chaos (double-click, back, refresh)

---

## Result classification

| Result | Meaning |
|--------|---------|
| PASS | Safe, understandable behavior |
| USABILITY_ISSUE | Works but confusing — needs clearer guidance |
| FAIL | Incorrect behavior or validation gap |
| BLOCKER | Crash, data corruption, security bypass |

---

## Evidence

- `docs/manuals/_assets/novice_human_results.json`
- `docs/manuals/_assets/novice_human_failures/` (screenshots)
