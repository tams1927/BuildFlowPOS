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
    [PermissionAuthorize("StockIn", "View")]
    public class StockInController : OperationalDbController
    {
        private readonly AuditService _auditService;
        private readonly NotificationService _notificationService;
        private readonly BranchService _branchService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;
        private readonly ItemUnitConversionService _conversionService;
        private readonly ICurrencyFormatter _currency;

        public StockInController(
            ITenantOperationalContextProvider ctxProvider,
            AuditService auditService,
            NotificationService notificationService,
            BranchService branchService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard,
            ItemUnitConversionService conversionService,
            ICurrencyFormatter currency)
            : base(ctxProvider)
        {
            _auditService = auditService;
            _notificationService = notificationService;
            _branchService = branchService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
            _conversionService = conversionService;
            _currency = currency;
        }

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var suppliersQuery = _context.Suppliers
                .AsNoTracking()
                .Where(s => s.IsActive);

            var itemsQuery = _context.Items
                .AsNoTracking()
                .Include(i => i.Unit)
                .Include(i => i.BaseUnit)
                .Where(i => i.Status == "Active");

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                suppliersQuery = suppliersQuery.Where(s => s.TenantId == tenantId || s.TenantId == null);
                itemsQuery = itemsQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);
            }

            ViewBag.Suppliers = await suppliersQuery
                .OrderBy(s => s.SupplierName)
                .ToListAsync();

            ViewBag.Items = await itemsQuery
                .OrderBy(i => i.ItemName)
                .ToListAsync();

            var currentBranch = await _branchService.GetCurrentBranchAsync(User);
            ViewBag.CurrentBranch = currentBranch;
            ViewBag.AllBranches = _branchService.IsGlobalUser(User)
                ? await _branchService.GetAllActiveBranchesAsync()
                : null;

            var query = _context.StockInHeaders
                .AsNoTracking()
                .Include(h => h.Supplier)
                .Include(h => h.Branch)
                .Include(h => h.StockInDetails)
                    .ThenInclude(d => d.Item)
                        .ThenInclude(i => i!.BaseUnit)
                .Include(h => h.StockInDetails)
                    .ThenInclude(d => d.ReceivedUnit)
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(h => h.TenantId == tenantId || h.TenantId == null);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(h =>
                    h.StockInNumber.ToLower().Contains(term) ||
                    (h.Supplier != null && h.Supplier.SupplierName.ToLower().Contains(term)) ||
                    (h.InvoiceNumber != null && h.InvoiceNumber.ToLower().Contains(term)) ||
                    (h.Branch != null && h.Branch.Name.ToLower().Contains(term)));
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var stockIns = await query
                .OrderByDescending(h => h.DateReceived)
                .ThenByDescending(h => h.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View(new PagedResult<StockInHeader>
            {
                Items = stockIns,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("StockIn", "Create")]
        public async Task<IActionResult> Create(
            int supplierId,
            string? invoiceNumber,
            DateTime dateReceived,
            int itemId,
            decimal quantity,
            int? receivedUnitId,
            decimal unitCost,
            string? remarks,
            int? branchId)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

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

            var supplierQuery = _context.Suppliers
                .Where(s => s.Id == supplierId && s.IsActive);

            var itemQuery = _context.Items
                .Where(i => i.Id == itemId && i.Status == "Active");

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                supplierQuery = supplierQuery.Where(s => s.TenantId == tenantId || s.TenantId == null);
                itemQuery = itemQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);
            }

            var supplier = await supplierQuery.FirstOrDefaultAsync();

            if (supplier == null)
            {
                TempData["ErrorMessage"] = "Supplier not found or inactive.";
                return RedirectToAction(nameof(Index));
            }

            var item = await itemQuery.Include(i => i.BaseUnit).Include(i => i.Unit).FirstOrDefaultAsync();

            if (item == null)
            {
                TempData["ErrorMessage"] = "Item not found or inactive.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(supplier.TenantId) ||
                !await _tenantGuard.CanAccessTenantAsync(item.TenantId))
            {
                return Forbid();
            }

            int? resolvedBranchId = null;

            if (branchId.HasValue && _branchService.IsGlobalUser(User))
            {
                var branch = await _context.Branches
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == branchId.Value && b.IsActive);

                if (branch == null)
                {
                    TempData["ErrorMessage"] = "Selected branch was not found or inactive.";
                    return RedirectToAction(nameof(Index));
                }

                if (!await _tenantGuard.CanAccessTenantAsync(branch.TenantId))
                {
                    return Forbid();
                }

                resolvedBranchId = branch.Id;
            }
            else
            {
                var currentBranch = await _branchService.GetCurrentBranchAsync(User);

                if (currentBranch != null)
                {
                    if (!await _tenantGuard.CanAccessTenantAsync(currentBranch.TenantId))
                    {
                        return Forbid();
                    }

                    resolvedBranchId = currentBranch.Id;
                }
            }

            var resolvedUnitId = receivedUnitId ?? _conversionService.GetBaseUnitId(item);
            decimal conversionQty;
            decimal baseQty;
            try
            {
                conversionQty = await _conversionService.GetFactorAsync(itemId, resolvedUnitId);
                baseQty = quantity * conversionQty;
            }
            catch
            {
                TempData["ErrorMessage"] = "Invalid received unit for this item.";
                return RedirectToAction(nameof(Index));
            }

            var cost = ItemUnitConversionService.ComputePurchaseCost(quantity, unitCost, conversionQty);
            var totalCost = cost.TotalCost;
            var stockInNumber = await GenerateStockInNumberAsync(tenantId);

            var header = new StockInHeader
            {
                TenantId = tenantId,
                StockInNumber = stockInNumber,
                SupplierId = supplierId,
                BranchId = resolvedBranchId,
                InvoiceNumber = invoiceNumber,
                DateReceived = dateReceived == default ? DateTime.Now : dateReceived,
                Remarks = remarks,
                TotalCost = totalCost,
                AmountPaid = 0,
                BalanceDue = totalCost,
                PaymentStatus = "Unpaid",
                CreatedAt = DateTime.Now
            };

            var detail = new StockInDetail
            {
                ItemId = itemId,
                Quantity = baseQty,
                ReceivedUnitId = resolvedUnitId,
                ReceivedQuantity = quantity,
                ConversionQuantity = conversionQty,
                BaseQuantity = baseQty,
                CostPerReceivedUnit = cost.CostPerReceivedUnit,
                CostPerBaseUnit = cost.CostPerBaseUnit,
                UnitCost = cost.CostPerBaseUnit,
                TotalCost = totalCost
            };

            header.StockInDetails.Add(detail);

            if (resolvedBranchId.HasValue)
            {
                await _branchService.AddStockAsync(resolvedBranchId.Value, itemId, baseQty);
            }
            else
            {
                item.CurrentStock += baseQty;
            }

            if (item.CostPrice != cost.CostPerBaseUnit && cost.CostPerBaseUnit > 0)
            {
                item.CostPrice = cost.CostPerBaseUnit;
            }

            if (!item.TenantId.HasValue && tenantId.HasValue)
            {
                item.TenantId = tenantId;
            }

            if (!supplier.TenantId.HasValue && tenantId.HasValue)
            {
                supplier.TenantId = tenantId;
            }

            _context.StockInHeaders.Add(header);

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "StockIn",
                "CREATED",
                $"Stock-in saved. Stock In #: {stockInNumber}, Branch: {resolvedBranchId?.ToString() ?? "N/A"}, Supplier: {supplier.SupplierName}, Item: {item.ItemName}, Received: {quantity:0.###}, Base Qty: {baseQty:0.###}, Cost/Received: {cost.CostPerReceivedUnit:N2}, Cost/Base: {cost.CostPerBaseUnit:N2}, Total: {totalCost:N2}",
                "StockInHeader",
                header.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            var receivedUnit = await _context.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == resolvedUnitId);
            var unitLabel = receivedUnit?.ShortName ?? receivedUnit?.UnitName ?? "units";
            var baseLabel = item.BaseUnit?.ShortName ?? item.Unit?.ShortName ?? "base";
            TempData["SuccessMessage"] =
                $"Stock-in saved. Received {quantity:0.###} {unitLabel} → inventory +{baseQty:0.###} {baseLabel}.";

            await _notificationService.CreateStockInNotificationAsync(stockInNumber, supplier.SupplierName, item.ItemName, baseQty);

            var stockForNotification = resolvedBranchId.HasValue
                ? await _branchService.GetBranchStockAsync(resolvedBranchId.Value, itemId)
                : item.CurrentStock;

            if (stockForNotification <= 0)
            {
                await _notificationService.CreateOutOfStockNotificationAsync(item.ItemName);
            }
            else if (stockForNotification <= item.ReorderLevel)
            {
                await _notificationService.CreateLowStockNotificationAsync(item.ItemName);
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<string> GenerateStockInNumberAsync(int? tenantId)
        {
            var prefix = $"SIN-{DateTime.Now:yyyyMMdd}-";

            var query = _context.StockInHeaders.Where(h => h.StockInNumber.StartsWith(prefix));
            if (tenantId.HasValue)
                query = query.Where(h => h.TenantId == tenantId);

            var countToday = await query.CountAsync();
            return $"{prefix}{(countToday + 1):0000}";
        }
    }
}