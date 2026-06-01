# Phase 5.0A — Table Classification

**Purpose:** Classify every database table for future Database-Per-Tenant architecture.  
**Status:** Architecture reference only — no schema or runtime changes in Phase 5.0A.  
**Date:** 2026-06-01

---

## Classification Legend

| Label | Meaning |
|---|---|
| PLATFORM | Belongs in the shared Platform Database (SaaS registry, billing, identity) |
| TENANT | Belongs in each tenant's isolated operational database |
| TRANSITION | Ambiguous — requires an explicit design decision before migration |

---

## A. PLATFORM TABLES

Tables that govern SaaS infrastructure, billing, and global administration.  
These tables remain in the Platform Database **permanently**.  
SuperAdmin SaaS management depends entirely on these tables.

| Table | Entity Class | Rationale |
|---|---|---|
| `Tenants` | `Tenant` | SaaS tenant registry. Governs status, limits, subscription. SuperAdmin-owned. |
| `SubscriptionPlans` | `SubscriptionPlan` | Global billing plan catalog. No TenantId. Platform-wide. |
| `RolePermissions` | `RolePermission` | Global permission matrix (RoleName × ModuleName). No TenantId. Shared by all tenants today; will be replicated or sourced from platform in per-tenant migration. |

### ASP.NET Identity Tables (Platform — Identity Separation Deferred)

These tables are managed by `IdentityDbContext<ApplicationUser>` and will remain in the platform architecture. Identity separation (placing tenant users in tenant DBs) is explicitly **deferred to a post-Phase-5.0A decision**.

| Table | Entity | Note |
|---|---|---|
| `AspNetUsers` | `ApplicationUser` | Has `TenantId` — used for both SuperAdmin (null) and tenant staff (set). Remains platform for now. |
| `AspNetRoles` | `IdentityRole` | Global role definitions (SuperAdmin, TenantAdmin, Admin, Cashier, etc.) |
| `AspNetUserRoles` | `IdentityUserRole<string>` | User ↔ Role assignments |
| `AspNetUserClaims` | `IdentityUserClaim<string>` | User claims |
| `AspNetRoleClaims` | `IdentityRoleClaim<string>` | Role claims |
| `AspNetUserLogins` | `IdentityUserLogin<string>` | External login providers |
| `AspNetUserTokens` | `IdentityUserToken<string>` | Password-reset and 2FA tokens |

---

## B. TENANT OPERATIONAL TABLES

Tables containing business data for a single tenant.  
Each tenant database will contain exactly these tables.  
`TenantId` column filters become **unnecessary** once data lives in an isolated database.

### Product / Inventory Master

| Table | Entity | Has TenantId | Notes |
|---|---|---|---|
| `Categories` | `Category` | Yes | Product taxonomy per tenant |
| `Units` | `Unit` | Yes | UOM definitions per tenant |
| `Items` | `Item` | Yes | Product / SKU master per tenant |

### Supplier & Purchasing

| Table | Entity | Has TenantId | Notes |
|---|---|---|---|
| `Suppliers` | `Supplier` | Yes | AP master per tenant |
| `StockInHeaders` | `StockInHeader` | Yes | Stock receipt documents |
| `StockInDetails` | `StockInDetail` | Via header | Stock receipt line items |
| `PurchaseOrders` | `PurchaseOrder` | Yes | PO documents |
| `PurchaseOrderItems` | `PurchaseOrderItem` | Via PO | PO line items |
| `SupplierPayments` | `SupplierPayment` | Via Supplier/StockIn | AP payments (no direct TenantId column) |

### Customer & Sales

