using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize(Roles = "SuperAdmin,TenantAdmin")]
    [PermissionAuthorize("Users", "View")]
    public class UsersController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly AuditService _auditService;
        private readonly NotificationService _notificationService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;
        private readonly TenantLimitGuard _limitGuard;

        public UsersController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            AuditService auditService,
            NotificationService notificationService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard,
            TenantLimitGuard limitGuard)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _auditService = auditService;
            _notificationService = notificationService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
            _limitGuard = limitGuard;
        }

        // ─────────────────────────────────────────────────────────────
        // INDEX
        // ─────────────────────────────────────────────────────────────

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            string? statusFilter = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            IQueryable<ApplicationUser> userQuery;

            if (isSuperAdmin)
            {
                // SuperAdmin sees ALL users across all tenants
                userQuery = _userManager.Users.AsNoTracking();
            }
            else
            {
                // Tenant users see ONLY their own tenant's users.
                // Strictly exclude platform users (TenantId == null).
                var me = await _userManager.GetUserAsync(User);
                var myTenantId = me?.TenantId;

                if (!myTenantId.HasValue)
                {
                    // Misconfigured tenant user — show nothing
                    return View(new PagedResult<UserListVm>
                    {
                        Items = new List<UserListVm>(),
                        PageNumber = 1, PageSize = pageSize,
                        TotalRecords = 0, SearchTerm = searchTerm
                    });
                }

                // Strict equality — no null fallback, so platform users are invisible
                userQuery = _userManager.Users.AsNoTracking()
                    .Where(u => u.TenantId == myTenantId);
            }

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

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber, (int)Math.Ceiling(totalRecords / (double)pageSize));

            var pagedUsers = await userQuery
                .OrderBy(u => u.FullName)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var userViewModels = new List<UserListVm>();

            foreach (var user in pagedUsers)
            {
                var roles = await _userManager.GetRolesAsync(user);

                // For non-SuperAdmin viewers: never surface the SuperAdmin account
                if (!isSuperAdmin && roles.Contains("SuperAdmin"))
                    continue;

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

            // Role dropdown: SuperAdmin is hidden from non-SuperAdmin managers
            var allRoles = await _roleManager.Roles.OrderBy(r => r.Name).ToListAsync();
            if (!isSuperAdmin)
                allRoles = allRoles.Where(r => r.Name != "SuperAdmin").ToList();

            ViewBag.Roles = allRoles;
            ViewBag.StatusFilter = statusFilter;
            ViewBag.IsSuperAdmin = isSuperAdmin;

            return View(new PagedResult<UserListVm>
            {
                Items = userViewModels,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        // ─────────────────────────────────────────────────────────────
        // CREATE
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Users", "Create")]
        public async Task<IActionResult> Create(
            string fullName,
            string userName,
            string email,
            string password,
            string role)
        {
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            // Prevent non-SuperAdmin from creating a SuperAdmin user
            if (!isSuperAdmin && role == "SuperAdmin")
            {
                TempData["ErrorMessage"] = "You are not authorized to assign the SuperAdmin role.";
                return RedirectToAction(nameof(Index));
            }

            // Determine TenantId for the new user
            int? newUserTenantId;
            if (isSuperAdmin)
            {
                // SuperAdmin can create users — they stay without TenantId (platform) 
                // unless they use the Tenants module to create tenant users.
                // For the Users page, SuperAdmin creating via this form gets no TenantId.
                newUserTenantId = null;
            }
            else
            {
                // Tenant user must belong to current user's tenant
                var me = await _userManager.GetUserAsync(User);
                newUserTenantId = me?.TenantId;

                if (!newUserTenantId.HasValue)
                {
                    TempData["ErrorMessage"] = "Cannot create user: your account is not associated with a tenant.";
                    return RedirectToAction(nameof(Index));
                }
            }

            // Enforce user limit for tenant
            if (newUserTenantId.HasValue)
            {
                var limitCheck = await _limitGuard.CanAddUserAsync(newUserTenantId.Value);
                if (!limitCheck.Allowed)
                {
                    TempData["ErrorMessage"] = limitCheck.Message;
                    return RedirectToAction(nameof(Index));
                }
            }

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
                TenantId = newUserTenantId,
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
                    result.Errors.FirstOrDefault()?.Description ?? "Unable to create user.";
                return RedirectToAction(nameof(Index));
            }

            await _userManager.AddToRoleAsync(user, role);

            await _auditService.LogAsync(
                User, "Users", "CREATED",
                $"User created. Username: {user.UserName}, Role: {role}",
                "ApplicationUser", user.Id,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = "User created successfully.";
            await _notificationService.CreateNewUserNotificationAsync(user.UserName!, role);

            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        // EDIT
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Users", "Edit")]
        public async Task<IActionResult> Edit(
            string id,
            string fullName,
            string email,
            string role,
            bool isActive)
        {
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction(nameof(Index));
            }

            // Non-SuperAdmin guards
            if (!isSuperAdmin)
            {
                var me = await _userManager.GetUserAsync(User);
                var myTenantId = me?.TenantId;

                // Must belong to same tenant
                if (!myTenantId.HasValue || user.TenantId != myTenantId)
                {
                    return Forbid();
                }

                // Cannot edit a SuperAdmin user
                var targetRoles = await _userManager.GetRolesAsync(user);
                if (targetRoles.Contains("SuperAdmin"))
                {
                    TempData["ErrorMessage"] = "You cannot edit a SuperAdmin user.";
                    return RedirectToAction(nameof(Index));
                }

                // Cannot assign SuperAdmin role
                if (role == "SuperAdmin")
                {
                    TempData["ErrorMessage"] = "You are not authorized to assign the SuperAdmin role.";
                    return RedirectToAction(nameof(Index));
                }
            }

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(role))
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
                    updateResult.Errors.FirstOrDefault()?.Description ?? "Unable to update user.";
                return RedirectToAction(nameof(Index));
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, role);

            await _auditService.LogAsync(
                User, "Users", "UPDATED",
                $"User updated. Username: {user.UserName}, Role: {role}, Active: {user.IsActive}",
                "ApplicationUser", user.Id,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = "User updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        // DEACTIVATE
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Users", "Delete")]
        public async Task<IActionResult> Deactivate(string id)
        {
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!isSuperAdmin)
            {
                var me = await _userManager.GetUserAsync(User);
                var myTenantId = me?.TenantId;

                if (!myTenantId.HasValue || user.TenantId != myTenantId)
                    return Forbid();

                var targetRoles = await _userManager.GetRolesAsync(user);
                if (targetRoles.Contains("SuperAdmin"))
                {
                    TempData["ErrorMessage"] = "You cannot deactivate a SuperAdmin user.";
                    return RedirectToAction(nameof(Index));
                }

                // Guard: prevent deactivating the last active TenantAdmin in this tenant
                if (targetRoles.Contains("TenantAdmin"))
                {
                    var activeTenantAdmins = await _userManager.GetUsersInRoleAsync("TenantAdmin");
                    var activeCount = activeTenantAdmins.Count(u => u.TenantId == myTenantId && u.IsActive && u.Id != user.Id);
                    if (activeCount == 0)
                    {
                        TempData["ErrorMessage"] = "Cannot deactivate the last active TenantAdmin. Promote another user first.";
                        return RedirectToAction(nameof(Index));
                    }
                }
            }

            user.IsActive = false;
            await _userManager.UpdateAsync(user);

            await _auditService.LogAsync(
                User, "Users", "USER_DEACTIVATED",
                $"User deactivated. Username: {user.UserName}",
                "ApplicationUser", user.Id,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = "User deactivated successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        // ACTIVATE
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Users", "Edit")]
        public async Task<IActionResult> Activate(string id)
        {
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!isSuperAdmin)
            {
                var me = await _userManager.GetUserAsync(User);
                var myTenantId = me?.TenantId;

                if (!myTenantId.HasValue || user.TenantId != myTenantId)
                    return Forbid();

                var targetRoles = await _userManager.GetRolesAsync(user);
                if (targetRoles.Contains("SuperAdmin"))
                {
                    TempData["ErrorMessage"] = "You cannot activate a SuperAdmin user.";
                    return RedirectToAction(nameof(Index));
                }
            }

            user.IsActive = true;
            await _userManager.UpdateAsync(user);

            await _auditService.LogAsync(
                User, "Users", "USER_ACTIVATED",
                $"User activated. Username: {user.UserName}",
                "ApplicationUser", user.Id,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"User \"{user.UserName}\" has been activated.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        // RESET PASSWORD
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Users", "Edit")]
        public async Task<IActionResult> ResetPassword(string id, string newPassword)
        {
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            {
                TempData["ErrorMessage"] = "New password must be at least 8 characters.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!isSuperAdmin)
            {
                var me = await _userManager.GetUserAsync(User);
                var myTenantId = me?.TenantId;

                if (!myTenantId.HasValue || user.TenantId != myTenantId)
                    return Forbid();

                var targetRoles = await _userManager.GetRolesAsync(user);
                if (targetRoles.Contains("SuperAdmin"))
                {
                    TempData["ErrorMessage"] = "You cannot reset the password of a SuperAdmin user.";
                    return RedirectToAction(nameof(Index));
                }
            }

            var removeResult = await _userManager.RemovePasswordAsync(user);
            if (!removeResult.Succeeded)
            {
                TempData["ErrorMessage"] = removeResult.Errors.FirstOrDefault()?.Description ?? "Failed to reset password.";
                return RedirectToAction(nameof(Index));
            }

            var addResult = await _userManager.AddPasswordAsync(user, newPassword);
            if (!addResult.Succeeded)
            {
                TempData["ErrorMessage"] = addResult.Errors.FirstOrDefault()?.Description ?? "Failed to set new password.";
                return RedirectToAction(nameof(Index));
            }

            // Require the user to change their password on next login
            user.ForcePasswordChange = true;
            await _userManager.UpdateAsync(user);

            await _auditService.LogAsync(
                User, "Users", "USER_PASSWORD_RESET",
                $"Password reset for user: {user.UserName}. ForcePasswordChange set.",
                "ApplicationUser", user.Id,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Password for \"{user.UserName}\" has been reset. They will be prompted to change it on next login.";
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
