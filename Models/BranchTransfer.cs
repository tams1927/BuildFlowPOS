using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class BranchTransfer : ITenantEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string TransferNumber { get; set; } = string.Empty;

        public int FromBranchId { get; set; }

        public int ToBranchId { get; set; }

        /// <summary>Draft ? Pending ? Approved ? Completed | Cancelled</summary>
        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "Draft";

        [StringLength(500)]
        public string? Notes { get; set; }

        [StringLength(450)]
        public string? CreatedByUserId { get; set; }

        [StringLength(150)]
        public string? CreatedByUserName { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? ApprovedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }

        /// <summary>Tenant this transfer belongs to. Nullable for backward compatibility.</summary>
        public int? TenantId { get; set; }

        public Tenant? Tenant { get; set; }

        public Branch? FromBranch { get; set; }

        public Branch? ToBranch { get; set; }

        public ICollection<BranchTransferItem> BranchTransferItems { get; set; } = new List<BranchTransferItem>();
    }
}
