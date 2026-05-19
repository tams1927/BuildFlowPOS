using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareManagementSystem.Models
{
    public class Item
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

        public int? SupplierId { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal CurrentStock { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal ReorderLevel { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CostPrice { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SellingPrice { get; set; }

        [StringLength(50)]
        public string Status { get; set; } = "Active";

        [StringLength(250)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Category? Category { get; set; }

        public Unit? Unit { get; set; }

        public Supplier? Supplier { get; set; }
    }
}