namespace HardwareManagementSystem.ViewModels
{
    public class InventoryStatusReportViewModel
    {
        public int ItemId { get; set; }

        public string ItemCode { get; set; } = string.Empty;

        public string ItemName { get; set; } = string.Empty;

        public string CategoryName { get; set; } = string.Empty;

        public string UnitName { get; set; } = string.Empty;

        public decimal CurrentStock { get; set; }

        public decimal ReorderLevel { get; set; }

        public decimal CostPrice { get; set; }

        public decimal SellingPrice { get; set; }

        public decimal InventoryValue { get; set; }

        public string StockStatus { get; set; } = string.Empty;
    }
}