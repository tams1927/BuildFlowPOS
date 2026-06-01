using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HardwareManagementSystem.Services
{
    public class PermissionService
    {
        private readonly ApplicationDbContext _context;
        private List<RolePermission>? _cachedPermissions;
        private List<string>? _cachedRoleNames;

        public PermissionService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string moduleName, string permissionType = "View")
        {
            if (user == null || !user.Identity?.IsAuthenticated == true)
                return false;

            if (_cachedRoleNames == null)
            {
                _cachedRoleNames = user.Claims
                    .Where(c => c.Type == ClaimTypes.Role)
                    .Select(c => c.Value)
                    .ToList();
            }

            if (!_cachedRoleNames.Any())
                return false;

            // SuperAdmin bypasses all permission checks — full access to every module
            if (_cachedRoleNames.Contains("SuperAdmin"))
                return true;

            if (_cachedPermissions == null)
            {
                _cachedPermissions = await _context.RolePermissions
                    .AsNoTracking()
                    .Where(p => _cachedRoleNames.Contains(p.RoleName))
                    .ToListAsync();
            }

            var permissions = _cachedPermissions
                .Where(p => p.ModuleName == moduleName)
                .ToList();

            return permissionType switch
            {
                "View" => permissions.Any(p => p.CanView),
                "Create" => permissions.Any(p => p.CanCreate),
                "Edit" => permissions.Any(p => p.CanEdit),
                "Delete" => permissions.Any(p => p.CanDelete),
                "Print" => permissions.Any(p => p.CanPrint),
                "Export" => permissions.Any(p => p.CanExport),
                _ => false
            };
        }
    }
}