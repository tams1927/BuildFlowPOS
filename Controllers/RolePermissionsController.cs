using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize(Roles = "SuperAdmin,TenantAdmin")]
    [PermissionAuthorize("Users", "View")]
    public class RolePermissionsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;

        public RolePermissionsController(
            ApplicationDbContext context,
            AuditService auditService)
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

            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            var selectedRole = string.IsNullOrWhiteSpace(role) ? "TenantAdmin" : role;

            // Non-SuperAdmin cannot view or edit SuperAdmin role permissions
            if (!isSuperAdmin && selectedRole == "SuperAdmin")
            {
                TempData["ErrorMessage"] = "You are not authorized to view SuperAdmin permissions.";
                return RedirectToAction(nameof(Index), new { role = "TenantAdmin" });
            }

            ViewBag.SelectedRole = selectedRole;
            ViewBag.IsSuperAdmin = isSuperAdmin;

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

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber, (int)Math.Ceiling(totalRecords / (double)pageSize));

            var permissions = await query
                .OrderBy(p => p.ModuleName)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Roles dropdown: hide SuperAdmin from non-SuperAdmin users
            var allRoles = await _context.Roles
                .AsNoTracking()
                .OrderBy(r => r.Name)
                .Select(r => r.Name!)
                .ToListAsync();

            if (!isSuperAdmin)
                allRoles = allRoles.Where(r => r != "SuperAdmin").ToList();

            ViewBag.Roles = allRoles;

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
        public async Task<IActionResult> Update(
            List<RolePermission> permissions,
            string? selectedRole,
            string? searchTerm,
            int pageNumber = 1,
            int pageSize = 10)
        {
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            if (permissions == null || !permissions.Any())
            {
                TempData["ErrorMessage"] = "No permissions submitted.";
                return RedirectToAction(nameof(Index), new { role = selectedRole, searchTerm, pageNumber, pageSize });
            }

            var roleName = permissions.First().RoleName;

            // Prevent non-SuperAdmin from modifying SuperAdmin role
            if (!isSuperAdmin && roleName == "SuperAdmin")
            {
                TempData["ErrorMessage"] = "You are not authorized to modify SuperAdmin permissions.";
                return RedirectToAction(nameof(Index), new { role = "TenantAdmin" });
            }

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Invalid permission submission.";
                return RedirectToAction(nameof(Index), new { role = roleName, searchTerm, pageNumber, pageSize });
            }

            foreach (var submitted in permissions)
            {
                // Double-check: if this row is for SuperAdmin and caller is not SuperAdmin, skip
                if (!isSuperAdmin && submitted.RoleName == "SuperAdmin")
                    continue;

                var existing = await _context.RolePermissions
                    .FirstOrDefaultAsync(p =>
                        p.RoleName == submitted.RoleName &&
                        p.ModuleName == submitted.ModuleName);

                if (existing == null)
                    continue;

                // Prevent TenantAdmin self-lockout from permissions management
                if (submitted.RoleName == "TenantAdmin" && submitted.ModuleName == "RolePermissions")
                {
                    submitted.CanView = true;
                    submitted.CanEdit = true;
                }

                existing.CanView = submitted.CanView;
                existing.CanCreate = submitted.CanCreate;
                existing.CanEdit = submitted.CanEdit;
                existing.CanDelete = submitted.CanDelete;
                existing.CanPrint = submitted.CanPrint;
                existing.CanExport = submitted.CanExport;
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User, "RolePermissions", "UPDATED",
                $"Role permissions updated for role: {roleName}",
                "RolePermission", roleName,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = "Role permissions updated successfully.";

            return RedirectToAction(nameof(Index), new { role = roleName, searchTerm, pageNumber, pageSize });
        }
    }
}
