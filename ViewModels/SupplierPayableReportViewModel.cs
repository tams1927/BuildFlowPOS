namespace HardwareManagementSystem.ViewModels
{
    public class SupplierPayableReportViewModel
    {
        public int StockInId { get; set; }

        public string StockInNumber { get; set; } = string.Empty;

        public string SupplierName { get; set; } = string.Empty;

        public string? InvoiceNumber { get; set; }

        public DateTime DateReceived { get; set; }

        public DateTime? DueDate { get; set; }

        public decimal TotalCost { get; set; }

        public decimal AmountPaid { get; set; }

        public decimal BalanceDue { get; set; }

        public string PaymentStatus { get; set; } = string.Empty;

        public string? PaymentMethod { get; set; }

        public int AgingDays { get; set; }
    }
}