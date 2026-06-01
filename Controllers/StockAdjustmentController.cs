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
    [PermissionAuthorize("StockAdjustment", "View")]
    public class StockAdjustmentController : OperationalDbController
    {
        // Shared platform context — used ONLY for platform-owned reads (e.g. Tenants).
        private readonly ApplicationDbContext _platformDb;
        private readonly AuditService _auditService;
        private readonly NotificationService _notificationService;
        private readonly BranchService _branchService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public StockAdjustmentController(
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

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            string? typeFilter = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var query = _context.StockAdjustmentHeaders
                .AsNoTracking()
                .Include(a => a.Branch)
                .Include(a => a.StockAdjustmentDetails)
                    .ThenInclude(d => d.Item)
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(a => a.TenantId == tenantId || a.TenantId == null);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(a =>
                    a.AdjustmentNumber.ToLower().Contains(term) ||
                    (a.Reason != null && a.Reason.ToLower().Contains(term)) ||
                    (a.CreatedBy != null && a.CreatedBy.ToLower().Contains(term)) ||
                    a.StockAdjustmentDetails.Any(d =>
                        d.Item != null &&
                        d.Item.ItemName.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(typeFilter) && typeFilter != "all")
            {
                query = query.Where(a =>
                    a.AdjustmentType.ToLower() == typeFilter.ToLower());
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var adjustments = await query
                .OrderByDescending(a => a.AdjustmentDate)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.TypeFilter = typeFilter;

            return View(new PagedResult<StockAdjustmentHeader>
            {
                Items = adjustments,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        public async Task<IActionResult> Create()
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var itemsQuery = _context.Items
                .AsNoTracking()
                .Where(i => i.Status == "Active");

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                itemsQuery = itemsQuery.Where(i =>
                    i.TenantId == tenantId ||
                    i.TenantId == null);
            }

            ViewBag.Items = await itemsQuery
                .OrderBy(i => i.ItemName)
                .ToListAsync();

            var currentBranch = await _branchService.GetCurrentBranchAsync(User);

            if (currentBranch != null &&
                !await _tenantGuard.CanAccessTenantAsync(currentBranch.TenantId))
            {
                return Forbid();
            }

            ViewBag.CurrentBranch = currentBranch;

            if (currentBranch != null)
            {
                var stockQuery = _context.BranchProductStocks
                    .AsNoTracking()
                    .Where(s => s.BranchId == currentBranch.Id);

                if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                {
                    stockQuery = stockQuery.Where(s =>
                        s.TenantId == tenantId ||
                        s.TenantId == null);
                }

                ViewBag.BranchStockMap = await stockQuery
                    .ToDictionaryAsync(s => s.ProductId, s => s.Quantity);
            }

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("StockAdjustment", "Create")]
        public async Task<IActionResult> Create(
            string adjustmentType,
            string? reason,
            int itemId,
            decimal quantity)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (itemId <= 0 || quantity <= 0)
            {
                TempData["ErrorMessage"] =
                    "Item and quantity are required.";

                return RedirectToAction(nameof(Create));
            }

            var itemQuery = _context.Items
                .Where(i => i.Id == itemId);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                itemQuery = itemQuery.Where(i =>
                    i.TenantId == tenantId ||
                    i.TenantId == null);
            }

            var item = await itemQuery.FirstOrDefaultAsync();

            if (item == null)
            {
                TempData["ErrorMessage"] =
                    "Selected item not found.";

                return RedirectToAction(nameof(Create));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(item.TenantId))
            {
                return Forbid();
            }

            if (adjustmentType != "Increase" &&
                adjustmentType != "Decrease")
            {
                TempData["ErrorMessage"] =
                    "Invalid adjustment type.";

                return RedirectToAction(nameof(Create));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["ErrorMessage"] =
                    "Reason is required for stock adjustments.";

                return RedirectToAction(nameof(Create));
            }

            var currentBranch = await _branchService.GetCurrentBranchAsync(User);

            if (currentBranch != null &&
                !await _tenantGuard.CanAccessTenantAsync(currentBranch.TenantId))
            {
                return Forbid();
            }

            decimal stockBefore;

            if (currentBranch != null)
            {
                stockBefore = await _branchService
                    .GetBranchStockAsync(currentBranch.Id, itemId);
            }
            else
            {
                stockBefore = item.CurrentStock;
            }

            decimal stockAfter;

            if (adjustmentType == "Increase")
            {
                stockAfter = stockBefore + quantity;
            }
            else
            {
                if (quantity > stockBefore)
                {
                    TempData["ErrorMessage"] =
                        $"Insufficient stock for decrease adjustment. Available: {stockBefore:0.###}";

                    return RedirectToAction(nameof(Create));
                }

                stockAfter = stockBefore - quantity;
            }

            using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                var adjustmentNumber =
                    await GenerateAdjustmentNumberAsync(tenantId);

                var userName =
                    User.Identity?.Name ?? "Unknown";

                var header = new StockAdjustmentHeader
                {
                    TenantId = tenantId,
                    AdjustmentNumber = adjustmentNumber,
                    AdjustmentDate = DateTime.Now,
                    AdjustmentType = adjustmentType,
                    Reason = reason,
                    CreatedBy = userName,
                    BranchId = currentBranch?.Id,
                    CreatedAt = DateTime.Now
                };

                header.StockAdjustmentDetails.Add(
                    new StockAdjustmentDetail
                    {
                        ItemId = item.Id,
                        Quantity = quantity,
                        StockBefore = stockBefore,
                        StockAfter = stockAfter
                    });

                if (currentBranch != null)
                {
                    if (adjustmentType == "Increase")
                    {
                        await _branchService.AddStockAsync(
                            currentBranch.Id,
                            itemId,
                            quantity);
                    }
                    else
                    {
                        await _branchService.DeductStockAsync(
                            currentBranch.Id,
                            itemId,
                            quantity);
                    }
                }
                else
                {
                    item.CurrentStock = stockAfter;
                }

                if (!item.TenantId.HasValue && tenantId.HasValue)
                {
                    item.TenantId = tenantId;
                }

                _context.StockAdjustmentHeaders.Add(header);

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    User,
                    "StockAdjustment",
                    "CREATED",
                    $"Stock adjustment {adjustmentNumber}. Branch: {currentBranch?.Name ?? "N/A"}, Type: {adjustmentType}, Item: {item.ItemName}, Qty: {quantity:0.###}, Before: {stockBefore:0.###}, After: {stockAfter:0.###}, Reason: {reason}",
                    "StockAdjustmentHeader",
                    header.Id.ToString(),
                    HttpContext.Connection.RemoteIpAddress?.ToString()
                );

                await transaction.CommitAsync();

                await _notificationService
                    .CreateStockAdjustmentNotificationAsync(
                        adjustmentNumber,
                        item.ItemName,
                        adjustmentType,
                        quantity);

                if (stockAfter <= 0)
                {
                    await _notificationService
                        .CreateOutOfStockNotificationAsync(item.ItemName);
                }
                else if (stockAfter <= item.ReorderLevel)
                {
                    await _notificationService
                        .CreateLowStockNotificationAsync(item.ItemName);
                }

                TempData["SuccessMessage"] =
                    "Stock adjustment saved successfully.";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                TempData["ErrorMessage"] =
                    $"Unable to save stock adjustment: {ex.Message}";

                return RedirectToAction(nameof(Create));
            }
        }

        [HttpGet]
        public async Task<IActionResult> PrintSlip(int id)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var adj = await _context.StockAdjustmentHeaders
                .AsNoTracking()
                .Include(a => a.Branch)
                .Include(a => a.StockAdjustmentDetails)
                    .ThenInclude(d => d.Item)
                        .ThenInclude(i => i!.Unit)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (adj == null) return NotFound();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue &&
                adj.TenantId.HasValue && adj.TenantId != tenantId)
                return Forbid();

            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();
            var tenant   = tenantId.HasValue
                ? await _platformDb.Tenants.FindAsync(tenantId.Value) : null;

            ViewBag.PrintSettings = settings;
            ViewBag.PrintTenant   = tenant;
            ViewBag.PrintBranch   = adj.Branch;

            return View(adj);
        }

        private async Task<string> GenerateAdjustmentNumberAsync(int? tenantId)
        {
            var prefix = $"ADJ-{DateTime.Now:yyyyMMdd}-";

            var query = _context.StockAdjustmentHeaders.Where(h => h.AdjustmentNumber.StartsWith(prefix));
            if (tenantId.HasValue)
                query = query.Where(h => h.TenantId == tenantId);

            var count = await query.CountAsync();
            return $"{prefix}{(count + 1):D5}";
        }
    }
}