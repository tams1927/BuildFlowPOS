using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class Customer : ITenantEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(150)]
        public string CustomerName { get; set; } = string.Empty;

        [StringLength(30)]
        public string? ContactNumber { get; set; }

        [StringLength(150)]
        public string? Email { get; set; }

        [StringLength(250)]
        public string? Address { get; set; }

        [StringLength(50)]
        public string CustomerType { get; set; } = "Walk-in";

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Maximum credit amount this customer is allowed to carry.
        /// Zero (0) means no credit limit enforced.
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal CreditLimit { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>Tenant this customer belongs to. Nullable for backward compatibility.</summary>
        public int? TenantId { get; set; }

        public Tenant? Tenant { get; set; }
    }
}