# RC1.8.1 — Receiving Workflow UX & Warehouse Completion

Builds on RC1.8 without changing the purchasing architecture.

## Workflow (unchanged)

```
Supplier → Purchase Order → Receive Delivery → Auto Stock In → Inventory → Supplier Payable
```

## UI Enhancements

### Purchase Order Details
- **Receiving Progress** summary above Receive button (Ordered / Received / Remaining + progress bar)
- Color-coded bar: Blue (Sent), Orange (Partial), Green (Completed)
- **Receiving History** timeline — newest first, per receipt: date, received by, invoice, accepted, damaged, reference
- Empty state: *No deliveries have been received yet.*
- **Partial Delivery** / **Fully Received** badges

### Receiving List
- Expanded search: PO number, receipt number, supplier, supplier invoice (`InvoiceNumber`), delivery reference (`Remarks`), branch, received by
- Server-side pagination (10 default), newest first

### Receiving Details
- Header fields: PO, supplier, branch, received by, invoice, delivery reference, remarks, date
- **Items Received** table: ordered, accepted, damaged, rejected, unit, cost, total
- Partial / fully received indicator
- Linked damaged goods badges

### Dashboard
- KPI cards: Today's Receipts, Pending Deliveries, Partially Received POs, Completed POs Today, Supplier Payables Due
- **Pending Deliveries** widget (5 newest POs with Receive action)

### Supplier Invoice Search
- `InvoiceNumber` searchable in Receiving, Supplier Payables report, Audit Trail (via description)
- Audit events now include invoice number when provided on receive

## Files Changed

| File | Change |
|---|---|
| `ViewModels/PoReceivingViewModels.cs` | **New** — progress, history, detail line, pending delivery VMs |
| `Controllers/PurchaseOrdersController.cs` | Progress + history in Details; invoice in audit |
| `Controllers/ReceivingController.cs` | Search received-by; enhanced Details |
| `Controllers/HomeController.cs` | Warehouse KPIs + pending deliveries widget data |
| `Controllers/AuditTrailController.cs` | EntityReference search |
| `Controllers/ReportsController.cs` | SupplierPayables invoice/PO/remarks search |
| `Views/PurchaseOrders/Details.cshtml` | Progress + receiving history |
| `Views/Receiving/Details.cshtml` | Enhanced receipt display |
| `Views/Receiving/Index.cshtml` | Search placeholder |
| `Views/Home/Index.cshtml` | Warehouse KPIs + pending deliveries widget |
| `Tools/ReceivingUxQaRunner.cs` | **New** `--qa-receiving-ux` |
| `Program.cs` | QA hook |

## Migrations

None — UI and query enhancements only.

## QA

```bash
dotnet run -- --qa-receiving-ux
dotnet run -- --qa-receiving
```

## Remaining Limitations

- Delivery reference shares `InvoiceNumber` field when no separate ref entered (no duplicate field per RC1.8 constraint)
- Dashboard KPIs respect current branch filter when branch is selected
