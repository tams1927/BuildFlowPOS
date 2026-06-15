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
        private readonly ItemUnitConversionService _conversionService;

        public ProductsController(
            ITenantOperationalContextProvider ctxProvider,
            AuditService auditService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard,
            ItemUnitConversionService conversionService)
            : base(ctxProvider)
        {
            _auditService = auditService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
            _conversionService = conversionService;
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
                .Include(i => i.BaseUnit)
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
            int baseUnitId,
            int? supplierId,
            decimal reorderLevel,
            decimal costPrice,
            decimal sellingPrice,
            string? description,
            string? barcode)
        {
            var tenantId = await _tenantContext.GetCurrentTenantIdAsync();
            if (baseUnitId <= 0) baseUnitId = unitId;

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
                BaseUnitId = baseUnitId,
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

            await _conversionService.EnsureBaseConversionAsync(item, tenantId);

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
            int baseUnitId,
            int? supplierId,
            decimal reorderLevel,
            decimal costPrice,
            decimal sellingPrice,
            string status,
            string? description,
            string? barcode)
        {
            var item = await _context.Items.FindAsync(id);
            if (baseUnitId <= 0) baseUnitId = unitId;

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
            item.BaseUnitId = baseUnitId;
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

            await _conversionService.EnsureBaseConversionAsync(item, tenantId);

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

        [HttpGet]
        public async Task<IActionResult> GetConversions(int itemId)
        {
            var item = await _context.Items.AsNoTracking()
                .Include(i => i.BaseUnit)
                .FirstOrDefaultAsync(i => i.Id == itemId);
            if (item == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(item.TenantId)) return Forbid();

            var conversions = await _conversionService.GetActiveConversionsAsync(itemId);
            return Json(new
            {
                itemId,
                baseUnitId = _conversionService.GetBaseUnitId(item),
                baseUnitName = item.BaseUnit?.UnitName ?? item.Unit?.UnitName,
                conversions = conversions.Select(c => new
                {
                    c.Id,
                    c.UnitId,
                    unitName = c.Unit?.UnitName,
                    shortName = c.Unit?.ShortName,
                    c.ConversionQuantity,
                    c.IsDefaultPurchaseUnit,
                    c.IsActive
                })
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetUnitsForItem(int itemId)
        {
            var item = await _context.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId);
            if (item == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(item.TenantId)) return Forbid();

            var conversions = await _conversionService.GetActiveConversionsAsync(itemId);
            var baseId = _conversionService.GetBaseUnitId(item);
            var list = new List<object>();
            if (!conversions.Any(c => c.UnitId == baseId))
            {
                var baseUnit = await _context.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == baseId);
                list.Add(new { unitId = baseId, label = $"{baseUnit?.UnitName} (base)", conversionQuantity = 1m, isDefaultPurchaseUnit = true });
            }
            foreach (var c in conversions)
            {
                list.Add(new
                {
                    unitId = c.UnitId,
                    label = $"{c.Unit?.UnitName} (1 = {c.ConversionQuantity:0.###} base)",
                    conversionQuantity = c.ConversionQuantity,
                    isDefaultPurchaseUnit = c.IsDefaultPurchaseUnit
                });
            }
            return Json(list);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Products", "Edit")]
        public async Task<IActionResult> SaveConversion(
            int itemId, int unitId, decimal conversionQuantity, bool isDefaultPurchaseUnit)
        {
            if (conversionQuantity <= 0)
            {
                TempData["ErrorMessage"] = "Conversion quantity must be greater than zero.";
                return RedirectToAction(nameof(Index));
            }

            var item = await _context.Items.FirstOrDefaultAsync(i => i.Id == itemId);
            if (item == null) { TempData["ErrorMessage"] = "Product not found."; return RedirectToAction(nameof(Index)); }
            if (!await _tenantGuard.CanAccessTenantAsync(item.TenantId)) return Forbid();

            var tenantId = await _tenantContext.GetCurrentTenantIdAsync();
            var existing = await _context.ItemUnitConversions
                .FirstOrDefaultAsync(c => c.ItemId == itemId && c.UnitId == unitId);

            if (isDefaultPurchaseUnit)
            {
                var others = await _context.ItemUnitConversions.Where(c => c.ItemId == itemId && c.IsDefaultPurchaseUnit).ToListAsync();
                foreach (var o in others) o.IsDefaultPurchaseUnit = false;
            }

            if (existing != null)
            {
                existing.ConversionQuantity = conversionQuantity;
                existing.IsDefaultPurchaseUnit = isDefaultPurchaseUnit;
                existing.IsActive = true;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                _context.ItemUnitConversions.Add(new ItemUnitConversion
                {
                    TenantId = tenantId ?? item.TenantId,
                    ItemId = itemId,
                    UnitId = unitId,
                    ConversionQuantity = conversionQuantity,
                    IsDefaultPurchaseUnit = isDefaultPurchaseUnit,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
            await _conversionService.EnsureBaseConversionAsync(item, tenantId);
            TempData["SuccessMessage"] = "Unit conversion saved.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Products", "Edit")]
        public async Task<IActionResult> DeactivateConversion(int id)
        {
            var conv = await _context.ItemUnitConversions.Include(c => c.Item).FirstOrDefaultAsync(c => c.Id == id);
            if (conv == null) { TempData["ErrorMessage"] = "Conversion not found."; return RedirectToAction(nameof(Index)); }
            if (conv.Item != null && !await _tenantGuard.CanAccessTenantAsync(conv.Item.TenantId)) return Forbid();

            var baseId = conv.Item != null ? _conversionService.GetBaseUnitId(conv.Item) : 0;
            if (conv.UnitId == baseId)
            {
                TempData["ErrorMessage"] = "Cannot deactivate the base unit conversion.";
                return RedirectToAction(nameof(Index));
            }

            conv.IsActive = false;
            conv.IsDefaultPurchaseUnit = false;
            conv.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Unit conversion deactivated.";
            return RedirectToAction(nameof(Index));
        }
    }
}