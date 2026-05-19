using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    [PermissionAuthorize("Users", "View")]
    public class RolePermissionsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;

        public RolePermissionsController(ApplicationDbContext context, AuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        public async Task<IActionResult> Index(string roleName = "Admin")
        {
            var roles = await _context.Roles
                .OrderBy(r => r.Name)
                .Select(r => r.Name!)
                .ToListAsync();

            ViewBag.Roles = roles;
            ViewBag.SelectedRole = roleName;

            var permissions = await _context.RolePermissions
                .Where(p => p.RoleName == roleName)
                .OrderBy(p => p.ModuleName)
                .ToListAsync();

            return View(permissions);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(List<RolePermission> permissions)
        {
            if (permissions == null || !permissions.Any())
            {
                TempData["ErrorMessage"] = "No permissions submitted.";
                return RedirectToAction(nameof(Index));
            }

            var roleName = permissions.First().RoleName;

            foreach (var submitted in permissions)
            {
                var existing = await _context.RolePermissions
                    .FirstOrDefaultAsync(p =>
                        p.RoleName == submitted.RoleName &&
                        p.ModuleName == submitted.ModuleName);

                if (existing != null)
                {
                    existing.CanView = submitted.CanView;
                    existing.CanCreate = submitted.CanCreate;
                    existing.CanEdit = submitted.CanEdit;
                    existing.CanDelete = submitted.CanDelete;
                    existing.CanPrint = submitted.CanPrint;
                    existing.CanExport = submitted.CanExport;
                }
            }

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
                User,
                "RolePermissions",
                "UPDATED",
                $"Role permissions updated for role: {roleName}",
                "RolePermission",
                roleName,
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Role permissions updated successfully.";

            return RedirectToAction(nameof(Index), new { roleName });
        }
    }
}