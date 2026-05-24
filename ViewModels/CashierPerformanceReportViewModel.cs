namespace HardwareManagementSystem.ViewModels
{
    public class CashierPerformanceReportViewModel
    {
        public string CashierName { get; set; } = string.Empty;

        public int TransactionCount { get; set; }

        public decimal GrossSales { get; set; }

        public decimal TotalDiscounts { get; set; }

        public decimal NetSales { get; set; }

        public decimal CashSales { get; set; }

        public decimal GCashSales { get; set; }

        public decimal CreditSales { get; set; }

        public int VoidCount { get; set; }

        public decimal VoidAmount { get; set; }

        public decimal AverageSale { get; set; }
    }
}