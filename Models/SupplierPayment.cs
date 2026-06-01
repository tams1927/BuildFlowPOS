using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class SupplierPayment : ITenantEntity
    {
        public int Id { get; set; }

        public int StockInHeaderId { get; set; }

        public int SupplierId { get; set; }

        public DateTime PaymentDate { get; set; } = DateTime.Now;

        [Column(TypeName = "decimal(18,2)")]
        public decimal AmountPaid { get; set; }

        [Required]
        [StringLength(50)]
        public string PaymentMethod { get; set; } = string.Empty;

        [StringLength(100)]
        public string? ReferenceNumber { get; set; }

        [StringLength(250)]
        public string? Remarks { get; set; }

        [StringLength(100)]
        public string CreatedBy { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // =========================================
        // NAVIGATION
        // =========================================

        public StockInHeader? StockInHeader { get; set; }

        public Supplier? Supplier { get; set; }
    }
}