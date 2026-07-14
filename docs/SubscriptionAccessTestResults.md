# Subscription Access Test Results

**Date:** 2026-07-15  
**Runner:** `dotnet run -- --qa-subscription-access --business-date=2026-07-15`

---

## Summary

| Result | Count |
|--------|-------|
| PASS | **15** |
| FAIL | **0** |

Machine-readable: `docs/SubscriptionAccessTestResults.json`

---

## Test matrix

| # | Case | Expected | Actual | Result |
|---|------|----------|--------|--------|
| 1 | Active paid before expiration | Allowed | Allowed | PASS |
| 2 | Trial before expiration | Allowed | Allowed | PASS |
| 3 | Trial on expiration date (inclusive) | Allowed | Allowed | PASS |
| 4 | Trial one day after expiration (Manila) | Denied | Denied | PASS |
| 5 | Status = Expired | Denied | Denied | PASS |
| 6 | Suspended | Denied | Denied | PASS |
| 7 | Disabled tenant | Denied | Denied | PASS |
| 8 | No plan + no expiration (Trial) | Denied | Denied | PASS |
| 9 | UTC Jul 14 / Manila Jul 15 mismatch | Denied | Denied | PASS |
| 10 | Last minute of expiration day | Allowed | Allowed | PASS |
| 11 | Month-end expiration | Denied after | Denied | PASS |
| 12 | Year-end inclusive | Allowed on Dec 31 | Allowed | PASS |
| 13 | Leap-year Feb 29 | Denied Mar 1 | Denied | PASS |
| 14 | Active with plan, no expiration | Allowed (perpetual) | Allowed | PASS |
| 15 | TCS manual — Jul 15 2026 business date | Denied | Denied | PASS |

---

## Route / session tests (design verification)

| # | Case | Implementation | Status |
|---|------|----------------|--------|
| 11–14 | Active session after expiry | `TenantStatusFilter` re-evaluates each request | Verified by design |
| 15–18 | POS/inventory/report POST | Global filter on all non-exempt MVC actions | Verified by design |
| 19 | AJAX 403 JSON | `IsApiOrAjaxRequest` → JsonResult 403 | Verified in code |
| 20–21 | Logout + expired page | Account actions whitelisted | Verified in code |
| 22 | SuperAdmin tenants page | SuperAdmin bypass | Verified in code |
| 23–24 | Tenant isolation | Per-tenant `TenantId` evaluation | Verified in code |

---

## Build / regression

| Check | Result |
|-------|--------|
| `dotnet build` | 0 errors, 0 warnings |
| `--qa-tenant-runtime-audit` | **36 / 36 PASS** |

---

## Manual reproduction (TCS)

**Tenant:** Tams Construction Supply — Trial, expires **July 14, 2026**  
**Simulated business date:** July 15, 2026 (Asia/Manila via QA clock)

1. Login → **blocked** (redirect to SubscriptionExpired, no cookie)
2. Direct POS URL → **blocked** by filter
3. SuperAdmin extend +1 month → access restored on next login
4. Widget shows **Expired** + renewal badge when date passed
