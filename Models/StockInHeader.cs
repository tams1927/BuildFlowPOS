using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareManagementSystem.Models
{
    public class StockInHeader
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string StockInNumber { get; set; } = string.Empty;

        public DateTime DateReceived { get; set; } = DateTime.Now;

        public int SupplierId { get; set; }

        [StringLength(100)]
        public string? InvoiceNumber { get; set; }

        [StringLength(250)]
        public string? Remarks { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalCost { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Supplier? Supplier { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal AmountPaid { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal BalanceDue { get; set; } = 0;

        public DateTime? DueDate { get; set; }

        [StringLength(50)]
        public string PaymentStatus { get; set; } = "Unpaid";
        // Unpaid, Partial, Paid

        [StringLength(50)]
        public string? PaymentMethod { get; set; }

        [StringLength(100)]
        public string? PaymentReferenceNumber { get; set; }

        public ICollection<StockInDetail> StockInDetails { get; set; } = new List<StockInDetail>();
    }
}