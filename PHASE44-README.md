# Phase 4.4 — Purchase Orders & Supplier Ordering

**Date:** May 31, 2026  
**Build Result:** ✅ 0 Errors · 0 Warnings  
**Migration:** `Phase44_PurchaseOrders`

---

## Overview

Phase 4.4 implements a complete **Purchase Order (PO) workflow** for hardware and construction supply businesses. The system covers the full lifecycle: creation → supplier approval → delivery receiving → auto stock-in → inventory update, with tenant isolation, audit logging, SweetAlert confirmations, toast notifications, and A4 PDF generation.

---

## Target Flow

```
Supplier
   ↓
Purchase Order (Draft)
   ↓
Send to Supplier (Sent)
   ↓
Receive Stocks → Creates StockInHeader + StockInDetails automatically
   ↓
Inventory Updated (CurrentStock / BranchProductStock)
   ↓
Status: Received (or PartiallyReceived → repeat until complete)
```

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `Models/PurchaseOrder.cs` | Main PO model (header) |
| `Models/PurchaseOrderItem.cs` | PO line item model with partial-receiving tracking |
| `Controllers/PurchaseOrdersController.cs` | CRUD + workflow (Send/Cancel/Receive) + PDF |
| `Views/PurchaseOrders/Index.cshtml` | PO list with KPI row and status filter |
| `Views/PurchaseOrders/Create.cshtml` | Dynamic line-item PO creation form |
| `Views/PurchaseOrders/Edit.cshtml` | Edit Draft PO (locked for non-Draft) |
| `Views/PurchaseOrders/Details.cshtml` | Full PO details with action buttons |
| `Views/PurchaseOrders/Receive.cshtml` | Partial/full receiving form |
| `Views/PurchaseOrders/Print.cshtml` | Browser-printable A4 PO document |
| `Migrations/…Phase44_PurchaseOrders.cs` | EF Core schema migration |

### Modified Files

| File | Change |
|------|--------|
| `Data/ApplicationDbContext.cs` | Added `DbSet<PurchaseOrder>`, `DbSet<PurchaseOrderItem>`, relationships, and indexes |
| `Services/Pdf/DocumentPdfService.cs` | Added `GeneratePurchaseOrderPdf()` |
| `Controllers/HomeController.cs` | Added PO KPI queries and low-stock intelligence |
| `Views/Home/Index.cshtml` | Added PO KPI card row + "Generate PO" button on low-stock widget |
| `Data/Seeders/DbSeeder.cs` | Added `BranchManager` to permission seeding scope |

---

## Database Changes

### New Tables

**`PurchaseOrders`**

| Column | Type | Notes |
|--------|------|-------|
| Id | int (PK) | Auto-increment |
| TenantId | int? | FK → Tenants (nullable) |
| BranchId | int? | FK → Branches (nullable) |
| SupplierId | int | FK → Suppliers (Restrict) |
| PONumber | nvarchar(50) | Tenant-scoped unique number |
| PODate | datetime2 | Order date |
| ExpectedDeliveryDate | datetime2? | Optional |
| Status | nvarchar(30) | Draft/Sent/PartiallyReceived/Received/Cancelled |
| Notes | nvarchar(500)? | Optional notes |
| CreatedBy | nvarchar(150)? | Username |
| CreatedAtUtc | datetime2 | UTC timestamp |
| UpdatedAtUtc | datetime2? | Last update UTC |

**`PurchaseOrderItems`**

| Column | Type | Notes |
|--------|------|-------|
| Id | int (PK) | Auto-increment |
| PurchaseOrderId | int | FK → PurchaseOrders (Cascade) |
| ItemId | int | FK → Items (Restrict) |
| Quantity | decimal(18,3) | Ordered quantity |
| QuantityReceived | decimal(18,3) | Cumulative received (partial tracking) |
| UnitCost | decimal(18,2) | Unit cost at time of PO |
| TotalCost | decimal(18,2) | Quantity × UnitCost |

### New Indexes

| Index | Columns | Unique | Notes |
|-------|---------|--------|-------|
| `UX_PurchaseOrders_TenantId_PONumber` | TenantId, PONumber | ✅ | Filter: TenantId IS NOT NULL |
| `IX_PurchaseOrders_TenantId_Status_PODate` | TenantId, Status, PODate | ❌ | Common filter query |
| `IX_PurchaseOrders_TenantId_SupplierId` | TenantId, SupplierId | ❌ | Supplier lookup |

---

## New Models

### `PurchaseOrder`
- **Status enum values:** `Draft`, `Sent`, `PartiallyReceived`, `Received`, `Cancelled`
- **Computed helpers (NotMapped):**
  - `TotalAmount` — sum of all line items
  - `IsEditable` — true only for Draft
  - `CanBeSent` — true for Draft
  - `CanBeCancelled` — true for Draft or Sent
  - `CanBeReceived` — true for Sent or PartiallyReceived

