# Phase 4.6 — Inventory Intelligence & Stock Analytics

**Date:** May 31, 2026  
**Build Result:** ✅ 0 Errors · 0 Warnings  
**Migration:** `Phase46_InventoryIntelligence`

---

## Overview

Phase 4.6 transforms the inventory module from a stock-tracking system into an inventory decision-support system. It adds 7 intelligence reports (Fast Moving, Slow Moving, Dead Stock, Reorder Suggestions, Stock Aging, Inventory Valuation, ABC Analysis), an Inventory Health Score dashboard widget, Top-10 widgets, clickable KPI cards, and direct Purchase Order generation from the Reorder Suggestions report.

---

## Inventory Intelligence Architecture

```
Item.CurrentStock + Item.CostPrice
      │
      ├── Inventory Valuation (by category)
      ├── Dead Stock Detection (no sales in N days)
      ├── Reorder Suggestions (stock ≤ reorder level)
      ├── Stock Aging (by last stock-in date)
      └── Health Score

SalesDetail.Quantity + SalesHeader.SalesDate
      │
      ├── Fast Moving Items (sorted by qty sold)
      ├── Slow Moving Items (< threshold activity)
      └── ABC Analysis (revenue contribution %)
```

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `ViewModels/InventoryAnalyticsViewModels.cs` | All 8 inventory analytics viewmodels |
| `Controllers/ReportsController.Inventory.cs` | All 7 report actions + helpers + health score |
| `Views/Reports/FastMovingItems.cshtml` | Fast Moving Items report |
| `Views/Reports/SlowMovingItems.cshtml` | Slow Moving Items report |
| `Views/Reports/DeadStock.cshtml` | Dead Stock report |
| `Views/Reports/ReorderSuggestions.cshtml` | Reorder Suggestions + PO generation |
| `Views/Reports/StockAging.cshtml` | Stock Aging by bucket |
| `Views/Reports/InventoryValuationReport.cshtml` | Inventory Valuation (grouped by category) |
| `Views/Reports/ABCAnalysis.cshtml` | ABC Revenue Classification |
| `Migrations/…Phase46_InventoryIntelligence.*` | EF migration for MaxStockLevel |
| `PHASE46-README.md` | This report |

### Modified Files

| File | Change |
|------|--------|
| `Models/Item.cs` | Added `MaxStockLevel decimal(18,3)?` |
| `Services/Pdf/ReportPdfService.cs` | Added `GenerateInventoryValuationPdf()` method |
| `Controllers/HomeController.cs` | Added inventory intelligence KPI queries |
| `Views/Home/Index.cshtml` | Added Inventory Intelligence section with 6 KPIs, Health Score, Top-10 widgets |
| `Data/Seeders/DbSeeder.cs` | Added 7 new permission modules |

---

## Database Changes

### Modified Table: `Items`

| Column Added | Type | Default | Notes |
|---|---|---|---|
| `MaxStockLevel` | `decimal(18,3)?` | NULL | Configures max stock for reorder qty calc |

---

## ViewModels

| ViewModel | Usage |
|-----------|-------|
| `FastMovingItemsViewModel` + `FastMovingItemRow` | Fast moving report |
| `SlowMovingItemsViewModel` + `SlowMovingItemRow` | Slow moving report |
| `DeadStockViewModel` + `DeadStockRow` | Dead stock with age color |
| `ReorderSuggestionsViewModel` + `ReorderSuggestionRow` | Reorder with suggested qty + supplier |
| `StockAgingViewModel` + `StockAgingRow` | 5-bucket stock aging |
| `InventoryValuationReport` + `InventoryValuationGroup` + `InventoryValuationRow` | Grouped valuation |
| `ABCAnalysisViewModel` + `ABCAnalysisRow` | Revenue classification |
| `InventoryHealthScore` | Health score formula result |

---

## Report Logic

### Fast Moving Items (`/Reports/FastMovingItems`)
- Queries `SalesDetail` → `SalesHeader` (Status = "Completed") within date range
- Groups by `ItemId`, sums `Quantity` (QtySold) and `LineTotal` (Revenue)
- Orders by QtySold descending
- Filters: Date Range, Branch, Category
- PDF: tabular via `GenerateSimpleReportPdf`

### Slow Moving Items (`/Reports/SlowMovingItems`)
- Items with `CurrentStock > 0` where sales in last N days < threshold OR days since last sale > threshold
- Threshold configurable: 30 / 60 / 90 / 180 / 365 days
- Orders by qtySold ascending, then by days inactive descending
- PDF: tabular

