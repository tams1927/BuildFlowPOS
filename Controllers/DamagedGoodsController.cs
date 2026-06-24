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
    [PermissionAuthorize("DamagedGoods", "View")]
    public class DamagedGoodsController : OperationalDbController
    {
        private readonly ApplicationDbContext _platformDb;
        private readonly AuditService _auditService;
        private readonly BranchService _branchService;
        private readonly DamagedStockService _damagedStockService;
        private readonly ItemUnitConversionService _unitConversionService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public DamagedGoodsController(
            ITenantOperationalContextProvider ctxProvider,
            ApplicationDbContext platformDb,
            AuditService auditService,
            BranchService branchService,
            DamagedStockService damagedStockService,
            ItemUnitConversionService unitConversionService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard)
            : base(ctxProvider)
        {
            _platformDb = platformDb;
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

            var query = _context.DamagedGoodsHeaders.AsNoTracking()
                .Include(h => h.Branch)
                .Include(h => h.Details).ThenInclude(d => d.Item)
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                query = query.Where(h => h.TenantId == tenantId || h.TenantId == null);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(h =>
                    h.DamageNumber.ToLower().Contains(term) ||
                    (h.Remarks != null && h.Remarks.ToLower().Contains(term)) ||
                    h.Details.Any(d => d.Item != null && d.Item.ItemName.ToLower().Contains(term)));
            }

            var total = await query.CountAsync();
            pageNumber = PagedResult<object>.ValidatePageNumber(pageNumber, (int)Math.Ceiling(total / (double)pageSize));

            var items = await query.OrderByDescending(h => h.DamageDate)
                .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();

            return View(new PagedResult<DamagedGoodsHeader>
            {
                Items = items, PageNumber = pageNumber, PageSize = pageSize,
                TotalRecords = total, SearchTerm = searchTerm
            });
        }

        [PermissionAuthorize("DamagedGoods", "Create")]
        public async Task<IActionResult> Create()
        {
            await LoadCreateViewBagsAsync();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("DamagedGoods", "Create")]
        public async Task<IActionResult> Create(
            int itemId, int unitId, decimal quantity, string reason, string? notes, int? branchId)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();
            if (itemId <= 0 || quantity <= 0 || string.IsNullOrWhiteSpace(reason))
            {
                TempData["ErrorMessage"] = "Item, quantity, and reason are required.";
                return RedirectToAction(nameof(Create));
            }

            var item = await ScopedItemQuery(itemId, tenantId).FirstOrDefaultAsync();
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
                var header = new DamagedGoodsHeader
                {
                    TenantId = tenantId,
                    BranchId = branch?.Id,
                    DamageNumber = await GenerateDamageNumberAsync(tenantId),
                    DamageDate = DateTime.Now,
                    Status = "Pending",
                    Remarks = notes,
                    CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                    CreatedAtUtc = DateTime.UtcNow
                };

                header.Details.Add(new DamagedGoodsDetail
                {
                    ItemId = itemId,
                    Quantity = quantity,
                    UnitId = unitId,
                    ConversionQuantity = factor,
                    BaseQuantity = baseQty,
                    Reason = reason,
                    Notes = notes
                });

                await _damagedStockService.MoveSellableToDamagedAsync(branch?.Id, itemId, baseQty);

                _context.DamagedGoodsHeaders.Add(header);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(User, "DamagedGoods", "DAMAGED_GOODS_CREATED",
                    $"Damage {header.DamageNumber}: {baseQty:0.###} base units of item #{itemId} ({reason})",
                    "DamagedGoodsHeader", header.Id.ToString(),
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                await tx.CommitAsync();
                TempData["SuccessMessage"] = $"Damaged goods record {header.DamageNumber} created.";
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
            var header = await _context.DamagedGoodsHeaders.AsNoTracking()
                .Include(h => h.Branch)
                .Include(h => h.Details).ThenInclude(d => d.Item).ThenInclude(i => i!.Unit)
                .Include(h => h.Details).ThenInclude(d => d.Unit)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null) return NotFound();
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue && header.TenantId.HasValue && header.TenantId != tenantId)
                return Forbid();

            ViewBag.LinkedReturn = await _context.SupplierReturnHeaders.AsNoTracking()
                .FirstOrDefaultAsync(r => r.LinkedDamagedGoodsId == id);

            return View(header);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("DamagedGoods", "Edit")]
        public async Task<IActionResult> Dispose(int id)
        {
            return await RunDetailActionAsync(id, "Disposed", async (header, detail) =>
            {
                await _damagedStockService.DisposeDamagedAsync(header.BranchId, detail.ItemId, detail.BaseQuantity);
                await _auditService.LogAsync(User, "DamagedGoods", "DAMAGED_GOODS_DISPOSED",
                    $"Disposed {detail.BaseQuantity:0.###} base units from {header.DamageNumber}",
                    "DamagedGoodsHeader", header.Id.ToString());
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("DamagedGoods", "Edit")]
        public async Task<IActionResult> Recover(int id, decimal? quantity)
        {
            return await RunDetailActionAsync(id, "Recovered", async (header, detail) =>
            {
                var qty = quantity ?? detail.BaseQuantity;
                if (qty <= 0 || qty > detail.BaseQuantity)
                    throw new InvalidOperationException("Invalid recover quantity.");
                await _damagedStockService.RecoverDamagedAsync(header.BranchId, detail.ItemId, qty);
                await _auditService.LogAsync(User, "DamagedGoods", "DAMAGED_GOODS_RECOVERED",
                    $"Recovered {qty:0.###} base units from {header.DamageNumber}",
                    "DamagedGoodsHeader", header.Id.ToString());
            }, quantity.HasValue && quantity < await GetDetailBaseQty(id) ? "Partially Recovered" : "Recovered");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("DamagedGoods", "Edit")]
        public async Task<IActionResult> ReturnToSupplier(int id)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();
            var header = await _context.DamagedGoodsHeaders
                .Include(h => h.Details).ThenInclude(d => d.Item)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null) return NotFound();
            if (header.Status != "Pending")
            {
                TempData["ErrorMessage"] = "Only pending damage records can be returned to supplier.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var detail = header.Details.FirstOrDefault();
            if (detail?.Item == null)
            {
                TempData["ErrorMessage"] = "Damage record has no line items.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var supplierId = detail.Item.SupplierId;
            if (!supplierId.HasValue)
            {
                TempData["ErrorMessage"] = "Item has no default supplier. Set supplier on product first.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var existing = await _context.SupplierReturnHeaders.AnyAsync(r => r.LinkedDamagedGoodsId == id);
            if (existing)
            {
                TempData["ErrorMessage"] = "Supplier return already exists for this damage record.";
                return RedirectToAction(nameof(Details), new { id });
            }

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var returnHeader = new SupplierReturnHeader
                {
                    TenantId = tenantId,
                    BranchId = header.BranchId,
                    SupplierId = supplierId.Value,
                    ReturnNumber = await GenerateReturnNumberAsync(tenantId),
                    ReturnDate = DateTime.Now,
                    Status = "Pending",
                    LinkedDamagedGoodsId = header.Id,
                    Remarks = $"From damage {header.DamageNumber}",
                    CreatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                    CreatedAtUtc = DateTime.UtcNow
                };

                returnHeader.Details.Add(new SupplierReturnDetail
                {
                    ItemId = detail.ItemId,
                    Quantity = detail.Quantity,
                    UnitId = detail.UnitId,
                    ConversionQuantity = detail.ConversionQuantity,
                    BaseQuantity = detail.BaseQuantity,
                    Reason = detail.Reason,
                    Notes = detail.Notes
                });

                header.Status = "ReturnedToSupplier";
                header.UpdatedAtUtc = DateTime.UtcNow;

                _context.SupplierReturnHeaders.Add(returnHeader);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(User, "DamagedGoods", "DAMAGED_GOODS_RETURNED_TO_SUPPLIER",
                    $"Damage {header.DamageNumber} linked to return {returnHeader.ReturnNumber}",
                    "SupplierReturnHeader", returnHeader.Id.ToString());

                await tx.CommitAsync();
                TempData["SuccessMessage"] = $"Supplier return {returnHeader.ReturnNumber} created.";
                return RedirectToAction("Details", "SupplierReturns", new { id = returnHeader.Id });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("DamagedGoods", "Edit")]
        public async Task<IActionResult> Cancel(int id)
        {
            return await RunDetailActionAsync(id, "Cancelled", async (header, detail) =>
            {
                await _damagedStockService.RecoverDamagedAsync(header.BranchId, detail.ItemId, detail.BaseQuantity);
                await _auditService.LogAsync(User, "DamagedGoods", "DAMAGED_GOODS_CANCELLED",
                    $"Cancelled {header.DamageNumber}; restored {detail.BaseQuantity:0.###} base units",
                    "DamagedGoodsHeader", header.Id.ToString());
            }, onlyIfStatus: "Pending");
        }

        private async Task<IActionResult> RunDetailActionAsync(
            int id, string newStatus, Func<DamagedGoodsHeader, DamagedGoodsDetail, Task> action,
            string? overrideStatus = null, string? onlyIfStatus = null)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();
            var header = await _context.DamagedGoodsHeaders
                .Include(h => h.Details)
                .FirstOrDefaultAsync(h => h.Id == id);

            if (header == null) return NotFound();
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue && header.TenantId.HasValue && header.TenantId != tenantId)
                return Forbid();

            if (onlyIfStatus != null && header.Status != onlyIfStatus)
            {
                TempData["ErrorMessage"] = $"Action not allowed for status {header.Status}.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (header.Status is "Cancelled" or "Disposed" or "Recovered" or "ReturnedToSupplier")
            {
                TempData["ErrorMessage"] = "This damage record is already finalized.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var detail = header.Details.FirstOrDefault();
            if (detail == null)
            {
                TempData["ErrorMessage"] = "No detail lines on damage record.";
                return RedirectToAction(nameof(Details), new { id });
            }

            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                await action(header, detail);
                header.Status = overrideStatus ?? newStatus;
                header.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                TempData["SuccessMessage"] = $"Damage record updated: {header.Status}.";
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
            var itemsQuery = _context.Items.AsNoTracking().Where(i => i.Status == "Active");
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                itemsQuery = itemsQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);

            ViewBag.Items = await itemsQuery.OrderBy(i => i.ItemName).ToListAsync();
            ViewBag.Units = await _context.Units.AsNoTracking()
                .Where(u => u.IsActive).OrderBy(u => u.UnitName).ToListAsync();
            ViewBag.Branches = await _branchService.GetAllActiveBranchesAsync();
            ViewBag.CurrentBranch = await _branchService.GetCurrentBranchAsync(User);
            ViewBag.Reasons = new[] { "Damaged", "Broken", "Rust", "Expired", "Lost", "Theft", "Other" };
        }

        private IQueryable<Item> ScopedItemQuery(int itemId, int? tenantId)
        {
            var q = _context.Items.Where(i => i.Id == itemId);
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                q = q.Where(i => i.TenantId == tenantId || i.TenantId == null);
            return q;
        }

        private async Task<Branch?> ResolveBranchAsync(int? branchId)
        {
            if (branchId.HasValue)
                return await _context.Branches.FirstOrDefaultAsync(b => b.Id == branchId.Value);
            return await _branchService.GetCurrentBranchAsync(User);
        }

        private async Task<decimal> GetDetailBaseQty(int damageId)
        {
            var d = await _context.DamagedGoodsDetails.AsNoTracking()
                .FirstOrDefaultAsync(x => x.DamagedGoodsHeaderId == damageId);
            return d?.BaseQuantity ?? 0;
        }

        private async Task<string> GenerateDamageNumberAsync(int? tenantId)
        {
            var prefix = $"DMG-{DateTime.Now:yyyyMMdd}-";
            var q = _context.DamagedGoodsHeaders.Where(h => h.DamageNumber.StartsWith(prefix));
            if (tenantId.HasValue) q = q.Where(h => h.TenantId == tenantId);
            return $"{prefix}{(await q.CountAsync() + 1):D5}";
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
