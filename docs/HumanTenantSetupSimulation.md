# Human Tenant Setup Simulation

**Date:** 2026-07-15  
**Script:** `docs/manuals/_build/human_tenant_setup_simulation.py`  
**Results JSON:** `docs/manuals/_assets/human_tenant_setup_results.json`  
**Persona:** BuildFlow Hardware Trading — first-time tenant administrator setup

---

## Credentials (Development)

Passwords are **not stored in repository files**. Set via environment variables before running:

| Role | Username | Env var |
|------|----------|---------|
| Tenant Admin | `adminTCS` | `SIM_ADMIN_PASSWORD` |
| Cashier (created in sim) | `buildflow_cashier` | `SIM_CASHIER_PASSWORD` |

---

## Summary (revised post-simulation audit)

| Metric | Value |
|--------|-------|
| Total steps in script | 91 |
| **Applicable passed** | **82** |
| **Applicable failed** | **0** |
| **Not applicable** | **9** (steps 87–95 — park/retrieve sale) |
| **Not executed** | **0** |
| Base URL | `https://localhost:7232` |

> **Do not** report 91/91 PASS — park steps were not executed. See `docs/ParkedSaleFeatureVerification.md`.

---

## Workflow Coverage

| Phase | Steps | Result |
|-------|-------|--------|
| A. Login & Settings | 1–9 | PASS |
| B. Branch setup | 10–14 | PASS |
| C. Users / Cashier | 17–18 | PASS |
| D. Units | 20–22 | PASS |
| E. Categories | 24–26 | PASS |
| F. Supplier | 28 | PASS |
| G. Customers | 31–32 | PASS |
| H. Products | 34–39 | PASS |
| I. Purchase Order | 40–46 | PASS |
| J. Receiving (RC18) | 47–53 | PASS |
| K. Inventory | 54, 58 | PASS |
| L. Stock Adjustment | 59 | PASS |
| M. Quotation | 63 | PASS |
| N. Delivery Receipt | 68 | PASS |
| O. POS walk-in sale | 72–85 | PASS |
| P. POS state reset | 86 | PASS |
| Q. Park sale | 87–95 | **N/A** — feature absent (see ParkedSaleFeatureVerification.md) |
| R. Credit sale & collection | 96, 98 | PASS |
| S. Supplier payment | 101 | PASS |
| T–V. Returns / damaged / supplier return | 104, 108, 111 | PASS (manual follow-up markers) |
| W. Expense | 115 | PASS |
| X. Reports & dashboard | 117–126 | PASS |
| Y. Access & isolation | 127–132 | PASS |

---

## How to Re-run

```powershell
# 1. Preview tenant reset
dotnet run -- --tenant-reset --tenant-id=1 --retain-admin=adminTCS

# 2. Execute reset (operational data only — password is separate)
dotnet run -- --tenant-reset --tenant-id=1 --retain-admin=adminTCS --confirm

# 3. Set admin password from env (not logged in docs)
$env:TENANT_ADMIN_PASSWORD="<secret>"
dotnet run -- --set-tenant-admin-password --tenant-id=1 --user=adminTCS

# 4. Start app
dotnet run --urls "https://localhost:7232"

# 5. Run simulation
$env:SIM_ADMIN_USER="adminTCS"
$env:SIM_ADMIN_PASSWORD="<secret>"
$env:SIM_CASHIER_PASSWORD="<secret>"
python docs/manuals/_build/human_tenant_setup_simulation.py
```

Full per-step detail: `human_tenant_setup_results.json`.
