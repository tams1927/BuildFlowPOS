using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareManagementSystem.Models
{
    public class StockAdjustmentDetail
    {
        public int Id { get; set; }

        public int StockAdjustmentHeaderId { get; set; }

        public int ItemId { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal StockBefore { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal StockAfter { get; set; }

        public StockAdjustmentHeader? StockAdjustmentHeader { get; set; }

        public Item? Item { get; set; }
    }
}