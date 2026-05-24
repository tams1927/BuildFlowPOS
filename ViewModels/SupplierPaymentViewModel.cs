using System.ComponentModel.DataAnnotations;

namespace HardwareManagementSystem.ViewModels
{
    public class SupplierPaymentViewModel
    {
        public int StockInId { get; set; }

        public string StockInNumber { get; set; } = string.Empty;

        public string SupplierName { get; set; } = string.Empty;

        public decimal TotalCost { get; set; }

        public decimal AmountPaid { get; set; }

        public decimal BalanceDue { get; set; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Payment amount must be greater than zero.")]
        public decimal PaymentAmount { get; set; }

        [Required]
        public string PaymentMethod { get; set; } = string.Empty;

        public string? PaymentReferenceNumber { get; set; }

        public string? Remarks { get; set; }
    }
}