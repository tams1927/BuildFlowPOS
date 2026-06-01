# Release Validation Checklist

Manual sign-off checklist to run in **staging** (and again as a smoke test in production after
deploy). Run once as a **shared-mode** tenant and once as a **routing-enabled (dedicated)** tenant
to confirm parity. Pair with `docs/ProductionSmokeTest.md`.

Legend: ☐ = to verify. Record tester, date, and result for each section.

---

## Login & Identity
- ☐ TenantAdmin can log in; wrong password is rejected; 5 failed attempts → 15-min lockout
- ☐ Password change works; new password honors policy (≥ 8 chars, 1 digit)
- ☐ Session persists; logout clears session; protected pages redirect to `/Account/Login`

## SuperAdmin
- ☐ SuperAdmin logs in and sees the SaaS dashboard / Tenants / SubscriptionPlans
- ☐ SuperAdmin cannot open tenant operational pages it shouldn't (role gating intact)
- ☐ SuperAdmin always runs on the shared database (diagnostics confirm)
- ☐ Default SuperAdmin password has been rotated

## TenantAdmin
- ☐ Sees only their own tenant's data
- ☐ Cannot access SaaS-only modules (Tenants/SubscriptionPlans/SuperAdmin)
- ☐ Can manage Users/Roles within tenant scope

## Branch Selector (top-right)
- ☐ Current branch name displays correctly
- ☐ Assigned branches list loads; switching branch works and persists
- ☐ Newly created/assigned branches appear immediately (dedicated tenant: no stale shared reads)

## Settings
- ☐ Edit Business Name / TIN / VAT% / Receipt Footer → saved and reflected on receipts/PDFs
- ☐ Logo upload works (≤ 2 MB, jpg/png/gif/webp); logo shows in layout and on documents
- ☐ Theme color change applies
- ☐ (Dedicated tenant) settings persist to the dedicated DB, not shared

## Inventory
- ☐ Item list, search, create/edit; current stock accurate
- ☐ Reorder level flags low stock

## Stock-In
- ☐ Create Stock-In (header + details); inventory increments; supplier link correct
- ☐ Payment status (Unpaid/Partial/Paid) tracks AmountPaid

## POS
- ☐ Cash sale checkout; change computed; receipt prints with correct header/footer
- ☐ Stock decrements on sale

## Sales
- ☐ Sales history list; receipt reprint; void/return path works
- ☐ Credit sale posts a CustomerLedger CHARGE

## Purchase Orders
- ☐ Create PO (Draft→Sent); receive PO (full/partial); stock and PO status update

## Quotations
- ☐ Create quotation; convert quotation to sale; converted link recorded

## Delivery Receipts
- ☐ Create delivery receipt (optionally linked to a sale); print

## Expenses
- ☐ Create expense (category, amount, branch); appears in reports

## AR / AP
- ☐ Customer Collection posts a PAYMENT; customer balance decreases
- ☐ Supplier Payment recorded; stock-in payable decreases
- ☐ Customer SOA / Aging and Supplier Statement / Aging show correct figures

## Reports
- ☐ Dashboard KPIs (sales count/total, expenses) correct
- ☐ Sales Detail, Profit, Inventory Status/Valuation, Fast/Slow/Dead, Reorder, ABC
- ☐ Collections, Aging Receivables/Payables, VAT/Tax summary
- ☐ (Dedicated tenant) all numbers sourced from the dedicated DB

## Imports
- ☐ Download template; upload Excel; map columns; preview shows validation errors
- ☐ Confirm import of Suppliers / Customers / Products / Opening Stock
- ☐ (Dedicated tenant) imported rows land only in the dedicated DB

## Audit Logs
- ☐ Privileged actions appear in the audit trail (create/edit/delete, routing, settings)
- ☐ (Dedicated tenant) tenant sees its dedicated audit records
- ☐ SuperAdmin sees platform-level audit data (with tenant filter)

## Notifications
- ☐ Notification dropdown loads; unread count correct; mark-as-read / mark-all work

## Dedicated Routing
- ☐ Diagnostics show Provisioned/Migrated/RoutingEnabled = Yes and Runtime Database = Dedicated for the pilot tenant
- ☐ Every other tenant shows Runtime Database = Shared
- ☐ Operational writes for the pilot tenant land in the dedicated DB; none leak to shared

## Rollback
- ☐ Disable Routing → Runtime Database = Shared immediately; no crash; TenantAdmin still logs in
- ☐ Re-enable Routing → Runtime Database = Dedicated; branches/assignments/imports visible again
- ☐ Dedicated database retained (never deleted) through the cycle

---

### Environment sanity (production)
- ☐ `ASPNETCORE_ENVIRONMENT = Production`
- ☐ HTTPS enforced; HSTS active; secure cookies; security headers present
- ☐ `SeedSettings:EnableDemoDataCleanup` is false/absent; `QA:EnableCleanup` is false/absent
- ☐ `/health` returns healthy
- ☐ `dotnet build` → 0 Errors / 0 Warnings
