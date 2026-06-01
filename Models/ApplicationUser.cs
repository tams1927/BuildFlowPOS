using Microsoft.AspNetCore.Identity;

namespace HardwareManagementSystem.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Tenant this user belongs to.
        /// Null for Admin/SuperAdmin users who have cross-tenant access.
        /// </summary>
        public int? TenantId { get; set; }

        public Tenant? Tenant { get; set; }

        /// <summary>
        /// When true the user is redirected to ChangePassword immediately after login.
        /// Set by SuperAdmin after a forced password reset. Cleared on successful change.
        /// </summary>
        public bool ForcePasswordChange { get; set; } = false;
    }
}