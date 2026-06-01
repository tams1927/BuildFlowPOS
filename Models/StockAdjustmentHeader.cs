using System.ComponentModel.DataAnnotations;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class StockAdjustmentHeader : ITenantEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string AdjustmentNumber { get; set; } = string.Empty;

        public DateTime AdjustmentDate { get; set; } = DateTime.Now;

        [Required]
        [StringLength(50)]
        public string AdjustmentType { get; set; } = "Increase";

        [StringLength(250)]
        public string? Reason { get; set; }

        [StringLength(150)]
        public string? CreatedBy { get; set; }

        /// <summary>Branch this adjustment belongs to. Nullable for backward compatibility.</summary>
        public int? BranchId { get; set; }

        public Branch? Branch { get; set; }

        /// <summary>Tenant this adjustment belongs to. Nullable for backward compatibility.</summary>
        public int? TenantId { get; set; }

        public Tenant? Tenant { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public ICollection<StockAdjustmentDetail> StockAdjustmentDetails { get; set; } = new List<StockAdjustmentDetail>();
    }
}