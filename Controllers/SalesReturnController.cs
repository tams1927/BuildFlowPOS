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
    [PermissionAuthorize("SalesReturn", "View")]
    public class SalesReturnController : OperationalDbController
    {
        private readonly AuditService _auditService;
        private readonly NotificationService _notificationService;
        private readonly BranchService _branchService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public SalesReturnController(
            ITenantOperationalContextProvider ctxProvider,
            AuditService auditService,
            NotificationService notificationService,
            BranchService branchService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard)
            : base(ctxProvider)
        {
            _auditService = auditService;
            _notificationService = notificationService;
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

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var query = _context.SalesReturnHeaders
                .AsNoTracking()
                .Include(r => r.SalesHeader)
                .Include(r => r.SalesReturnDetails)
                    .ThenInclude(d => d.Item)
                        .ThenInclude(i => i!.Unit)
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(r =>
                    r.SalesHeader != null &&
                    (r.SalesHeader.TenantId == tenantId ||
                     r.SalesHeader.TenantId == null));
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(r =>
                    r.ReturnNumber.ToLower().Contains(term) ||
                    (r.SalesHeader != null &&
                     r.SalesHeader.SalesNumber.ToLower().Contains(term)) ||
                    (r.Reason != null &&
                     r.Reason.ToLower().Contains(term)) ||
                    (r.CreatedBy != null &&
                     r.CreatedBy.ToLower().Contains(term)));
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var returns = await query
                .OrderByDescending(r => r.ReturnDate)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View(new PagedResult<SalesReturnHeader>
            {
                Items = returns,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        public async Task<IActionResult> Create(int? saleId)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var salesQuery = _context.SalesHeaders
                .AsNoTracking()
                .Where(s => s.Status == "Completed");

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                salesQuery = salesQuery.Where(s =>
                    s.TenantId == tenantId ||
                    s.TenantId == null);
            }

            ViewBag.Sales = await salesQuery
                .OrderByDescending(s => s.SalesDate)
                .Take(200)
                .ToListAsync();

            SalesHeader? selectedSale = null;

            if (saleId.HasValue)
            {
                var selectedSaleQuery = _context.SalesHeaders
                    .Include(s => s.Customer)
                    .Include(s => s.SalesDetails)
                        .ThenInclude(d => d.Item)
                            .ThenInclude(i => i!.Unit)
                    .Where(s =>
                        s.Id == saleId.Value &&
                        s.Status == "Completed");

                if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                {
                    selectedSaleQuery = selectedSaleQuery.Where(s =>
                        s.TenantId == tenantId ||
                        s.TenantId == null);
                }

                selectedSale = await selectedSaleQuery.FirstOrDefaultAsync();

                if (selectedSale != null &&
                    !await _tenantGuard.CanAccessTenantAsync(selectedSale.TenantId))
                {
                    return Forbid();
                }

                if (selectedSale != null)
                {
                    var detailIds = selectedSale.SalesDetails
                        .Select(d => d.Id)
                        .ToList();

                    var returnedQtys = await _context.SalesReturnDetails
                        .AsNoTracking()
                        .Where(r => detailIds.Contains(r.SalesDetailId))
                        .GroupBy(r => r.SalesDetailId)
                        .Select(g => new
                        {
                            SalesDetailId = g.Key,
                            Returned = g.Sum(x => x.QuantityReturned)
                        })
                        .ToListAsync();

                    ViewBag.ReturnedQtys = returnedQtys
                        .ToDictionary(x => x.SalesDetailId, x => x.Returned);
                }
            }

            ViewBag.SelectedSale = selectedSale;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("SalesReturn", "Create")]
        public async Task<IActionResult> Create(
            int salesHeaderId,
            int salesDetailId,
            decimal quantityReturned,
            string reason,
            bool restoreToInventory = true)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (salesHeaderId <= 0)
            {
                TempData["ErrorMessage"] = "Invalid sale selected.";
                return RedirectToAction(nameof(Create));
            }

            if (salesDetailId <= 0)
            {
                TempData["ErrorMessage"] = "Invalid item selected.";
                return RedirectToAction(nameof(Create), new { saleId = salesHeaderId });
            }

            if (quantityReturned <= 0)
            {
                TempData["ErrorMessage"] = "Return quantity must be greater than zero.";
                return RedirectToAction(nameof(Create), new { saleId = salesHeaderId });
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["ErrorMessage"] = "Return reason is required.";
                return RedirectToAction(nameof(Create), new { saleId = salesHeaderId });
            }

            var saleQuery = _context.SalesHeaders
                .Include(s => s.SalesDetails)
                    .ThenInclude(d => d.Item)
                .Where(s => s.Id == salesHeaderId);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                saleQuery = saleQuery.Where(s =>
                    s.TenantId == tenantId ||
                    s.TenantId == null);
            }

            var sale = await saleQuery.FirstOrDefaultAsync();

            if (sale == null)
            {
                TempData["ErrorMessage"] = "Original sale not found.";
                return RedirectToAction(nameof(Create));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(sale.TenantId))
            {
                return Forbid();
            }

            if (!sale.TenantId.HasValue && tenantId.HasValue)
            {
                sale.TenantId = tenantId;
            }

            if (sale.Status == "Voided")
            {
                TempData["ErrorMessage"] = "Cannot process a return for a voided sale.";
                return RedirectToAction(nameof(Create));
            }

            var detail = sale.SalesDetails
                .FirstOrDefault(d => d.Id == salesDetailId);

            if (detail == null || detail.Item == null)
            {
                TempData["ErrorMessage"] = "Selected item not found in this sale.";
                return RedirectToAction(nameof(Create), new { saleId = salesHeaderId });
            }

            if (!await _tenantGuard.CanAccessTenantAsync(detail.Item.TenantId))
            {
                return Forbid();
            }

            var alreadyReturnedQty = await _context.SalesReturnDetails
                .Where(r => r.SalesDetailId == salesDetailId)
                .SumAsync(r => r.QuantityReturned);

            var remainingQty = detail.Quantity - alreadyReturnedQty;

            if (remainingQty <= 0)
            {
                TempData["ErrorMessage"] = "This item has already been fully returned.";
                return RedirectToAction(nameof(Create), new { saleId = salesHeaderId });
            }

            if (quantityReturned > remainingQty)
            {
                TempData["ErrorMessage"] =
                    $"Return quantity ({quantityReturned:0.###}) exceeds remaining returnable quantity ({remainingQty:0.###}).";

                return RedirectToAction(nameof(Create), new { saleId = salesHeaderId });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var returnNumber = await GenerateReturnNumberAsync(tenantId);
                var refundAmount = quantityReturned * detail.UnitPrice;

                var header = new SalesReturnHeader
                {
                    ReturnNumber  = returnNumber,
                    ReturnDate    = DateTime.Now,
                    SalesHeaderId = sale.Id,
                    Reason        = reason.Trim(),
                    RefundAmount  = refundAmount,
                    TenantId      = tenantId,
                    CreatedBy     = User.Identity?.Name ?? "Unknown",
                    CreatedAt     = DateTime.Now
                };

                header.SalesReturnDetails.Add(new SalesReturnDetail
                {
                    SalesDetailId = detail.Id,
                    ItemId = detail.ItemId,
                    QuantityReturned = quantityReturned,
                    UnitPrice = detail.UnitPrice,
                    LineRefundAmount = refundAmount,
                    RestoreToInventory = restoreToInventory
                });

                if (restoreToInventory)
                {
                    if (sale.BranchId.HasValue)
                    {
                        await _branchService.AddStockAsync(
                            sale.BranchId.Value,
                            detail.ItemId,
                            quantityReturned);
                    }
                    else
                    {
                        detail.Item.CurrentStock += quantityReturned;
                    }

                    if (!detail.Item.TenantId.HasValue && tenantId.HasValue)
                    {
                        detail.Item.TenantId = tenantId;
                    }
                }

                _context.SalesReturnHeaders.Add(header);

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                await _auditService.LogAsync(
                    User,
                    "SalesReturn",
                    "RETURN CREATED",
                    $"Return #{returnNumber} — Receipt: {sale.SalesNumber}, Item: {detail.Item.ItemName}, Qty: {quantityReturned:0.###}, Refund: {refundAmount:N2}, RestoreStock: {restoreToInventory}, Reason: {reason}",
                    "SalesReturnHeader",
                    header.Id.ToString(),
                    HttpContext.Connection.RemoteIpAddress?.ToString()
                );

                await _notificationService.CreateAsync(
                    "Sales Return Processed",
                    $"Return #{returnNumber} for receipt {sale.SalesNumber}. Refund: ₱{refundAmount:N2}.",
                    "Info",
                    "bi bi-arrow-return-left",
                    null,
                    "/SalesReturn"
                );

                if (restoreToInventory)
                {
                    var updatedItem = await _context.Items.FindAsync(detail.ItemId);

                    if (updatedItem != null)
                    {
                        var stockToCheck = updatedItem.CurrentStock;

                        if (sale.BranchId.HasValue)
                        {
                            stockToCheck = await _branchService.GetBranchStockAsync(
                                sale.BranchId.Value,
                                detail.ItemId);
                        }

                        if (stockToCheck <= 0)
                        {
                            await _notificationService
                                .CreateOutOfStockNotificationAsync(updatedItem.ItemName);
                        }
                        else if (stockToCheck <= updatedItem.ReorderLevel)
                        {
                            await _notificationService
                                .CreateLowStockNotificationAsync(updatedItem.ItemName);
                        }
                    }
                }

                TempData["SuccessMessage"] =
                    $"Sales return {returnNumber} processed. Refund: ₱{refundAmount:N2}";

                return RedirectToAction(nameof(Index));
            }
            catch
            {
                await transaction.RollbackAsync();

                TempData["ErrorMessage"] =
                    "Unable to save sales return. Please try again.";

                return RedirectToAction(nameof(Create), new { saleId = salesHeaderId });
            }
        }

        private async Task<string> GenerateReturnNumberAsync(int? tenantId)
        {
            var prefix = $"RET-{DateTime.Now:yyyyMMdd}-";

            var query = _context.SalesReturnHeaders.Where(r => r.ReturnNumber.StartsWith(prefix));
            if (tenantId.HasValue)
                query = query.Where(r => r.TenantId == tenantId);

            var countToday = await query.CountAsync();
            return $"{prefix}{(countToday + 1):D4}";
        }
    }
}