using System.ComponentModel.DataAnnotations;

namespace HardwareManagementSystem.Models
{
    public class Notification
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

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
