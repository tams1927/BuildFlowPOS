using System.ComponentModel.DataAnnotations;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    /// <summary>
    /// Associates an application user with one or more branches.
    /// TenantAdmin / Admin are implicitly granted access to all branches.
    /// </summary>
    public class UserBranch : IBranchEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(450)]
        public string UserId { get; set; } = string.Empty;

        public int BranchId { get; set; }

        public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;

        public Branch? Branch { get; set; }
    }
}
