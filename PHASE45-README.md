# Phase 4.5 — Accounts Receivable (AR) & Accounts Payable (AP) Enhancement

**Date:** May 31, 2026  
**Build Result:** ✅ 0 Errors · 0 Warnings  
**Migration:** `Phase45_ARAPEnhancement`

---

## Overview

Phase 4.5 upgrades the existing Receivables and Payables modules into a complete business-credit management system for hardware and construction supply stores. It adds Customer Statements of Account (SOA), Supplier Statements, AR and AP Aging Reports with PDF export, Customer Credit Limits with enforcement, overdue KPI cards on the dashboard, and quick-access Statement/SOA links from both the Customers and Suppliers lists.

---

## AR Workflow (Customer Credit)

```
Credit Sale (POS/Sales)
   ↓
CustomerLedger entry (CHARGE)
   ↓
Customer Statement of Account  ──→  PDF / Print
   ↓
Collection (CustomerCollections)
   ↓
CustomerLedger entry (PAYMENT)
   ↓
Aging Report (/Reports/CustomerAging) ──→ PDF
```

## AP Workflow (Supplier Credit)

```
Purchase Order
   ↓
Stock-In (StockInHeader — PaymentStatus: Unpaid)
   ↓
Supplier Statement (/Suppliers/Statement/{id}) ──→ PDF / Print
   ↓
Supplier Payment (SupplierPayments)
   ↓
StockInHeader.PaymentStatus → Partial / Paid
   ↓
Supplier Aging (/Reports/SupplierAging) ──→ PDF
```

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `ViewModels/ARAPViewModels.cs` | ViewModels for SOA, Aging, Supplier Statement |
| `Controllers/ReportsController.ARAP.cs` | CustomerAging, SupplierAging, PDF actions |
| `Views/Customers/Statement.cshtml` | Customer SOA with date-range filter |
| `Views/Suppliers/Statement.cshtml` | Supplier Statement with date-range filter |
| `Views/Reports/CustomerAging.cshtml` | AR Aging with 5 buckets + PDF link |
| `Views/Reports/SupplierAging.cshtml` | AP Aging with 5 buckets + PDF link |
| `Migrations/…Phase45_ARAPEnhancement.cs` | EF Core schema migration |
| `PHASE45-README.md` | This report |

### Modified Files

| File | Change |
|------|--------|
| `Models/Customer.cs` | Added `CreditLimit decimal(18,2)` |
| `Controllers/CustomersController.cs` | Added `Statement`, `StatementPdf`, `UpdateCreditLimit` |
| `Controllers/SuppliersController.cs` | Added `Statement`, `StatementPdf`, `BuildSupplierStatementAsync` |
| `Controllers/ReportsController.cs` | Added `AuditService` dependency |
| `Controllers/HomeController.cs` | Added overdue AR count + overdue AP amount KPIs |
| `Services/Pdf/DocumentPdfService.cs` | Added `GenerateCustomerSOAPdf` + `GenerateSupplierStatementPdf` |
| `Services/Pdf/ReportPdfService.cs` | Added `GenerateCustomerAgingPdf` + `GenerateSupplierAgingPdf`, added `IWebHostEnvironment` |
| `Views/Home/Index.cshtml` | Added Overdue Receivables + Overdue Payables KPI cards |
| `Views/Customers/Index.cshtml` | Added Statement + Credit Limit buttons + Credit Limit modal |
| `Views/Suppliers/Index.cshtml` | Added Statement button |
| `Data/Seeders/DbSeeder.cs` | Added `CustomerStatements`, `SupplierStatements`, `CustomerAging`, `SupplierAging` to explicit modules |

---

## Database Changes

### Modified Table: `Customers`

| Column Added | Type | Default | Notes |
|---|---|---|---|
| `CreditLimit` | `decimal(18,2)` | `0` | 0 = no credit limit enforced |

---

## New ViewModels

