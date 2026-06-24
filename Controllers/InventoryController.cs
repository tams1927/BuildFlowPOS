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
    [PermissionAuthorize("Inventory", "View")]
    public class InventoryController : OperationalDbController
    {
        private readonly AuditService _auditService;
        private readonly NotificationService _notificationService;
        private readonly BranchService _branchService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public InventoryController(
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
            string? searchTerm = null,
            string? categoryFilter = null,
            string? statusFilter = null,
            string? stockFilter = null,
            int? branchId = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var allBranches = await _branchService.GetAllActiveBranchesAsync();
            ViewBag.Branches = allBranches;

            Branch? selectedBranch;

            if (branchId.HasValue && _branchService.IsGlobalUser(User))
            {
                selectedBranch = allBranches.FirstOrDefault(b => b.Id == branchId.Value)
                    ?? await _branchService.GetCurrentBranchAsync(User);
            }
            else
            {
                selectedBranch = await _branchService.GetCurrentBranchAsync(User);
            }

            if (selectedBranch != null &&
                !await _tenantGuard.CanAccessTenantAsync(selectedBranch.TenantId))
            {
                return Forbid();
            }

            ViewBag.SelectedBranch = selectedBranch;
            ViewBag.SelectedBranchId = selectedBranch?.Id;

            Dictionary<int, decimal>? branchStockMap = null;

            if (selectedBranch != null)
            {
                var stockQuery = _context.BranchProductStocks
                    .AsNoTracking()
                    .Where(s => s.BranchId == selectedBranch.Id);

                if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                {
                    stockQuery = stockQuery.Where(s =>
                        s.TenantId == tenantId ||
                        s.TenantId == null);
                }

                branchStockMap = await stockQuery
                    .ToDictionaryAsync(s => s.ProductId, s => s.Quantity);

                ViewBag.BranchDamagedMap = await stockQuery
                    .ToDictionaryAsync(s => s.ProductId, s => s.DamagedStock);
            }

            ViewBag.BranchStockMap = branchStockMap;

            var categoryQuery = _context.Categories
                .AsNoTracking()
                .Where(c => c.IsActive);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                categoryQuery = categoryQuery.Where(c =>
                    c.TenantId == tenantId ||
                    c.TenantId == null);
            }

            ViewBag.Categories = await categoryQuery
                .OrderBy(c => c.CategoryName)
                .ToListAsync();

            var query = _context.Items
                .AsNoTracking()
                .Include(i => i.Category)
                .Include(i => i.Unit)
                .Include(i => i.BaseUnit)
                .Include(i => i.Supplier)
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(i =>
                    i.TenantId == tenantId ||
                    i.TenantId == null);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(i =>
                    i.ItemName.ToLower().Contains(term) ||
                    i.ItemCode.ToLower().Contains(term) ||
                    (i.Category != null &&
                     i.Category.CategoryName.ToLower().Contains(term)) ||
                    (i.Supplier != null &&
                     i.Supplier.SupplierName.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(categoryFilter) &&
                categoryFilter != "all")
            {
                query = query.Where(i =>
                    i.Category != null &&
                    i.Category.CategoryName.ToLower() == categoryFilter.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(statusFilter) &&
                statusFilter != "all")
            {
                query = query.Where(i =>
                    i.Status.ToLower() == statusFilter.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(stockFilter) &&
                branchStockMap != null)
            {
                var allItems = await query
                    .Select(i => new { i.Id, i.ReorderLevel })
                    .ToListAsync();

                var matchIds = stockFilter switch
                {
                    "low" => allItems
                        .Where(i =>
                            (branchStockMap.TryGetValue(i.Id, out var q) ? q : 0) > 0 &&
                            (branchStockMap.TryGetValue(i.Id, out var q2) ? q2 : 0) <= i.ReorderLevel)
                        .Select(i => i.Id)
                        .ToHashSet(),

                    "out" => allItems
                        .Where(i =>
                            (branchStockMap.TryGetValue(i.Id, out var q) ? q : 0) <= 0)
                        .Select(i => i.Id)
                        .ToHashSet(),

                    _ => null
                };

                if (matchIds != null)
                {
                    query = query.Where(i => matchIds.Contains(i.Id));
                }
            }
            else if (!string.IsNullOrWhiteSpace(stockFilter))
            {
                query = stockFilter switch
                {
                    "low" => query.Where(i =>
                        i.CurrentStock > 0 &&
                        i.CurrentStock <= i.ReorderLevel),

                    "out" => query.Where(i =>
                        i.CurrentStock <= 0),

                    _ => query
                };
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var items = await query
                .OrderBy(i => i.ItemName)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CategoryFilter = categoryFilter;
            ViewBag.StatusFilter = statusFilter;
            ViewBag.StockFilter = stockFilter;
            ViewBag.BranchIdFilter = selectedBranch?.Id;

            var activeItemsQuery = _context.Items
                .Where(i => i.Status == "Active");

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                activeItemsQuery = activeItemsQuery.Where(i =>
                    i.TenantId == tenantId ||
                    i.TenantId == null);
            }

            if (branchStockMap != null)
            {
                var allItemIds = await activeItemsQuery
                    .Select(i => new { i.Id, i.ReorderLevel })
                    .ToListAsync();

                ViewBag.TotalActive = allItemIds.Count;

                ViewBag.TotalLowStock = allItemIds.Count(i =>
                {
                    var q = branchStockMap.TryGetValue(i.Id, out var v) ? v : 0;
                    return q > 0 && q <= i.ReorderLevel;
                });

                ViewBag.TotalOutOfStock = allItemIds.Count(i =>
                    (branchStockMap.TryGetValue(i.Id, out var v) ? v : 0) <= 0);
            }
            else
            {
                ViewBag.TotalActive = await activeItemsQuery.CountAsync();

                ViewBag.TotalLowStock = await activeItemsQuery.CountAsync(i =>
                    i.CurrentStock > 0 &&
                    i.CurrentStock <= i.ReorderLevel);

                ViewBag.TotalOutOfStock = await activeItemsQuery.CountAsync(i =>
                    i.CurrentStock <= 0);
            }

            return View(new PagedResult<Item>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        // Legacy: product creation is now handled by ProductsController.
        // This action is kept to avoid breaking any existing references.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Inventory", "Create")]
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
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (string.IsNullOrWhiteSpace(itemCode) ||
                string.IsNullOrWhiteSpace(itemName))
            {
                TempData["ErrorMessage"] =
                    "Item code and item name are required.";

                return RedirectToAction(nameof(Index));
            }

            if (currentStock < 0 ||
                reorderLevel < 0 ||
                costPrice < 0 ||
                sellingPrice < 0)
            {
                TempData["ErrorMessage"] =
                    "Stock, reorder level, cost price, and selling price cannot be negative.";

                return RedirectToAction(nameof(Index));
            }

            var code = itemCode.Trim();

            var duplicateQuery = _context.Items.AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                duplicateQuery = duplicateQuery.Where(i =>
                    i.TenantId == tenantId ||
                    i.TenantId == null);
            }

            var exists = await duplicateQuery.AnyAsync(i =>
                i.ItemCode == code);

            if (exists)
            {
                TempData["ErrorMessage"] = "Item code already exists.";
                return RedirectToAction(nameof(Index));
            }

            var item = new Item
            {
                TenantId = tenantId,
                ItemCode = code,
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

        // Legacy: product master editing is now handled by ProductsController.
        // This action is kept to avoid breaking any existing references.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Inventory", "Edit")]
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
                TempData["ErrorMessage"] =
                    "Inventory item not found.";

                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(item.TenantId))
            {
                return Forbid();
            }

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (string.IsNullOrWhiteSpace(itemCode) ||
                string.IsNullOrWhiteSpace(itemName))
            {
                TempData["ErrorMessage"] =
                    "Item code and item name are required.";

                return RedirectToAction(nameof(Index));
            }

            if (currentStock < 0 ||
                reorderLevel < 0 ||
                costPrice < 0 ||
                sellingPrice < 0)
            {
                TempData["ErrorMessage"] =
                    "Stock, reorder level, cost price, and selling price cannot be negative.";

                return RedirectToAction(nameof(Index));
            }

            var code = itemCode.Trim();

            var duplicateQuery = _context.Items
                .Where(i =>
                    i.Id != id &&
                    i.ItemCode == code);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                duplicateQuery = duplicateQuery.Where(i =>
                    i.TenantId == tenantId ||
                    i.TenantId == null);
            }

            var exists = await duplicateQuery.AnyAsync();

            if (exists)
            {
                TempData["ErrorMessage"] = "Item code already exists.";
                return RedirectToAction(nameof(Index));
            }

            var oldStock = item.CurrentStock;

            item.ItemCode = code;
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

            if (!item.TenantId.HasValue && tenantId.HasValue)
            {
                item.TenantId = tenantId;
            }

            // ============================================
            // AUTO-CREATE STOCK ADJUSTMENT RECORD (H-6)
            // ============================================

            if (oldStock != currentStock)
            {
                var currentBranch = await _branchService.GetCurrentBranchAsync(User);

                var today      = DateTime.Now.ToString("yyyyMMdd");
                var adjCount   = await _context.StockAdjustmentHeaders.CountAsync();
                var adjNumber  = $"ADJ-{today}-{(adjCount + 1):D5}";
                var adjType    = currentStock > oldStock ? "Increase" : "Decrease";
                var adjQty     = Math.Abs(currentStock - oldStock);

                var adjHeader = new StockAdjustmentHeader
                {
                    AdjustmentNumber = adjNumber,
                    AdjustmentDate   = DateTime.Now,
                    AdjustmentType   = adjType,
                    Reason           = "Manual inventory edit",
                    CreatedBy        = User.Identity?.Name,
                    BranchId         = currentBranch?.Id,
                    TenantId         = tenantId,
                    StockAdjustmentDetails = new List<StockAdjustmentDetail>
                    {
                        new StockAdjustmentDetail
                        {
                            ItemId      = item.Id,
                            Quantity    = adjQty,
                            StockBefore = oldStock,
                            StockAfter  = currentStock
                        }
                    }
                };

                _context.StockAdjustmentHeaders.Add(adjHeader);
            }

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

            TempData["SuccessMessage"] =
                "Inventory item updated successfully.";

            return RedirectToAction(nameof(Index));
        }

        // Legacy: product deactivation is now handled by ProductsController.Delete.
        // This action is kept to avoid breaking any existing references.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Inventory", "Delete")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var item = await _context.Items.FindAsync(id);

            if (item == null)
            {
                TempData["ErrorMessage"] =
                    "Inventory item not found.";

                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(item.TenantId))
            {
                return Forbid();
            }

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            item.Status = "Inactive";

            if (!item.TenantId.HasValue && tenantId.HasValue)
            {
                item.TenantId = tenantId;
            }

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

            TempData["SuccessMessage"] =
                "Inventory item deactivated successfully.";

            await _notificationService
                .CreateItemDeactivatedNotificationAsync(item.ItemName);

            return RedirectToAction(nameof(Index));
        }
    }
}