namespace HardwareManagementSystem.ViewModels
{
    public class SupplierPaymentHistoryReportViewModel
    {
        public DateTime PaymentDate { get; set; }

        public string StockInNumber { get; set; } = string.Empty;

        public string SupplierName { get; set; } = string.Empty;

        public decimal AmountPaid { get; set; }

        public string PaymentMethod { get; set; } = string.Empty;

        public string? ReferenceNumber { get; set; }

        public string? Remarks { get; set; }

        public string CreatedBy { get; set; } = string.Empty;
    }
}