using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class SalesReturnDetail : ITenantEntity
    {
        public int Id { get; set; }

        public int SalesReturnHeaderId { get; set; }

        public int SalesDetailId { get; set; }

        public int ItemId { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal QuantityReturned { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal LineRefundAmount { get; set; }

        public bool RestoreToInventory { get; set; } = true;

        public SalesReturnHeader? SalesReturnHeader { get; set; }

        public SalesDetail? SalesDetail { get; set; }

        public Item? Item { get; set; }
    }
}