| Table | Entity | Has TenantId | Notes |
|---|---|---|---|
| `Customers` | `Customer` | Yes | AR master per tenant |
| `CustomerLedgers` | `CustomerLedger` | Via Customer | AR ledger (no direct TenantId column — filtered via Customer) |
| `SalesHeaders` | `SalesHeader` | Yes | POS/invoice documents |
| `SalesDetails` | `SalesDetail` | Via header | Sale line items |
| `SalesReturnHeaders` | `SalesReturnHeader` | Yes | Return documents |
| `SalesReturnDetails` | `SalesReturnDetail` | Via header | Return line items |
| `Quotations` | `Quotation` | Yes | Quote documents |
| `QuotationItems` | `QuotationItem` | Via Quotation | Quote line items |
| `DeliveryReceipts` | `DeliveryReceipt` | Yes | DR documents |
| `DeliveryReceiptItems` | `DeliveryReceiptItem` | Via DR | DR line items |

### Inventory Operations

| Table | Entity | Has TenantId | Notes |
|---|---|---|---|
| `StockAdjustmentHeaders` | `StockAdjustmentHeader` | Yes | Adjustment documents |
| `StockAdjustmentDetails` | `StockAdjustmentDetail` | Via header | Adjustment line items |

### Expenses

| Table | Entity | Has TenantId | Notes |
|---|---|---|---|
| `Expenses` | `Expense` | Yes | Operating expenses per tenant |

### Branch Operations

| Table | Entity | Has TenantId | Notes |
|---|---|---|---|
| `Branches` | `Branch` | Yes | Physical locations per tenant |
| `BranchProductStocks` | `BranchProductStock` | Yes + BranchId | Per-branch stock levels → `IBranchEntity` |
| `UserBranches` | `UserBranch` | Via Branch | User ↔ Branch access assignments → `IBranchEntity` |
| `BranchTransfers` | `BranchTransfer` | Yes | Inter-branch stock transfers |
| `BranchTransferItems` | `BranchTransferItem` | Via Transfer | Transfer line items |

### Tenant Configuration

| Table | Entity | Has TenantId | Notes |
|---|---|---|---|
| `SystemSettings` | `SystemSetting` | Yes (nullable) | Tenant-owned branding, receipt, VAT config. `TenantId = null` is the legacy/platform default row. In DB-per-tenant, each database has exactly one settings row. |

### Import / Data Entry

| Table | Entity | Has TenantId | Notes |
|---|---|---|---|
| `ImportBatches` | `ImportBatch` | Yes | Excel import history |
| `ImportBatchRows` | `ImportBatchRow` | Via Batch | Import staging rows |

---

## C. TRANSITION TABLES

Tables that require an explicit design decision before migration.  
These are **classified as Tenant for Phase 5.0A** but must be reviewed when Phase 5.0C begins.

| Table | Entity | Issue | Recommended Future Decision |
|---|---|---|---|
| `AuditTrails` | `AuditTrail` | Has `TenantId` (nullable). SuperAdmin needs cross-tenant forensic view. Tenant users need their own view. No EF FK to `Tenant`. | Write to **Tenant DB** for operational audits. Keep platform-level audit separately or use a fan-out query from SuperAdmin. |
| `Notifications` | `Notification` | Has `TenantId` (nullable). `TenantId = null` means **broadcast to all tenants** — a platform-level concept. | Keep `TenantId = null` broadcast rows in Platform DB. Per-tenant rows go to Tenant DB. OR use a platform-only notification table and push to tenant DBs. |

### Accepted Risk AR-001 Reminder

Tables with `TenantId = null` legacy rows (pre-Phase-4.0 backfill):  
`Items`, `SalesHeaders`, `StockInHeaders`, `Expenses`, `Branches`, etc.  
These null rows must be **cleaned up or migrated** before column filters can be dropped in Phase 5.0E.

---

## Summary Count

| Category | Table Count |
|---|---|
| Platform (explicit DbSets) | 3 |
| Platform (Identity — implicit) | 7 |
| Tenant Operational | 31 |
| Transition | 2 |
| **Total** | **43** |

---

## EF Migration History Table

| Table | Owner |
|---|---|
| `__EFMigrationsHistory` | Exists in every physical database. After the split, both Platform DB and Tenant DB will each have their own history table. |

---

*Phase 5.0A — No schema changes. Classification only.*
