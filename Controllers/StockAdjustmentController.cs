using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("StockAdjustment", "View")]
    public class StockAdjustmentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;
        private readonly NotificationService _notificationService;

        public StockAdjustmentController(
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
            var adjustments = await _context.StockAdjustmentHeaders
                .AsNoTracking()
                .Include(a => a.StockAdjustmentDetails)
                    .ThenInclude(d => d.Item)
                .OrderByDescending(a => a.AdjustmentDate)
                .ToListAsync();

            return View(adjustments);
        }

        public async Task<IActionResult> Create()
        {
            ViewBag.Items = await _context.Items
                .AsNoTracking()
                .Where(i => i.Status == "Active")
                .OrderBy(i => i.ItemName)
                .ToListAsync();

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string adjustmentType,
            string? reason,
            int itemId,
            decimal quantity)
        {
            if (itemId <= 0 || quantity <= 0)
            {
                TempData["ErrorMessage"] =
                    "Item and quantity are required.";

                return RedirectToAction(nameof(Create));
            }

            var item = await _context.Items
                .FirstOrDefaultAsync(i => i.Id == itemId);

            if (item == null)
            {
                TempData["ErrorMessage"] =
                    "Selected item not found.";

                return RedirectToAction(nameof(Create));
            }

            if (adjustmentType != "Increase" &&
                adjustmentType != "Decrease")
            {
                TempData["ErrorMessage"] =
                    "Invalid adjustment type.";

                return RedirectToAction(nameof(Create));
            }

            var stockBefore = item.CurrentStock;
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
                        "Insufficient stock for decrease adjustment.";

                    return RedirectToAction(nameof(Create));
                }

                stockAfter = stockBefore - quantity;
            }

            using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                var adjustmentNumber =
                    await GenerateAdjustmentNumberAsync();

                var userName =
                    User.Identity?.Name ?? "Unknown";

                var header = new StockAdjustmentHeader
                {
                    AdjustmentNumber = adjustmentNumber,
                    AdjustmentDate = DateTime.Now,
                    AdjustmentType = adjustmentType,
                    Reason = reason,
                    CreatedBy = userName,
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

                item.CurrentStock = stockAfter;

                _context.StockAdjustmentHeaders.Add(header);

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    User,
                    "StockAdjustment",
                    "CREATED",
                    $"Stock adjustment created. Number: {adjustmentNumber}, Type: {adjustmentType}, Item: {item.ItemName}, Qty: {quantity:0.###}, Before: {stockBefore:0.###}, After: {stockAfter:0.###}, Reason: {reason}",
                    "StockAdjustmentHeader",
                    header.Id.ToString(),
                    HttpContext.Connection.RemoteIpAddress?.ToString()
                );

                await transaction.CommitAsync();

                await _notificationService.CreateStockAdjustmentNotificationAsync(
                    adjustmentNumber, item.ItemName, adjustmentType, quantity);

                if (stockAfter <= 0)
                    await _notificationService.CreateOutOfStockNotificationAsync(item.ItemName);
                else if (stockAfter <= item.ReorderLevel)
                    await _notificationService.CreateLowStockNotificationAsync(item.ItemName);

                TempData["SuccessMessage"] =
                    "Stock adjustment saved successfully.";

                return RedirectToAction(nameof(Index));
            }
            catch
            {
                await transaction.RollbackAsync();

                TempData["ErrorMessage"] =
                    "Unable to save stock adjustment.";

                return RedirectToAction(nameof(Create));
            }
        }

        private async Task<string> GenerateAdjustmentNumberAsync()
        {
            var today =
                DateTime.Now.ToString("yyyyMMdd");

            var count =
                await _context.StockAdjustmentHeaders.CountAsync();

            return $"ADJ-{today}-{(count + 1):D5}";
        }
    }
}