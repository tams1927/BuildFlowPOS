namespace HardwareManagementSystem.ViewModels
{
    public class CollectionDetailReportViewModel
    {
        public string CollectionNumber { get; set; } = string.Empty;

        public DateTime CollectionDate { get; set; }

        public string CustomerName { get; set; } = string.Empty;

        public string PaymentMethod { get; set; } = string.Empty;

        public string? PaymentReferenceNumber { get; set; }

        public decimal PreviousBalance { get; set; }

        public decimal PaymentAmount { get; set; }

        public decimal RemainingBalance { get; set; }

        public string CollectedBy { get; set; } = string.Empty;
    }
}