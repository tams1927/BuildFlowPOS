using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("UserBranches", "View")]
    public class UserBranchesController : OperationalDbController
    {
        // Identity users stay on the shared platform store (UserManager).
        // Branch assignments (UserBranches) + Branches are tenant operational data → routed _context.
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AuditService _auditService;
        private readonly BranchService _branchService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public UserBranchesController(
            ITenantOperationalContextProvider ctxProvider,
            UserManager<ApplicationUser> userManager,
            AuditService auditService,
            BranchService branchService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard)
            : base(ctxProvider)
        {
            _userManager = userManager;
            _auditService = auditService;
            _branchService = branchService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
        }

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            IQueryable<ApplicationUser> userQuery;

            if (isSuperAdmin)
            {
                // SuperAdmin sees all non-SuperAdmin users (no point assigning branches to SA)
                userQuery = _userManager.Users.AsNoTracking()
                    .Where(u => u.TenantId != null);  // Only show tenant-scoped users
            }
            else
            {
                // Tenant users see only users from their own tenant.
                // SuperAdmin users (TenantId == null) must never appear here.
                var me = await _userManager.GetUserAsync(User);
                var myTenantId = me?.TenantId;

                if (!myTenantId.HasValue)
                {
                    return View(new PagedResult<UserBranchListVm>
                    {
                        Items = new List<UserBranchListVm>(),
                        PageNumber = 1, PageSize = pageSize, TotalRecords = 0, SearchTerm = searchTerm
                    });
                }

                userQuery = _userManager.Users.AsNoTracking()
                    .Where(u => u.TenantId == myTenantId);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                userQuery = userQuery.Where(u =>
                    u.FullName.ToLower().Contains(term) ||
                    (u.UserName != null && u.UserName.ToLower().Contains(term)));
            }

            var totalRecords = await userQuery.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber, (int)Math.Ceiling(totalRecords / (double)pageSize));

            var users = await userQuery
                .OrderBy(u => u.FullName)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var userIds = users.Select(u => u.Id).ToList();

            var assignmentsQuery = _context.UserBranches
                .AsNoTracking()
                .Include(ub => ub.Branch)
                .Where(ub => userIds.Contains(ub.UserId));

            if (!isSuperAdmin)
            {
                var me = await _userManager.GetUserAsync(User);
                var myTenantId = me?.TenantId;

                if (myTenantId.HasValue)
                {
                    assignmentsQuery = assignmentsQuery.Where(ub =>
                        ub.Branch != null && ub.Branch.TenantId == myTenantId);
                }
            }

            var assignments = await assignmentsQuery.ToListAsync();

            var assignmentMap = assignments
                .GroupBy(ub => ub.UserId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var userVms = new List<UserBranchListVm>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);

                // Never show SuperAdmin in branch assignments
                if (roles.Contains("SuperAdmin"))
                    continue;

                userVms.Add(new UserBranchListVm
                {
                    UserId = user.Id,
                    FullName = user.FullName,
                    UserName = user.UserName ?? "",
                    Role = roles.FirstOrDefault() ?? "No Role",
                    IsGlobal = roles.Any(r => r == "TenantAdmin"),
                    AssignedBranches = assignmentMap.TryGetValue(user.Id, out var ubs)
                        ? ubs
                        : new List<UserBranch>()
                });
            }

            // Branches dropdown: only show tenant-scoped branches
            List<Branch> availableBranches;
            if (isSuperAdmin)
            {
                availableBranches = await _branchService.GetAllActiveBranchesAsync();
            }
            else
            {
                var me2 = await _userManager.GetUserAsync(User);
                var myTenantId2 = me2?.TenantId;
                availableBranches = myTenantId2.HasValue
                    ? await _context.Branches
                        .AsNoTracking()
                        .Where(b => b.IsActive && b.TenantId == myTenantId2)
                        .OrderBy(b => b.Name)
                        .ToListAsync()
                    : new List<Branch>();
            }

            ViewBag.AllBranches = availableBranches;

            return View(new PagedResult<UserBranchListVm>
            {
                Items = userVms,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("UserBranches", "Create")]
        public async Task<IActionResult> Assign(string userId, int branchId)
        {
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction(nameof(Index));
            }

            // Never allow assigning branches to SuperAdmin
            var targetRoles = await _userManager.GetRolesAsync(user);
            if (targetRoles.Contains("SuperAdmin"))
            {
                TempData["ErrorMessage"] = "SuperAdmin does not require branch assignments.";
                return RedirectToAction(nameof(Index));
            }

            if (!isSuperAdmin)
            {
                var me = await _userManager.GetUserAsync(User);
                var myTenantId = me?.TenantId;

                if (!myTenantId.HasValue || user.TenantId != myTenantId)
                    return Forbid();
            }

            var branch = await _context.Branches.FindAsync(branchId);
            if (branch == null || !branch.IsActive)
            {
                TempData["ErrorMessage"] = "Branch not found or is inactive.";
                return RedirectToAction(nameof(Index));
            }

            if (!isSuperAdmin)
            {
                var me = await _userManager.GetUserAsync(User);
                if (me?.TenantId.HasValue == true && branch.TenantId != me.TenantId)
                    return Forbid();
            }

            var exists = await _context.UserBranches
                .AnyAsync(ub => ub.UserId == userId && ub.BranchId == branchId);

            if (exists)
            {
                TempData["ErrorMessage"] = $"{user.FullName} is already assigned to {branch.Name}.";
                return RedirectToAction(nameof(Index));
            }

            _context.UserBranches.Add(new UserBranch
            {
                UserId = userId,
                BranchId = branchId,
                AssignedAtUtc = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User, "UserBranches", "Assign",
                $"User '{user.UserName}' assigned to branch '{branch.Name}' ({branch.Code})");

            TempData["SuccessMessage"] = $"{user.FullName} assigned to {branch.Name}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("UserBranches", "Delete")]
        public async Task<IActionResult> Remove(int id)
        {
            bool isSuperAdmin = User.IsInRole("SuperAdmin");

            var ub = await _context.UserBranches
                .Include(x => x.Branch)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (ub == null)
            {
                TempData["ErrorMessage"] = "Assignment not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!isSuperAdmin)
            {
                var me = await _userManager.GetUserAsync(User);
                var myTenantId = me?.TenantId;

                if (ub.Branch != null && myTenantId.HasValue && ub.Branch.TenantId != myTenantId)
                    return Forbid();

                var assignedUser = await _userManager.FindByIdAsync(ub.UserId);
                if (assignedUser != null)
                {
                    var assignedRoles = await _userManager.GetRolesAsync(assignedUser);
                    if (assignedRoles.Contains("SuperAdmin"))
                        return Forbid();

                    if (myTenantId.HasValue && assignedUser.TenantId != myTenantId)
                        return Forbid();
                }
            }

            var branchName = ub.Branch?.Name ?? "Unknown";
            var targetUser = await _userManager.FindByIdAsync(ub.UserId);

            _context.UserBranches.Remove(ub);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User, "UserBranches", "Remove",
                $"User '{targetUser?.UserName ?? ub.UserId}' removed from branch '{branchName}'");

            TempData["SuccessMessage"] = "Branch assignment removed successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}
