# HardBuild POS — Production Smoke Test Checklist

**Project:** HardwareManagementSystem / HardBuild POS  
**Purpose:** Verify core functionality after every production deployment or environment change  
**Last Updated:** 2026-05-29

---

## How to Use This Checklist

Run this checklist on the **live production URL** immediately after deployment.  
Use a **real or dedicated test account** — do not use the default admin seed account for real transactions.  
Mark each item ✅ Pass / ❌ Fail. Investigate any failure before declaring the deployment successful.

**Tester:** ___________________________  
**Deployment version/date:** ___________________________  
**Environment URL:** ___________________________

---

## 1. Authentication

- [ ] **1.1 Login page loads** — Navigate to `/Account/Login`. Page renders with username/password fields and login button.
- [ ] **1.2 Invalid login rejected** — Enter wrong credentials. Confirm error message appears; no redirect to dashboard.
- [ ] **1.3 Successful login** — Enter valid credentials. Confirm redirect to dashboard. No JS errors in browser console.
- [ ] **1.4 Session persists** — Refresh the page while logged in. Confirm still authenticated.

---

## 2. Dashboard

- [ ] **2.1 Dashboard loads** — Navigate to `/Home/Index`. Page renders without errors.
- [ ] **2.2 KPI cards display** — Today's sales, expenses, inventory value, and low-stock count cards render with numeric values.
- [ ] **2.3 No loading spinner stuck** — Charts and summary sections fully load within 5 seconds.
- [ ] **2.4 Notifications bell** — Notification count badge is visible (or shows 0 if none pending).

---

## 3. Branch Switch

- [ ] **3.1 Branch selector visible** — Branch selector in the navigation bar or header is present.
- [ ] **3.2 Switch branch** — Select a different branch. Confirm the page refreshes/redirects and the active branch label updates.
- [ ] **3.3 Data isolation** — After switching branch, dashboard and inventory show data relevant to the selected branch only.

---

## 4. POS Checkout

- [ ] **4.1 POS page loads** — Navigate to `/POS`. Page renders the product search and cart area.
- [ ] **4.2 Product search** — Type a product name or barcode in the search box. Confirm matching products appear.
- [ ] **4.3 Add to cart** — Click a product to add it. Confirm it appears in the cart with correct price and quantity.
- [ ] **4.4 Adjust quantity** — Change the quantity in the cart. Confirm the subtotal updates correctly.
- [ ] **4.5 Process sale (cash)** — Complete a test sale with cash payment. Confirm the sale is saved and the receipt / confirmation appears.
- [ ] **4.6 Inventory decremented** — After the test sale, verify the sold item's stock count decreased in Inventory.

---

## 5. Stock-In

- [ ] **5.1 Stock-In index loads** — Navigate to `/StockIn`. List renders.
- [ ] **5.2 Create stock-in** — Create a new stock-in record for a test supplier and item. Confirm it saves successfully.
- [ ] **5.3 Inventory updated** — Verify the item's stock count increased after the stock-in is confirmed.

---

## 6. Stock Adjustment

- [ ] **6.1 Stock Adjustment index loads** — Navigate to `/StockAdjustment`. List renders.
- [ ] **6.2 Create adjustment** — Create a new adjustment (increase or decrease). Confirm it saves.
- [ ] **6.3 Audit trail entry** — Verify an audit trail entry was created for the adjustment.

---

## 7. Branch Transfer

- [ ] **7.1 Branch Transfers index loads** — Navigate to `/BranchTransfers`. List renders.
- [ ] **7.2 Create transfer** — Create a transfer request from the current branch to another branch. Confirm it saves with `Pending` status.
- [ ] **7.3 Approve / complete transfer** — Switch to the receiving branch and approve/complete the transfer. Confirm stock moves between branches.

---

## 8. Customer Collection

- [ ] **8.1 Customer Collections index loads** — Navigate to `/CustomerCollections`. List renders.
- [ ] **8.2 Record a collection** — Select a customer with an outstanding balance and record a payment. Confirm the balance updates.
- [ ] **8.3 Receipt or confirmation** — Confirm a success message or receipt is shown.

