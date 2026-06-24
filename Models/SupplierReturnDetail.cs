using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareManagementSystem.Models
{
    public class SupplierReturnDetail
    {
        public int Id { get; set; }

        public int SupplierReturnHeaderId { get; set; }

        public int ItemId { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; }

        public int UnitId { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal ConversionQuantity { get; set; } = 1;

        [Column(TypeName = "decimal(18,3)")]
        public decimal BaseQuantity { get; set; }

        [Required]
        [StringLength(30)]
        public string Reason { get; set; } = "Damaged";

        [StringLength(500)]
        public string? Notes { get; set; }

        public SupplierReturnHeader? SupplierReturnHeader { get; set; }

        public Item? Item { get; set; }

        [ForeignKey(nameof(UnitId))]
        public Unit? Unit { get; set; }
    }
}
