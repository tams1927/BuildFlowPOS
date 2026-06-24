using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class Item : ITenantEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ItemCode { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string ItemName { get; set; } = string.Empty;

        public int CategoryId { get; set; }

        public int UnitId { get; set; }

        /// <summary>
        /// Unit used for inventory, reports, valuation, POS deduction, and stock balance.
        /// Defaults to <see cref="UnitId"/> for existing products.
        /// </summary>
        public int BaseUnitId { get; set; }

        public int? SupplierId { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal CurrentStock { get; set; }

        /// <summary>Sellable stock is <see cref="CurrentStock"/>; damaged bucket is tracked separately.</summary>
        [Column(TypeName = "decimal(18,3)")]
        public decimal DamagedStock { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal ReorderLevel { get; set; }

        /// <summary>
        /// Maximum desired stock level. Used for reorder quantity calculation.
        /// When null, suggested reorder qty falls back to avg monthly sales × 2.
        /// </summary>
        [Column(TypeName = "decimal(18,3)")]
        public decimal? MaxStockLevel { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CostPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SellingPrice { get; set; }

        [StringLength(50)]
        public string Status { get; set; } = "Active";

        [StringLength(250)]
        public string? Description { get; set; }

        /// <summary>Barcode / EAN / UPC. Used for POS barcode scanner input.</summary>
        [StringLength(100)]
        public string? Barcode { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>Tenant this product belongs to. Nullable for backward compatibility.</summary>
        public int? TenantId { get; set; }

        public Tenant? Tenant { get; set; }

        public Category? Category { get; set; }

        public Unit? Unit { get; set; }

        [ForeignKey(nameof(BaseUnitId))]
        public Unit? BaseUnit { get; set; }

        public ICollection<ItemUnitConversion> UnitConversions { get; set; } = new List<ItemUnitConversion>();

        public Supplier? Supplier { get; set; }
    }
}