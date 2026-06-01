namespace HardwareManagementSystem.ViewModels
{
    public class InventoryValuationViewModel
    {
        public int ItemId { get; set; }

        public string ItemCode { get; set; } = string.Empty;

        public string ItemName { get; set; } = string.Empty;

        public string CategoryName { get; set; } = string.Empty;

        public string UnitName { get; set; } = string.Empty;

        public decimal Quantity { get; set; }

        public decimal CostPrice { get; set; }

        public decimal SellingPrice { get; set; }

        public decimal CostValue { get; set; }

        public decimal SellingValue { get; set; }

        public decimal ReorderLevel { get; set; }

        public string StockStatus { get; set; } = string.Empty;

        public int? BranchId { get; set; }

        public string? BranchName { get; set; }
    }
}
