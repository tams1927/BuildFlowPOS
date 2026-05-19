using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareManagementSystem.Models
{
    public class StockInDetail
    {
        public int Id { get; set; }

        public int StockInHeaderId { get; set; }

        public int ItemId { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitCost { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalCost { get; set; }

        public StockInHeader? StockInHeader { get; set; }

        public Item? Item { get; set; }
    }
}