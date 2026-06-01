using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class DeliveryReceipt : ITenantEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(30)]
        public string DRNumber { get; set; } = string.Empty;

        public DateTime DeliveryDate { get; set; } = DateTime.Today;

        public int? CustomerId { get; set; }
        public Customer? Customer { get; set; }

        [StringLength(250)]
        public string? DeliveryAddress { get; set; }

        [StringLength(100)]
        public string? DriverName { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }

        /// <summary>Pending | Delivered | Cancelled</summary>
        [StringLength(20)]
        public string Status { get; set; } = "Pending";

        /// <summary>Optional link to the originating sale.</summary>
        public int? SalesHeaderId { get; set; }
        public SalesHeader? SalesHeader { get; set; }

        [StringLength(450)]
        public string? CreatedByUserId { get; set; }

        public int? TenantId { get; set; }
        public Tenant? Tenant { get; set; }

        public int? BranchId { get; set; }
        public Branch? Branch { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? DeliveredAtUtc { get; set; }

        public ICollection<DeliveryReceiptItem> Items { get; set; } = new List<DeliveryReceiptItem>();
    }
}
