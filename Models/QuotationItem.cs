using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class QuotationItem : ITenantEntity
    {
        public int Id { get; set; }

        public int QuotationId { get; set; }
        public Quotation? Quotation { get; set; }

        public int? ItemId { get; set; }
        public Item? Item { get; set; }

        [StringLength(200)]
        public string Description { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,4)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal DiscountPercent { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Subtotal { get; set; }

        public int SortOrder { get; set; } = 0;
    }
}
