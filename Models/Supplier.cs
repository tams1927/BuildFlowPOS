using System.ComponentModel.DataAnnotations;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class Supplier : ITenantEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(150)]
        public string SupplierName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? ContactPerson { get; set; }

        [StringLength(30)]
        public string? ContactNumber { get; set; }

        [StringLength(150)]
        public string? Email { get; set; }

        [StringLength(250)]
        public string? Address { get; set; }

        [StringLength(250)]
        public string? Remarks { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>Tenant this supplier belongs to. Nullable for backward compatibility.</summary>
        public int? TenantId { get; set; }

        public Tenant? Tenant { get; set; }
    }
}