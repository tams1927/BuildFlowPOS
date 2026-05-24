namespace HardwareManagementSystem.ViewModels
{
    public class ProfitReportViewModel
    {
        public string SalesNumber { get; set; } = string.Empty;

        public DateTime SalesDate { get; set; }

        public string ItemCode { get; set; } = string.Empty;

        public string ItemName { get; set; } = string.Empty;

        public decimal QuantitySold { get; set; }

        public decimal SellingPrice { get; set; }

        public decimal CostPrice { get; set; }

        public decimal SalesAmount { get; set; }

        public decimal CostAmount { get; set; }

        public decimal GrossProfit { get; set; }

        public decimal ProfitMarginPercent { get; set; }
    }
}