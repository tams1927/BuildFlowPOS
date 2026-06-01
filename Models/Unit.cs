using System.ComponentModel.DataAnnotations;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class Unit : ITenantEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string UnitName { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string ShortName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? UnitType { get; set; }

        [StringLength(250)]
        public string? Description { get; set; }

        public bool AllowsDecimal { get; set; } = true;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>Tenant this unit belongs to. Nullable for backward compatibility.</summary>
        public int? TenantId { get; set; }

        public Tenant? Tenant { get; set; }
    }
}