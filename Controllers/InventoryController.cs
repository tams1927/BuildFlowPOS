using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Inventory", "View")]
    public class InventoryController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;
        private readonly NotificationService _notificationService;

        public InventoryController(ApplicationDbContext context, AuditService auditService, NotificationService notificationService)
        {
            _context = context;
            _auditService = auditService;
            _notificationService = notificationService;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Categories = await _context.Categories
                .AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.CategoryName)
                .ToListAsync();

            ViewBag.Units = await _context.Units
                .AsNoTracking()
                .Where(u => u.IsActive)
                .OrderBy(u => u.UnitName)
                .ToListAsync();

            ViewBag.Suppliers = await _context.Suppliers
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.SupplierName)
                .ToListAsync();

            var items = await _context.Items
                .AsNoTracking()
                .Include(i => i.Category)
                .Include(i => i.Unit)
                .Include(i => i.Supplier)
                .OrderBy(i => i.ItemName)
                .ToListAsync();

            return View(items);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string itemCode,
            string itemName,
            int categoryId,
            int unitId,
            int? supplierId,
            decimal currentStock,
            decimal reorderLevel,
            decimal costPrice,
            decimal sellingPrice,
            string? description)
        {
            if (string.IsNullOrWhiteSpace(itemCode) || string.IsNullOrWhiteSpace(itemName))
            {
                TempData["ErrorMessage"] = "Item code and item name are required.";
                return RedirectToAction(nameof(Index));
            }

            var exists = await _context.Items.AnyAsync(i => i.ItemCode == itemCode.Trim());

            if (exists)
            {
                TempData["ErrorMessage"] = "Item code already exists.";
                return RedirectToAction(nameof(Index));
            }

            var item = new Item
            {
                ItemCode = itemCode.Trim(),
                ItemName = itemName.Trim(),
                CategoryId = categoryId,
                UnitId = unitId,
                SupplierId = supplierId,
                CurrentStock = currentStock,
                ReorderLevel = reorderLevel,
                CostPrice = costPrice,
                SellingPrice = sellingPrice,
                Description = description,
                Status = "Active",
                CreatedAt = DateTime.Now
            };

            _context.Items.Add(item);
            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
                User,
                "Inventory",
                "ITEM CREATED",
                $"Item created. Code: {item.ItemCode}, Name: {item.ItemName}, Stock: {item.CurrentStock:0.###}, Cost: {item.CostPrice:N2}, Price: {item.SellingPrice:N2}",
                "Item",
                item.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Inventory item added successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            string itemCode,
            string itemName,
            int categoryId,
            int unitId,
            int? supplierId,
            decimal currentStock,
            decimal reorderLevel,
            decimal costPrice,
            decimal sellingPrice,
            string status,
            string? description)
        {
            var item = await _context.Items.FindAsync(id);

            if (item == null)
            {
                TempData["ErrorMessage"] = "Inventory item not found.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(itemCode) || string.IsNullOrWhiteSpace(itemName))
            {
                TempData["ErrorMessage"] = "Item code and item name are required.";
                return RedirectToAction(nameof(Index));
            }

            var exists = await _context.Items.AnyAsync(i =>
                i.Id != id &&
                i.ItemCode == itemCode.Trim());

            if (exists)
            {
                TempData["ErrorMessage"] = "Item code already exists.";
                return RedirectToAction(nameof(Index));
            }

            item.ItemCode = itemCode.Trim();
            item.ItemName = itemName.Trim();
            item.CategoryId = categoryId;
            item.UnitId = unitId;
            item.SupplierId = supplierId;
            item.CurrentStock = currentStock;
            item.ReorderLevel = reorderLevel;
            item.CostPrice = costPrice;
            item.SellingPrice = sellingPrice;
            item.Status = status;
            item.Description = description;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
                User,
                "Inventory",
                "ITEM UPDATED",
                $"Item updated. Code: {item.ItemCode}, Name: {item.ItemName}, Stock: {item.CurrentStock:0.###}, Cost: {item.CostPrice:N2}, Price: {item.SellingPrice:N2}, Status: {item.Status}",
                "Item",
                item.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );
            TempData["SuccessMessage"] = "Inventory item updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(int id)
        {
            var item = await _context.Items.FindAsync(id);

            if (item == null)
            {
                TempData["ErrorMessage"] = "Inventory item not found.";
                return RedirectToAction(nameof(Index));
            }

            item.Status = "Inactive";

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
                User,
                "Inventory",
                "ITEM DEACTIVATED",
                $"Item deactivated. Code: {item.ItemCode}, Name: {item.ItemName}",
                "Item",
                item.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );
            TempData["SuccessMessage"] = "Inventory item deactivated successfully.";

            await _notificationService.CreateItemDeactivatedNotificationAsync(item.ItemName);

            return RedirectToAction(nameof(Index));
        }
    }
}