### Dead Stock Detection (`/Reports/DeadStock`)
- Items with `CurrentStock > 0` and no `Completed` sales in the specified period (default: 90 days)
- Threshold configurable: 30 / 60 / 90 / 180 / 365 days
- Color coding: Yellow (90-180 days), Orange (180-365 days), Red (365+ days or never sold)
- Orders by DaysSinceLastSale descending

### Reorder Algorithm (`/Reports/ReorderSuggestions`)
- Items where `Item.CurrentStock <= Item.ReorderLevel`
- **Suggested Qty:**
  - If `MaxStockLevel` is set: `MaxStockLevel - CurrentStock`
  - Otherwise: `(AvgMonthlySales × 2) - CurrentStock` (avg from last 90 days / 3)
  - Fallback if still ≤ 0: `ReorderLevel - CurrentStock + 1`
- Shows preferred supplier from `Item.SupplierId`
- Estimated cost = `SuggestedQty × CostPrice`

### Stock Aging (`/Reports/StockAging`)
- Uses last stock-in date per item from `StockInDetail` → `StockInHeader.DateReceived`
- Buckets current stock into 5 age bands based on `(today - lastStockInDate).Days`:
  - 0–30 · 31–60 · 61–90 · 91–180 · 180+ Days
- Values computed as `Qty × WeightedAvgCost`

### Inventory Valuation (`/Reports/InventoryValuationReport`)
- All active items with `CurrentStock > 0`
- Method: **Weighted Average Cost** (using `Item.CostPrice` as the stored WAC)
- `InventoryValue = CurrentStock × CostPrice`
- Grouped by Category with subtotals and grand total
- PDF: custom `GenerateInventoryValuationPdf()` with category groups

### ABC Analysis (`/Reports/ABCAnalysis`)
- Computes revenue per item from `SalesDetail` for the date range
- Sorts by revenue descending
- Assigns classification:
  - **A** = top items summing to 80% cumulative revenue
  - **B** = next items summing to 95% cumulative
  - **C** = remainder
- Cumulative progress bar in view
- PDF: tabular with classification column

---

## Inventory Health Score

**Formula:**
```
Score = 100
  - (lowStockPct  × 30)   → up to 30 points for low stock
  - (criticalPct  × 20)   → up to 20 points for critical stock
  - (deadStockPct × 30)   → up to 30 points for dead stock
  - (overstockPct × 20)   → up to 20 points for overstock
```

Where `overstock` = items with stock > 2× their reorder level.

| Score | Status | Color | Message |
|-------|--------|-------|---------|
| 80–100 | Good  | Green | Inventory is well-managed. Continue monitoring. |
| 60–79  | Fair  | Yellow | Address low-stock and dead-stock items. |
| 0–59   | Poor  | Red   | Immediate action required. |

---

## Dashboard Enhancements

**6 new Inventory Intelligence KPI cards** (all clickable):

| Card | Links to | Metric |
|------|----------|--------|
| Inventory Value | InventoryValuationReport | Total ₱ at cost |
| Fast Moving | FastMovingItems | Items sold in last 30 days |
| Slow Moving | SlowMovingItems | Items inactive 90+ days |
| Dead Stock | DeadStock | Items with 0 sales in 90 days |
| Needs Reorder | ReorderSuggestions | Items at/below reorder level |
| ABC Analysis | ABCAnalysis | Revenue classification link |

**3 new dashboard widgets:**
- Inventory Health Score (circular gauge with score, status, recommendation)
- Top 10 Fast Moving (ranked list)
- Top 10 Highest Inventory Value (ranked list)

---

## Purchase Order Integration

From `/Reports/ReorderSuggestions`:
1. User selects items via checkboxes (Select All / Clear buttons)
2. Clicks "Generate Purchase Order" → SweetAlert confirmation
3. On confirm, POST to `GeneratePOFromReorder` which:
   - Validates at least one item selected
   - Builds redirect URL to `PurchaseOrders/Create` with item IDs and supplier pre-selected
   - Logs audit event `PURCHASE_ORDER_GENERATED_FROM_REORDER`
   - Shows success toast

---

## Audit Events

| Event | Trigger | Module |
|-------|---------|--------|
| `FAST_MOVING_REPORT_VIEWED` | FastMovingItems accessed | FastMovingItems |
| `SLOW_MOVING_REPORT_VIEWED` | SlowMovingItems accessed | SlowMovingItems |
| `DEAD_STOCK_REPORT_VIEWED` | DeadStock accessed | DeadStock |
| `REORDER_REPORT_VIEWED` | ReorderSuggestions accessed | ReorderSuggestions |
| `STOCK_AGING_REPORT_VIEWED` | StockAging accessed | StockAging |
| `INVENTORY_VALUATION_VIEWED` | InventoryValuationReport accessed | InventoryValuation |
| `ABC_ANALYSIS_VIEWED` | ABCAnalysis accessed | ABCAnalysis |
| `PURCHASE_ORDER_GENERATED_FROM_REORDER` | PO generated from reorder | PurchaseOrders |

