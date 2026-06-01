using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace HardwareManagementSystem.Services
{
    public class TenantContext : ITenantContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly UserManager<ApplicationUser> _userManager;
        private int? _resolvedTenantId;
        private bool _resolved = false;

        public TenantContext(
            IHttpContextAccessor httpContextAccessor,
            UserManager<ApplicationUser> userManager)
        {
            _httpContextAccessor = httpContextAccessor;
            _userManager = userManager;
        }
        public Task<int?> GetCurrentTenantIdAsync()
        {
            return Task.FromResult(CurrentTenantId);
        }

        public int? CurrentTenantId
        {
            get
            {
                if (!_resolved)
                    Resolve();
                return _resolvedTenantId;
            }
        }

        public bool IsGlobalUser
        {
            get
            {
                var user = _httpContextAccessor.HttpContext?.User;
                if (user == null) return true;
                // Only SuperAdmin is a platform-global user (no tenant scope).
                // TenantAdmin is scoped to their own tenant — IsGlobalUser is false for them.
                return user.IsInRole("SuperAdmin");
            }
        }

        private void Resolve()
        {
            _resolved = true;
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null) return;

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return;

            var appUser = _userManager.FindByIdAsync(userId).GetAwaiter().GetResult();
            _resolvedTenantId = appUser?.TenantId;
        }
    }
}
