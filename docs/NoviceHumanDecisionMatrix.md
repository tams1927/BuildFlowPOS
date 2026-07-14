# Novice Human Decision Matrix

**Gap-closure run:** 81 scenarios — 2026-07-15

| Module | User action | Missing prerequisite | Expected response | Actual response | Result |
|--------|-------------|----------------------|-------------------|-----------------|--------|
| Subscription | Login expired tenant | Active subscription | Block + expired page | SubscriptionExpired | PASS |
| Subscription | Repeat login 3× | Active subscription | Same block | SubscriptionExpired each attempt | PASS |
| Subscription | Direct URL (no cookie) | Auth + subscription | Login redirect | Login redirect | PASS |
| Subscription | Extend 0 days | Valid extension value | Plain error | TempData error | PASS |
| Subscription | Renew 1 month | — | Immediate tenant login | Login succeeds | PASS |
| Dashboard | Open with no setup | Branch | Empty state + guidance | Setup guide banner | PASS |
| POS | Open before branch | Branch | Empty/guidance, no 500 | Page loads safely | PASS |
| Products | Open before branch | Branch | Empty/guidance | Page loads safely | PASS |
| Settings | Save blank | Required fields | Field validation | Validation shown | PASS |
| Settings | Double-click Save | — | One row persisted | Ana Hardware saved once | PASS |
| Branches | Blank submit | Name/code | Modal validation | Modal stays open | PASS |
| Branches | Double-click Save | — | One branch | Ana Main Store once | PASS |
| Units | Create Piece/pc | — | Saved once | Visible in list | PASS |
| Units | Duplicate Piece/pc | Unique unit | Inline modal error, modal open | Field errors shown | PASS |
| Units | Case variation piece/PC | Unique unit | Blocked case-insensitive | Blocked | PASS |
| Suppliers | Invalid email formats | Valid or empty | Field error in modal | Inline validation | PASS |
| Suppliers | Valid email | — | Saved | Reload success | PASS |
| Categories | Blank submit | Name | Block | Modal validation | PASS |
| Products | Blank submit | Required fields | Block | Modal validation | PASS |
| POS | Checkout empty cart | Cart lines | Swal warning | Warning shown | PASS |
| Credit | Walk-in + Credit mode | Credit customer | Blocked | Swal block | PASS |
| Credit | Valid credit sale | Customer + stock | One sale, one ledger | DB verified | PASS |
| PO | Save no supplier/lines | Supplier + lines | HTML5/server block | Validation | PASS |
| PO | Create valid PO | Supplier + product | Draft PO, no stock | Details Draft | PASS |
| Receiving | Receive after Send PO | Sent PO | One stock-in | Receipt created | PASS |
| Adjustment | Zero qty / no reason | Valid adjustment | Blocked | No movement | PASS |
| Adjustment | Decrease > stock | Available qty | Blocked | No negative stock | PASS |
| Adjustment | Valid increase | Product + branch | One movement | DB movement +1 | PASS |
| POS | Sell after receive | Branch stock | Checkout once | Double-click → one sale | PASS |
| POS | Browser Back after sale | — | No resubmit | No duplicate sale | PASS |
| Reports | All report types empty | — | No 500 | Page OK | PASS |
| Reports | Customer/Supplier stmt empty | — | Clear empty state | Sparse copy | USABILITY |
| Auth | Cashier → Settings | Settings permission | Access denied | AccessDenied | PASS |
| Auth | Cashier → POS | Branch + role | POS allowed | POS loads | PASS |
| Chaos | Double-click PO / Enter checkout | — | No duplicate txn | Blocked safely | PASS |

**Button classification summary**

| Action | Classification |
|--------|----------------|
| Complete Checkout (empty cart) | WARNING (Swal) |
| Credit + walk-in customer | BLOCKED (Swal) |
| Duplicate unit in modal | BLOCKED WITH INLINE VALIDATION |
| Invalid supplier email | BLOCKED WITH INLINE VALIDATION |
| Create PO (no lines) | BLOCKED WITH VALIDATION |
| Receive Delivery | ENABLED after PO Sent |
| Cashier Settings link | BLOCKED (permission redirect) |
| Extend subscription 0 days | BLOCKED WITH VALIDATION |
