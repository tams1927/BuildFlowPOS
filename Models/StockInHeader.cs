using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareManagementSystem.Models
{
    public class StockInHeader
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string StockInNumber { get; set; } = string.Empty;

        public DateTime DateReceived { get; set; } = DateTime.Now;

        public int SupplierId { get; set; }

        [StringLength(100)]
        public string? InvoiceNumber { get; set; }

        [StringLength(250)]
        public string? Remarks { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalCost { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Supplier? Supplier { get; set; }

        public ICollection<StockInDetail> StockInDetails { get; set; } = new List<StockInDetail>();
    }
}