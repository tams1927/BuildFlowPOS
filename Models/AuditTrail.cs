using System.ComponentModel.DataAnnotations;

namespace HardwareManagementSystem.Models
{
    public class AuditTrail
    {
        public int Id { get; set; }

        [StringLength(100)]
        public string? UserId { get; set; }

        [StringLength(150)]
        public string? UserName { get; set; }

        [Required]
        [StringLength(100)]
        public string ModuleName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string ActionName { get; set; } = string.Empty;

        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        [StringLength(50)]
        public string? ReferenceType { get; set; }

        [StringLength(100)]
        public string? ReferenceId { get; set; }

        [StringLength(50)]
        public string? IpAddress { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}