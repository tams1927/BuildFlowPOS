using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareManagementSystem.Models
{
    public class CustomerLedger
    {
        public int Id { get; set; }

        [Required]
        public int CustomerId { get; set; }

        [ForeignKey(nameof(CustomerId))]
        public Customer? Customer { get; set; }

        [Required]
        [StringLength(50)]
        public string TransactionType { get; set; } = string.Empty;
        // CHARGE
        // PAYMENT
        // ADJUSTMENT

        [Required]
        [StringLength(50)]
        public string ReferenceNumber { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal DebitAmount { get; set; } = 0;
        // Customer owes amount

        [Column(TypeName = "decimal(18,2)")]
        public decimal CreditAmount { get; set; } = 0;
        // Customer payment amount

        [Column(TypeName = "decimal(18,2)")]
        public decimal RunningBalance { get; set; } = 0;

        [StringLength(500)]
        public string? Remarks { get; set; }

        public DateTime TransactionDate { get; set; } = DateTime.Now;

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [StringLength(50)]
        public string? PaymentMethod { get; set; }

        [StringLength(100)]
        public string? PaymentReferenceNumber { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal BalanceBefore { get; set; } = 0;
    }
}