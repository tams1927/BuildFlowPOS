using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.ViewModels;
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

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            string? categoryFilter = null,
            string? statusFilter = null,
            string? stockFilter = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

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

            var query = _context.Items
                .AsNoTracking()
                .Include(i => i.Category)
                .Include(i => i.Unit)
                .Include(i => i.Supplier)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(i =>
                    i.ItemName.ToLower().Contains(term) ||
                    i.ItemCode.ToLower().Contains(term) ||
                    (i.Category != null && i.Category.CategoryName.ToLower().Contains(term)) ||
                    (i.Supplier != null && i.Supplier.SupplierName.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(categoryFilter) && categoryFilter != "all")
            {
                query = query.Where(i => i.Category != null && i.Category.CategoryName.ToLower() == categoryFilter.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "all")
            {
                query = query.Where(i => i.Status.ToLower() == statusFilter.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(stockFilter))
            {
                query = stockFilter switch
                {
                    "low" => query.Where(i => i.CurrentStock > 0 && i.CurrentStock <= i.ReorderLevel),
                    "out" => query.Where(i => i.CurrentStock <= 0),
                    _ => query
                };
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var items = await query
                .OrderBy(i => i.ItemName)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CategoryFilter = categoryFilter;
            ViewBag.StatusFilter = statusFilter;
            ViewBag.StockFilter = stockFilter;

            ViewBag.TotalActive = await _context.Items.CountAsync(i => i.Status == "Active");
            ViewBag.TotalLowStock = await _context.Items.CountAsync(i =>
                i.Status == "Active" && i.CurrentStock > 0 && i.CurrentStock <= i.ReorderLevel);
            ViewBag.TotalOutOfStock = await _context.Items.CountAsync(i => i.CurrentStock <= 0);

            return View(new PagedResult<Item>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
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

            if (currentStock < 0 || reorderLevel < 0 || costPrice < 0 || sellingPrice < 0)
            {
                TempData["ErrorMessage"] = "Stock, reorder level, cost price, and selling price cannot be negative.";
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

            if (currentStock < 0 || reorderLevel < 0 || costPrice < 0 || sellingPrice < 0)
            {
                TempData["ErrorMessage"] = "Stock, reorder level, cost price, and selling price cannot be negative.";
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