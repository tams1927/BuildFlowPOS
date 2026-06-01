using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class SubscriptionPlan : IPlatformEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Description { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal MonthlyPrice { get; set; }

        public int MaxBranches { get; set; } = 3;

        public int MaxUsers { get; set; } = 10;

        public int MaxProducts { get; set; } = 500;

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; } = 0;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
