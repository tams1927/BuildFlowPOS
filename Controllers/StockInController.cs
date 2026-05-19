using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("StockIn", "View")]
    public class StockInController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;
        private readonly NotificationService _notificationService;

        public StockInController(ApplicationDbContext context, AuditService auditService, NotificationService notificationService)
        {
            _context = context;
            _auditService = auditService;
            _notificationService = notificationService;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Suppliers = await _context.Suppliers
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.SupplierName)
                .ToListAsync();

            ViewBag.Items = await _context.Items
                .AsNoTracking()
                .Include(i => i.Unit)
                .Where(i => i.Status == "Active")
                .OrderBy(i => i.ItemName)
                .ToListAsync();

            var stockIns = await _context.StockInHeaders
                .AsNoTracking()
                .Include(h => h.Supplier)
                .Include(h => h.StockInDetails)
                    .ThenInclude(d => d.Item)
                       .ThenInclude(i => i!.Unit)
                .OrderByDescending(h => h.DateReceived)
                .ThenByDescending(h => h.Id)
                .ToListAsync();

            return View(stockIns);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            int supplierId,
            string? invoiceNumber,
            DateTime dateReceived,
            int itemId,
            decimal quantity,
            decimal unitCost,
            string? remarks)
        {
            if (supplierId <= 0)
            {
                TempData["ErrorMessage"] = "Please select a supplier.";
                return RedirectToAction(nameof(Index));
            }

            if (itemId <= 0)
            {
                TempData["ErrorMessage"] = "Please select an item.";
                return RedirectToAction(nameof(Index));
            }

            if (quantity <= 0)
            {
                TempData["ErrorMessage"] = "Quantity must be greater than zero.";
                return RedirectToAction(nameof(Index));
            }

            if (unitCost < 0)
            {
                TempData["ErrorMessage"] = "Unit cost cannot be negative.";
                return RedirectToAction(nameof(Index));
            }

            var supplier = await _context.Suppliers
                .FirstOrDefaultAsync(s => s.Id == supplierId && s.IsActive);

            if (supplier == null)
            {
                TempData["ErrorMessage"] = "Supplier not found or inactive.";
                return RedirectToAction(nameof(Index));
            }

            var item = await _context.Items
                .FirstOrDefaultAsync(i => i.Id == itemId && i.Status == "Active");

            if (item == null)
            {
                TempData["ErrorMessage"] = "Item not found or inactive.";
                return RedirectToAction(nameof(Index));
            }

            var totalCost = quantity * unitCost;

            var stockInNumber = await GenerateStockInNumberAsync();

            var header = new StockInHeader
            {
                StockInNumber = stockInNumber,
                SupplierId = supplierId,
                InvoiceNumber = invoiceNumber,
                DateReceived = dateReceived == default ? DateTime.Now : dateReceived,
                Remarks = remarks,
                TotalCost = totalCost,
                CreatedAt = DateTime.Now
            };

            var detail = new StockInDetail
            {
                ItemId = itemId,
                Quantity = quantity,
                UnitCost = unitCost,
                TotalCost = totalCost
            };

            header.StockInDetails.Add(detail);

            item.CurrentStock += quantity;

            if (item.CostPrice != unitCost && unitCost > 0)
            {
                item.CostPrice = unitCost;
            }

            _context.StockInHeaders.Add(header);

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
                User,
                "StockIn",
                "CREATED",
                $"Stock-in saved. Stock In #: {stockInNumber}, Supplier: {supplier.SupplierName}, Item: {item.ItemName}, Quantity: {quantity:0.###}, Unit Cost: {unitCost:N2}, Total Cost: {totalCost:N2}",
                "StockInHeader",
                header.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = $"Stock-in saved. {item.ItemName} stock increased by {quantity:0.###}.";

            await _notificationService.CreateStockInNotificationAsync(stockInNumber, supplier.SupplierName, item.ItemName, quantity);

            if (item.CurrentStock <= 0)
                await _notificationService.CreateOutOfStockNotificationAsync(item.ItemName);
            else if (item.CurrentStock <= item.ReorderLevel)
                await _notificationService.CreateLowStockNotificationAsync(item.ItemName);

            return RedirectToAction(nameof(Index));
        }

        private async Task<string> GenerateStockInNumberAsync()
        {
            var today = DateTime.Now;
            var prefix = $"SIN-{today:yyyyMMdd}-";

            var countToday = await _context.StockInHeaders
                .CountAsync(h => h.StockInNumber.StartsWith(prefix));

            return $"{prefix}{(countToday + 1).ToString("0000")}";
        }
    }
}