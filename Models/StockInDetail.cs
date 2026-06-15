using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class StockInDetail : ITenantEntity
    {
        public int Id { get; set; }

        public int StockInHeaderId { get; set; }

        public int ItemId { get; set; }

        /// <summary>Base-unit quantity added to inventory (legacy column name retained).</summary>
        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; }

        public int? ReceivedUnitId { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal ReceivedQuantity { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal ConversionQuantity { get; set; } = 1;

        [Column(TypeName = "decimal(18,3)")]
        public decimal BaseQuantity { get; set; }

        /// <summary>Cost per base inventory unit (valuation). Mirrors <see cref="CostPerBaseUnit"/>.</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitCost { get; set; }

        /// <summary>Supplier cost per received/purchase unit (what the user enters).</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal CostPerReceivedUnit { get; set; }

        /// <summary>Computed: CostPerReceivedUnit ÷ ConversionQuantity.</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal CostPerBaseUnit { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalCost { get; set; }

        public StockInHeader? StockInHeader { get; set; }

        public Item? Item { get; set; }

        [ForeignKey(nameof(ReceivedUnitId))]
        public Unit? ReceivedUnit { get; set; }
    }
}