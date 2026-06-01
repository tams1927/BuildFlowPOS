namespace HardwareManagementSystem.ViewModels
{
    public class ExpenseVsProfitReportViewModel
    {
        public int? BranchId { get; set; }

        public string? BranchName { get; set; }

        public decimal GrossSales { get; set; }

        public decimal CostOfGoods { get; set; }

        public decimal GrossProfit { get; set; }

        public decimal OperatingExpenses { get; set; }

        public decimal NetProfit { get; set; }

        public decimal ProfitMarginPercent { get; set; }
    }
}