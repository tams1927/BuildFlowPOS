namespace HardwareManagementSystem.ViewModels
{
    public class CustomerBalanceReportViewModel
    {
        public int CustomerId { get; set; }

        public string CustomerName { get; set; } = string.Empty;

        public string CustomerType { get; set; } = string.Empty;

        public string? ContactNumber { get; set; }

        public decimal TotalCharges { get; set; }

        public decimal TotalPayments { get; set; }

        public decimal OutstandingBalance { get; set; }

        public DateTime? LastTransactionDate { get; set; }
    }
}