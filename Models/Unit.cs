using System.ComponentModel.DataAnnotations;

namespace HardwareManagementSystem.Models
{
    public class Unit
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
    }
}