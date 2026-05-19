using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareManagementSystem.Models
{
    public class SalesHeader
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string SalesNumber { get; set; } = string.Empty;

        public DateTime SalesDate { get; set; } = DateTime.Now;

        public int? CustomerId { get; set; }

        [StringLength(450)]
        public string? CashierId { get; set; }

        [StringLength(150)]
        public string? CashierName { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubTotal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; set; }

        [StringLength(50)]
        public string? DiscountType { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountValue { get; set; }

        [StringLength(150)]
        public string? DiscountReason { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal VatAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal AmountReceived { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ChangeAmount { get; set; }

        [StringLength(50)]
        public string PaymentMethod { get; set; } = "Cash";

        [StringLength(100)]
        public string? ReferenceNumber { get; set; }

        [StringLength(50)]
        public string Status { get; set; } = "Completed";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Customer? Customer { get; set; }

        public ICollection<SalesDetail> SalesDetails { get; set; } = new List<SalesDetail>();
    }
}