# Phase 5.0D.2 + 5.0D.4 — Full & Final Module Cutover QA Report

- **Test run:** 2026-06-05 13:38:49 (local), id `20260605_133843`
- **Runner:** dev-only `Tools/Phase50D2QaRunner.cs` via `dotnet run -- --qa-phase50d2` (web server NOT started)
- **Environment:** Development
- **Tenant created:** `QA_Tenant_20260605_133843` (TenantId = 14)
- **Dedicated database:** `QA_TenantDb_20260605_133843`
- **Overall result:** PASS — 50/50 steps passed

## Steps

| # | Step | Result | Detail |
|---|------|--------|--------|
| 1 | Ensure SuperAdmin exists | PASS | superadmin present |
| 2 | Create Tenant (Dedicated) | PASS | TenantId=14, Db=QA_TenantDb_20260605_133843 |
| 3 | Create TenantAdmin | PASS | qa_admin_20260605_133843 |
| 4 | Provision dedicated DB | PASS | Dedicated database 'QA_TenantDb_20260605_133843' provisioned successfully. The tenant continues to use the shared database (routing inactive). (created=True, migrated=True, seeded=True) |
| 5 | Test connection (dedicated) | PASS | Connection successful. |
| 6 | Migrate tenant data | PASS | Migrated 1 rows across 24 tables. All counts validated. (rows=1, tables=24) |
| 7 | Enable routing + diagnostics (Runtime=Dedicated) | PASS | Provisioned=True, DataMigrated=True, RoutingEnabled=True, RuntimeDatabase=Dedicated |
| 8 | Operational context routes to TenantDbContext | PASS | Resolved context = TenantDbContext |
| 9 | Create Branch x2 / Category / Unit / Supplier / Customer | PASS | Branch=1/2, Cat=1, Unit=1, Sup=1, Cust=1 |
| 10 | Create Product | PASS | ItemId=1, Code=QAITM20260605_133843 |
| 11 | Create Purchase Order | PASS | POId=1, No=QAPO20260605_133843, Status=Sent |
| 12 | Receive Purchase Order | PASS | Item.CurrentStock=20 |
| 13 | Stock-In header + detail | PASS | StockInId=1, Item.CurrentStock=30 |
| 14 | Stock Adjustment | PASS | AdjId=1, Item.CurrentStock=35 |
| 15 | Branch Transfer | PASS | TransferId=1, No=QATR20260605_133843 |
| 16 | Create Quotation | PASS | QuoteId=1, No=QAQT20260605_133843 |
| 17 | Convert Quotation to Sale | PASS | SaleId=1, No=QAQSALE20260605_133843 |
| 18 | Create Delivery Receipt | PASS | DRId=1, No=QADR20260605_133843 |
| 19 | POS Sale (cash) | PASS | SaleId=2, No=QASALE20260605_133843 |
| 20 | Credit Sale + ledger CHARGE | PASS | SaleId=3, No=QACRED20260605_133843 |
| 21 | Customer Collection (ledger PAYMENT) | PASS | Ref=QACOL20260605_133843, balance 240→140 |
| 22 | Sales Return | PASS | ReturnId=1, No=QARET20260605_133843 |
| 23 | Supplier Payment | PASS | PaymentId=1, Ref=QASP20260605_133843 |
| 24 | Expense | PASS | ExpenseId=1, No=QAEXP20260605_133843 |
| 25 | Set Main Branch (single main enforced) | PASS | mainBranchCount after switch=1 |
| 26 | Assign User Branch | PASS | UserBranch rows for admin@mainBranch=1 (no cross-db FK — UserId is a plain string) |
| 27 | Write tenant audit record to dedicated context | PASS | Action=QA_AUDIT_READ_20260605_133843 |
| 28 | Update tenant settings via routed context | PASS | BusinessName=QA_Biz_20260605_133843, TIN=QA-TIN-20260605_133843, VAT=8%, Footer set |
| 29 | Import service routes to TenantDbContext (ambient principal) | PASS | Resolved context = TenantDbContext |
| 30 | Import Suppliers via ExcelImportService | PASS | success=1, failed=0 |
| 31 | Import Customers via ExcelImportService | PASS | success=1, failed=0 |
| 32 | Import Items (Products) via ExcelImportService | PASS | success=1, failed=0 |
| 33 | Dedicated DB contains all QA operational rows | PASS | Branches=2, Categories=1, Units=1, Items=1, Suppliers=1, Customers=1, PurchaseOrders=1, PurchaseOrderItems=1, StockInHeaders=1, StockInDetails=1, StockAdjustmentHeaders=1, StockAdjustmentDetails=1, BranchTransfers=1, BranchTransferItems=1, Quotations=1, QuotationItems=1, DeliveryReceipts=1, DeliveryReceiptItems=1, SalesHeaders=3, SalesDetails=3, CustomerLedgers=2, SalesReturnHeaders=1, SalesReturnDetails=1, SupplierPayments=1, Expenses=1, SystemSettings(QA biz)=1, UserBranches=1, AuditTrails(QA)=1, Imported Supplier=1, Imported Customer=1, Imported Item=1 |
| 34 | Shared DB contains NONE of the QA operational rows | PASS | Branches=0, Categories=0, Units=0, Items=0, Suppliers=0, Customers=0, PurchaseOrders=0, StockInHeaders=0, StockAdjustmentHeaders=0, BranchTransfers=0, Quotations=0, DeliveryReceipts=0, SalesHeaders=0, CustomerLedgers=0, SalesReturnHeaders=0, SupplierPayments=0, Expenses=0, SystemSettings(QA biz)=0, UserBranches=0, AuditTrails(QA)=0, Imported Supplier=0, Imported Customer=0, Imported Item=0 |
| 35 | Customer SOA reads dedicated ledger | PASS | entries=2, outstanding=140.00 |
| 36 | Supplier Statement reads dedicated | PASS | stockIns=1, payments=1 |
| 37 | Customer Aging reflects outstanding | PASS | outstanding=140.00 |
| 38 | Supplier Aging reflects payable | PASS | payable=0.00 |
| 39 | Inventory Valuation computes from dedicated | PASS | stock=30.000, cost=50.00, value=1500.00000 |
| 40 | Fast/Slow/Dead-moving uses dedicated sales | PASS | soldQty=6.000 (QA item is moving → not dead) |
| 41 | Reorder Suggestions query runs on dedicated | PASS | reorderCandidates=1 |
| 42 | ABC Analysis aggregates dedicated inventory | PASS | totalInventoryValue=1500.00000 |
| 43 | Dashboard KPIs read dedicated | PASS | sales=3, salesTotal=480.00, expenses=350.00 |
| 44 | Receipt/PDF header uses dedicated settings | PASS | BusinessName=QA_Biz_20260605_133843, TIN=QA-TIN-20260605_133843, VAT=8.00, FooterSet=True |
| 45 | Read Audit Entries from dedicated (not shared) | PASS | dedicated=1, shared=0 |
| 46 | Branch selector reads dedicated assignments | PASS | assignedBranches(dedicated)=1 |
| 47 | Rollback: runtime returns Shared | PASS | RuntimeDatabase=Shared |
| 48 | Rollback: TenantAdmin can still authenticate | PASS | password check passed |
| 49 | Re-enable: runtime returns Dedicated | PASS | RuntimeDatabase=Dedicated |
| 50 | Re-enable: provider returns TenantDbContext | PASS | Resolved context = TenantDbContext |