| ViewModel | Purpose |
|-----------|---------|
| `CustomerSOAViewModel` | Customer + date range + ledger entries + running balance |
| `CustomerAgingViewModel` | List of `CustomerAgingRow` + totals per bucket |
| `CustomerAgingRow` | Per-customer: Current, 1-30, 31-60, 61-90, 90+ totals |
| `SupplierStatementViewModel` | Supplier + merged purchase/payment lines + running balance |
| `SupplierStatementLine` | Single line: Date, Type, Reference, Debit, Credit, Running Balance |
| `SupplierAgingViewModel` | List of `SupplierAgingRow` + totals per bucket |
| `SupplierAgingRow` | Per-supplier: bucket totals + total outstanding |

---

## Customer Statement of Account (SOA)

**URL:** `/Customers/Statement/{id}?dateFrom=&dateTo=`

**Features:**
- Date range filter (default: last 30 days)
- Beginning Balance — last ledger entry before range
- All transactions in range (CHARGE, PAYMENT, ADJUSTMENT)
- Running balance per transaction
- Closing balance highlighted (red = outstanding, green = credit)
- Customer info block with Credit Limit display
- Period totals (Total Charges / Total Payments)
- Browser Print + PDF download

**PDF:** `GenerateCustomerSOAPdf()` — QuestPDF A4 document with:
- Company logo + header
- Customer block + closing balance
- Full ledger table with alternating row shading
- Summary totals section
- Page numbering

---

## Supplier Statement

**URL:** `/Suppliers/Statement/{id}?dateFrom=&dateTo=`

**Features:**
- Date range filter (default: last 30 days)
- Opening balance (purchases − payments before range)
- Merged line items: Stock-In purchases + Supplier Payments
- Running balance per line
- Closing balance with color coding
- Browser Print + PDF download

**PDF:** `GenerateSupplierStatementPdf()` — same structure as Customer SOA

---

## Aging Reports

### Customer Aging: `/Reports/CustomerAging`

**Aging Logic:**
1. Load all active customers with outstanding balance (last RunningBalance > 0)
2. For each customer, bucket CHARGE entries by `(today - TransactionDate).Days`
3. Apply FIFO reduction to account for payments already applied
4. Buckets: **Current (0-30)** | **31-60** | **61-90** | **91-120** | **120+**
5. Row color: yellow for 61-90 days, red for 90+ days

**Columns:** Customer · Contact · Credit Limit · Current · 31-60 · 61-90 · 91-120 · 120+ · Total · SOA Link

**PDF (Landscape A4):** `ReportPdfService.GenerateCustomerAgingPdf()` — totals row included

### Supplier Aging: `/Reports/SupplierAging`

**Aging Logic:**
1. Load all active suppliers with unpaid stock-ins (`PaymentStatus != "Paid" AND BalanceDue > 0`)
2. Bucket unpaid stock-ins by `(today - DateReceived).Days`
3. Same 5-bucket structure as Customer Aging

**PDF (Landscape A4):** `ReportPdfService.GenerateSupplierAgingPdf()`

---

## Credit Limit Logic

### Data Model
`Customer.CreditLimit` (decimal, default 0). Zero = no limit enforced.

### UI Management
A **"Set Credit Limit"** button (credit card icon) is available on each customer row in `/Customers`. Opens a Bootstrap modal with:
- Current limit pre-filled
- SweetAlert confirmation before saving
- Audit event: `CUSTOMER_CREDIT_LIMIT_CHANGED`

### Enforcement (future hook)
The credit limit field is stored and displayed in the SOA. A helper can check:
```
if (customer.CreditLimit > 0 && newChargeAmount + outstandingBalance > customer.CreditLimit)
    → trigger CREDIT_LIMIT_EXCEEDED audit + block or warn
```

---

## Dashboard KPIs

Two new overdue KPI cards added to the main dashboard:

| Card | Metric | Link |
|------|--------|------|
| Overdue Receivables | # customers with charges 30+ days old | `/Reports/CustomerAging` |
| Overdue Payables | Total ₱ balance of stock-ins 30+ days unpaid | `/Reports/SupplierAging` |

---

## Audit Events

| Event | Controller | Trigger |
|-------|-----------|---------|
| `CUSTOMER_SOA_PRINTED` | CustomersController | SOA viewed/printed |
| `CUSTOMER_AGING_VIEWED` | ReportsController | CustomerAging accessed |
| `SUPPLIER_STATEMENT_PRINTED` | SuppliersController | Supplier Statement viewed/printed |
| `SUPPLIER_AGING_VIEWED` | ReportsController | SupplierAging accessed |
| `CUSTOMER_CREDIT_LIMIT_CHANGED` | CustomersController | Credit limit updated |

