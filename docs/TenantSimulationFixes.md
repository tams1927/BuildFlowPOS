# Tenant Simulation Fixes

Simulation harness fixes only — **no application business-logic changes** were required for the final 91/91 pass.

---

## 1. Playwright async/sync handler

| | |
|--|--|
| **Symptom** | Steps 4+ failed with `object tuple can't be used in 'await' expression` |
| **Reproduction** | Run simulation after settings step |
| **Root cause** | `run_step()` always `await fn()` but placeholder lambdas returned sync tuples |
| **Files** | `docs/manuals/_build/human_tenant_setup_simulation.py` |
| **Fix** | Detect coroutine vs sync return in `run_step()`; use `record()` for grouped steps |
| **Regression** | Re-run `--sim` |

---

## 2. Product unit dropdown labels

| | |
|--|--|
| **Symptom** | Step 34 timeout on `unitId` select |
| **Root cause** | Options render as `Piece (pc)` not `Piece`; `label=re.compile` invalid in Playwright Python |
| **Fix** | Added `select_option_contains()` helper |
| **Regression** | Product create steps 34–36 |

---

## 3. PO item/supplier selects

| | |
|--|--|
| **Symptom** | Step 40 `'re.Pattern' object is not iterable` |
| **Fix** | Use `select_option_contains()` for supplier and line items |

---

## 4. Stock adjustment reason field

| | |
|--|--|
| **Symptom** | Step 59 fill on `<select name="reason">` |
| **Fix** | `select_option("Physical Count Adjustment")` |

---

## 5. Supplier payment Pay link

| | |
|--|--|
| **Symptom** | Step 101 clicked sidebar "Supplier Payments" instead of row Pay link |
| **Fix** | Locator scoped to `table tbody a:has-text("Pay")` |

---

## 6. Logout verification

| | |
|--|--|
| **Symptom** | Step 127 URL stayed at `/` |
| **Fix** | Open user dropdown → submit logout form → verify `/Users/Index` redirects to login |

---

## 7. Tenant reset FK order

| | |
|--|--|
| **Symptom** | Reset failed: `FK_StockInHeaders_PurchaseOrders`, `FK_SupplierReturnHeaders_DamagedGoodsHeaders` |
| **Files** | `Data/Seeders/TenantOperationalResetService.cs` |
| **Fix** | Delete StockIn before PO; SupplierReturn before DamagedGoods |

---

## Application code changes

**None** — frozen architecture and business rules preserved.
