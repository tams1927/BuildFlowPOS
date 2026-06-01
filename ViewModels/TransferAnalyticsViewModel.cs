namespace HardwareManagementSystem.ViewModels
{
    public class TransferAnalyticsViewModel
    {
        public string TransferNumber { get; set; } = string.Empty;

        public string FromBranch { get; set; } = string.Empty;

        public string ToBranch { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public int ItemCount { get; set; }

        public decimal TotalQuantity { get; set; }

        public string CreatedBy { get; set; } = string.Empty;

        public int? CompletionDays =>
            CompletedAt.HasValue
                ? (int?)(CompletedAt.Value - CreatedAt).TotalDays
                : null;
    }

    public class TransferProductRowViewModel
    {
        public string ItemCode { get; set; } = string.Empty;

        public string ItemName { get; set; } = string.Empty;

        public decimal TotalQuantityTransferred { get; set; }

        public int TransferCount { get; set; }
    }
}
