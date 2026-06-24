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
    [PermissionAuthorize("SupplierReturns", "View")]
    public class SupplierReturnsController : OperationalDbController
    {
        private readonly AuditService _auditService;
        private readonly BranchService _branchService;
        private readonly DamagedStockService _damagedStockService;
        private readonly ItemUnitConversionService _unitConversionService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        private static readonly HashSet<string> DamagedReasons = new(StringComparer.OrdinalIgnoreCase)
        {
            "Damaged", "Broken", "Rust", "Expired"
        };

        public SupplierReturnsController(
            ITenantOperationalContextProvider ctxProvider,
            AuditService auditService,
            BranchService branchService,
            DamagedStockService damagedStockService,
            ItemUnitConversionService unitConversionService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard)
            : base(ctxProvider)
        {
            _auditService = auditService;
            _branchService = branchService;
            _damagedStockService = damagedStockService;
            _unitConversionService = unitConversionService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
        }

        public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = 10, string? searchTerm = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var query = _context.SupplierReturnHeaders.AsNoTracking()
                .Include(h => h.Supplier)
                .Include(h => h.Branch)
                .Include(h => h.Details).ThenInclude(d => d.Item)
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                query = query.Where(h => h.TenantId == tenantId || h.TenantId == null);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(h =>
                    h.ReturnNumber.ToLower().Contains(term) ||
                    (h.Supplier != null && h.Supplier.SupplierName.ToLower().Contains(term)));
            }

            var total = await query.CountAsync();
            pageNumber = PagedResult<object>.ValidatePageNumber(pageNumber, (int)Math.Ceiling(total / (double)pageSize));

            var items = await query.OrderByDescending(h => h.ReturnDate)
                .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

            return View(new PagedResult<SupplierReturnHeader>
            {
                Items = items, PageNumber = pageNumber, PageSize = pageSize,
                TotalRecords = total, SearchTerm = searchTerm
            });
        }

        [PermissionAuthorize("SupplierReturns", "Create")]
        public async Task<IActionResult> Create(int? damagedGoodsId)
        {
            await LoadCreateViewBagsAsync();
            ViewBag.DamagedGoodsId = damagedGoodsId;
            if (damagedGoodsId.HasValue)
            {
                ViewBag.LinkedDamage = await _context.DamagedGoodsHeaders.AsNoTracking()
                    .Include(h => h.Details).ThenInclude(d => d.Item)
                    .FirstOrDefaultAsync(h => h.Id == damagedGoodsId.Value);
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("SupplierReturns", "Create")]
        public async Task<IActionResult> Create(
            int supplierId, int itemId, int unitId, decimal quantity, string reason, string? notes,
            int? branchId, int? damagedGoodsId)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();
            if (supplierId <= 0 || itemId <= 0 || quantity <= 0 || string.IsNullOrWhiteSpace(reason))
            {
                TempData["ErrorMessage"] = "Supplier, item, quantity, and reason are required.";
                return RedirectToAction(nameof(Create));
            }

            if (damagedGoodsId.HasValue)
            {
                TempData["ErrorMessage"] = "Use Return to Supplier from the damage record to link returns.";
                return RedirectToAction(nameof(Create));
            }

            var item = await _context.Items.FirstOrDefaultAsync(i => i.Id == itemId);
            if (item == null)
            {
                TempData["ErrorMessage"] = "Item not found.";
                return RedirectToAction(nameof(Create));
            }

            var branch = await ResolveBranchAsync(branchId);
            var baseQty = await _unitConversionService.ToBaseQuantityAsync(itemId, unitId, quantity);
            var factor = await _unitConversionService.GetFactorAsync(itemId, unitId);

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var header = new SupplierReturnHeader
                {
                    TenantId = tenantId,
                    BranchId = branch?.Id,
                    SupplierId = supplierId,
                    ReturnNumber = await GenerateReturnNumberAsync(tenantId),
                    ReturnDate = DateTime.Now,
                    Status = "Pending",
                    Remarks = notes,
                    CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                    CreatedAtUtc = DateTime.UtcNow
                };

                header.Details.Add(new SupplierReturnDetail
                {
                    ItemId = itemId,
                    Quantity = quantity,
                    UnitId = unitId,
                    ConversionQuantity = factor,
                    BaseQuantity = baseQty,
                    Reason = reason,
                    Notes = notes
                });

                if (DamagedReasons.Contains(reason))
                    await _damagedStockService.MoveSellableToDamagedAsync(branch?.Id, itemId, baseQty);
                else
                    await _damagedStockService.DeductSellableAsync(branch?.Id, itemId, baseQty);

                _context.SupplierReturnHeaders.Add(header);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(User, "SupplierReturns", "SUPPLIER_RETURN_CREATED",
                    $"Return {header.ReturnNumber}: {baseQty:0.###} base units to supplier #{supplierId}",
                    "SupplierReturnHeader", header.Id.ToString());

                await tx.CommitAsync();
                TempData["SuccessMessage"] = $"Supplier return {header.ReturnNumber} created.";
                return RedirectToAction(nameof(Details), new { id = header.Id });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Create));
            }
        }

        public async Task<IActionResult> Details(int id)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();
            var header = await _context.SupplierReturnHeaders.AsNoTracking()
                .Include(h => h.Supplier)
                .Include(h => h.Branch)
                .Include(h => h.LinkedDamagedGoods)
                .Include(h => h.Details).ThenInclude(d => d.Item).ThenInclude(i => i!.Unit)
                .Include(h => h.Details).ThenInclude(d => d.Unit)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null) return NotFound();
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue && header.TenantId.HasValue && header.TenantId != tenantId)
                return Forbid();

            return View(header);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("SupplierReturns", "Edit")]
        public async Task<IActionResult> MarkSent(int id)
        {
            return await UpdateStatusAsync(id, "Sent", "SUPPLIER_RETURN_SENT", onlyFrom: "Pending");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("SupplierReturns", "Edit")]
        public async Task<IActionResult> MarkCredited(int id)
        {
            return await FinalizeReturnAsync(id, "Credited", "SUPPLIER_RETURN_CREDITED", addReplacementStock: false);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("SupplierReturns", "Edit")]
        public async Task<IActionResult> MarkReplaced(int id)
        {
            return await FinalizeReturnAsync(id, "Replaced", "SUPPLIER_RETURN_REPLACED", addReplacementStock: true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("SupplierReturns", "Edit")]
        public async Task<IActionResult> Cancel(int id)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();
            var header = await _context.SupplierReturnHeaders
                .Include(h => h.Details)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null) return NotFound();
            if (header.Status != "Pending")
            {
                TempData["ErrorMessage"] = "Only pending returns can be cancelled.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var detail = header.Details.FirstOrDefault();
            if (detail == null)
            {
                TempData["ErrorMessage"] = "Return has no lines.";
                return RedirectToAction(nameof(Details), new { id });
            }

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                if (!header.LinkedDamagedGoodsId.HasValue)
                {
                    if (DamagedReasons.Contains(detail.Reason))
                        await _damagedStockService.RecoverDamagedAsync(header.BranchId, detail.ItemId, detail.BaseQuantity);
                    else
                        await _damagedStockService.AddSellableAsync(header.BranchId, detail.ItemId, detail.BaseQuantity);
                }

                header.Status = "Cancelled";
                header.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(User, "SupplierReturns", "SUPPLIER_RETURN_CANCELLED",
                    $"Cancelled return {header.ReturnNumber}",
                    "SupplierReturnHeader", header.Id.ToString());

                await tx.CommitAsync();
                TempData["SuccessMessage"] = "Supplier return cancelled.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task<IActionResult> UpdateStatusAsync(int id, string newStatus, string auditAction, string onlyFrom)
        {
            var header = await _context.SupplierReturnHeaders.FirstOrDefaultAsync(h => h.Id == id);
            if (header == null) return NotFound();
            if (header.Status != onlyFrom)
            {
                TempData["ErrorMessage"] = $"Cannot mark as {newStatus} from status {header.Status}.";
                return RedirectToAction(nameof(Details), new { id });
            }

            header.Status = newStatus;
            header.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(User, "SupplierReturns", auditAction,
                $"Return {header.ReturnNumber} → {newStatus}",
                "SupplierReturnHeader", header.Id.ToString());

            TempData["SuccessMessage"] = $"Return marked as {newStatus}.";
            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task<IActionResult> FinalizeReturnAsync(
            int id, string status, string auditAction, bool addReplacementStock)
        {
            var header = await _context.SupplierReturnHeaders
                .Include(h => h.Details)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null) return NotFound();
            if (header.Status is "Closed" or "Cancelled")
            {
                TempData["ErrorMessage"] = "Return is already finalized.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var detail = header.Details.FirstOrDefault();
            if (detail == null)
            {
                TempData["ErrorMessage"] = "Return has no lines.";
                return RedirectToAction(nameof(Details), new { id });
            }

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                if (addReplacementStock)
                    await _damagedStockService.AddSellableAsync(header.BranchId, detail.ItemId, detail.BaseQuantity);

                if (header.LinkedDamagedGoodsId.HasValue)
                {
                    await _damagedStockService.DeductDamagedAsync(header.BranchId, detail.ItemId, detail.BaseQuantity);
                    var damage = await _context.DamagedGoodsHeaders
                        .FirstOrDefaultAsync(d => d.Id == header.LinkedDamagedGoodsId.Value);
                    if (damage != null)
                    {
                        damage.Status = status == "Replaced" ? "Recovered" : "Disposed";
                        damage.UpdatedAtUtc = DateTime.UtcNow;
                    }
                }
                else if (DamagedReasons.Contains(detail.Reason))
                {
                    await _damagedStockService.DeductDamagedAsync(header.BranchId, detail.ItemId, detail.BaseQuantity);
                }

                header.Status = status;
                header.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(User, "SupplierReturns", auditAction,
                    $"Return {header.ReturnNumber} finalized as {status}",
                    "SupplierReturnHeader", header.Id.ToString());

                await tx.CommitAsync();
                TempData["SuccessMessage"] = $"Supplier return marked as {status}.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task LoadCreateViewBagsAsync()
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();
            ViewBag.Suppliers = await _context.Suppliers.AsNoTracking()
                .Where(s => s.IsActive).OrderBy(s => s.SupplierName).ToListAsync();
            var itemsQuery = _context.Items.AsNoTracking().Where(i => i.Status == "Active");
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                itemsQuery = itemsQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);
            ViewBag.Items = await itemsQuery.OrderBy(i => i.ItemName).ToListAsync();
            ViewBag.Units = await _context.Units.AsNoTracking().Where(u => u.IsActive).ToListAsync();
            ViewBag.Branches = await _branchService.GetAllActiveBranchesAsync();
            ViewBag.CurrentBranch = await _branchService.GetCurrentBranchAsync(User);
            ViewBag.Reasons = new[] { "Damaged", "Broken", "Rust", "Expired", "WrongItem", "Other" };
        }

        private async Task<Branch?> ResolveBranchAsync(int? branchId)
        {
            if (branchId.HasValue)
                return await _context.Branches.FirstOrDefaultAsync(b => b.Id == branchId.Value);
            return await _branchService.GetCurrentBranchAsync(User);
        }

        private async Task<string> GenerateReturnNumberAsync(int? tenantId)
        {
            var prefix = $"SR-{DateTime.Now:yyyyMMdd}-";
            var q = _context.SupplierReturnHeaders.Where(h => h.ReturnNumber.StartsWith(prefix));
            if (tenantId.HasValue) q = q.Where(h => h.TenantId == tenantId);
            return $"{prefix}{(await q.CountAsync() + 1):D5}";
        }
    }
}