## Dedicated DB row verification (expected > 0)

| Table | Rows in Dedicated |
|-------|-------------------|
| Branches | 2 |
| Categories | 1 |
| Units | 1 |
| Items | 1 |
| Suppliers | 1 |
| Customers | 1 |
| PurchaseOrders | 1 |
| PurchaseOrderItems | 1 |
| StockInHeaders | 1 |
| StockInDetails | 1 |
| StockAdjustmentHeaders | 1 |
| StockAdjustmentDetails | 1 |
| BranchTransfers | 1 |
| BranchTransferItems | 1 |
| Quotations | 1 |
| QuotationItems | 1 |
| DeliveryReceipts | 1 |
| DeliveryReceiptItems | 1 |
| SalesHeaders | 3 |
| SalesDetails | 3 |
| CustomerLedgers | 2 |
| SalesReturnHeaders | 1 |
| SalesReturnDetails | 1 |
| SupplierPayments | 1 |
| Expenses | 1 |
| SystemSettings(QA biz) | 1 |
| UserBranches | 1 |
| AuditTrails(QA) | 1 |
| Imported Supplier | 1 |
| Imported Customer | 1 |
| Imported Item | 1 |

## Shared DB row verification (expected 0 for QA operational rows)

| Table | Rows in Shared |
|-------|----------------|
| Branches | 0 |
| Categories | 0 |
| Units | 0 |
| Items | 0 |
| Suppliers | 0 |
| Customers | 0 |
| PurchaseOrders | 0 |
| StockInHeaders | 0 |
| StockAdjustmentHeaders | 0 |
| BranchTransfers | 0 |
| Quotations | 0 |
| DeliveryReceipts | 0 |
| SalesHeaders | 0 |
| CustomerLedgers | 0 |
| SalesReturnHeaders | 0 |
| SupplierPayments | 0 |
| Expenses | 0 |
| SystemSettings(QA biz) | 0 |
| UserBranches | 0 |
| AuditTrails(QA) | 0 |
| Imported Supplier | 0 |
| Imported Customer | 0 |
| Imported Item | 0 |

> Platform rows (AspNetUsers, AspNetRoles, Tenants, SubscriptionPlans, RolePermissions) intentionally remain in the shared DB.

## Routing & rollback

- Rollback result: disable→Shared, auth OK, re-enable→Dedicated

## Notes

- Dedicated database was NOT deleted (per spec).
- Cleanup skipped (QA:EnableCleanup not true). Test data retained with QA_ prefix.
