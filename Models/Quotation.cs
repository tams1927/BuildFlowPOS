using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class Quotation : ITenantEntity
    {
        public int Id { get; set; }

        public int? TenantId { get; set; }
        public Tenant? Tenant { get; set; }

        public int? BranchId { get; set; }
        public Branch? Branch { get; set; }

        public int? CustomerId { get; set; }
        public Customer? Customer { get; set; }

        [Required]
        [StringLength(30)]
        public string QuotationNo { get; set; } = string.Empty;

        /// <summary>Walk-in or override name when no Customer record is selected.</summary>
        [StringLength(150)]
        public string? CustomerName { get; set; }

        public DateTime QuotationDate { get; set; } = DateTime.Today;

        public DateTime? ValidUntil { get; set; }

        /// <summary>Draft | Sent | Accepted | Expired | Voided</summary>
        [StringLength(20)]
        public string Status { get; set; } = "Draft";

        [StringLength(500)]
        public string? Notes { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        [StringLength(450)]
        public string? CreatedByUserId { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Foreign key to the SalesHeader created via Convert to Sale. Null until converted.</summary>
        public int? ConvertedToSaleId { get; set; }

        public SalesHeader? ConvertedToSale { get; set; }

        public ICollection<QuotationItem> Items { get; set; } = new List<QuotationItem>();
    }
}