### `PurchaseOrderItem`
- `QuantityReceived` tracks cumulative received for partial receiving
- `QuantityRemaining` (NotMapped) = `Quantity - QuantityReceived`

---

## New Controllers

### `PurchaseOrdersController`

| Action | Method | Permission | Description |
|--------|--------|-----------|-------------|
| `Index` | GET | View | List with KPIs and status filter |
| `Details` | GET | View | Full PO view with action buttons |
| `Create` | GET/POST | Create | Create Draft PO with line items |
| `Edit` | GET/POST | Edit | Edit Draft PO (locked otherwise) |
| `SendPO` | POST | Edit | Draft → Sent |
| `CancelPO` | POST | Delete | Draft/Sent → Cancelled |
| `Receive` | GET/POST | Edit | Receive stocks (partial or full) |
| `GenerateFromLowStock` | GET | Create | Pre-fill Create with low-stock items |
| `Print` | GET | Print | Browser-printable A4 document |
| `DownloadPdf` | GET | Print | QuestPDF A4 download |

---

## New Views

| View | Description |
|------|------------|
| `Index` | Paginated list with 5 KPI cards (Draft/Sent/Partial/Received/Cancelled) |
| `Create` | Supplier + dates header, dynamic line-item table, "Generate PO" from low-stock |
| `Edit` | Same layout as Create, pre-populated, locked for non-Draft |
| `Details` | Supplier info block, PO details, line item table with received/remaining tracking |
| `Receive` | Line-by-line receiving form with remaining qty pre-filled |
| `Print` | Uses `_PrintDoc.cshtml` shared layout with company logo support |

---

## PO Workflow

```
[Create PO]
     ↓
  Status: Draft
     ↓
[Send to Supplier]  ─── SweetAlert confirm required
     ↓
  Status: Sent
     ↓
[Receive Stocks]    ─── SweetAlert confirm required
     ↓
  (Partial?)──────────→ Status: PartiallyReceived
     ↓ (All received)        ↓ [Receive Remaining]
  Status: Received    ←──────┘

  At any point in Draft/Sent:
[Cancel PO]         ─── SweetAlert confirm required
     ↓
  Status: Cancelled (terminal — cannot revert)
```

---

## Partial Receiving Workflow

1. PO contains Item A (Qty: 100), Item B (Qty: 50)
2. First receive: Item A = 60, Item B = 50
3. System creates `StockInHeader` + `StockInDetails` for received amounts
4. `PurchaseOrderItem.QuantityReceived` updated to 60 (A) and 50 (B)
5. PO Status → `PartiallyReceived` (Item A still has 40 remaining)
6. Second receive: Item A remaining = 40
7. System creates another `StockInHeader` + `StockInDetails`
8. PO Status → `Received` (all items complete)

---

## Supplier Integration

Supplier details are displayed on:
- **PO Details** — info block with Name, Tel, Email, Address
- **Print View** — supplier block in document body
- **PDF** — supplier block using QuestPDF layout

---

## Dashboard Integration

Four new KPI cards added to the dashboard:

| Card | Filter Link |
|------|------------|
| Draft POs | `/PurchaseOrders?statusFilter=Draft` |
| Sent POs (Awaiting Delivery) | `/PurchaseOrders?statusFilter=Sent` |
| Partially Received | `/PurchaseOrders?statusFilter=PartiallyReceived` |
| Critical + Low Stock | `/PurchaseOrders/GenerateFromLowStock` |

**Low-Stock Widget** now includes a **"Generate PO"** button that pre-loads the Create PO form with all items at or below reorder level.

---

## Inventory Intelligence

- `GenerateFromLowStock` queries items where `CurrentStock <= ReorderLevel`
- Pre-fills the Create PO form with suggested quantities: `ReorderLevel - CurrentStock` (minimum 1)
- Pre-fills unit cost from current `CostPrice`
- Dashboard shows combined Critical + Low Stock count

---

## Audit Events

| Event | Trigger |
|-------|---------|
| `PURCHASE_ORDER_CREATED` | New Draft PO saved |
| `PURCHASE_ORDER_UPDATED` | Draft PO edited |
| `PURCHASE_ORDER_SENT` | PO sent to supplier |
| `PURCHASE_ORDER_CANCELLED` | PO cancelled |
| `PURCHASE_ORDER_RECEIVED` | PO fully received |
| `PURCHASE_ORDER_PARTIAL_RECEIVED` | PO partially received |

