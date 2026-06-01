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

        public StockInController(
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

            var suppliersQuery = _context.Suppliers
                .AsNoTracking()
                .Where(s => s.IsActive);

            var itemsQuery = _context.Items
                .AsNoTracking()
                .Include(i => i.Unit)
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
                        .ThenInclude(i => i!.Unit)
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

            var item = await itemQuery.FirstOrDefaultAsync();

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

            var totalCost = quantity * unitCost;
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
                Quantity = quantity,
                UnitCost = unitCost,
                TotalCost = totalCost
            };

            header.StockInDetails.Add(detail);

            if (resolvedBranchId.HasValue)
            {
                await _branchService.AddStockAsync(resolvedBranchId.Value, itemId, quantity);
            }
            else
            {
                item.CurrentStock += quantity;
            }

            if (item.CostPrice != unitCost && unitCost > 0)
            {
                item.CostPrice = unitCost;
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
                $"Stock-in saved. Stock In #: {stockInNumber}, Branch: {resolvedBranchId?.ToString() ?? "N/A"}, Supplier: {supplier.SupplierName}, Item: {item.ItemName}, Quantity: {quantity:0.###}, Unit Cost: {unitCost:N2}, Total Cost: {totalCost:N2}",
                "StockInHeader",
                header.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = $"Stock-in saved. {item.ItemName} stock increased by {quantity:0.###}.";

            await _notificationService.CreateStockInNotificationAsync(stockInNumber, supplier.SupplierName, item.ItemName, quantity);

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