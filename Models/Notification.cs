using System.ComponentModel.DataAnnotations;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    /// <remarks>
    /// TRANSITION TABLE — Phase 5.0: classified as ITenantEntity pending design decision.
    /// TenantId = null rows are broadcast notifications (platform behaviour).
    /// See docs/Phase50A_TableClassification.md §C for details.
    /// </remarks>
    public class Notification : ITenantEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string Message { get; set; } = string.Empty;

        [StringLength(50)]
        public string Type { get; set; } = "Info"; // Info, Warning, Danger, Success

        [StringLength(50)]
        public string? Icon { get; set; }

        [StringLength(100)]
        public string? TargetRole { get; set; } // null = all roles

        [StringLength(200)]
        public string? LinkUrl { get; set; }

        public bool IsRead { get; set; } = false;

        /// <summary>Tenant this notification belongs to. Null means broadcast to all tenants.</summary>
        public int? TenantId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
