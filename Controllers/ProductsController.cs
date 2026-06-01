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
    [PermissionAuthorize("Products", "View")]
    public class ProductsController : OperationalDbController
    {
        private readonly AuditService _auditService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public ProductsController(
            ITenantOperationalContextProvider ctxProvider,
            AuditService auditService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard)
            : base(ctxProvider)
        {
            _auditService = auditService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
        }

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            string? categoryFilter = null,
            string? statusFilter = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var tenantId = await _tenantContext.GetCurrentTenantIdAsync();
            var isGlobalUser = _tenantContext.IsGlobalUser;

            var categoriesQuery = _context.Categories.AsNoTracking().Where(c => c.IsActive);
            var unitsQuery = _context.Units.AsNoTracking().Where(u => u.IsActive);
            var suppliersQuery = _context.Suppliers.AsNoTracking().Where(s => s.IsActive);

            if (!isGlobalUser && tenantId.HasValue)
            {
                categoriesQuery = categoriesQuery.Where(c => c.TenantId == tenantId || c.TenantId == null);
                unitsQuery = unitsQuery.Where(u => u.TenantId == tenantId || u.TenantId == null);
                suppliersQuery = suppliersQuery.Where(s => s.TenantId == tenantId || s.TenantId == null);
            }

            ViewBag.Categories = await categoriesQuery.OrderBy(c => c.CategoryName).ToListAsync();
            ViewBag.Units = await unitsQuery.OrderBy(u => u.UnitName).ToListAsync();
            ViewBag.Suppliers = await suppliersQuery.OrderBy(s => s.SupplierName).ToListAsync();

            var query = _context.Items
                .AsNoTracking()
                .Include(i => i.Category)
                .Include(i => i.Unit)
                .Include(i => i.Supplier)
                .AsQueryable();

            if (!isGlobalUser && tenantId.HasValue)
            {
                query = query.Where(i => i.TenantId == tenantId || i.TenantId == null);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(i =>
                    i.ItemName.ToLower().Contains(term) ||
                    i.ItemCode.ToLower().Contains(term) ||
                    (i.Description != null && i.Description.ToLower().Contains(term)) ||
                    (i.Category != null && i.Category.CategoryName.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(categoryFilter) && categoryFilter != "all")
            {
                if (int.TryParse(categoryFilter, out int catId))
                {
                    query = query.Where(i => i.CategoryId == catId);
                }
            }

            if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "all")
            {
                query = query.Where(i => i.Status.ToLower() == statusFilter.ToLower());
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

            var totalsQuery = _context.Items.AsQueryable();

            if (!isGlobalUser && tenantId.HasValue)
            {
                totalsQuery = totalsQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);
            }

            ViewBag.TotalActive = await totalsQuery.CountAsync(i => i.Status == "Active");
            ViewBag.TotalInactive = await totalsQuery.CountAsync(i => i.Status == "Inactive");

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
        [PermissionAuthorize("Products", "Create")]
        public async Task<IActionResult> Create(
            string? itemCode,
            string itemName,
            int categoryId,
            int unitId,
            int? supplierId,
            decimal reorderLevel,
            decimal costPrice,
            decimal sellingPrice,
            string? description,
            string? barcode)
        {
            var tenantId = await _tenantContext.GetCurrentTenantIdAsync();

            if (string.IsNullOrWhiteSpace(itemName))
            {
                TempData["ErrorMessage"] = "Product name is required.";
                return RedirectToAction(nameof(Index));
            }

            // Auto-generate SKU if left blank
            if (string.IsNullOrWhiteSpace(itemCode))
            {
                itemCode = await GenerateSkuAsync(categoryId, tenantId);
            }

            if (reorderLevel < 0 || costPrice < 0 || sellingPrice < 0)
            {
                TempData["ErrorMessage"] = "Reorder level, cost price, and selling price cannot be negative.";
                return RedirectToAction(nameof(Index));
            }

            var duplicateQuery = _context.Items.AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                duplicateQuery = duplicateQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);
            }

            if (await duplicateQuery.AnyAsync(i => i.ItemCode == itemCode.Trim()))
            {
                TempData["ErrorMessage"] = "Product code already exists.";
                return RedirectToAction(nameof(Index));
            }

            var item = new Item
            {
                TenantId = tenantId,
                ItemCode = itemCode.Trim(),
                ItemName = itemName.Trim(),
                CategoryId = categoryId,
                UnitId = unitId,
                SupplierId = supplierId,
                CurrentStock = 0,
                ReorderLevel = reorderLevel,
                CostPrice = costPrice,
                SellingPrice = sellingPrice,
                Description = description,
                Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim(),
                Status = "Active",
                CreatedAt = DateTime.Now
            };

            _context.Items.Add(item);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Products",
                "CREATED",
                $"Product created. Code: {item.ItemCode}, Name: {item.ItemName}, Cost: {item.CostPrice:N2}, Price: {item.SellingPrice:N2}",
                "Item",
                item.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Product created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Products", "Edit")]
        public async Task<IActionResult> Edit(
            int id,
            string itemCode,
            string itemName,
            int categoryId,
            int unitId,
            int? supplierId,
            decimal reorderLevel,
            decimal costPrice,
            decimal sellingPrice,
            string status,
            string? description,
            string? barcode)
        {
            var item = await _context.Items.FindAsync(id);

            if (item == null)
            {
                TempData["ErrorMessage"] = "Product not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(item.TenantId))
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(itemCode) || string.IsNullOrWhiteSpace(itemName))
            {
                TempData["ErrorMessage"] = "Product code and name are required.";
                return RedirectToAction(nameof(Index));
            }

            if (reorderLevel < 0 || costPrice < 0 || sellingPrice < 0)
            {
                TempData["ErrorMessage"] = "Reorder level, cost price, and selling price cannot be negative.";
                return RedirectToAction(nameof(Index));
            }

            var tenantId = await _tenantContext.GetCurrentTenantIdAsync();

            var duplicateQuery = _context.Items.Where(i => i.Id != id);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                duplicateQuery = duplicateQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);
            }

            if (await duplicateQuery.AnyAsync(i => i.ItemCode == itemCode.Trim()))
            {
                TempData["ErrorMessage"] = "Product code already exists.";
                return RedirectToAction(nameof(Index));
            }

            item.ItemCode = itemCode.Trim();
            item.ItemName = itemName.Trim();
            item.CategoryId = categoryId;
            item.UnitId = unitId;
            item.SupplierId = supplierId;
            item.ReorderLevel = reorderLevel;
            item.CostPrice = costPrice;
            item.SellingPrice = sellingPrice;
            item.Status = status;
            item.Description = description;
            item.Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();

            if (!item.TenantId.HasValue && tenantId.HasValue)
            {
                item.TenantId = tenantId;
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Products",
                "UPDATED",
                $"Product updated. Code: {item.ItemCode}, Name: {item.ItemName}, Status: {item.Status}",
                "Item",
                item.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Product updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<string> GenerateSkuAsync(int categoryId, int? tenantId)
        {
            var category = await _context.Categories.FindAsync(categoryId);
            var catName  = category?.CategoryName ?? "GEN";
            var prefix   = new string(catName.Where(char.IsLetterOrDigit).Take(3).ToArray()).ToUpper();
            if (prefix.Length == 0) prefix = "GEN";

            var baseQuery = _context.Items.AsQueryable();
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                baseQuery = baseQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);

            var lastCode = await baseQuery
                .Where(i => i.ItemCode.StartsWith(prefix + "-"))
                .OrderByDescending(i => i.ItemCode)
                .Select(i => i.ItemCode)
                .FirstOrDefaultAsync();

            int seq = 1;
            if (lastCode != null)
            {
                var parts = lastCode.Split('-');
                if (parts.Length >= 2 && int.TryParse(parts[^1], out int parsed))
                    seq = parsed + 1;
            }

            return $"{prefix}-{seq:D6}";
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Products", "Delete")]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _context.Items.FindAsync(id);

            if (item == null)
            {
                TempData["ErrorMessage"] = "Product not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(item.TenantId))
            {
                return Forbid();
            }

            var hasTransactions = await _context.SalesDetails.AnyAsync(s => s.ItemId == id)
                || await _context.StockInDetails.AnyAsync(s => s.ItemId == id);

            if (hasTransactions)
            {
                item.Status = "Inactive";
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    User,
                    "Products",
                    "DEACTIVATED",
                    $"Product deactivated (has transactions). Code: {item.ItemCode}, Name: {item.ItemName}",
                    "Item",
                    item.Id.ToString(),
                    HttpContext.Connection.RemoteIpAddress?.ToString()
                );

                TempData["SuccessMessage"] = "Product has existing transactions and was deactivated instead of deleted.";
                return RedirectToAction(nameof(Index));
            }

            _context.Items.Remove(item);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Products",
                "DELETED",
                $"Product deleted. Code: {item.ItemCode}, Name: {item.ItemName}",
                "Item",
                item.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Product deleted successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}