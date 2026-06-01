using System.ComponentModel.DataAnnotations;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    /// <remarks>
    /// TRANSITION TABLE — Phase 5.0: classified as ITenantEntity pending design decision.
    /// TenantId = null rows are SuperAdmin/platform audit entries.
    /// See docs/Phase50A_TableClassification.md §C for details.
    /// </remarks>
    public class AuditTrail : ITenantEntity
    {
        public int Id { get; set; }

        [StringLength(100)]
        public string? UserId { get; set; }

        [StringLength(150)]
        public string? UserName { get; set; }

        [Required]
        [StringLength(100)]
        public string ModuleName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string ActionName { get; set; } = string.Empty;

        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        [StringLength(50)]
        public string? ReferenceType { get; set; }

        [StringLength(100)]
        public string? ReferenceId { get; set; }

        [StringLength(50)]
        public string? IpAddress { get; set; }

        public string? Browser { get; set; }

        public string? OperatingSystem { get; set; }

        public string? DeviceType { get; set; }

        public string? UserAgent { get; set; }

        /// <summary>Tenant context for this audit log entry. Nullable for backward compatibility.</summary>
        public int? TenantId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}