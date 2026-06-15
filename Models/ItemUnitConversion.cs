using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    /// <summary>
    /// Maps a purchase/display unit to the item's base inventory unit.
    /// ConversionQuantity = how many base units equal one of this unit (e.g. 1 sack = 25 kg).
    /// </summary>
    public class ItemUnitConversion : ITenantEntity
    {
        public int Id { get; set; }

        public int? TenantId { get; set; }

        public int ItemId { get; set; }

        public int UnitId { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal ConversionQuantity { get; set; } = 1;

        public bool IsDefaultPurchaseUnit { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public Item? Item { get; set; }

        public Unit? Unit { get; set; }
    }
}