---

## 9. Supplier Payment

- [ ] **9.1 Supplier Payments index loads** — Navigate to `/SupplierPayments`. List renders.
- [ ] **9.2 Record a payment** — Select a supplier and record a test payment. Confirm the payable balance updates.
- [ ] **9.3 Audit trail entry** — Verify an audit trail entry was created for the payment.

---

## 10. Import (Excel Upload)

- [ ] **10.1 Import index loads** — Navigate to `/Import`. Page renders with upload form.
- [ ] **10.2 Upload Excel file** — Upload a valid Excel file (≤ 10 MB). Confirm it progresses to the preview/mapping step.
- [ ] **10.3 Preview mapping** — Confirm column mapping screen renders with the uploaded data preview.
- [ ] **10.4 Confirm import** — Confirm the import. Verify imported rows appear in the target entity list (products/inventory).
- [ ] **10.5 Oversized file rejected** — Attempt to upload a file > 10 MB. Confirm an appropriate error message is shown (not a 500 error).

---

## 11. Reports

- [ ] **11.1 Reports dashboard loads** — Navigate to `/Reports`. Index page renders with report links.
- [ ] **11.2 Sales Detail report** — Run the Sales Detail report for the current branch. Data renders correctly.
- [ ] **11.3 All-branches report** — If the logged-in user has access, switch to "All Branches" scope and run a report. Confirm data spans multiple branches.
- [ ] **11.4 Inventory Status report** — Run the Inventory Status report. Stock counts match known inventory levels.
- [ ] **11.5 Profit report** — Run the Profit/Loss report. Figures are non-zero and appear reasonable.
- [ ] **11.6 PDF export** — Export at least one report to PDF. Confirm the file downloads without error.

---

## 12. Audit Trail

- [ ] **12.1 Audit Trail index loads** — Navigate to `/AuditTrail`. Page renders with a list of recent actions.
- [ ] **12.2 Recent actions visible** — Confirm the actions performed during this smoke test (stock-in, adjustment, payments) appear in the log.
- [ ] **12.3 Filter by user** — Apply a filter by the test user. Confirm results narrow correctly.

---

## 13. Notifications

- [ ] **13.1 Notification bell accessible** — Click the notification bell icon. Notification panel/dropdown opens.
- [ ] **13.2 Mark all as read** — Click "Mark all as read". Confirm the badge count drops to 0 and no JS errors occur.
- [ ] **13.3 New notification triggers** — Perform an action known to trigger a notification (e.g., low stock threshold or transfer request). Confirm the bell badge increments.

---

## 14. Health Endpoint

- [ ] **14.1 Health check accessible** — From a browser or curl:

  ```
  GET https://pos.yourdomain.com/health
  ```

  Expected: `200 OK` with body `Healthy`

- [ ] **14.2 No authentication required** — Confirm the health endpoint responds even when not logged in.

---

## 15. Logout

- [ ] **15.1 Logout succeeds** — Click the logout button. Confirm redirect to the login page.
- [ ] **15.2 Session cleared** — After logout, attempt to navigate to `/Home/Index`. Confirm redirect back to login (session expired/cleared).
- [ ] **15.3 Back button does not restore session** — Use the browser back button after logout. Confirm the protected page does not load without re-authenticating.

---

## Result Summary

| Section | Pass | Fail | Notes |
|---------|------|------|-------|
| 1. Authentication | | | |
| 2. Dashboard | | | |
| 3. Branch Switch | | | |
| 4. POS Checkout | | | |
| 5. Stock-In | | | |
| 6. Stock Adjustment | | | |
| 7. Branch Transfer | | | |
| 8. Customer Collection | | | |
| 9. Supplier Payment | | | |
| 10. Import | | | |
| 11. Reports | | | |
| 12. Audit Trail | | | |
| 13. Notifications | | | |
| 14. Health Endpoint | | | |
| 15. Logout | | | |

**Overall Result:** ☐ PASS — All critical paths verified  /  ☐ FAIL — Issues found (see notes)

**Sign-off:** ___________________________ **Date:** _______________

---

*This checklist should be updated if new critical features are added to the system.*
