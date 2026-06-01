using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    /// <summary>
    /// Tracks per-branch stock level for each product.
    /// This is the branch-aware stock source. Item.CurrentStock is kept for backward
    /// compatibility with existing reports and POS but will be phased out once all
    /// branch-aware flows are fully active.
    /// </summary>
    public class BranchProductStock : IBranchEntity
    {
        public int Id { get; set; }

        public int BranchId { get; set; }

        public int ProductId { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; } = 0;

        /// <summary>
        /// Optional branch-specific reorder level override.
        /// Falls back to Item.ReorderLevel when null.
        /// </summary>
        [Column(TypeName = "decimal(18,3)")]
        public decimal? ReorderLevel { get; set; }

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public Branch? Branch { get; set; }

        public Item? Product { get; set; }

        /// <summary>Tenant this stock record belongs to. Nullable for backward compatibility.</summary>
        public int? TenantId { get; set; }

        public Tenant? Tenant { get; set; }
    }
}
