using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class SalesReturnHeader : ITenantEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ReturnNumber { get; set; } = string.Empty;

        /// <summary>Tenant this return belongs to. Nullable for backward compatibility.</summary>
        public int? TenantId { get; set; }

        public Tenant? Tenant { get; set; }

        public DateTime ReturnDate { get; set; } = DateTime.Now;

        public int SalesHeaderId { get; set; }

        [StringLength(250)]
        public string? Reason { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal RefundAmount { get; set; }

        [StringLength(150)]
        public string? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public SalesHeader? SalesHeader { get; set; }

        public ICollection<SalesReturnDetail> SalesReturnDetails { get; set; } = new List<SalesReturnDetail>();
    }
}