namespace HardwareManagementSystem.ViewModels
{
    public class SalesDetailReportViewModel
    {
        public string SalesNumber { get; set; } = string.Empty;

        public DateTime SalesDate { get; set; }

        public string CustomerName { get; set; } = string.Empty;

        public string CashierName { get; set; } = string.Empty;

        public string PaymentMethod { get; set; } = string.Empty;

        public decimal SubTotal { get; set; }

        public decimal DiscountAmount { get; set; }

        public decimal TotalAmount { get; set; }

        public string Status { get; set; } = string.Empty;
    }
}