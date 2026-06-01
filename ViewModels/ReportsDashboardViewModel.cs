namespace HardwareManagementSystem.ViewModels
{
    public class ReportsDashboardViewModel
    {
        public DateTime DateFrom { get; set; }

        public DateTime DateTo { get; set; }

        public int? BranchId { get; set; }

        public string? BranchName { get; set; }

        public decimal GrossSales { get; set; }

        public decimal TotalDiscounts { get; set; }

        public decimal NetSales { get; set; }

        public int TransactionCount { get; set; }

        public decimal CashSales { get; set; }

        public decimal GCashSales { get; set; }

        public decimal CreditSales { get; set; }

        public decimal Collections { get; set; }

        public decimal Returns { get; set; }

        public decimal VoidedSales { get; set; }

        public decimal OutstandingCustomerBalance { get; set; }

        public int LowStockCount { get; set; }

        public int OutOfStockCount { get; set; }

        public List<TopSellingItemReportRow> TopSellingItems { get; set; } = new();

        public List<PaymentSummaryReportRow> PaymentSummary { get; set; } = new();

        public List<CustomerBalanceReportRow> CustomerBalances { get; set; } = new();
    }

    public class TopSellingItemReportRow
    {
        public string ItemName { get; set; } = string.Empty;

        public decimal QuantitySold { get; set; }

        public decimal SalesAmount { get; set; }
    }

    public class PaymentSummaryReportRow
    {
        public string PaymentMethod { get; set; } = string.Empty;

        public int TransactionCount { get; set; }

        public decimal TotalAmount { get; set; }
    }

    public class CustomerBalanceReportRow
    {
        public int CustomerId { get; set; }

        public string CustomerName { get; set; } = string.Empty;

        public decimal Balance { get; set; }
    }
}