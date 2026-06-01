using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class Expense : ITenantEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ExpenseNumber { get; set; } = string.Empty;

        public DateTime ExpenseDate { get; set; } = DateTime.Now;

        [Required]
        [StringLength(100)]
        public string Category { get; set; } = string.Empty;
        // Rent, Utilities, Payroll, Delivery, Supplies, Maintenance, Misc

        [Required]
        [StringLength(200)]
        public string Description { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [StringLength(50)]
        public string PaymentMethod { get; set; } = "Cash";

        [StringLength(100)]
        public string? ReferenceNumber { get; set; }

        [StringLength(250)]
        public string? Remarks { get; set; }

        [StringLength(100)]
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>
        /// Branch this expense belongs to.
        /// Nullable for backward compatibility with existing records created before multi-branch.
        /// </summary>
        public int? BranchId { get; set; }

        public Branch? Branch { get; set; }

        /// <summary>Tenant this expense belongs to. Nullable for backward compatibility.</summary>
        public int? TenantId { get; set; }

        public Tenant? Tenant { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}