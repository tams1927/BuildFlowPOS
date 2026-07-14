# RC1.8 — Purchase Order Receiving Workflow

## Audit Finding (Pre-Change)

The PO receive engine **already existed** in `PurchaseOrdersController.Receive` (POST):

- Creates `StockInHeader` + `StockInDetail` automatically
- Increases branch/item inventory via `BranchService.AddStockAsync`
- Supports **partial receiving** via cumulative `PurchaseOrderItem.QuantityReceived`
- Sets PO status to `PartiallyReceived` or `Received`
- Creates supplier payable via `StockInHeader.BalanceDue` / `PaymentStatus`
- Preserves Phase 5.1 unit conversion fields (`ReceivedQuantity`, `ConversionQuantity`, `BaseQuantity`, `CostPerReceivedUnit`, `CostPerBaseUnit`)

RC1.8 **did not rebuild** this engine. It improved workflow naming, UI, traceability, and validation.

## Standard Workflow

```
Purchase Order (Sent)
    → Receive Delivery (Purchasing menu or PO Details)
    → Auto Stock-In (StockInHeader)
    → Inventory increase (base quantity)
    → Supplier Payable (BalanceDue on receipt)
    → Supplier Payment (existing SupplierPayments flow)
```

## Receiving UI

| Page | Route | Purpose |
|---|---|---|
| Receiving List | `/Receiving/Index` | All delivery receipts (PO + manual), search/filter/pagination |
| Receiving Details | `/Receiving/Details/{id}` | Receipt lines, costs, payment status, PO link |
| Receive Delivery | `/PurchaseOrders/Receive/{poId}` | Enter accepted/damaged quantities against PO |
| Manual Stock In | `/StockIn/Index` | Exceptional non-PO receipts (unchanged functionality) |

## Partial Delivery

- `QuantityReceived` stored in **base units** (cumulative)
- `QuantityRemaining` computed property on `PurchaseOrderItem`
- Multiple receipts create multiple `StockInHeader` rows (one per receive action)
- Over-receipt is **rejected** (client + server validation)

Example: Order 500 bags → receive 300 → status `PartiallyReceived` → receive 200 → status `Received`.

## Unit Conversion (Phase 5.1 Preserved)

| Field | Example (10 sacks × 25 kg) |
|---|---|
| ReceivedQuantity | 10 |
| ConversionQuantity | 25 |
| BaseQuantity | 250 kg |
| CostPerReceivedUnit | ₱1,000/sack |
| CostPerBaseUnit | ₱40/kg |
| LineTotal | ₱10,000 |

## Damaged / Rejected on Delivery

- **Accepted** quantity → sellable inventory + stock-in detail + payable
- **Damaged** quantity → `DamagedGoodsHeader` (reason: "Rejected on delivery"), **no** sellable inventory increase
- Damaged qty counts toward PO delivery (reduces remaining)
- **Short** quantity stays open on PO

## Supplier Payable

Each receipt with accepted goods creates its own `StockInHeader` with:

- `TotalCost` = sum of accepted line totals
- `BalanceDue` = `TotalCost` initially
- `PaymentStatus` = `Unpaid`

Partial payments use existing `SupplierPaymentsController.Pay`.

## Schema Changes (RC18)

Added to `StockInHeader`:

- `PurchaseOrderId` (nullable FK)
- `ReceivedBy` (nullable string)

Migrations: `RC18_ReceivingWorkflow` on both `ApplicationDbContext` and `TenantDbContext`.

## Permissions

Uses existing `RolePermission` modules:

- `PurchaseOrders` — create/send/receive PO
- `StockIn` — view receiving list, manual stock-in
- `DamagedGoods` — damaged on delivery records

## Audit Events

| Event | When |
|---|---|
| `PURCHASE_ORDER_RECEIPT_CREATED` | Stock-in receipt created from PO |
| `PURCHASE_ORDER_PARTIALLY_RECEIVED` | PO still has open quantity |
| `PURCHASE_ORDER_FULLY_RECEIVED` | All lines complete |
| `DELIVERY_DAMAGE_RECORDED` | Damaged/rejected qty on delivery |
| `MANUAL_STOCK_IN_CREATED` | Manual stock-in (renamed from CREATED) |

## QA

```bash
dotnet run -- --qa-receiving
```

### `--qa-receiving` (18/18 pass)

- Supplier/item/conversion setup
- PO create (10 sacks @ ₱1,000)
- Partial receive (+150 kg), PO `PartiallyReceived`, remaining 4 sacks
- StockInHeader created once per receipt
- Final receive (+250 kg total), PO `Received`
- Payable total ₱10,000 across receipts
- Over-receipt guard
- Receiving list pagination (10-row default) + PO search
- Manual Stock In without PO link
- Dedicated DB isolation (zero shared leakage)

### Regression (post-RC18)

| Suite | Result |
|---|---|
| `--qa-phase51` | 8/8 pass |
| `--qa-rc12` | 22/22 pass (after shared migration applied) |
| `--qa-phase53` | 11/11 pass |
| `--qa-save-confirm` | 4/4 pass |
| `--qa-direct-dedicated-tenant` | 34/34 pass |
| `dotnet build` | 0 errors, 0 warnings |

## Remaining Limitations

- Supplier Return auto-creation from damaged delivery is manual (link via Damaged Goods → Supplier Returns)
- Receive page does not show historical receipt list inline (use Receiving Details / PO audit)
- Date-range filter on Receiving list not yet added (supplier/branch/status/source supported)