All events include: TenantId (via TenantGuard), BranchId, UserId, Document Number, and IP address.

---

## Permissions

Module: **`PurchaseOrders`**  
Auto-discovered by `DbSeeder.GetAllModules()` via reflection.

| Role | View | Create | Edit | Delete | Print | Export |
|------|------|--------|------|--------|-------|--------|
| SuperAdmin | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| TenantAdmin | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| BranchManager | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

> `BranchManager` is now included in the permission seeding. Delete on sensitive management modules (Users, Branches, Settings, Roles, RolePermissions) is restricted for BranchManager.

---

## Print / PDF Features

### Browser Print (`Print.cshtml`)
- Uses `_PrintDoc.cshtml` shared layout
- Company logo (if configured)
- Supplier details block
- PO metadata (number, date, expected delivery, branch, status, notes)
- Line-item table with alternating row shading
- Grand total row
- Signature area (Prepared By / Approved By / Received By)

### A4 PDF (`DownloadPdf` → `DocumentPdfService.GeneratePurchaseOrderPdf`)
- QuestPDF-generated professional document
- Full company header with logo (from `SystemSetting.LogoPath`)
- Supplier block and delivery info
- Item table with alternating rows, subtotals
- Bold grand total
- Signature strip at bottom
- Page numbering footer

---

## SweetAlert Confirmations

| Action | Prompt |
|--------|--------|
| Send PO | "This will change the status to Sent. The PO will no longer be editable." |
| Cancel PO | "This action cannot be undone." — Red confirm button |
| Receive PO | "This will create a Stock-In record and update inventory levels." — Green confirm |
| Create PO | "This will create a new Draft PO." |
| Edit PO | "Update this purchase order?" |

---

## Toast Notifications

| Event | Message |
|-------|---------|
| PO Created | "Purchase Order {PONumber} created successfully." |
| PO Updated | "Purchase Order {PONumber} updated." |
| PO Sent | "Purchase Order {PONumber} sent to supplier." |
| PO Cancelled | "Purchase Order {PONumber} has been cancelled." |
| PO Fully Received | "Purchase Order {PONumber} fully received. Stock-In {SINNumber} created." |
| PO Partially Received | "Partial receipt recorded for {PONumber}. Stock-In {SINNumber} created." |

---

## Testing Checklist

### Functional Tests
- [ ] Create a Draft PO with multiple line items
- [ ] Edit a Draft PO (add/remove items)
- [ ] Verify Edit is locked for Sent/Received/Cancelled POs
- [ ] Send PO → verify status changes to Sent
- [ ] Cancel a Draft PO → verify terminal state
- [ ] Cancel a Sent PO → verify terminal state
- [ ] Receive all items → verify status = Received, StockIn created, stock updated
- [ ] Receive partial items → verify status = PartiallyReceived
- [ ] Receive remaining items → verify status = Received
- [ ] Generate PO from Low-Stock items → verify pre-fill is correct

### Tenant Isolation Tests
- [ ] Tenant A cannot view Tenant B's POs
- [ ] PO number sequence is per-tenant (PO-YYYYMMDD-000001 resets per tenant)
- [ ] Supplier dropdown shows only tenant's suppliers

### Branch Isolation Tests
- [ ] Branch-scoped user sees only their branch's POs
- [ ] Stock update goes to `BranchProductStock` when BranchId is set

### Print / PDF Tests
- [ ] Print view renders correctly (landscape + signature area)
- [ ] PDF download generates without error
- [ ] Company logo appears in PDF if configured

### Dashboard Tests
- [ ] Draft PO count card shows correct number
- [ ] Sent PO count card shows correct number
- [ ] Partially Received card shows correct number
- [ ] Low-Stock "Generate PO" button leads to pre-filled Create form

### Audit Trail Tests
- [ ] PURCHASE_ORDER_CREATED logged on create
- [ ] PURCHASE_ORDER_UPDATED logged on edit
- [ ] PURCHASE_ORDER_SENT logged on send
- [ ] PURCHASE_ORDER_CANCELLED logged on cancel
- [ ] PURCHASE_ORDER_RECEIVED logged on full receive
- [ ] PURCHASE_ORDER_PARTIAL_RECEIVED logged on partial receive

### Permission Tests
- [ ] User without PurchaseOrders View cannot access /PurchaseOrders
- [ ] User without Create cannot reach Create action
- [ ] User without Delete cannot Cancel PO
- [ ] BranchManager has View/Create/Edit/Print/Export on PurchaseOrders

---

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Migration applied: `Phase44_PurchaseOrders`

---

*Phase 4.4 complete. Next recommended phase: Phase 4.5 — Supplier Payments & Payables Integration (link POs to SupplierPayments for AP tracking).*
