using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("BranchTransfers", "View")]
    public class BranchTransfersController : OperationalDbController
    {
        // Shared platform context — used ONLY for platform-owned reads (e.g. Tenants).
        private readonly ApplicationDbContext _platformDb;
        private readonly AuditService _auditService;
        private readonly BranchService _branchService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public BranchTransfersController(
            ITenantOperationalContextProvider ctxProvider,
            ApplicationDbContext platformDb,
            AuditService auditService,
            BranchService branchService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard)
            : base(ctxProvider)
        {
            _platformDb = platformDb;
            _auditService = auditService;
            _branchService = branchService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
        }

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            string? statusFilter = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var query = _context.BranchTransfers
                .AsNoTracking()
                .Include(t => t.FromBranch)
                .Include(t => t.ToBranch)
                .Include(t => t.BranchTransferItems)
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(t => t.TenantId == tenantId || t.TenantId == null);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(t =>
                    t.TransferNumber.ToLower().Contains(term) ||
                    (t.FromBranch != null && t.FromBranch.Name.ToLower().Contains(term)) ||
                    (t.ToBranch != null && t.ToBranch.Name.ToLower().Contains(term)) ||
                    (t.CreatedByUserName != null && t.CreatedByUserName.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "all")
            {
                query = query.Where(t => t.Status == statusFilter);
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var transfers = await query
                .OrderByDescending(t => t.CreatedAtUtc)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.StatusFilter = statusFilter;

            return View(new PagedResult<BranchTransfer>
            {
                Items = transfers,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        [PermissionAuthorize("BranchTransfers", "Create")]
        public async Task<IActionResult> Create()
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var branches = await _branchService.GetAllActiveBranchesAsync();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                branches = branches
                    .Where(b => b.TenantId == tenantId || b.TenantId == null)
                    .ToList();
            }

            if (branches.Count < 2)
            {
                TempData["ErrorMessage"] = "At least two active branches are required to create a transfer.";
                return RedirectToAction(nameof(Index));
            }

            var itemsQuery = _context.Items
                .AsNoTracking()
                .Where(i => i.Status == "Active");

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                itemsQuery = itemsQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);
            }

            ViewBag.Branches = branches;
            ViewBag.CurrentBranch = await _branchService.GetCurrentBranchAsync(User);

            ViewBag.Items = await itemsQuery
                .OrderBy(i => i.ItemName)
                .ToListAsync();

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("BranchTransfers", "Create")]
        public async Task<IActionResult> Create(
            int fromBranchId,
            int toBranchId,
            string? notes,
            List<int> productIds,
            List<decimal> quantities)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (fromBranchId == toBranchId)
            {
                TempData["ErrorMessage"] = "Source and destination branches must be different.";
                return RedirectToAction(nameof(Create));
            }

            if (productIds == null || !productIds.Any() || productIds.All(p => p <= 0))
            {
                TempData["ErrorMessage"] = "At least one product is required.";
                return RedirectToAction(nameof(Create));
            }

            var fromBranch = await _context.Branches.FindAsync(fromBranchId);
            var toBranch = await _context.Branches.FindAsync(toBranchId);

            if (fromBranch == null || !fromBranch.IsActive ||
                toBranch == null || !toBranch.IsActive)
            {
                TempData["ErrorMessage"] = "One or both selected branches are invalid or inactive.";
                return RedirectToAction(nameof(Create));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(fromBranch.TenantId) ||
                !await _tenantGuard.CanAccessTenantAsync(toBranch.TenantId))
            {
                return Forbid();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var userName = User.Identity?.Name ?? "Unknown";

            var transferNumber = await GenerateTransferNumberAsync(tenantId);

            var transfer = new BranchTransfer
            {
                TenantId = tenantId,
                TransferNumber = transferNumber,
                FromBranchId = fromBranchId,
                ToBranchId = toBranchId,
                Status = "Pending",
                Notes = notes,
                CreatedByUserId = userId,
                CreatedByUserName = userName,
                CreatedAtUtc = DateTime.UtcNow
            };

            for (int i = 0; i < productIds.Count; i++)
            {
                var pid = productIds[i];
                var qty = i < quantities.Count ? quantities[i] : 0;

                if (pid <= 0 || qty <= 0)
                    continue;

                var item = await _context.Items
                    .FirstOrDefaultAsync(x => x.Id == pid && x.Status == "Active");

                if (item == null)
                    continue;

                if (!await _tenantGuard.CanAccessTenantAsync(item.TenantId))
                {
                    return Forbid();
                }

                var branchStock = await _branchService.GetBranchStockAsync(fromBranchId, pid);

                if (branchStock < qty)
                {
                    TempData["ErrorMessage"] =
                        $"Insufficient stock for '{item.ItemName}' in {fromBranch.Name}. Available: {branchStock:0.###}";

                    return RedirectToAction(nameof(Create));
                }

                if (!item.TenantId.HasValue && tenantId.HasValue)
                {
                    item.TenantId = tenantId;
                }

                transfer.BranchTransferItems.Add(new BranchTransferItem
                {
                    ProductId = pid,
                    Quantity = qty
                });
            }

            if (!transfer.BranchTransferItems.Any())
            {
                TempData["ErrorMessage"] = "No valid products were added to the transfer.";
                return RedirectToAction(nameof(Create));
            }

            _context.BranchTransfers.Add(transfer);

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "BranchTransfers",
                "CREATED",
                $"Transfer {transferNumber} created. From: {fromBranch.Name} -> {toBranch.Name}. Items: {transfer.BranchTransferItems.Count}",
                "BranchTransfer",
                transfer.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] =
                $"Transfer {transferNumber} created and is Pending approval.";

            return RedirectToAction(nameof(Details), new { id = transfer.Id });
        }

        public async Task<IActionResult> Details(int id)
        {
            var transfer = await _context.BranchTransfers
                .Include(t => t.FromBranch)
                .Include(t => t.ToBranch)
                .Include(t => t.BranchTransferItems)
                    .ThenInclude(ti => ti.Product)
                        .ThenInclude(p => p!.Unit)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (transfer == null)
            {
                return NotFound();
            }

            if (!await _tenantGuard.CanAccessTenantAsync(transfer.TenantId))
            {
                return Forbid();
            }

            var productIds = transfer.BranchTransferItems
                .Select(ti => ti.ProductId)
                .ToList();

            var stocksQuery = _context.BranchProductStocks
                .AsNoTracking()
                .Where(s =>
                    s.BranchId == transfer.FromBranchId &&
                    productIds.Contains(s.ProductId));

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                stocksQuery = stocksQuery.Where(s => s.TenantId == tenantId || s.TenantId == null);
            }

            var branchStocks = await stocksQuery
                .ToDictionaryAsync(s => s.ProductId, s => s.Quantity);

            ViewBag.BranchStocks = branchStocks;

            return View(transfer);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("BranchTransfers", "Approve")]
        public async Task<IActionResult> Approve(int id)
        {
            var transfer = await _context.BranchTransfers.FindAsync(id);

            if (transfer == null)
            {
                return NotFound();
            }

            if (!await _tenantGuard.CanAccessTenantAsync(transfer.TenantId))
            {
                return Forbid();
            }

            if (transfer.Status != "Pending")
            {
                TempData["ErrorMessage"] = "Only Pending transfers can be approved.";
                return RedirectToAction(nameof(Details), new { id });
            }

            transfer.Status = "Approved";
            transfer.ApprovedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "BranchTransfers",
                "APPROVED",
                $"Transfer {transfer.TransferNumber} approved.",
                "BranchTransfer",
                transfer.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] =
                $"Transfer {transfer.TransferNumber} approved.";

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("BranchTransfers", "Complete")]
        public async Task<IActionResult> Complete(int id)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var transfer = await _context.BranchTransfers
                    .Include(t => t.FromBranch)
                    .Include(t => t.ToBranch)
                    .Include(t => t.BranchTransferItems)
                        .ThenInclude(ti => ti.Product)
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (transfer == null)
                {
                    await transaction.RollbackAsync();
                    return NotFound();
                }

                if (!await _tenantGuard.CanAccessTenantAsync(transfer.TenantId))
                {
                    await transaction.RollbackAsync();
                    return Forbid();
                }

                if (transfer.Status != "Approved")
                {
                    await transaction.RollbackAsync();

                    var statusMessage = transfer.Status switch
                    {
                        "Completed" =>
                            "This transfer has already been completed.",
                        "Cancelled" =>
                            "Cancelled transfers cannot be completed.",
                        _ =>
                            "Only Approved transfers can be completed."
                    };

                    if (transfer.Status is "Completed" or "Cancelled")
                    {
                        await _auditService.LogAsync(
                            User,
                            "BranchTransfers",
                            "COMPLETE REJECTED",
                            $"Transfer {transfer.TransferNumber} completion rejected: status is {transfer.Status}.",
                            "BranchTransfer",
                            transfer.Id.ToString(),
                            HttpContext.Connection.RemoteIpAddress?.ToString());
                    }

                    TempData["ErrorMessage"] = statusMessage;
                    return RedirectToAction(nameof(Details), new { id });
                }

                if (transfer.FromBranch == null || transfer.ToBranch == null)
                {
                    await transaction.RollbackAsync();
                    TempData["ErrorMessage"] = "Transfer branch references are invalid.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                if (!await _tenantGuard.CanAccessTenantAsync(transfer.FromBranch.TenantId) ||
                    !await _tenantGuard.CanAccessTenantAsync(transfer.ToBranch.TenantId))
                {
                    await transaction.RollbackAsync();
                    return Forbid();
                }

                foreach (var item in transfer.BranchTransferItems)
                {
                    if (item.Product == null)
                    {
                        await transaction.RollbackAsync();
                        TempData["ErrorMessage"] = $"Product #{item.ProductId} was not found.";
                        return RedirectToAction(nameof(Details), new { id });
                    }

                    if (!await _tenantGuard.CanAccessTenantAsync(item.Product.TenantId))
                    {
                        await transaction.RollbackAsync();
                        return Forbid();
                    }
                }

                foreach (var item in transfer.BranchTransferItems)
                {
                    var srcStock = await _branchService
                        .GetBranchStockAsync(transfer.FromBranchId, item.ProductId);

                    if (srcStock < item.Quantity)
                    {
                        await transaction.RollbackAsync();

                        await _auditService.LogAsync(
                            User,
                            "BranchTransfers",
                            "COMPLETE FAILED",
                            $"Transfer {transfer.TransferNumber} failed: insufficient stock for '{item.Product?.ItemName}'. Available: {srcStock:0.###}, Required: {item.Quantity:0.###}.",
                            "BranchTransfer",
                            transfer.Id.ToString(),
                            HttpContext.Connection.RemoteIpAddress?.ToString());

                        TempData["ErrorMessage"] =
                            $"Insufficient stock for '{item.Product?.ItemName}' in source branch. Available: {srcStock:0.###}";

                        return RedirectToAction(nameof(Details), new { id });
                    }
                }

                foreach (var item in transfer.BranchTransferItems)
                {
                    await _branchService.DeductStockAsync(
                        transfer.FromBranchId,
                        item.ProductId,
                        item.Quantity);

                    await _branchService.AddStockAsync(
                        transfer.ToBranchId,
                        item.ProductId,
                        item.Quantity);
                }

                transfer.Status = "Completed";
                transfer.CompletedAtUtc = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                await _auditService.LogAsync(
                    User,
                    "BranchTransfers",
                    "COMPLETED",
                    $"Transfer {transfer.TransferNumber} completed. Stock moved from branch #{transfer.FromBranchId} to #{transfer.ToBranchId}.",
                    "BranchTransfer",
                    transfer.Id.ToString(),
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["SuccessMessage"] =
                    $"Transfer {transfer.TransferNumber} completed. Stock has been moved.";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                await _auditService.LogAsync(
                    User,
                    "BranchTransfers",
                    "COMPLETE FAILED",
                    $"Transfer #{id} could not be completed: {ex.Message}",
                    "BranchTransfer",
                    id.ToString(),
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["ErrorMessage"] =
                    $"Transfer could not be completed: {ex.Message}";
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("BranchTransfers", "Delete")]
        public async Task<IActionResult> Cancel(int id)
        {
            var transfer = await _context.BranchTransfers.FindAsync(id);

            if (transfer == null)
            {
                return NotFound();
            }

            if (!await _tenantGuard.CanAccessTenantAsync(transfer.TenantId))
            {
                return Forbid();
            }

            if (transfer.Status == "Completed" || transfer.Status == "Cancelled")
            {
                TempData["ErrorMessage"] =
                    $"Transfer is already {transfer.Status} and cannot be cancelled.";

                return RedirectToAction(nameof(Details), new { id });
            }

            transfer.Status = "Cancelled";

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "BranchTransfers",
                "CANCELLED",
                $"Transfer {transfer.TransferNumber} cancelled.",
                "BranchTransfer",
                transfer.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] =
                $"Transfer {transfer.TransferNumber} cancelled.";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> GetBranchStock(int branchId, int productId)
        {
            var branch = await _context.Branches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == branchId);

            if (branch == null)
            {
                return Json(new { quantity = 0 });
            }

            if (!await _tenantGuard.CanAccessTenantAsync(branch.TenantId))
            {
                return Forbid();
            }

            var qty = await _branchService.GetBranchStockAsync(branchId, productId);

            return Json(new { quantity = qty });
        }

        [HttpGet]
        [PermissionAuthorize("BranchTransfers", "View")]
        public async Task<IActionResult> PrintSlip(int id)
        {
            var transfer = await _context.BranchTransfers
                .AsNoTracking()
                .Include(t => t.FromBranch)
                .Include(t => t.ToBranch)
                .Include(t => t.BranchTransferItems)
                    .ThenInclude(ti => ti.Product)
                        .ThenInclude(p => p!.Unit)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (transfer == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(transfer.TenantId)) return Forbid();

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();
            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();
            var tenant   = tenantId.HasValue
                ? await _platformDb.Tenants.FindAsync(tenantId.Value) : null;

            ViewBag.PrintSettings = settings;
            ViewBag.PrintTenant   = tenant;

            return View(transfer);
        }

        private async Task<string> GenerateTransferNumberAsync(int? tenantId)
        {
            var prefix = $"TRF-{DateTime.UtcNow:yyyyMMdd}-";

            var query = _context.BranchTransfers.Where(t => t.TransferNumber.StartsWith(prefix));
            if (tenantId.HasValue)
                query = query.Where(t => t.TenantId == tenantId);

            var count = await query.CountAsync();
            return $"{prefix}{(count + 1):D4}";
        }
    }
}