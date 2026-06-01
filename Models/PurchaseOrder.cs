using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class PurchaseOrder : ITenantEntity
    {
        public int Id { get; set; }

        /// <summary>Tenant this PO belongs to. Nullable for backward compatibility.</summary>
        public int? TenantId { get; set; }

        public int? BranchId { get; set; }

        public int SupplierId { get; set; }

        [Required]
        [StringLength(50)]
        public string PONumber { get; set; } = string.Empty;

        public DateTime PODate { get; set; } = DateTime.Now;

        public DateTime? ExpectedDeliveryDate { get; set; }

        /// <summary>Draft | Sent | PartiallyReceived | Received | Cancelled</summary>
        [Required]
        [StringLength(30)]
        public string Status { get; set; } = "Draft";

        [StringLength(500)]
        public string? Notes { get; set; }

        [StringLength(150)]
        public string? CreatedBy { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAtUtc { get; set; }

        // ── Navigations ───────────────────────────────────────────────

        public Tenant? Tenant { get; set; }
        public Branch? Branch { get; set; }
        public Supplier? Supplier { get; set; }

        public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();

        // ── Computed helpers ──────────────────────────────────────────

        [NotMapped]
        public decimal TotalAmount => Items.Sum(i => i.TotalCost);

        [NotMapped]
        public bool IsEditable => Status == "Draft";

        [NotMapped]
        public bool CanBeSent => Status == "Draft";

        [NotMapped]
        public bool CanBeCancelled => Status is "Draft" or "Sent";

        [NotMapped]
        public bool CanBeReceived => Status is "Sent" or "PartiallyReceived";
    }
}