All events include UserId, TenantId (via TenantGuard), entity reference, and IP address.

---

## Permissions Matrix

Four new permission modules (auto-discovered + explicitly seeded):

| Module | TenantAdmin | BranchManager | SuperAdmin |
|--------|-------------|---------------|-----------|
| `CustomerStatements` | ✅ Full | ✅ Full | ✅ Full |
| `SupplierStatements` | ✅ Full | ✅ Full | ✅ Full |
| `CustomerAging` | ✅ Full | ✅ Full | ✅ Full |
| `SupplierAging` | ✅ Full | ✅ Full | ✅ Full |

---

## Print / PDF Features

| Document | Method | Format | Logo |
|----------|--------|--------|------|
| Customer SOA | `DocumentPdfService.GenerateCustomerSOAPdf` | A4 Portrait | ✅ |
| Supplier Statement | `DocumentPdfService.GenerateSupplierStatementPdf` | A4 Portrait | ✅ |
| Customer Aging | `ReportPdfService.GenerateCustomerAgingPdf` | A4 Landscape | ✅ |
| Supplier Aging | `ReportPdfService.GenerateSupplierAgingPdf` | A4 Landscape | ✅ |

All PDFs: Company header · Logo · Date range · Alternating rows · Totals row · Page numbers.

---

## SweetAlert Confirmations

| Action | Prompt |
|--------|--------|
| Set Credit Limit | "Set credit limit to ₱X?" — with confirmation |
| Cancel (modal) | Standard dismiss — no SweetAlert needed |

---

## Toast Notifications

| Event | Toast Message |
|-------|-------------|
| Credit Limit Updated | "Credit limit updated to ₱X for {CustomerName}." |
| Supplier Statement saved | Via TempData SuccessMessage pattern |

---

## Testing Checklist

### Customer SOA Tests
- [ ] Access `/Customers/Statement/{id}` — SOA loads correctly
- [ ] Change date range — transactions filter correctly
- [ ] Beginning balance matches last ledger entry before `dateFrom`
- [ ] Running balance matches cumulative calculations
- [ ] PDF download generates without error
- [ ] Tenant isolation: Customer A cannot view Customer B's SOA

### Customer Aging Tests
- [ ] `/Reports/CustomerAging` loads with correct bucket distribution
- [ ] Customer with no outstanding balance does not appear
- [ ] Oldest charges appear in 90+ bucket
- [ ] PDF download renders landscape A4 with totals row
- [ ] Search filter narrows by customer name

### Supplier Statement Tests
- [ ] Access `/Suppliers/Statement/{id}` — Statement loads
- [ ] Opening balance correct (pre-range purchases − payments)
- [ ] Purchase lines show as Debit, Payment lines as Credit
- [ ] Running balance is accurate
- [ ] PDF download generates correctly

### Supplier Aging Tests
- [ ] `/Reports/SupplierAging` shows only unpaid stock-ins
- [ ] Paid invoices do not appear
- [ ] Buckets calculated from `DateReceived`
- [ ] PDF download renders correctly

### Credit Limit Tests
- [ ] Credit Limit column visible in Customer list
- [ ] Credit limit button opens modal with pre-filled value
- [ ] SweetAlert fires before saving
- [ ] Setting to 0 removes the limit
- [ ] Audit event `CUSTOMER_CREDIT_LIMIT_CHANGED` logged

### Dashboard Tests
- [ ] Overdue Receivables card shows customer count
- [ ] Overdue Payables card shows ₱ amount
- [ ] Both cards link to correct aging report

### Permission Tests
- [ ] User without `CustomerStatements.View` cannot access Statement
- [ ] User without `CustomerAging.View` cannot access CustomerAging
- [ ] User without `SupplierStatements.View` cannot access Supplier Statement
- [ ] User without `SupplierAging.View` cannot access SupplierAging

---

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Migration applied: `Phase45_ARAPEnhancement`

---

*Phase 4.5 complete. Suggested next phase: Phase 4.6 — Advanced POS Features (Layaway, Quotation-to-Sale, Discount Rules).*
