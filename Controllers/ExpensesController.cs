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
    [PermissionAuthorize("Expenses", "View")]
    public class ExpensesController : OperationalDbController
    {
        private readonly AuditService _auditService;
        private readonly BranchService _branchService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public ExpensesController(
            ITenantOperationalContextProvider ctxProvider,
            AuditService auditService,
            BranchService branchService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard)
            : base(ctxProvider)
        {
            _auditService = auditService;
            _branchService = branchService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
        }

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            string? categoryFilter = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var query = _context.Expenses
                .AsNoTracking()
                .Include(e => e.Branch)
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(e => e.TenantId == tenantId || e.TenantId == null);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(e =>
                    e.ExpenseNumber.ToLower().Contains(term) ||
                    e.Description.ToLower().Contains(term) ||
                    (e.ReferenceNumber != null && e.ReferenceNumber.ToLower().Contains(term)) ||
                    (e.CreatedBy != null && e.CreatedBy.ToLower().Contains(term)) ||
                    (e.Branch != null && e.Branch.Name.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(categoryFilter))
            {
                query = query.Where(e => e.Category == categoryFilter);
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var expenses = await query
                .OrderByDescending(e => e.ExpenseDate)
                .ThenByDescending(e => e.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CategoryFilter = categoryFilter;

            var currentBranch = await _branchService.GetCurrentBranchAsync(User);

            if (currentBranch != null && !await _tenantGuard.CanAccessTenantAsync(currentBranch.TenantId))
            {
                return Forbid();
            }

            ViewBag.CurrentBranch = currentBranch;

            return View(new PagedResult<Expense>
            {
                Items = expenses,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Expenses", "Create")]
        public async Task<IActionResult> Create(
            DateTime expenseDate,
            string category,
            string description,
            decimal amount,
            string paymentMethod,
            string? referenceNumber,
            string? remarks)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (string.IsNullOrWhiteSpace(category))
            {
                TempData["ErrorMessage"] = "Expense category is required.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                TempData["ErrorMessage"] = "Expense description is required.";
                return RedirectToAction(nameof(Index));
            }

            if (amount <= 0)
            {
                TempData["ErrorMessage"] = "Expense amount must be greater than zero.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(paymentMethod))
            {
                TempData["ErrorMessage"] = "Payment method is required.";
                return RedirectToAction(nameof(Index));
            }

            if ((paymentMethod == "GCash" ||
                 paymentMethod == "Bank Transfer" ||
                 paymentMethod == "Check") &&
                string.IsNullOrWhiteSpace(referenceNumber))
            {
                TempData["ErrorMessage"] = "Reference number is required for this payment method.";
                return RedirectToAction(nameof(Index));
            }

            var expenseNumber = await GenerateExpenseNumberAsync(tenantId);

            var currentBranch = await _branchService.GetCurrentBranchAsync(User);

            if (currentBranch != null && !await _tenantGuard.CanAccessTenantAsync(currentBranch.TenantId))
            {
                return Forbid();
            }

            var expense = new Expense
            {
                TenantId = tenantId,
                ExpenseNumber = expenseNumber,
                ExpenseDate = expenseDate == default ? DateTime.Now : expenseDate,
                Category = category,
                Description = description,
                Amount = amount,
                PaymentMethod = paymentMethod,
                ReferenceNumber = referenceNumber,
                Remarks = remarks,
                CreatedBy = User.Identity?.Name ?? "Unknown",
                BranchId = currentBranch?.Id,
                CreatedAt = DateTime.Now
            };

            _context.Expenses.Add(expense);

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Expenses",
                "CREATED",
                $"Expense created. Expense #: {expenseNumber}, Category: {category}, Amount: {amount:N2}",
                "Expense",
                expense.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Expense saved successfully.";

            return RedirectToAction(nameof(Index));
        }

        private async Task<string> GenerateExpenseNumberAsync(int? tenantId)
        {
            var prefix = $"EXP-{DateTime.Now:yyyyMMdd}-";

            var query = _context.Expenses.Where(e => e.ExpenseNumber.StartsWith(prefix));
            if (tenantId.HasValue)
                query = query.Where(e => e.TenantId == tenantId);

            var countToday = await query.CountAsync();
            return $"{prefix}{(countToday + 1):0000}";
        }
    }
}