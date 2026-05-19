using System.ComponentModel.DataAnnotations;

namespace HardwareManagementSystem.Models
{
    public class RolePermission
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string RoleName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string ModuleName { get; set; } = string.Empty;

        public bool CanView { get; set; } = true;

        public bool CanCreate { get; set; } = false;

        public bool CanEdit { get; set; } = false;

        public bool CanDelete { get; set; } = false;

        public bool CanPrint { get; set; } = false;

        public bool CanExport { get; set; } = false;
    }
}