using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Branches", "View")]
    public class BranchesController : OperationalDbController
    {
        private readonly AuditService _auditService;
        private readonly BranchService _branchService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;
        private readonly TenantLimitGuard _limitGuard;

        public BranchesController(
            ITenantOperationalContextProvider ctxProvider,
            AuditService auditService,
            BranchService branchService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard,
            TenantLimitGuard limitGuard)
            : base(ctxProvider)
        {
            _auditService = auditService;
            _branchService = branchService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
            _limitGuard = limitGuard;
        }

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var query = _context.Branches
                .AsNoTracking()
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                // Strict equality — never surface branches from another tenant or platform-level null records
                query = query.Where(b => b.TenantId == tenantId);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(b =>
                    b.Name.ToLower().Contains(term) ||
                    b.Code.ToLower().Contains(term) ||
                    (b.Address != null && b.Address.ToLower().Contains(term)));
            }

            var total = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(total / (double)pageSize));

            var items = await query
                .OrderBy(b => b.IsMainBranch ? 0 : 1)
                .ThenBy(b => b.Name)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View(new PagedResult<Branch>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = total,
                SearchTerm = searchTerm
            });
        }

        [HttpPost]
        [PermissionAuthorize("Branches", "Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string name,
            string code,
            string? address,
            string? contactNumber,
            string? managerName,
            bool isMainBranch)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            // Enforce branch limit for tenant
            if (tenantId.HasValue)
            {
                var limitCheck = await _limitGuard.CanAddBranchAsync(tenantId.Value);
                if (!limitCheck.Allowed)
                {
                    TempData["ErrorMessage"] = limitCheck.Message;
                    return RedirectToAction(nameof(Index));
                }
            }

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
            {
                TempData["ErrorMessage"] = "Branch name and code are required.";
                return RedirectToAction(nameof(Index));
            }

            var codeUpper = code.Trim().ToUpper();

            var duplicateQuery = _context.Branches.AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                duplicateQuery = duplicateQuery.Where(b => b.TenantId == tenantId);
            }

            if (await duplicateQuery.AnyAsync(b => b.Code == codeUpper))
            {
                TempData["ErrorMessage"] = $"Branch code '{codeUpper}' already exists.";
                return RedirectToAction(nameof(Index));
            }

            if (isMainBranch)
            {
                var existingMainQuery = _context.Branches.Where(b => b.IsMainBranch);

                if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                {
                    existingMainQuery = existingMainQuery.Where(b => b.TenantId == tenantId);
                }

                var existingMain = await existingMainQuery.ToListAsync();
                existingMain.ForEach(b => b.IsMainBranch = false);
            }

            var branch = new Branch
            {
                TenantId = tenantId,
                Name = name.Trim(),
                Code = codeUpper,
                Address = address?.Trim(),
                ContactNumber = contactNumber?.Trim(),
                ManagerName = managerName?.Trim(),
                IsMainBranch = isMainBranch,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _context.Branches.Add(branch);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Branches",
                "Create",
                $"Branch created: {branch.Name} ({branch.Code})");

            TempData["SuccessMessage"] = $"Branch '{branch.Name}' created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [PermissionAuthorize("Branches", "Edit")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            string name,
            string code,
            string? address,
            string? contactNumber,
            string? managerName,
            bool isMainBranch,
            bool isActive)
        {
            var branch = await _context.Branches.FindAsync(id);

            if (branch == null)
            {
                return NotFound();
            }

            if (!await _tenantGuard.CanAccessTenantAsync(branch.TenantId))
            {
                return Forbid();
            }

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
            {
                TempData["ErrorMessage"] = "Branch name and code are required.";
                return RedirectToAction(nameof(Index));
            }

            var codeUpper = code.Trim().ToUpper();

            var duplicateQuery = _context.Branches
                .Where(b => b.Id != id && b.Code == codeUpper);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                duplicateQuery = duplicateQuery.Where(b => b.TenantId == tenantId);
            }

            if (await duplicateQuery.AnyAsync())
            {
                TempData["ErrorMessage"] = $"Branch code '{codeUpper}' is already used by another branch.";
                return RedirectToAction(nameof(Index));
            }

            if (isMainBranch && !branch.IsMainBranch)
            {
                var existingMainQuery = _context.Branches
                    .Where(b => b.IsMainBranch && b.Id != id);

                if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                {
                    existingMainQuery = existingMainQuery.Where(b => b.TenantId == tenantId);
                }

                var existingMain = await existingMainQuery.ToListAsync();
                existingMain.ForEach(b => b.IsMainBranch = false);
            }

            branch.Name = name.Trim();
            branch.Code = codeUpper;
            branch.Address = address?.Trim();
            branch.ContactNumber = contactNumber?.Trim();
            branch.ManagerName = managerName?.Trim();
            branch.IsMainBranch = isMainBranch;
            branch.IsActive = isActive;
            branch.UpdatedAtUtc = DateTime.UtcNow;

            if (!branch.TenantId.HasValue && tenantId.HasValue)
            {
                branch.TenantId = tenantId;
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Branches",
                "Edit",
                $"Branch updated: {branch.Name} ({branch.Code})");

            TempData["SuccessMessage"] = $"Branch '{branch.Name}' updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [PermissionAuthorize("Branches", "Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var branch = await _context.Branches.FindAsync(id);

            if (branch == null)
            {
                return NotFound();
            }

            if (!await _tenantGuard.CanAccessTenantAsync(branch.TenantId))
            {
                return Forbid();
            }

            if (branch.IsMainBranch)
            {
                TempData["ErrorMessage"] = "The main branch cannot be deleted.";
                return RedirectToAction(nameof(Index));
            }

            var hasTransactions =
                await _context.StockInHeaders.AnyAsync(s => s.BranchId == id) ||
                await _context.SalesHeaders.AnyAsync(s => s.BranchId == id) ||
                await _context.Expenses.AnyAsync(e => e.BranchId == id);

            if (hasTransactions)
            {
                branch.IsActive = false;
                branch.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    User,
                    "Branches",
                    "Deactivate",
                    $"Branch deactivated (has transactions): {branch.Name} ({branch.Code})");

                TempData["SuccessMessage"] = $"Branch '{branch.Name}' was deactivated because it has existing transactions.";
                return RedirectToAction(nameof(Index));
            }

            _context.Branches.Remove(branch);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Branches",
                "Delete",
                $"Branch deleted: {branch.Name} ({branch.Code})");

            TempData["SuccessMessage"] = $"Branch '{branch.Name}' deleted successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        // ACTIVATE BRANCH
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Branches", "Edit")]
        public async Task<IActionResult> ActivateBranch(int id)
        {
            var branch = await _context.Branches.FindAsync(id);

            if (branch == null) return NotFound();

            if (!await _tenantGuard.CanAccessTenantAsync(branch.TenantId))
                return Forbid();

            branch.IsActive = true;
            branch.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User, "Branches", "BRANCH_ACTIVATED",
                $"Branch activated: {branch.Name} ({branch.Code})",
                "Branch", branch.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Branch '{branch.Name}' has been activated.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        // DEACTIVATE BRANCH
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Branches", "Edit")]
        public async Task<IActionResult> DeactivateBranch(int id)
        {
            var branch = await _context.Branches.FindAsync(id);

            if (branch == null) return NotFound();

            if (!await _tenantGuard.CanAccessTenantAsync(branch.TenantId))
                return Forbid();

            if (branch.IsMainBranch)
            {
                TempData["ErrorMessage"] = "Cannot deactivate the main branch. Set another branch as main first.";
                return RedirectToAction(nameof(Index));
            }

            var activeCount = await _context.Branches
                .CountAsync(b => b.TenantId == branch.TenantId && b.IsActive && b.Id != id);

            if (activeCount < 1)
            {
                TempData["ErrorMessage"] = "Cannot deactivate the only active branch. Activate another branch first.";
                return RedirectToAction(nameof(Index));
            }

            branch.IsActive = false;
            branch.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User, "Branches", "BRANCH_DEACTIVATED",
                $"Branch deactivated: {branch.Name} ({branch.Code})",
                "Branch", branch.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Branch '{branch.Name}' has been deactivated.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        // SET MAIN BRANCH
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Branches", "Edit")]
        public async Task<IActionResult> SetMainBranch(int id)
        {
            var branch = await _context.Branches.FindAsync(id);

            if (branch == null) return NotFound();

            if (!await _tenantGuard.CanAccessTenantAsync(branch.TenantId))
                return Forbid();

            if (!branch.IsActive)
            {
                TempData["ErrorMessage"] = "Cannot set an inactive branch as the main branch. Activate it first.";
                return RedirectToAction(nameof(Index));
            }

            // Clear all other main-branch flags for this tenant
            var others = await _context.Branches
                .Where(b => b.TenantId == branch.TenantId && b.IsMainBranch && b.Id != id)
                .ToListAsync();

            foreach (var other in others)
            {
                other.IsMainBranch = false;
                other.UpdatedAtUtc = DateTime.UtcNow;
            }

            branch.IsMainBranch = true;
            branch.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User, "Branches", "MAIN_BRANCH_CHANGED",
                $"Main branch changed to: {branch.Name} ({branch.Code})",
                "Branch", branch.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"'{branch.Name}' is now set as the main branch.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SwitchBranch(int branchId, string? returnUrl)
        {
            var branch = await _context.Branches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == branchId);

            if (branch == null)
            {
                TempData["ErrorMessage"] = "Branch not found.";
                return RedirectToAction("Index", "Home");
            }

            if (!await _tenantGuard.CanAccessTenantAsync(branch.TenantId))
            {
                TempData["ErrorMessage"] = "You do not have access to that branch.";
                return RedirectToAction("Index", "Home");
            }

            var switched = await _branchService.SetCurrentBranchAsync(User, branchId);

            if (!switched)
            {
                TempData["ErrorMessage"] = "You do not have access to that branch.";
            }

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var branch = await _context.Branches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id);

            if (branch == null)
            {
                return NotFound();
            }

            if (!await _tenantGuard.CanAccessTenantAsync(branch.TenantId))
            {
                return Forbid();
            }

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var stocksQuery = _context.BranchProductStocks
                .AsNoTracking()
                .Include(s => s.Product)
                    .ThenInclude(p => p!.Unit)
                .Include(s => s.Product)
                    .ThenInclude(p => p!.Category)
                .Where(s => s.BranchId == id);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                stocksQuery = stocksQuery.Where(s => s.TenantId == tenantId);
            }

            var stocks = await stocksQuery
                .OrderBy(s => s.Product!.ItemName)
                .ToListAsync();

            ViewBag.Branch = branch;
            ViewBag.Stocks = stocks;

            return View();
        }
    }
}
