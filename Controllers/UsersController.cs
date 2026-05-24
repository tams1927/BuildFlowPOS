using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    [PermissionAuthorize("Users", "View")]
    public class UsersController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly AuditService _auditService;
        private readonly NotificationService _notificationService;

        public UsersController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            AuditService auditService,
            NotificationService notificationService)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _auditService = auditService;
            _notificationService = notificationService;
        }

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            string? statusFilter = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var userQuery = _userManager.Users.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                userQuery = userQuery.Where(u =>
                    u.FullName.ToLower().Contains(term) ||
                    (u.UserName != null && u.UserName.ToLower().Contains(term)) ||
                    (u.Email != null && u.Email.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                bool isActive = statusFilter == "active";
                userQuery = userQuery.Where(u => u.IsActive == isActive);
            }

            var totalRecords = await userQuery.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var pagedUsers = await userQuery
                .OrderBy(u => u.FullName)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var userViewModels = new List<UserListVm>();

            foreach (var user in pagedUsers)
            {
                var roles = await _userManager.GetRolesAsync(user);

                userViewModels.Add(new UserListVm
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    UserName = user.UserName ?? "",
                    Email = user.Email ?? "",
                    IsActive = user.IsActive,
                    Role = roles.FirstOrDefault() ?? "No Role"
                });
            }

            ViewBag.Roles = await _roleManager.Roles
                .OrderBy(r => r.Name)
                .ToListAsync();

            ViewBag.StatusFilter = statusFilter;

            return View(new PagedResult<UserListVm>
            {
                Items = userViewModels,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
    string fullName,
    string userName,
    string email,
    string password,
    string role)
        {
            if (string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(userName) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(role))
            {
                TempData["ErrorMessage"] = "Full name, username, password, and role are required.";
                return RedirectToAction(nameof(Index));
            }

            var existingUser = await _userManager.FindByNameAsync(userName.Trim());

            if (existingUser != null)
            {
                TempData["ErrorMessage"] = "Username already exists.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _roleManager.RoleExistsAsync(role))
            {
                TempData["ErrorMessage"] = "Selected role does not exist.";
                return RedirectToAction(nameof(Index));
            }

            var user = new ApplicationUser
            {
                FullName = fullName.Trim(),
                UserName = userName.Trim(),
                Email = email,
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await _userManager.CreateAsync(user, password);

            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] =
                    result.Errors.FirstOrDefault()?.Description
                    ?? "Unable to create user.";

                return RedirectToAction(nameof(Index));
            }

            await _userManager.AddToRoleAsync(user, role);

            await _auditService.LogAsync(
                User,
                "Users",
                "CREATED",
                $"User created. Username: {user.UserName}, Full Name: {user.FullName}, Role: {role}",
                "ApplicationUser",
                user.Id,
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "User created successfully.";

            await _notificationService.CreateNewUserNotificationAsync(user.UserName!, role);

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            string id,
            string fullName,
            string email,
            string role,
            bool isActive)
        {
            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(role))
            {
                TempData["ErrorMessage"] = "Full name and role are required.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _roleManager.RoleExistsAsync(role))
            {
                TempData["ErrorMessage"] = "Selected role does not exist.";
                return RedirectToAction(nameof(Index));
            }

            user.FullName = fullName.Trim();
            user.Email = email;
            user.IsActive = isActive;

            var updateResult = await _userManager.UpdateAsync(user);

            if (!updateResult.Succeeded)
            {
                TempData["ErrorMessage"] =
                    updateResult.Errors.FirstOrDefault()?.Description
                    ?? "Unable to update user.";

                return RedirectToAction(nameof(Index));
            }

            var currentRoles = await _userManager.GetRolesAsync(user);

            await _userManager.RemoveFromRolesAsync(user, currentRoles);

            await _userManager.AddToRoleAsync(user, role);

            await _auditService.LogAsync(
                User,
                "Users",
                "UPDATED",
                $"User updated. Username: {user.UserName}, Full Name: {user.FullName}, Role: {role}, Active: {user.IsActive}",
                "ApplicationUser",
                user.Id,
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "User updated successfully.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(string id)
        {
            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction(nameof(Index));
            }

            user.IsActive = false;

            await _userManager.UpdateAsync(user);

            await _auditService.LogAsync(
                User,
                "Users",
                "DEACTIVATED",
                $"User deactivated. Username: {user.UserName}, Full Name: {user.FullName}",
                "ApplicationUser",
                user.Id,
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "User deactivated successfully.";

            return RedirectToAction(nameof(Index));
        }
    }

    public class UserListVm
    {
        public string Id { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }
}