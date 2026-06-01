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
    [PermissionAuthorize("PurchaseOrders", "View")]
    public class PurchaseOrdersController : OperationalDbController
    {
        private readonly AuditService _auditService;
        private readonly BranchService _branchService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public PurchaseOrdersController(
            ITenantOperationalContextProvider ctxProvider,
            AuditService auditService,
            BranchService branchService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard)
            : base(ctxProvider)
        {
            _auditService = auditService;
            _branchService = branchService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
        }

        // ─────────────────────────────────────────────────────────────
        // INDEX
        // ─────────────────────────────────────────────────────────────

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            string? statusFilter = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var query = _context.PurchaseOrders
                .AsNoTracking()
                .Include(po => po.Supplier)
                .Include(po => po.Branch)
                .Include(po => po.Items)
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                query = query.Where(po => po.TenantId == tenantId || po.TenantId == null);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(po =>
                    po.PONumber.ToLower().Contains(term) ||
                    (po.Supplier != null && po.Supplier.SupplierName.ToLower().Contains(term)) ||
                    (po.Notes != null && po.Notes.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(statusFilter))
                query = query.Where(po => po.Status == statusFilter);

            var total = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber, (int)Math.Ceiling(total / (double)pageSize));

            var items = await query
                .OrderByDescending(po => po.PODate)
                .ThenByDescending(po => po.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.StatusFilter = statusFilter;

            // KPIs
            var kpiQuery = _context.PurchaseOrders.AsNoTracking();
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                kpiQuery = kpiQuery.Where(po => po.TenantId == tenantId || po.TenantId == null);

            ViewBag.DraftCount             = await kpiQuery.CountAsync(po => po.Status == "Draft");
            ViewBag.SentCount              = await kpiQuery.CountAsync(po => po.Status == "Sent");
            ViewBag.PartiallyReceivedCount = await kpiQuery.CountAsync(po => po.Status == "PartiallyReceived");
            ViewBag.ReceivedCount          = await kpiQuery.CountAsync(po => po.Status == "Received");
            ViewBag.CancelledCount         = await kpiQuery.CountAsync(po => po.Status == "Cancelled");

            // Dropdowns for create modal
            await LoadDropdowns(tenantId);

            return View(new PagedResult<PurchaseOrder>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = total,
                SearchTerm = searchTerm
            });
        }

        // ─────────────────────────────────────────────────────────────
        // DETAILS
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var po = await _context.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Supplier)
                .Include(p => p.Branch)
                .Include(p => p.Tenant)
                .Include(p => p.Items)
                    .ThenInclude(i => i.Item)
                        .ThenInclude(i => i!.Unit)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (po == null) return NotFound();

            if (!await _tenantGuard.CanAccessTenantAsync(po.TenantId))
                return Forbid();

            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();

            ViewBag.Settings = settings;

            return View(po);
        }

        // ─────────────────────────────────────────────────────────────
        // CREATE
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        [PermissionAuthorize("PurchaseOrders", "Create")]
        public async Task<IActionResult> Create()
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();
            await LoadDropdowns(tenantId);
            return View(new PurchaseOrder { PODate = DateTime.Today });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("PurchaseOrders", "Create")]
        public async Task<IActionResult> Create(
            int supplierId,
            DateTime poDate,
            DateTime? expectedDeliveryDate,
            string? notes,
            int? branchId,
            // line items — parallel arrays
            int[] itemIds,
            decimal[] quantities,
            decimal[] unitCosts)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (supplierId <= 0)
            {
                TempData["ErrorMessage"] = "Please select a supplier.";
                return RedirectToAction(nameof(Index));
            }

            if (itemIds == null || itemIds.Length == 0)
            {
                TempData["ErrorMessage"] = "Please add at least one item to the purchase order.";
                return RedirectToAction(nameof(Index));
            }

            var supplier = await _context.Suppliers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == supplierId && s.IsActive);
            if (supplier == null)
            {
                TempData["ErrorMessage"] = "Selected supplier not found or inactive.";
                return RedirectToAction(nameof(Index));
            }
            if (!await _tenantGuard.CanAccessTenantAsync(supplier.TenantId))
                return Forbid();

            int? resolvedBranchId = await ResolveBranchIdAsync(branchId);

            var poNumber = await GeneratePONumberAsync(tenantId);

            var po = new PurchaseOrder
            {
                TenantId             = tenantId,
                BranchId             = resolvedBranchId,
                SupplierId           = supplierId,
                PONumber             = poNumber,
                PODate               = poDate == default ? DateTime.Today : poDate,
                ExpectedDeliveryDate = expectedDeliveryDate,
                Status               = "Draft",
                Notes                = notes?.Trim(),
                CreatedBy            = User.Identity?.Name,
                CreatedAtUtc         = DateTime.UtcNow
            };

            // Validate each item belongs to the current tenant (prevents form-tamper IDOR)
            var validCreateIds = await _context.Items
                .Where(i => itemIds.Contains(i.Id) && (i.TenantId == tenantId || i.TenantId == null))
                .Select(i => i.Id)
                .ToHashSetAsync();

            for (int i = 0; i < itemIds.Length; i++)
            {
                if (itemIds[i] <= 0 || quantities[i] <= 0) continue;
                if (!validCreateIds.Contains(itemIds[i])) continue;

                var unitCost  = i < unitCosts.Length ? unitCosts[i] : 0;
                var totalCost = quantities[i] * unitCost;

                po.Items.Add(new PurchaseOrderItem
                {
                    ItemId    = itemIds[i],
                    Quantity  = quantities[i],
                    UnitCost  = unitCost,
                    TotalCost = totalCost
                });
            }

            if (!po.Items.Any())
            {
                TempData["ErrorMessage"] = "Please add at least one valid item.";
                return RedirectToAction(nameof(Index));
            }

            _context.PurchaseOrders.Add(po);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User, "PurchaseOrders", "PURCHASE_ORDER_CREATED",
                $"PO created: {poNumber}, Supplier: {supplier.SupplierName}, Items: {po.Items.Count}",
                "PurchaseOrder", po.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Purchase Order {poNumber} created successfully.";
            return RedirectToAction(nameof(Details), new { id = po.Id });
        }

        // ─────────────────────────────────────────────────────────────
        // EDIT
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        [PermissionAuthorize("PurchaseOrders", "Edit")]
        public async Task<IActionResult> Edit(int id)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var po = await _context.PurchaseOrders
                .Include(p => p.Items).ThenInclude(i => i.Item).ThenInclude(i => i!.Unit)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (po == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(po.TenantId)) return Forbid();

            if (!po.IsEditable)
            {
                TempData["ErrorMessage"] = $"Purchase Order {po.PONumber} cannot be edited — status is {po.Status}.";
                return RedirectToAction(nameof(Details), new { id });
            }

            await LoadDropdowns(tenantId);
            return View(po);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("PurchaseOrders", "Edit")]
        public async Task<IActionResult> Edit(
            int id,
            int supplierId,
            DateTime poDate,
            DateTime? expectedDeliveryDate,
            string? notes,
            int? branchId,
            int[] itemIds,
            decimal[] quantities,
            decimal[] unitCosts)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var po = await _context.PurchaseOrders
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (po == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(po.TenantId)) return Forbid();

            if (!po.IsEditable)
            {
                TempData["ErrorMessage"] = $"Purchase Order {po.PONumber} cannot be edited.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var supplier = await _context.Suppliers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == supplierId && s.IsActive);
            if (supplier == null)
            {
                TempData["ErrorMessage"] = "Supplier not found.";
                return RedirectToAction(nameof(Edit), new { id });
            }
            if (!await _tenantGuard.CanAccessTenantAsync(supplier.TenantId)) return Forbid();

            int? resolvedBranchId = await ResolveBranchIdAsync(branchId);

            po.SupplierId           = supplierId;
            po.PODate               = poDate == default ? DateTime.Today : poDate;
            po.ExpectedDeliveryDate = expectedDeliveryDate;
            po.Notes                = notes?.Trim();
            po.BranchId             = resolvedBranchId;
            po.UpdatedAtUtc         = DateTime.UtcNow;

            // Replace line items
            _context.PurchaseOrderItems.RemoveRange(po.Items);

            po.Items.Clear();

            // Validate each item belongs to the current tenant (prevents form-tamper IDOR)
            var validEditIds = await _context.Items
                .Where(i => itemIds.Contains(i.Id) && (i.TenantId == tenantId || i.TenantId == null))
                .Select(i => i.Id)
                .ToHashSetAsync();

            for (int i = 0; i < itemIds.Length; i++)
            {
                if (itemIds[i] <= 0 || quantities[i] <= 0) continue;
                if (!validEditIds.Contains(itemIds[i])) continue;

                var unitCost  = i < unitCosts.Length ? unitCosts[i] : 0;
                var totalCost = quantities[i] * unitCost;

                po.Items.Add(new PurchaseOrderItem
                {
                    ItemId    = itemIds[i],
                    Quantity  = quantities[i],
                    UnitCost  = unitCost,
                    TotalCost = totalCost
                });
            }

            if (!po.Items.Any())
            {
                TempData["ErrorMessage"] = "Please add at least one valid item.";
                await LoadDropdowns(tenantId);
                return View(po);
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User, "PurchaseOrders", "PURCHASE_ORDER_UPDATED",
                $"PO updated: {po.PONumber}",
                "PurchaseOrder", po.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Purchase Order {po.PONumber} updated.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ─────────────────────────────────────────────────────────────
        // SEND PO
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("PurchaseOrders", "Edit")]
        public async Task<IActionResult> SendPO(int id)
        {
            var po = await _context.PurchaseOrders.FindAsync(id);
            if (po == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(po.TenantId)) return Forbid();

            if (po.Status != "Draft")
            {
                TempData["ErrorMessage"] = $"Cannot send PO — current status is {po.Status}.";
                return RedirectToAction(nameof(Details), new { id });
            }

            po.Status        = "Sent";
            po.UpdatedAtUtc  = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User, "PurchaseOrders", "PURCHASE_ORDER_SENT",
                $"PO sent to supplier: {po.PONumber}",
                "PurchaseOrder", po.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Purchase Order {po.PONumber} sent to supplier.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ─────────────────────────────────────────────────────────────
        // CANCEL PO
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("PurchaseOrders", "Delete")]
        public async Task<IActionResult> CancelPO(int id)
        {
            var po = await _context.PurchaseOrders.FindAsync(id);
            if (po == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(po.TenantId)) return Forbid();

            if (!po.CanBeCancelled)
            {
                TempData["ErrorMessage"] = $"Cannot cancel PO in {po.Status} status.";
                return RedirectToAction(nameof(Details), new { id });
            }

            po.Status       = "Cancelled";
            po.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User, "PurchaseOrders", "PURCHASE_ORDER_CANCELLED",
                $"PO cancelled: {po.PONumber}",
                "PurchaseOrder", po.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Purchase Order {po.PONumber} has been cancelled.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ─────────────────────────────────────────────────────────────
        // RECEIVE (GET — show receive form)
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        [PermissionAuthorize("PurchaseOrders", "Edit")]
        public async Task<IActionResult> Receive(int id)
        {
            var po = await _context.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Supplier)
                .Include(p => p.Branch)
                .Include(p => p.Items)
                    .ThenInclude(i => i.Item)
                        .ThenInclude(i => i!.Unit)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (po == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(po.TenantId)) return Forbid();

            if (!po.CanBeReceived)
            {
                TempData["ErrorMessage"] = $"Cannot receive PO in {po.Status} status. PO must be Sent or PartiallyReceived.";
                return RedirectToAction(nameof(Details), new { id });
            }

            return View(po);
        }

        // ─────────────────────────────────────────────────────────────
        // RECEIVE (POST — process receipt + create Stock-In)
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("PurchaseOrders", "Edit")]
        public async Task<IActionResult> Receive(
            int id,
            int[] poItemIds,
            decimal[] receivedQtys)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var po = await _context.PurchaseOrders
                .Include(p => p.Supplier)
                .Include(p => p.Items)
                    .ThenInclude(i => i.Item)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (po == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(po.TenantId)) return Forbid();

            if (!po.CanBeReceived)
            {
                TempData["ErrorMessage"] = $"Cannot receive PO in {po.Status} status.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Validate at least one quantity > 0
            var hasAnyQty = receivedQtys.Any(q => q > 0);
            if (!hasAnyQty)
            {
                TempData["ErrorMessage"] = "Please enter at least one received quantity.";
                return RedirectToAction(nameof(Receive), new { id });
            }

            // Build stock-in header from the PO
            var stockInNumber = await GenerateStockInNumberAsync(tenantId);

            var stockInHeader = new StockInHeader
            {
                TenantId      = tenantId,
                BranchId      = po.BranchId,
                SupplierId    = po.SupplierId,
                StockInNumber = stockInNumber,
                DateReceived  = DateTime.Now,
                Remarks       = $"Received from PO: {po.PONumber}",
                TotalCost     = 0,
                AmountPaid    = 0,
                BalanceDue    = 0,
                PaymentStatus = "Unpaid",
                CreatedAt     = DateTime.Now
            };

            decimal totalCost = 0;

            for (int i = 0; i < poItemIds.Length; i++)
            {
                if (i >= receivedQtys.Length || receivedQtys[i] <= 0) continue;

                var poItem = po.Items.FirstOrDefault(pi => pi.Id == poItemIds[i]);
                if (poItem == null) continue;

                var remaining = poItem.Quantity - poItem.QuantityReceived;
                var receiveNow = Math.Min(receivedQtys[i], remaining);
                if (receiveNow <= 0) continue;

                poItem.QuantityReceived += receiveNow;

                var lineCost = receiveNow * poItem.UnitCost;
                totalCost += lineCost;

                // Update item stock
                var item = poItem.Item;
                if (item != null)
                {
                    if (po.BranchId.HasValue)
                    {
                        await _branchService.AddStockAsync(po.BranchId.Value, item.Id, receiveNow);
                    }
                    else
                    {
                        item.CurrentStock += receiveNow;
                    }

                    if (poItem.UnitCost > 0 && item.CostPrice != poItem.UnitCost)
                        item.CostPrice = poItem.UnitCost;
                }

                stockInHeader.StockInDetails.Add(new StockInDetail
                {
                    ItemId    = poItem.ItemId,
                    Quantity  = receiveNow,
                    UnitCost  = poItem.UnitCost,
                    TotalCost = lineCost
                });
            }

            if (!stockInHeader.StockInDetails.Any())
            {
                TempData["ErrorMessage"] = "No valid quantities to receive.";
                return RedirectToAction(nameof(Receive), new { id });
            }

            stockInHeader.TotalCost  = totalCost;
            stockInHeader.BalanceDue = totalCost;

            _context.StockInHeaders.Add(stockInHeader);

            // Determine PO final status
            bool allReceived = po.Items.All(pi => pi.QuantityReceived >= pi.Quantity);
            po.Status       = allReceived ? "Received" : "PartiallyReceived";
            po.UpdatedAtUtc = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var auditEvent = allReceived ? "PURCHASE_ORDER_RECEIVED" : "PURCHASE_ORDER_PARTIAL_RECEIVED";

            await _auditService.LogAsync(
                User, "PurchaseOrders", auditEvent,
                $"PO {po.PONumber} received. Stock-In: {stockInNumber}. Status: {po.Status}",
                "PurchaseOrder", po.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = allReceived
                ? $"Purchase Order {po.PONumber} fully received. Stock-In {stockInNumber} created."
                : $"Partial receipt recorded for {po.PONumber}. Stock-In {stockInNumber} created.";

            return RedirectToAction(nameof(Details), new { id });
        }

        // ─────────────────────────────────────────────────────────────
        // GENERATE PO FROM LOW-STOCK ITEMS
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        [PermissionAuthorize("PurchaseOrders", "Create")]
        public async Task<IActionResult> GenerateFromLowStock()
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var itemsQuery = _context.Items
                .AsNoTracking()
                .Include(i => i.Unit)
                .Include(i => i.Supplier)
                .Where(i => i.Status == "Active" && i.CurrentStock <= i.ReorderLevel);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                itemsQuery = itemsQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);

            var lowStockItems = await itemsQuery.OrderBy(i => i.ItemName).ToListAsync();

            await LoadDropdowns(tenantId);

            ViewBag.LowStockItems = lowStockItems;
            return View("Create", new PurchaseOrder { PODate = DateTime.Today });
        }

        // ─────────────────────────────────────────────────────────────
        // PRINT VIEW
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        [PermissionAuthorize("PurchaseOrders", "Print")]
        public async Task<IActionResult> Print(int id)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var po = await _context.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Supplier)
                .Include(p => p.Branch)
                .Include(p => p.Tenant)
                .Include(p => p.Items)
                    .ThenInclude(i => i.Item)
                        .ThenInclude(i => i!.Unit)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (po == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(po.TenantId)) return Forbid();

            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();

            ViewBag.PrintSettings   = settings;
            ViewBag.PrintTenant     = po.Tenant;
            ViewBag.PrintBranch     = po.Branch;
            ViewBag.PrintDocTitle   = "PURCHASE ORDER";
            ViewBag.PrintDocNumber  = po.PONumber;
            ViewBag.PrintDocDate    = po.PODate.ToString("MMMM dd, yyyy");

            return View(po);
        }

        // ─────────────────────────────────────────────────────────────
        // DOWNLOAD PDF
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        [PermissionAuthorize("PurchaseOrders", "Print")]
        public async Task<IActionResult> DownloadPdf(int id)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var po = await _context.PurchaseOrders
                .AsNoTracking()
                .Include(p => p.Supplier)
                .Include(p => p.Branch)
                .Include(p => p.Tenant)
                .Include(p => p.Items)
                    .ThenInclude(i => i.Item)
                        .ThenInclude(i => i!.Unit)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (po == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(po.TenantId)) return Forbid();

            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();

            var pdfService = HttpContext.RequestServices.GetRequiredService<Services.Pdf.DocumentPdfService>();
            var bytes = pdfService.GeneratePurchaseOrderPdf(po, settings, po.Tenant, settings?.LogoPath);

            return File(bytes, "application/pdf", $"PO-{po.PONumber}.pdf");
        }

        // ─────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────

        private async Task LoadDropdowns(int? tenantId)
        {
            var suppliersQuery = _context.Suppliers.AsNoTracking().Where(s => s.IsActive);
            var itemsQuery = _context.Items.AsNoTracking()
                .Include(i => i.Unit)
                .Where(i => i.Status == "Active");

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                suppliersQuery = suppliersQuery.Where(s => s.TenantId == tenantId || s.TenantId == null);
                itemsQuery     = itemsQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);
            }

            ViewBag.Suppliers = await suppliersQuery.OrderBy(s => s.SupplierName).ToListAsync();
            ViewBag.Items     = await itemsQuery.OrderBy(i => i.ItemName).ToListAsync();

            var currentBranch = await _branchService.GetCurrentBranchAsync(User);
            ViewBag.CurrentBranch = currentBranch;
            ViewBag.AllBranches   = _branchService.IsGlobalUser(User)
                ? await _branchService.GetAllActiveBranchesAsync()
                : null;
        }

        private async Task<int?> ResolveBranchIdAsync(int? branchId)
        {
            if (branchId.HasValue && _branchService.IsGlobalUser(User))
            {
                var branch = await _context.Branches.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == branchId.Value && b.IsActive);
                if (branch != null && await _tenantGuard.CanAccessTenantAsync(branch.TenantId))
                    return branch.Id;
                return null;
            }

            var currentBranch = await _branchService.GetCurrentBranchAsync(User);
            return currentBranch?.Id;
        }

        private async Task<string> GeneratePONumberAsync(int? tenantId)
        {
            var prefix = $"PO-{DateTime.Now:yyyyMMdd}-";
            var query  = _context.PurchaseOrders.Where(po => po.PONumber.StartsWith(prefix));
            if (tenantId.HasValue)
                query = query.Where(po => po.TenantId == tenantId);
            var count = await query.CountAsync();
            return $"{prefix}{(count + 1):000000}";
        }

        private async Task<string> GenerateStockInNumberAsync(int? tenantId)
        {
            var prefix = $"SIN-{DateTime.Now:yyyyMMdd}-";
            var query  = _context.StockInHeaders.Where(h => h.StockInNumber.StartsWith(prefix));
            if (tenantId.HasValue)
                query = query.Where(h => h.TenantId == tenantId);
            var count = await query.CountAsync();
            return $"{prefix}{(count + 1):0000}";
        }
    }
}
