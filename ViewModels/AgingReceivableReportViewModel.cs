namespace HardwareManagementSystem.ViewModels
{
    public class AgingReceivableReportViewModel
    {
        public string CustomerName { get; set; } = string.Empty;

        public decimal TotalBalance { get; set; }

        public decimal CurrentBalance { get; set; }

        public decimal Days30 { get; set; }

        public decimal Days60 { get; set; }

        public decimal Days90 { get; set; }

        public decimal Over90Days { get; set; }

        public DateTime? LastTransactionDate { get; set; }

        public string Status { get; set; } = string.Empty;
    }
}