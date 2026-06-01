using System.ComponentModel.DataAnnotations;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class Branch : ITenantEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string Code { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Address { get; set; }

        [StringLength(30)]
        public string? ContactNumber { get; set; }

        [StringLength(150)]
        public string? ManagerName { get; set; }

        public bool IsMainBranch { get; set; } = false;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Tenant this branch belongs to. Nullable for backward compatibility.</summary>
        public int? TenantId { get; set; }

        public Tenant? Tenant { get; set; }
    }
}
