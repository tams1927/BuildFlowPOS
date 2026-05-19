using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareManagementSystem.Models
{
    public class SalesDetail
    {
        public int Id { get; set; }

        public int SalesHeaderId { get; set; }

        public int ItemId { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal LineTotal { get; set; }

        public SalesHeader? SalesHeader { get; set; }

        public Item? Item { get; set; }
    }
}