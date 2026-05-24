namespace HardwareManagementSystem.ViewModels
{
    public class FastSlowMovingViewModel
    {
        public int ItemId { get; set; }

        public string ItemName { get; set; } = string.Empty;

        public decimal QuantitySold { get; set; }

        public decimal SalesAmount { get; set; }

        public decimal CurrentStock { get; set; }

        public DateTime? LastSoldDate { get; set; }
    }
}