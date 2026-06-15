using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class PurchaseOrderItem : ITenantEntity
    {
        public int Id { get; set; }

        public int PurchaseOrderId { get; set; }

        public int ItemId { get; set; }

        /// <summary>Quantity ordered in <see cref="OrderedUnitId"/> (supplier-friendly unit).</summary>
        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; }

        public int? OrderedUnitId { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal OrderedQuantity { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal ConversionQuantity { get; set; } = 1;

        [Column(TypeName = "decimal(18,3)")]
        public decimal BaseQuantity { get; set; }

        /// <summary>Cumulative base-unit quantity received (supports partial receiving).</summary>
        [Column(TypeName = "decimal(18,3)")]
        public decimal QuantityReceived { get; set; } = 0;

        /// <summary>Cost per base inventory unit. Mirrors <see cref="CostPerBaseUnit"/>.</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitCost { get; set; }

        /// <summary>Supplier cost per ordered/purchase unit (what the user enters).</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal CostPerOrderedUnit { get; set; }

        /// <summary>Computed: CostPerOrderedUnit ÷ ConversionQuantity.</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal CostPerBaseUnit { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalCost { get; set; }

        // ── Navigations ───────────────────────────────────────────────

        public PurchaseOrder? PurchaseOrder { get; set; }
        public Item? Item { get; set; }

        [ForeignKey(nameof(OrderedUnitId))]
        public Unit? OrderedUnit { get; set; }

        // ── Computed helpers ──────────────────────────────────────────

        [NotMapped]
        public decimal QuantityRemaining => Quantity - (ConversionQuantity > 0 ? QuantityReceived / ConversionQuantity : QuantityReceived);

        [NotMapped]
        public decimal BaseQuantityRemaining => BaseQuantity - QuantityReceived;
    }
}
