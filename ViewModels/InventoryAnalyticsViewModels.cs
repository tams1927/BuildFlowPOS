namespace HardwareManagementSystem.ViewModels
{
    // ── Fast-Moving Items ──────────────────────────────────────────────

    public class FastMovingItemsViewModel
    {
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
        public int? BranchId { get; set; }
        public string? BranchName { get; set; }
        public int? CategoryId { get; set; }
        public List<FastMovingItemRow> Rows { get; set; } = new();
    }

    public class FastMovingItemRow
    {
        public int     ItemId       { get; set; }
        public string  ItemCode     { get; set; } = string.Empty;
        public string  ItemName     { get; set; } = string.Empty;
        public string  Category     { get; set; } = string.Empty;
        public string  Unit         { get; set; } = string.Empty;
        public decimal QtySold      { get; set; }
        public decimal Revenue      { get; set; }
        public decimal CurrentStock { get; set; }
        public decimal CostPrice    { get; set; }
    }

    // ── Slow-Moving Items ──────────────────────────────────────────────

    public class SlowMovingItemsViewModel
    {
        public int? BranchId { get; set; }
        public string? BranchName { get; set; }
        public int? CategoryId { get; set; }
        public int ThresholdDays { get; set; }
        public List<SlowMovingItemRow> Rows { get; set; } = new();
    }

    public class SlowMovingItemRow
    {
        public int       ItemId           { get; set; }
        public string    ItemCode         { get; set; } = string.Empty;
        public string    ItemName         { get; set; } = string.Empty;
        public string    Category         { get; set; } = string.Empty;
        public decimal   CurrentStock     { get; set; }
        public decimal   InventoryValue   { get; set; }
        public decimal   QtySold          { get; set; }
        public DateTime? LastSoldDate     { get; set; }
        public int       DaysSinceLastSale { get; set; }
    }

    // ── Dead Stock ─────────────────────────────────────────────────────

    public class DeadStockViewModel
    {
        public int DaysThreshold { get; set; }
        public int? BranchId { get; set; }
        public string? BranchName { get; set; }
        public int? CategoryId { get; set; }
        public List<DeadStockRow> Rows { get; set; } = new();
        public decimal TotalValue => Rows.Sum(r => r.InventoryValue);
    }

    public class DeadStockRow
    {
        public int       ItemId           { get; set; }
        public string    ItemCode         { get; set; } = string.Empty;
        public string    ItemName         { get; set; } = string.Empty;
        public string    Category         { get; set; } = string.Empty;
        public decimal   CurrentStock     { get; set; }
        public decimal   InventoryValue   { get; set; }
        public DateTime? LastSaleDate     { get; set; }
        public int       DaysSinceLastSale { get; set; }
        /// <summary>yellow | orange | red</summary>
        public string    AgeColor         { get; set; } = "yellow";
    }

    // ── Reorder Suggestions ────────────────────────────────────────────

    public class ReorderSuggestionsViewModel
    {
        public int? BranchId { get; set; }
        public string? BranchName { get; set; }
        public int? CategoryId { get; set; }
        public List<ReorderSuggestionRow> Rows { get; set; } = new();
    }

    public class ReorderSuggestionRow
    {
        public int      ItemId             { get; set; }
        public string   ItemCode           { get; set; } = string.Empty;
        public string   ItemName           { get; set; } = string.Empty;
        public string   Category           { get; set; } = string.Empty;
        public decimal  CurrentStock       { get; set; }
        public decimal  ReorderLevel       { get; set; }
        public decimal? MaxStockLevel      { get; set; }
        public decimal  SuggestedQty       { get; set; }
        public string?  PreferredSupplier  { get; set; }
        public int?     SupplierId         { get; set; }
        public decimal  AvgMonthlySales    { get; set; }
        public decimal  CostPrice          { get; set; }
    }

    // ── Stock Aging ────────────────────────────────────────────────────

    public class StockAgingViewModel
    {
        public int? BranchId { get; set; }
        public string? BranchName { get; set; }
        public int? CategoryId { get; set; }
        public DateTime AsOfDate { get; set; }
        public List<StockAgingRow> Rows { get; set; } = new();

        public decimal TotalQty0_30   => Rows.Sum(r => r.Qty0_30);
        public decimal TotalQty31_60  => Rows.Sum(r => r.Qty31_60);
        public decimal TotalQty61_90  => Rows.Sum(r => r.Qty61_90);
        public decimal TotalQty91_180 => Rows.Sum(r => r.Qty91_180);
        public decimal TotalQty180P   => Rows.Sum(r => r.Qty180Plus);

        public decimal TotalVal0_30   => Rows.Sum(r => r.Value0_30);
        public decimal TotalVal31_60  => Rows.Sum(r => r.Value31_60);
        public decimal TotalVal61_90  => Rows.Sum(r => r.Value61_90);
        public decimal TotalVal91_180 => Rows.Sum(r => r.Value91_180);
        public decimal TotalVal180P   => Rows.Sum(r => r.Value180Plus);
        public decimal TotalValue     => Rows.Sum(r => r.TotalValue);
    }

    public class StockAgingRow
    {
        public int     ItemId     { get; set; }
        public string  ItemCode   { get; set; } = string.Empty;
        public string  ItemName   { get; set; } = string.Empty;
        public string  Category   { get; set; } = string.Empty;
        public decimal Qty0_30    { get; set; }
        public decimal Qty31_60   { get; set; }
        public decimal Qty61_90   { get; set; }
        public decimal Qty91_180  { get; set; }
        public decimal Qty180Plus { get; set; }
        public decimal Value0_30   { get; set; }
        public decimal Value31_60  { get; set; }
        public decimal Value61_90  { get; set; }
        public decimal Value91_180 { get; set; }
        public decimal Value180Plus { get; set; }
        public decimal TotalQty   => Qty0_30 + Qty31_60 + Qty61_90 + Qty91_180 + Qty180Plus;
        public decimal TotalValue => Value0_30 + Value31_60 + Value61_90 + Value91_180 + Value180Plus;
    }

    // ── Inventory Valuation ────────────────────────────────────────────

    public class InventoryValuationReport
    {
        public int? BranchId { get; set; }
        public string? BranchName { get; set; }
        public int? CategoryId { get; set; }
        public DateTime AsOfDate { get; set; }
        public List<InventoryValuationGroup> Groups { get; set; } = new();
        public decimal GrandTotal => Groups.Sum(g => g.Subtotal);
        public decimal GrandTotalQty => Groups.Sum(g => g.TotalQty);
    }

    public class InventoryValuationGroup
    {
        public string  Category    { get; set; } = string.Empty;
        public List<InventoryValuationRow> Items { get; set; } = new();
        public decimal Subtotal    => Items.Sum(i => i.InventoryValue);
        public decimal TotalQty    => Items.Sum(i => i.QtyOnHand);
    }

    public class InventoryValuationRow
    {
        public int     ItemId         { get; set; }
        public string  ItemCode       { get; set; } = string.Empty;
        public string  ItemName       { get; set; } = string.Empty;
        public string  Unit           { get; set; } = string.Empty;
        public decimal QtyOnHand      { get; set; }
        public decimal AverageCost    { get; set; }
        public decimal InventoryValue { get; set; }
    }

    // ── ABC Analysis ───────────────────────────────────────────────────

    public class ABCAnalysisViewModel
    {
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
        public int? BranchId { get; set; }
        public string? BranchName { get; set; }
        public List<ABCAnalysisRow> Rows { get; set; } = new();
        public decimal TotalRevenue => Rows.Sum(r => r.Revenue);
        public int ACount => Rows.Count(r => r.Classification == "A");
        public int BCount => Rows.Count(r => r.Classification == "B");
        public int CCount => Rows.Count(r => r.Classification == "C");
    }

    public class ABCAnalysisRow
    {
        public int     ItemId           { get; set; }
        public string  ItemCode         { get; set; } = string.Empty;
        public string  ItemName         { get; set; } = string.Empty;
        public string  Category         { get; set; } = string.Empty;
        public decimal Revenue          { get; set; }
        public decimal ContributionPct  { get; set; }
        public decimal CumulativePct    { get; set; }
        public string  Classification   { get; set; } = "C"; // A | B | C
    }

    // ── Inventory Health Score ─────────────────────────────────────────

    public class InventoryHealthScore
    {
        public int     Score           { get; set; }
        public string  Status          { get; set; } = "Good"; // Good | Fair | Poor
        public string  StatusColor     { get; set; } = "success";
        public string  Recommendation  { get; set; } = string.Empty;
        public int     TotalItems      { get; set; }
        public int     LowStockCount   { get; set; }
        public int     CriticalCount   { get; set; }
        public int     DeadStockCount  { get; set; }
        public int     OverstockCount  { get; set; }
    }
}