---

## Permissions Matrix

| Module | TenantAdmin | BranchManager | SuperAdmin |
|--------|-------------|---------------|-----------|
| `FastMovingItems` | ✅ Full | ✅ Full | ✅ Full |
| `SlowMovingItems` | ✅ Full | ✅ Full | ✅ Full |
| `DeadStock` | ✅ Full | ✅ Full | ✅ Full |
| `ReorderSuggestions` | ✅ Full | ✅ Full | ✅ Full |
| `StockAging` | ✅ Full | ✅ Full | ✅ Full |
| `InventoryValuation` | ✅ Full | ✅ Full | ✅ Full |
| `ABCAnalysis` | ✅ Full | ✅ Full | ✅ Full |

---

## PDF Features

| Report | PDF Method | Format |
|--------|-----------|--------|
| Fast Moving | `GenerateSimpleReportPdf` | A4 Portrait |
| Slow Moving | `GenerateSimpleReportPdf` | A4 Portrait |
| Dead Stock | `GenerateSimpleReportPdf` | A4 Portrait |
| Reorder Suggestions | `GenerateSimpleReportPdf` | A4 Portrait |
| Stock Aging | `GenerateSimpleReportPdf` | A4 Portrait |
| Inventory Valuation | `GenerateInventoryValuationPdf` (custom) | A4 Portrait |
| ABC Analysis | `GenerateSimpleReportPdf` | A4 Portrait |

All PDFs: Tenant name · Branch · Date range · Generated timestamp · Page numbers.
`GenerateInventoryValuationPdf` additionally: category groups + subtotals + grand total + logo.

---

## Testing Checklist

### Fast Moving Tests
- [ ] Report loads with date range filter
- [ ] Items ranked by qty sold descending
- [ ] Category and branch filters work
- [ ] PDF downloads and shows correct data
- [ ] Items with no sales are excluded

### Slow Moving Tests
- [ ] Items with 0 sales in threshold period appear
- [ ] "Never sold" items appear with ∞ indicator
- [ ] Threshold dropdown changes results
- [ ] Items in stock but never sold are included

### Dead Stock Tests
- [ ] Only items with CurrentStock > 0 appear
- [ ] Color coding: yellow/orange/red applied correctly
- [ ] Configurable days filter (30, 60, 90, 180, 365)
- [ ] Items never sold show "—" for last sale date

### Reorder Suggestions Tests
- [ ] Only items with stock ≤ ReorderLevel appear
- [ ] SuggestedQty = (MaxStockLevel - CurrentStock) when MaxStockLevel is set
- [ ] SuggestedQty = (AvgMonthly × 2) - CurrentStock when MaxStockLevel is null
- [ ] Select All / Clear All checkboxes work
- [ ] SweetAlert fires before PO generation
- [ ] Redirect to PO Create with items pre-selected
- [ ] Audit event `PURCHASE_ORDER_GENERATED_FROM_REORDER` logged

### Stock Aging Tests
- [ ] Items bucketed by last stock-in date age
- [ ] Items with no stock-in history still appear (in 180+ bucket)
- [ ] Total value = sum across all buckets
- [ ] Bucket totals row is correct

### Inventory Valuation Tests
- [ ] All active items with stock > 0 appear
- [ ] Grouped by category with subtotals
- [ ] Grand total = sum of all subtotals
- [ ] Category filter narrows results
- [ ] PDF shows category groups with formatted tables

### ABC Analysis Tests
- [ ] Items sorted by revenue descending
- [ ] Cumulative % calculated correctly
- [ ] A class = items summing to 80%, B = 80-95%, C = 95-100%
- [ ] Color-coded badges: green(A), yellow(B), red(C)
- [ ] KPI shows count of A, B, C items

### Health Score Tests
- [ ] Score between 0-100
- [ ] Score ≥ 80 = Good (green)
- [ ] Score 60-79 = Fair (yellow)
- [ ] Score < 60 = Poor (red)
- [ ] Recommendation text varies by score

### Dashboard Tests
- [ ] 6 Inventory Intelligence KPI cards visible
- [ ] All cards link to correct reports
- [ ] Health Score gauge renders with correct percentage
- [ ] Top 10 Fast Moving list populated
- [ ] Top 10 High Value list populated

---

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Migration applied: `Phase46_InventoryIntelligence`

---

*Phase 4.6 complete. Suggested next phase: Phase 4.7 — Financial Reporting & Period-End Accounting.*
