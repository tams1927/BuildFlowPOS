using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("SalesReturn", "View")]
    public class SalesReturnController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;
        private readonly NotificationService _notificationService;

        public SalesReturnController(
            ApplicationDbContext context,
            AuditService auditService,
            NotificationService notificationService)
        {
            _context = context;
            _auditService = auditService;
            _notificationService = notificationService;
        }

        public async Task<IActionResult> Index()
        {
            var returns = await _context.SalesReturnHeaders
                .AsNoTracking()
                .Include(r => r.SalesHeader)
                .Include(r => r.SalesReturnDetails)
                    .ThenInclude(d => d.Item)
                        .ThenInclude(i => i!.Unit)
                .OrderByDescending(r => r.ReturnDate)
                .ToListAsync();

            return View(returns);
        }

        public async Task<IActionResult> Create(int? saleId)
        {
            ViewBag.Sales = await _context.SalesHeaders
                .AsNoTracking()
                .Where(s => s.Status == "Completed")
                .OrderByDescending(s => s.SalesDate)
                .Take(200)
                .ToListAsync();

            SalesHeader? selectedSale = null;

            if (saleId.HasValue)
            {
                selectedSale = await _context.SalesHeaders
                    .Include(s => s.Customer)
                    .Include(s => s.SalesDetails)
                        .ThenInclude(d => d.Item)
                            .ThenInclude(i => i!.Unit)
                    .FirstOrDefaultAsync(s => s.Id == saleId.Value && s.Status == "Completed");

                if (selectedSale != null)
                {
                    var detailIds = selectedSale.SalesDetails.Select(d => d.Id).ToList();
                    var returnedQtys = await _context.SalesReturnDetails
                        .AsNoTracking()
                        .Where(r => detailIds.Contains(r.SalesDetailId))
                        .GroupBy(r => r.SalesDetailId)
                        .Select(g => new { SalesDetailId = g.Key, Returned = g.Sum(x => x.QuantityReturned) })
                        .ToListAsync();

                    ViewBag.ReturnedQtys = returnedQtys.ToDictionary(x => x.SalesDetailId, x => x.Returned);
                }
            }

            ViewBag.SelectedSale = selectedSale;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            int salesHeaderId,
            int salesDetailId,
            decimal quantityReturned,
            string reason,
            bool restoreToInventory = true)
        {
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

            var sale = await _context.SalesHeaders
                .Include(s => s.SalesDetails)
                    .ThenInclude(d => d.Item)
                .FirstOrDefaultAsync(s => s.Id == salesHeaderId);

            if (sale == null)
            {
                TempData["ErrorMessage"] = "Original sale not found.";
                return RedirectToAction(nameof(Create));
            }

            if (sale.Status == "Voided")
            {
                TempData["ErrorMessage"] = "Cannot process a return for a voided sale.";
                return RedirectToAction(nameof(Create));
            }

            var detail = sale.SalesDetails.FirstOrDefault(d => d.Id == salesDetailId);

            if (detail == null || detail.Item == null)
            {
                TempData["ErrorMessage"] = "Selected item not found in this sale.";
                return RedirectToAction(nameof(Create), new { saleId = salesHeaderId });
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
                TempData["ErrorMessage"] = $"Return quantity ({quantityReturned:0.###}) exceeds remaining returnable quantity ({remainingQty:0.###}).";
                return RedirectToAction(nameof(Create), new { saleId = salesHeaderId });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var returnNumber = await GenerateReturnNumberAsync();
                var refundAmount = quantityReturned * detail.UnitPrice;

                var header = new SalesReturnHeader
                {
                    ReturnNumber = returnNumber,
                    ReturnDate = DateTime.Now,
                    SalesHeaderId = sale.Id,
                    Reason = reason.Trim(),
                    RefundAmount = refundAmount,
                    CreatedBy = User.Identity?.Name ?? "Unknown",
                    CreatedAt = DateTime.Now
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
                    detail.Item.CurrentStock += quantityReturned;

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
                        if (updatedItem.CurrentStock <= 0)
                            await _notificationService.CreateOutOfStockNotificationAsync(updatedItem.ItemName);
                        else if (updatedItem.CurrentStock <= updatedItem.ReorderLevel)
                            await _notificationService.CreateLowStockNotificationAsync(updatedItem.ItemName);
                    }
                }

                TempData["SuccessMessage"] = $"Sales return {returnNumber} processed. Refund: ₱{refundAmount:N2}";
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Unable to save sales return. Please try again.";
                return RedirectToAction(nameof(Create), new { saleId = salesHeaderId });
            }
        }

        private async Task<string> GenerateReturnNumberAsync()
        {
            var prefix = $"RET-{DateTime.Now:yyyyMMdd}-";
            var countToday = await _context.SalesReturnHeaders
                .CountAsync(r => r.ReturnNumber.StartsWith(prefix));
            return $"{prefix}{(countToday + 1):D4}";
        }
    }
}