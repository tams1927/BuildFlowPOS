using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Users", "View")]
    public class RolesController : Controller
    {
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;

        public RolesController(
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext context,
            AuditService auditService)
        {
            _roleManager = roleManager;
            _context = context;
            _auditService = auditService;
        }

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            var query = _roleManager.Roles.AsNoTracking().AsQueryable();

            // Non-SuperAdmin cannot see or modify the SuperAdmin role
            if (!isSuperAdmin)
                query = query.Where(r => r.Name != "SuperAdmin");

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(r => r.Name != null && r.Name.ToLower().Contains(term));
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var roles = await query
                .OrderBy(r => r.Name)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.IsSuperAdmin = isSuperAdmin;

            return View(new PagedResult<IdentityRole>
            {
                Items = roles,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string roleName)
        {
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            if (string.IsNullOrWhiteSpace(roleName))
            {
                TempData["ErrorMessage"] = "Role name is required.";
                return RedirectToAction(nameof(Index));
            }

            roleName = roleName.Trim();

            // Prevent non-SuperAdmin from creating the SuperAdmin role
            if (!isSuperAdmin && roleName.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "You are not authorized to create the SuperAdmin role.";
                return RedirectToAction(nameof(Index));
            }

            if (await _roleManager.RoleExistsAsync(roleName))
            {
                TempData["ErrorMessage"] = "Role already exists.";
                return RedirectToAction(nameof(Index));
            }

            var result = await _roleManager.CreateAsync(new IdentityRole(roleName));

            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = result.Errors.FirstOrDefault()?.Description ?? "Unable to create role.";
                return RedirectToAction(nameof(Index));
            }

            await CreateDefaultPermissionsForRoleAsync(roleName);

            await _auditService.LogAsync(
                User, "Users", "ROLE CREATED",
                $"Role created: {roleName}",
                "IdentityRole", roleName,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = "Role created successfully.";
            return RedirectToAction(nameof(Index));
        }

        private async Task CreateDefaultPermissionsForRoleAsync(string roleName)
        {
            var modules = typeof(Program).Assembly
                .GetTypes()
                .Where(t =>
                    typeof(Controller).IsAssignableFrom(t) &&
                    !t.IsAbstract &&
                    t.Name.EndsWith("Controller"))
                .Select(t => t.Name.Replace("Controller", ""))
                .Where(name => name != "Account" && name != "Notifications")
                .OrderBy(name => name)
                .ToList();

            var existing = await _context.RolePermissions
                .Where(p => p.RoleName == roleName)
                .Select(p => p.ModuleName)
                .ToListAsync();

            var toAdd = modules
                .Where(module => !existing.Contains(module))
                .Select(module => new RolePermission
                {
                    RoleName = roleName,
                    ModuleName = module,
                    CanView = false,
                    CanCreate = false,
                    CanEdit = false,
                    CanDelete = false,
                    CanPrint = false,
                    CanExport = false
                })
                .ToList();

            if (toAdd.Any())
            {
                await _context.RolePermissions.AddRangeAsync(toAdd);
                await _context.SaveChangesAsync();
            }
        }
    }
}
