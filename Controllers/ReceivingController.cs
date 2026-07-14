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
    /// <summary>
    /// RC1.8 — Supplier delivery receiving list/details.
    /// Uses existing <see cref="StockInHeader"/> records as the receipt source of truth.
    /// </summary>
    [Authorize]
    [PermissionAuthorize("StockIn", "View")]
    public class ReceivingController : OperationalDbController
    {
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;
        private readonly BranchService _branchService;

        public ReceivingController(
            ITenantOperationalContextProvider ctxProvider,
            ITenantContext tenantContext,
            TenantGuard tenantGuard,
            BranchService branchService)
            : base(ctxProvider)
        {
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
            _branchService = branchService;
        }

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            string? supplierFilter = null,
            string? branchFilter = null,
            string? statusFilter = null,
            string? sourceFilter = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var suppliersQuery = _context.Suppliers.AsNoTracking().Where(s => s.IsActive);
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                suppliersQuery = suppliersQuery.Where(s => s.TenantId == tenantId || s.TenantId == null);

            ViewBag.Suppliers = await suppliersQuery.OrderBy(s => s.SupplierName).ToListAsync();
            ViewBag.Branches = _branchService.IsGlobalUser(User)
                ? await _branchService.GetAllActiveBranchesAsync()
                : await _branchService.GetAssignedBranchesAsync(
                    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "");
            ViewBag.SupplierFilter = supplierFilter;
            ViewBag.BranchFilter = branchFilter;
            ViewBag.StatusFilter = statusFilter;
            ViewBag.SourceFilter = sourceFilter;

            var query = _context.StockInHeaders
                .AsNoTracking()
                .Include(h => h.Supplier)
                .Include(h => h.Branch)
                .Include(h => h.PurchaseOrder)
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                query = query.Where(h => h.TenantId == tenantId || h.TenantId == null);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(h =>
                    h.StockInNumber.ToLower().Contains(term) ||
                    (h.InvoiceNumber != null && h.InvoiceNumber.ToLower().Contains(term)) ||
                    (h.ReceivedBy != null && h.ReceivedBy.ToLower().Contains(term)) ||
                    (h.Supplier != null && h.Supplier.SupplierName.ToLower().Contains(term)) ||
                    (h.Branch != null && h.Branch.Name.ToLower().Contains(term)) ||
                    (h.PurchaseOrder != null && h.PurchaseOrder.PONumber.ToLower().Contains(term)) ||
                    (h.Remarks != null && h.Remarks.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(supplierFilter) && supplierFilter != "all"
                && int.TryParse(supplierFilter, out var supplierId))
            {
                query = query.Where(h => h.SupplierId == supplierId);
            }

            if (!string.IsNullOrWhiteSpace(branchFilter) && branchFilter != "all"
                && int.TryParse(branchFilter, out var branchId))
            {
                query = query.Where(h => h.BranchId == branchId);
            }

            if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "all")
            {
                query = query.Where(h => h.PaymentStatus == statusFilter);
            }

            if (!string.IsNullOrWhiteSpace(sourceFilter) && sourceFilter != "all")
            {
                query = sourceFilter == "po"
                    ? query.Where(h => h.PurchaseOrderId != null)
                    : query.Where(h => h.PurchaseOrderId == null);
            }

            var totalRecords = await query.CountAsync();
            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber, (int)Math.Ceiling(totalRecords / (double)pageSize));

            var receipts = await query
                .OrderByDescending(h => h.DateReceived)
                .ThenByDescending(h => h.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View(new PagedResult<StockInHeader>
            {
                Items = receipts,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        public async Task<IActionResult> Details(int id)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var header = await _context.StockInHeaders
                .AsNoTracking()
                .Include(h => h.Supplier)
                .Include(h => h.Branch)
                .Include(h => h.PurchaseOrder)
                .Include(h => h.StockInDetails)
                    .ThenInclude(d => d.Item)
                        .ThenInclude(i => i!.Unit)
                .Include(h => h.StockInDetails)
                    .ThenInclude(d => d.ReceivedUnit)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(header.TenantId)) return Forbid();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue
                && header.TenantId.HasValue && header.TenantId != tenantId)
                return Forbid();

            ViewBag.Payments = await _context.SupplierPayments
                .AsNoTracking()
                .Where(p => p.StockInHeaderId == id)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();

            var damagedHeaders = await _context.DamagedGoodsHeaders
                .AsNoTracking()
                .Include(h => h.Details)
                .Where(h => h.Remarks != null && h.Remarks.Contains(header.StockInNumber))
                .ToListAsync();

            var poItems = header.PurchaseOrderId.HasValue
                ? await _context.PurchaseOrderItems
                    .AsNoTracking()
                    .Include(pi => pi.OrderedUnit)
                    .Where(pi => pi.PurchaseOrderId == header.PurchaseOrderId)
                    .ToListAsync()
                : new List<PurchaseOrderItem>();

            var detailLines = header.StockInDetails.Select(d =>
            {
                var poItem = poItems.FirstOrDefault(pi => pi.ItemId == d.ItemId);
                var damaged = damagedHeaders
                    .SelectMany(dh => dh.Details)
                    .Where(dd => dd.ItemId == d.ItemId)
                    .Sum(dd => dd.Quantity);

                return new ReceivingDetailLineVm
                {
                    ItemId = d.ItemId,
                    ItemName = d.Item?.ItemName ?? $"Item #{d.ItemId}",
                    ItemCode = d.Item?.ItemCode,
                    OrderedQty = poItem != null
                        ? (poItem.OrderedQuantity > 0 ? poItem.OrderedQuantity : poItem.Quantity)
                        : 0,
                    AcceptedQty = d.ReceivedQuantity,
                    DamagedQty = damaged,
                    RejectedQty = damaged,
                    UnitLabel = d.ReceivedUnit?.ShortName ?? d.Item?.Unit?.ShortName ?? "-",
                    CostPerUnit = d.CostPerReceivedUnit,
                    LineTotal = d.TotalCost
                };
            }).ToList();

            ViewBag.DetailLines = detailLines;
            ViewBag.DamagedHeaders = damagedHeaders;

            var isPartial = header.PurchaseOrder != null && header.PurchaseOrder.Status == "PartiallyReceived";
            var isComplete = header.PurchaseOrder != null && header.PurchaseOrder.Status == "Received";
            ViewBag.IsPartialDelivery = isPartial;
            ViewBag.IsFullyReceived = isComplete;

            if (poItems.Any())
            {
                var totalOrdered = poItems.Sum(i => i.OrderedQuantity > 0 ? i.OrderedQuantity : i.Quantity);
                var totalReceived = poItems.Sum(i =>
                    i.ConversionQuantity > 0 ? i.QuantityReceived / i.ConversionQuantity : i.QuantityReceived);
                ViewBag.PoRemaining = Math.Max(0, totalOrdered - totalReceived);
                ViewBag.PoUnitLabel = poItems.Count == 1
                    ? poItems.First().OrderedUnit?.ShortName ?? poItems.First().Item?.Unit?.ShortName ?? "units"
                    : "units";
            }

            return View(header);
        }
    }
}
