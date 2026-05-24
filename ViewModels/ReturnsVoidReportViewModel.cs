namespace HardwareManagementSystem.ViewModels
{
    public class ReturnsVoidReportViewModel
    {
        public string ReportType { get; set; } = string.Empty;
        // RETURN or VOID

        public string ReferenceNumber { get; set; } = string.Empty;

        public string OriginalReceiptNumber { get; set; } = string.Empty;

        public DateTime TransactionDate { get; set; }

        public string Reason { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public string ProcessedBy { get; set; } = string.Empty;
    }
}