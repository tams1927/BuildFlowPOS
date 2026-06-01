namespace HardwareManagementSystem.ViewModels
{
    public class InventoryMovementVm
    {
        public DateTime Date { get; set; }
        public string MovementType { get; set; } = string.Empty;
        public string? Branch { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public decimal? QuantityIn { get; set; }
        public decimal? QuantityOut { get; set; }
        public string? Reference { get; set; }
        public string? Notes { get; set; }
    }
}
