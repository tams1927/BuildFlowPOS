using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.ViewModels;
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

        public async Task<IActionResult> Index(
            string? role,
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var selectedRole = string.IsNullOrWhiteSpace(role) ? "Admin" : role;

            ViewBag.SelectedRole = selectedRole;

            var query = _context.RolePermissions
                .AsNoTracking()
                .Where(p => p.RoleName == selectedRole)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(p => p.ModuleName.ToLower().Contains(term));
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var permissions = await query
                .OrderBy(p => p.ModuleName)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // ============================================
            // LOAD ROLES FOR DROPDOWN
            // ============================================

            ViewBag.Roles = await _context.Roles
                .AsNoTracking()
                .OrderBy(r => r.Name)
                .Select(r => r.Name!)
                .ToListAsync();

            return View(new PagedResult<RolePermission>
            {
                Items = permissions,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
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

            return RedirectToAction(nameof(Index), new { role = roleName });
        }
    }
}