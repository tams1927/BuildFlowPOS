namespace HardwareManagementSystem.ViewModels
{
    public class PoReceivingProgressVm
    {
        public decimal Ordered { get; set; }
        public decimal Received { get; set; }
        public decimal Remaining { get; set; }
        public int PercentComplete { get; set; }
        public string UnitLabel { get; set; } = "units";
        public string ProgressColor { get; set; } = "primary"; // primary=blue, success=green, warning=orange
        public bool IsFullyReceived { get; set; }
        public bool IsPartial { get; set; }
    }

    public class PoReceiptHistoryVm
    {
        public int StockInHeaderId { get; set; }
        public string ReceiptNumber { get; set; } = string.Empty;
        public DateTime DateReceived { get; set; }
        public string? ReceivedBy { get; set; }
        public string? SupplierInvoice { get; set; }
        public decimal AcceptedQty { get; set; }
        public decimal DamagedQty { get; set; }
        public string UnitLabel { get; set; } = "units";
        public string? Reference { get; set; }
        public decimal TotalCost { get; set; }
    }

    public class ReceivingDetailLineVm
    {
        public int ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public string? ItemCode { get; set; }
        public decimal OrderedQty { get; set; }
        public decimal AcceptedQty { get; set; }
        public decimal DamagedQty { get; set; }
        public decimal RejectedQty { get; set; }
        public string UnitLabel { get; set; } = "-";
        public decimal CostPerUnit { get; set; }
        public decimal LineTotal { get; set; }
    }

    public class PendingDeliveryVm
    {
        public int PurchaseOrderId { get; set; }
        public string PONumber { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public decimal RemainingQty { get; set; }
        public string UnitLabel { get; set; } = "units";
        public string Status { get; set; } = string.Empty;
        public DateTime PODate { get; set; }
    }
}
