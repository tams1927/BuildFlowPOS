using HardwareManagementSystem.Constants;
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
    public class InventoryMovementController : OperationalDbController
    {
        private readonly BranchService _branchService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public InventoryMovementController(
            ITenantOperationalContextProvider ctxProvider,
            BranchService branchService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard)
            : base(ctxProvider)
        {
            _branchService = branchService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
        }

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 25,
            string? searchTerm = null,
            string? movementType = null,
            int? productId = null,
            int? branchId = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var from = dateFrom?.Date ?? DateTime.Today.AddDays(-30);
            var to   = (dateTo?.Date ?? DateTime.Today).AddDays(1);

            // ── Tenant scope ─────────────────────────────────────────
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            // ── Branch resolution ────────────────────────────────────
            var allBranches = await _branchService.GetAllActiveBranchesAsync();

            Branch? selectedBranch;

            if (branchId.HasValue && _branchService.IsGlobalUser(User))
            {
                selectedBranch = allBranches.FirstOrDefault(b => b.Id == branchId.Value);
            }
            else
            {
                selectedBranch = await _branchService.GetCurrentBranchAsync(User);
            }

            // Non-global users are ALWAYS locked to their current branch
            if (!_branchService.IsGlobalUser(User))
            {
                selectedBranch = await _branchService.GetCurrentBranchAsync(User);
                branchId = selectedBranch?.Id;
            }

            ViewBag.SelectedBranch    = selectedBranch;
            ViewBag.SelectedBranchId  = branchId;

            // ── Build unified movement rows ──────────────────────────
            var movements = new List<InventoryMovementVm>();

            var lowerSearch = searchTerm?.Trim().ToLower();

            // ── 1. STOCK-IN ──────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(movementType) || movementType == "StockIn")
            {
                var stockInQuery = _context.StockInDetails
                    .AsNoTracking()
                    .Include(d => d.Item)
                        .ThenInclude(i => i!.Unit)
                    .Include(d => d.StockInHeader)
                        .ThenInclude(h => h!.Supplier)
                    .Include(d => d.StockInHeader)
                        .ThenInclude(h => h!.Branch)
                    .Where(d =>
                        d.StockInHeader != null &&
                        d.StockInHeader.DateReceived >= from &&
                        d.StockInHeader.DateReceived < to &&
                        (productId == null || d.ItemId == productId) &&
                        (branchId == null || d.StockInHeader.BranchId == branchId));

                // Tenant filter
                if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                    stockInQuery = stockInQuery.Where(d =>
                        d.StockInHeader!.TenantId == tenantId ||
                        d.StockInHeader!.TenantId == null);

                var stockIns = await stockInQuery.ToListAsync();

                // Search applied in memory (cross-nav columns)
                var filtered = string.IsNullOrWhiteSpace(lowerSearch)
                    ? stockIns
                    : stockIns.Where(d =>
                        (d.Item?.ItemName.ToLower().Contains(lowerSearch) == true) ||
                        (d.Item?.ItemCode.ToLower().Contains(lowerSearch) == true) ||
                        (d.StockInHeader?.StockInNumber.ToLower().Contains(lowerSearch) == true));

                movements.AddRange(filtered.Select(d => new InventoryMovementVm
                {
                    Date          = d.StockInHeader!.DateReceived,
                    MovementType  = "Stock-In",
                    Branch        = d.StockInHeader.Branch?.Name,
                    ProductId     = d.ItemId,
                    ProductName   = d.Item?.ItemName ?? "N/A",
                    ProductCode   = d.Item?.ItemCode ?? "",
                    Unit          = d.Item?.Unit?.ShortName ?? "",
                    QuantityIn    = d.Quantity,
                    QuantityOut   = null,
                    Reference     = d.StockInHeader.StockInNumber,
                    Notes         = d.StockInHeader.Supplier?.SupplierName
                }));
            }

            // ── 2. SALES ─────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(movementType) || movementType == "Sale")
            {
                var salesQuery = _context.SalesDetails
                    .AsNoTracking()
                    .Include(d => d.Item)
                        .ThenInclude(i => i!.Unit)
                    .Include(d => d.SalesHeader)
                        .ThenInclude(h => h!.Branch)
                    .Where(d =>
                        d.SalesHeader != null &&
                        d.SalesHeader.Status == AppStatuses.Completed &&
                        d.SalesHeader.SalesDate >= from &&
                        d.SalesHeader.SalesDate < to &&
                        (productId == null || d.ItemId == productId) &&
                        (branchId == null || d.SalesHeader.BranchId == branchId));

                if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                    salesQuery = salesQuery.Where(d =>
                        d.SalesHeader!.TenantId == tenantId ||
                        d.SalesHeader!.TenantId == null);

                var sales = await salesQuery.ToListAsync();

                var filtered = string.IsNullOrWhiteSpace(lowerSearch)
                    ? sales
                    : sales.Where(d =>
                        (d.Item?.ItemName.ToLower().Contains(lowerSearch) == true) ||
                        (d.Item?.ItemCode.ToLower().Contains(lowerSearch) == true) ||
                        (d.SalesHeader?.SalesNumber.ToLower().Contains(lowerSearch) == true));

                movements.AddRange(filtered.Select(d => new InventoryMovementVm
                {
                    Date         = d.SalesHeader!.SalesDate,
                    MovementType = "Sale",
                    Branch       = d.SalesHeader.Branch?.Name,
                    ProductId    = d.ItemId,
                    ProductName  = d.Item?.ItemName ?? "N/A",
                    ProductCode  = d.Item?.ItemCode ?? "",
                    Unit         = d.Item?.Unit?.ShortName ?? "",
                    QuantityIn   = null,
                    QuantityOut  = d.Quantity,
                    Reference    = d.SalesHeader.SalesNumber,
                    Notes        = d.SalesHeader.CashierName
                }));
            }

            // ── 3. STOCK ADJUSTMENTS ─────────────────────────────────
            if (string.IsNullOrWhiteSpace(movementType) || movementType == "Adjustment")
            {
                var adjQuery = _context.StockAdjustmentDetails
                    .AsNoTracking()
                    .Include(d => d.Item)
                        .ThenInclude(i => i!.Unit)
                    .Include(d => d.StockAdjustmentHeader)
                        .ThenInclude(h => h!.Branch)
                    .Where(d =>
                        d.StockAdjustmentHeader != null &&
                        d.StockAdjustmentHeader.AdjustmentDate >= from &&
                        d.StockAdjustmentHeader.AdjustmentDate < to &&
                        (productId == null || d.ItemId == productId) &&
                        (branchId == null || d.StockAdjustmentHeader.BranchId == branchId));

                if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                    adjQuery = adjQuery.Where(d =>
                        d.StockAdjustmentHeader!.TenantId == tenantId ||
                        d.StockAdjustmentHeader!.TenantId == null);

                var adjustments = await adjQuery.ToListAsync();

                var filtered = string.IsNullOrWhiteSpace(lowerSearch)
                    ? adjustments
                    : adjustments.Where(d =>
                        (d.Item?.ItemName.ToLower().Contains(lowerSearch) == true) ||
                        (d.Item?.ItemCode.ToLower().Contains(lowerSearch) == true) ||
                        (d.StockAdjustmentHeader?.AdjustmentNumber.ToLower().Contains(lowerSearch) == true));

                movements.AddRange(filtered.Select(d => new InventoryMovementVm
                {
                    Date         = d.StockAdjustmentHeader!.AdjustmentDate,
                    MovementType = d.StockAdjustmentHeader.AdjustmentType == "Increase"
                                       ? "Adjustment +"
                                       : "Adjustment -",
                    Branch       = d.StockAdjustmentHeader.Branch?.Name,
                    ProductId    = d.ItemId,
                    ProductName  = d.Item?.ItemName ?? "N/A",
                    ProductCode  = d.Item?.ItemCode ?? "",
                    Unit         = d.Item?.Unit?.ShortName ?? "",
                    QuantityIn   = d.StockAdjustmentHeader.AdjustmentType == "Increase" ? d.Quantity : null,
                    QuantityOut  = d.StockAdjustmentHeader.AdjustmentType == "Decrease" ? d.Quantity : null,
                    Reference    = d.StockAdjustmentHeader.AdjustmentNumber,
                    Notes        = d.StockAdjustmentHeader.Reason
                }));
            }

            // ── 4. TRANSFERS ─────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(movementType) || movementType == "Transfer")
            {
                var fromUtc = from.ToUniversalTime();
                var toUtc   = to.ToUniversalTime();

                var transferQuery = _context.BranchTransferItems
                    .AsNoTracking()
                    .Include(ti => ti.Product)
                        .ThenInclude(p => p!.Unit)
                    .Include(ti => ti.BranchTransfer)
                        .ThenInclude(t => t!.FromBranch)
                    .Include(ti => ti.BranchTransfer)
                        .ThenInclude(t => t!.ToBranch)
                    .Where(ti =>
                        ti.BranchTransfer != null &&
                        ti.BranchTransfer.Status == AppStatuses.Completed &&
                        ti.BranchTransfer.CompletedAtUtc.HasValue &&
                        ti.BranchTransfer.CompletedAtUtc.Value >= fromUtc &&
                        ti.BranchTransfer.CompletedAtUtc.Value < toUtc &&
                        (productId == null || ti.ProductId == productId) &&
                        (branchId == null ||
                            ti.BranchTransfer.FromBranchId == branchId ||
                            ti.BranchTransfer.ToBranchId == branchId));

                if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                    transferQuery = transferQuery.Where(ti =>
                        ti.BranchTransfer!.TenantId == tenantId ||
                        ti.BranchTransfer!.TenantId == null);

                var completedTransfers = await transferQuery.ToListAsync();

                var filteredTransfers = string.IsNullOrWhiteSpace(lowerSearch)
                    ? completedTransfers
                    : completedTransfers.Where(ti =>
                        (ti.Product?.ItemName.ToLower().Contains(lowerSearch) == true) ||
                        (ti.Product?.ItemCode.ToLower().Contains(lowerSearch) == true) ||
                        (ti.BranchTransfer?.TransferNumber.ToLower().Contains(lowerSearch) == true));

                foreach (var ti in filteredTransfers)
                {
                    var t    = ti.BranchTransfer!;
                    var date = t.CompletedAtUtc!.Value.ToLocalTime();

                    if (branchId == null || t.FromBranchId == branchId)
                    {
                        movements.Add(new InventoryMovementVm
                        {
                            Date         = date,
                            MovementType = "Transfer Out",
                            Branch       = t.FromBranch?.Name,
                            ProductId    = ti.ProductId,
                            ProductName  = ti.Product?.ItemName ?? "N/A",
                            ProductCode  = ti.Product?.ItemCode ?? "",
                            Unit         = ti.Product?.Unit?.ShortName ?? "",
                            QuantityIn   = null,
                            QuantityOut  = ti.Quantity,
                            Reference    = t.TransferNumber,
                            Notes        = $"To {t.ToBranch?.Name}"
                        });
                    }

                    if (branchId == null || t.ToBranchId == branchId)
                    {
                        movements.Add(new InventoryMovementVm
                        {
                            Date         = date,
                            MovementType = "Transfer In",
                            Branch       = t.ToBranch?.Name,
                            ProductId    = ti.ProductId,
                            ProductName  = ti.Product?.ItemName ?? "N/A",
                            ProductCode  = ti.Product?.ItemCode ?? "",
                            Unit         = ti.Product?.Unit?.ShortName ?? "",
                            QuantityIn   = ti.Quantity,
                            QuantityOut  = null,
                            Reference    = t.TransferNumber,
                            Notes        = $"From {t.FromBranch?.Name}"
                        });
                    }
                }
            }

            // ── Sort + paginate (in-memory, cross-source merge) ───────
            var sorted = movements.OrderByDescending(m => m.Date).ToList();
            var totalRecords = sorted.Count;

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var paged = sorted
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // ── ViewBags ─────────────────────────────────────────────
            ViewBag.MovementType = movementType;
            ViewBag.ProductId    = productId;
            ViewBag.BranchId     = branchId;
            ViewBag.DateFrom     = from;
            ViewBag.DateTo       = dateTo?.Date ?? DateTime.Today;

            ViewBag.AllBranches = _branchService.IsGlobalUser(User)
                ? allBranches
                : selectedBranch != null
                    ? new List<Branch> { selectedBranch }
                    : new List<Branch>();

            var itemsQuery = _context.Items
                .AsNoTracking()
                .Where(i => i.Status == AppStatuses.Active);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                itemsQuery = itemsQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);

            ViewBag.AllProducts = await itemsQuery
                .OrderBy(i => i.ItemName)
                .Select(i => new { i.Id, i.ItemName, i.ItemCode })
                .ToListAsync();

            return View(new PagedResult<InventoryMovementVm>
            {
                Items        = paged,
                PageNumber   = pageNumber,
                PageSize     = pageSize,
                TotalRecords = totalRecords,
                SearchTerm   = searchTerm
            });
        }
    }
}
