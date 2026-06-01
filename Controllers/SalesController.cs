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
    [PermissionAuthorize("Sales", "View")]
    public class SalesController : OperationalDbController
    {
        // Shared platform context — used ONLY for platform-owned reads (e.g. Tenants),
        // which never live in a tenant's dedicated database.
        private readonly ApplicationDbContext _platformDb;
        private readonly AuditService _auditService;
        private readonly NotificationService _notificationService;
        private readonly BranchService _branchService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public SalesController(
            ITenantOperationalContextProvider ctxProvider,
            ApplicationDbContext platformDb,
            AuditService auditService,
            NotificationService notificationService,
            BranchService branchService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard)
            : base(ctxProvider)
        {
            _platformDb = platformDb;
            _auditService = auditService;
            _notificationService = notificationService;
            _branchService = branchService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
        }

        // ============================================
        // INDEX
        // ============================================

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            string? paymentFilter = null,
            string? statusFilter = null,
            int? branchId = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            // ============================================
            // TENANT SCOPE
            // ============================================

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            // ============================================
            // BRANCH RESOLUTION
            // ============================================

            var allBranches = await _branchService.GetAllActiveBranchesAsync();

            ViewBag.Branches = allBranches;

            Branch? selectedBranch;

            if (branchId.HasValue && _branchService.IsGlobalUser(User))
            {
                selectedBranch = allBranches
                    .FirstOrDefault(b => b.Id == branchId.Value);
            }
            else
            {
                selectedBranch = await _branchService.GetCurrentBranchAsync(User);
            }

            ViewBag.SelectedBranch = selectedBranch;
            ViewBag.SelectedBranchId = selectedBranch?.Id;

            // ============================================
            // QUERY
            // ============================================

            var query = _context.SalesHeaders
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.SalesDetails)
                    .ThenInclude(d => d.Item)
                        .ThenInclude(i => i!.Unit)
                .AsQueryable();

            // ── 1. Tenant filter (first) ──────────────────────────────
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(s => s.TenantId == tenantId || s.TenantId == null);
            }

            // ── 2. Branch filter ──────────────────────────────────────
            if (selectedBranch != null)
            {
                query = query.Where(s => s.BranchId == selectedBranch.Id);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(s =>
                    s.SalesNumber.ToLower().Contains(term) ||
                    (s.Customer != null &&
                     s.Customer.CustomerName.ToLower().Contains(term)) ||
                    (s.CashierName != null &&
                     s.CashierName.ToLower().Contains(term)) ||
                    (s.ReferenceNumber != null &&
                     s.ReferenceNumber.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(paymentFilter) &&
                paymentFilter != "all")
            {
                query = query.Where(s =>
                    s.PaymentMethod.ToLower() ==
                    paymentFilter.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(statusFilter) &&
                statusFilter != "all")
            {
                query = query.Where(s =>
                    s.Status.ToLower() ==
                    statusFilter.ToLower());
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var items = await query
                .OrderByDescending(s => s.SalesDate)
                .ThenByDescending(s => s.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // ============================================
            // SETTINGS
            // ============================================

            var settings = await _context.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();

            ViewBag.SystemSetting = settings;

            // ============================================
            // KPI STATS
            // ============================================

            var today = DateTime.Today;

            var todayQuery = _context.SalesHeaders
                .AsNoTracking()
                .Where(s => s.SalesDate.Date == today && s.Status != "Voided");

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                todayQuery = todayQuery
                    .Where(s => s.TenantId == tenantId || s.TenantId == null);
            }

            if (selectedBranch != null)
            {
                todayQuery = todayQuery
                    .Where(s => s.BranchId == selectedBranch.Id);
            }

            var todayStats = await todayQuery
                .Select(s => new
                {
                    s.TotalAmount,
                    s.PaymentMethod
                })
                .ToListAsync();

            ViewBag.TodayTotalSales =
                todayStats.Sum(s => s.TotalAmount);

            ViewBag.TodayTransactions =
                todayStats.Count;

            ViewBag.TodayCashPayments =
                todayStats
                    .Where(s => s.PaymentMethod == "Cash")
                    .Sum(s => s.TotalAmount);

            ViewBag.TodayDigitalPayments =
                todayStats
                    .Where(s => s.PaymentMethod != "Cash")
                    .Sum(s => s.TotalAmount);

            ViewBag.PaymentFilter = paymentFilter;
            ViewBag.StatusFilter = statusFilter;

            return View(new PagedResult<SalesHeader>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        // ============================================
        // VOID SALE
        // ============================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Sales", "Delete")]
        public async Task<IActionResult> VoidSale(
            int id,
            string voidReason)
        {
            if (string.IsNullOrWhiteSpace(voidReason))
            {
                TempData["ErrorMessage"] =
                    "Void reason is required.";

                return RedirectToAction(nameof(Index));
            }

            var sale = await _context.SalesHeaders
                .Include(s => s.SalesDetails)
                    .ThenInclude(d => d.Item)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null)
            {
                TempData["ErrorMessage"] =
                    "Sale not found.";

                return RedirectToAction(nameof(Index));
            }

            // ============================================
            // TENANT ACCESS PROTECTION (C-1)
            // ============================================

            if (!await _tenantGuard.CanAccessAsync(sale.TenantId))
            {
                TempData["ErrorMessage"] = "Access denied.";
                return RedirectToAction(nameof(Index));
            }

            // ============================================
            // BRANCH ACCESS PROTECTION
            // ============================================

            var currentBranch =
                await _branchService.GetCurrentBranchAsync(User);

            if (!_branchService.IsGlobalUser(User))
            {
                if (sale.BranchId != currentBranch?.Id)
                {
                    TempData["ErrorMessage"] =
                        "Access denied.";

                    return RedirectToAction(nameof(Index));
                }
            }

            if (sale.Status == "Voided")
            {
                TempData["ErrorMessage"] =
                    "This sale is already voided.";

                return RedirectToAction(nameof(Index));
            }

            using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                foreach (var detail in sale.SalesDetails)
                {
                    if (detail.Item == null)
                        continue;

                    // ============================================
                    // BRANCH STOCK RESTORE
                    // ============================================

                    if (sale.BranchId.HasValue)
                    {
                        await _branchService.AddStockAsync(
                            sale.BranchId.Value,
                            detail.ItemId,
                            detail.Quantity);
                    }
                    else
                    {
                        // LEGACY fallback
                        detail.Item.CurrentStock += detail.Quantity;
                    }
                }

                sale.Status = "Voided";

                // ============================================
                // REVERSE CUSTOMER LEDGER FOR CREDIT SALES (C-5)
                // ============================================

                if (sale.PaymentMethod == "Credit" && sale.CustomerId.HasValue)
                {
                    var lastLedger = await _context.CustomerLedgers
                        .Where(l => l.CustomerId == sale.CustomerId.Value)
                        .OrderByDescending(l => l.Id)
                        .FirstOrDefaultAsync();

                    var prevBalance = lastLedger?.RunningBalance ?? 0;

                    _context.CustomerLedgers.Add(new CustomerLedger
                    {
                        CustomerId      = sale.CustomerId.Value,
                        TransactionType = "VOID",
                        ReferenceNumber = sale.SalesNumber,
                        DebitAmount     = 0,
                        CreditAmount    = sale.TotalAmount,
                        RunningBalance  = prevBalance - sale.TotalAmount,
                        BalanceBefore   = prevBalance,
                        TransactionDate = DateTime.Now,
                        CreatedBy       = User.Identity?.Name,
                        CreatedAt       = DateTime.Now,
                        Remarks         = $"Voided sale reversal: {sale.SalesNumber}"
                    });
                }

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    User,
                    "Sales",
                    "SALE VOIDED",
                    $"Sale voided. Receipt: {sale.SalesNumber}, Total: {sale.TotalAmount:N2}, Reason: {voidReason}",
                    "SalesHeader",
                    sale.Id.ToString(),
                    HttpContext.Connection.RemoteIpAddress?.ToString()
                );

                await _notificationService.CreateAsync(
                    "Sale Voided",
                    $"Receipt {sale.SalesNumber} was voided. Total: ₱{sale.TotalAmount:N2}.",
                    "Warning",
                    "bi bi-x-circle",
                    null,
                    "/Sales"
                );

                await transaction.CommitAsync();

                TempData["SuccessMessage"] =
                    $"Sale {sale.SalesNumber} voided successfully.";
            }
            catch
            {
                await transaction.RollbackAsync();

                TempData["ErrorMessage"] =
                    "Unable to void sale.";
            }

            return RedirectToAction(nameof(Index));
        }

        // ============================================
        // RECEIPT
        // ============================================

        public async Task<IActionResult> Receipt(int id)
        {
            var sale = await _context.SalesHeaders
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.SalesDetails)
                    .ThenInclude(d => d.Item)
                        .ThenInclude(i => i!.Unit)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null)
            {
                TempData["ErrorMessage"] =
                    "Receipt not found.";

                return RedirectToAction(nameof(Index));
            }

            // ============================================
            // BRANCH ACCESS PROTECTION
            // ============================================

            var currentBranch =
                await _branchService.GetCurrentBranchAsync(User);

            if (!_branchService.IsGlobalUser(User))
            {
                if (sale.BranchId != currentBranch?.Id)
                {
                    TempData["ErrorMessage"] =
                        "Access denied.";

                    return RedirectToAction(nameof(Index));
                }
            }

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            // ============================================
            // TENANT ACCESS PROTECTION (C-1)
            // ============================================

            if (!await _tenantGuard.CanAccessAsync(sale.TenantId))
            {
                TempData["ErrorMessage"] = "Access denied.";
                return RedirectToAction(nameof(Index));
            }

            var settings = await _context.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();

            ViewBag.SystemSetting = settings;

            // Load branch for receipt header
            Branch? branch = null;
            if (sale.BranchId.HasValue)
                branch = await _context.Branches.FindAsync(sale.BranchId.Value);
            ViewBag.ReceiptBranch = branch;

            if (tenantId.HasValue)
                ViewBag.ReceiptTenant = await _platformDb.Tenants.FindAsync(tenantId.Value);

            return View(sale);
        }
    }
}