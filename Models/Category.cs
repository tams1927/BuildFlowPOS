using System.ComponentModel.DataAnnotations;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class Category : ITenantEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string CategoryName { get; set; } = string.Empty;

        [StringLength(250)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>Tenant this category belongs to. Nullable for backward compatibility.</summary>
        public int? TenantId { get; set; }

        public Tenant? Tenant { get; set; }
    }
}