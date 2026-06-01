using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class PurchaseOrderItem : ITenantEntity
    {
        public int Id { get; set; }

        public int PurchaseOrderId { get; set; }

        public int ItemId { get; set; }

        /// <summary>Quantity originally ordered.</summary>
        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; }

        /// <summary>Cumulative quantity received so far (supports partial receiving).</summary>
        [Column(TypeName = "decimal(18,3)")]
        public decimal QuantityReceived { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitCost { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalCost { get; set; }

        // ── Navigations ───────────────────────────────────────────────

        public PurchaseOrder? PurchaseOrder { get; set; }
        public Item? Item { get; set; }

        // ── Computed helpers ──────────────────────────────────────────

        [NotMapped]
        public decimal QuantityRemaining => Quantity - QuantityReceived;
    }
}
