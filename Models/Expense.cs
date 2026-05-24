using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HardwareManagementSystem.Models
{
    public class Expense
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

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}