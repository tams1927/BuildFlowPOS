namespace HardwareManagementSystem.ViewModels
{
    public class VatTaxSummaryViewModel
    {
        public decimal GrossSales { get; set; }

        public decimal VatSales { get; set; }

        public decimal VatAmount { get; set; }

        public decimal VatExemptSales { get; set; }

        public decimal ZeroRatedSales { get; set; }

        public decimal NetOfVatSales { get; set; }
    }
}