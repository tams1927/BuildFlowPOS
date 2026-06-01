using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class DeliveryReceiptItem : ITenantEntity
    {
        public int Id { get; set; }

        public int DeliveryReceiptId { get; set; }
        public DeliveryReceipt? DeliveryReceipt { get; set; }

        public int? ItemId { get; set; }
        public Item? Item { get; set; }

        [Required]
        [StringLength(200)]
        public string Description { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,4)")]
        public decimal Quantity { get; set; }

        [StringLength(30)]
        public string? Unit { get; set; }

        public int SortOrder { get; set; } = 0;
    }
}
