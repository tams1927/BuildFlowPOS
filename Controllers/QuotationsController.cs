using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.Pdf;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Quotations", "View")]
    public class QuotationsController : OperationalDbController
    {
        // Shared platform context — used ONLY for platform-owned reads (e.g. Tenants).
        private readonly ApplicationDbContext _platformDb;
        private readonly BranchService _branchService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;
        private readonly AuditService _auditService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly DocumentPdfService _pdfService;

        public QuotationsController(
            ITenantOperationalContextProvider ctxProvider,
            ApplicationDbContext platformDb,
            BranchService branchService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard,
            AuditService auditService,
            UserManager<ApplicationUser> userManager,
            DocumentPdfService pdfService)
            : base(ctxProvider)
        {
            _platformDb = platformDb;
            _branchService = branchService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
            _auditService = auditService;
            _userManager = userManager;
            _pdfService = pdfService;
        }

        // ─────────────────────────────────────────────────────────────
        // INDEX
        // ─────────────────────────────────────────────────────────────

        public async Task<IActionResult> Index(
            string? searchTerm = null,
            string? statusFilter = null,
            int pageNumber = 1,
            int pageSize = 20)
        {
            ViewData["Title"] = "Quotations";

            var tenantId      = await _tenantGuard.GetEffectiveTenantIdAsync();
            var currentBranch = await _branchService.GetCurrentBranchAsync(User);

            var query = _context.Quotations
                .AsNoTracking()
                .Include(q => q.Customer)
                .Include(q => q.Branch)
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                query = query.Where(q => q.TenantId == tenantId || q.TenantId == null);

            if (currentBranch != null)
                query = query.Where(q => q.BranchId == currentBranch.Id || q.BranchId == null);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(q =>
                    q.QuotationNo.ToLower().Contains(term) ||
                    (q.CustomerName != null && q.CustomerName.ToLower().Contains(term)) ||
                    (q.Customer != null && q.Customer.CustomerName.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(statusFilter))
                query = query.Where(q => q.Status == statusFilter);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(q => q.QuotationDate)
                .ThenByDescending(q => q.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.SearchTerm   = searchTerm;
            ViewBag.StatusFilter = statusFilter;
            ViewBag.PageNumber   = pageNumber;
            ViewBag.PageSize     = pageSize;
            ViewBag.Total        = total;
            ViewBag.TotalPages   = (int)Math.Ceiling(total / (double)pageSize);

            return View(items);
        }

        // ─────────────────────────────────────────────────────────────
        // CREATE
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewData["Title"] = "New Quotation";
            await LoadDropdownsAsync();
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();
            return View(new Quotation
            {
                QuotationDate = DateTime.Today,
                ValidUntil    = DateTime.Today.AddDays(7),
                Status        = "Draft",
                QuotationNo   = await GenerateQuotationNoAsync(tenantId)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Quotations", "Create")]
        public async Task<IActionResult> Create(
            Quotation quotation,
            int[] itemIds,
            string[] descriptions,
            decimal[] quantities,
            decimal[] unitPrices,
            decimal[] discountPercents)
        {
            ModelState.Remove(nameof(Quotation.Tenant));
            ModelState.Remove(nameof(Quotation.Branch));
            ModelState.Remove(nameof(Quotation.Customer));
            ModelState.Remove(nameof(Quotation.Items));

            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "New Quotation";
                await LoadDropdownsAsync();
                return View(quotation);
            }

            var tenantId      = await _tenantGuard.GetEffectiveTenantIdAsync();
            var currentBranch = await _branchService.GetCurrentBranchAsync(User);

            quotation.TenantId         = tenantId;
            quotation.BranchId         = currentBranch?.Id;
            quotation.CreatedByUserId  = _userManager.GetUserId(User);
            quotation.CreatedAtUtc     = DateTime.UtcNow;
            quotation.UpdatedAtUtc     = DateTime.UtcNow;

            // Build line items
            quotation.Items.Clear();
            decimal total = 0;
            for (int i = 0; i < descriptions.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(descriptions[i])) continue;
                var qty      = i < quantities.Length      ? quantities[i]      : 1m;
                var price    = i < unitPrices.Length      ? unitPrices[i]      : 0m;
                var disc     = i < discountPercents.Length? discountPercents[i]: 0m;
                var subtotal = qty * price * (1 - disc / 100m);
                total += subtotal;

                quotation.Items.Add(new QuotationItem
                {
                    ItemId          = i < itemIds.Length && itemIds[i] > 0 ? itemIds[i] : null,
                    Description     = descriptions[i].Trim(),
                    Quantity        = qty,
                    UnitPrice       = price,
                    DiscountPercent = disc,
                    Subtotal        = Math.Round(subtotal, 2),
                    SortOrder       = i
                });
            }
            quotation.TotalAmount = Math.Round(total, 2);

            _context.Quotations.Add(quotation);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(User, "Quotations", "Create",
                $"Quotation {quotation.QuotationNo} created — Total: {quotation.TotalAmount:N2}");

            TempData["SuccessMessage"] = $"Quotation {quotation.QuotationNo} created.";
            return RedirectToAction(nameof(Details), new { id = quotation.Id });
        }

        // ─────────────────────────────────────────────────────────────
        // DETAILS
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            ViewData["Title"] = "Quotation Details";

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var q = await _context.Quotations
                .AsNoTracking()
                .Include(x => x.Items).ThenInclude(i => i.Item).ThenInclude(i => i!.Unit)
                .Include(x => x.Customer)
                .Include(x => x.Branch)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (q == null) return NotFound();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue &&
                q.TenantId.HasValue && q.TenantId != tenantId)
                return Forbid();

            return View(q);
        }

        // ─────────────────────────────────────────────────────────────
        // PRINT / DOWNLOAD PDF
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Print(int id)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var q = await _context.Quotations
                .AsNoTracking()
                .Include(x => x.Items).ThenInclude(i => i.Item).ThenInclude(i => i!.Unit)
                .Include(x => x.Customer)
                .Include(x => x.Branch)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (q == null) return NotFound();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue &&
                q.TenantId.HasValue && q.TenantId != tenantId)
                return Forbid();

            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();
            var tenant   = tenantId.HasValue
                ? await _platformDb.Tenants.FindAsync(tenantId.Value)
                : null;

            ViewBag.PrintSettings = settings;
            ViewBag.PrintTenant   = tenant;
            ViewBag.PrintBranch   = q.Branch;

            return View(q);
        }

        [HttpGet]
        public async Task<IActionResult> DownloadPdf(int id)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var q = await _context.Quotations
                .AsNoTracking()
                .Include(x => x.Items).ThenInclude(i => i.Item).ThenInclude(i => i!.Unit)
                .Include(x => x.Customer)
                .Include(x => x.Branch)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (q == null) return NotFound();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue &&
                q.TenantId.HasValue && q.TenantId != tenantId)
                return Forbid();

            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();
            var tenant   = tenantId.HasValue
                ? await _platformDb.Tenants.FindAsync(tenantId.Value)
                : null;

            var bytes = _pdfService.GenerateQuotationPdf(q, settings, tenant, settings?.LogoPath);
            return File(bytes, "application/pdf", $"Quotation-{q.QuotationNo}.pdf");
        }

        // ─────────────────────────────────────────────────────────────
        // UPDATE STATUS
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Quotations", "Edit")]
        public async Task<IActionResult> UpdateStatus(int id, string status)
        {
            var q = await _context.Quotations.FindAsync(id);
            if (q == null) return NotFound();
            if (!await _tenantGuard.CanAccessAsync(q.TenantId)) return Forbid();

            q.Status        = status;
            q.UpdatedAtUtc  = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(User, "Quotations", "StatusUpdate",
                $"Quotation {q.QuotationNo} status changed to {status}");

            TempData["SuccessMessage"] = $"Quotation {q.QuotationNo} marked as {status}.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ─────────────────────────────────────────────────────────────
        // CONVERT TO SALE
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Quotations", "Edit")]
        public async Task<IActionResult> ConvertToSale(int id)
        {
            var q = await _context.Quotations
                .Include(x => x.Items)
                    .ThenInclude(i => i.Item)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (q == null) return NotFound();
            if (!await _tenantGuard.CanAccessAsync(q.TenantId)) return Forbid();

            if (q.Status != "Accepted")
            {
                TempData["ErrorMessage"] = "Only Accepted quotations can be converted to a sale.";
                return RedirectToAction(nameof(Details), new { id });
            }

            if (q.ConvertedToSaleId.HasValue)
            {
                TempData["ErrorMessage"] = "This quotation has already been converted to a sale.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Build line items — only quotation lines that have a linked Item record
            var saleDetails = new List<SalesDetail>();
            foreach (var qi in q.Items.Where(i => i.ItemId.HasValue && i.Item != null))
            {
                saleDetails.Add(new SalesDetail
                {
                    ItemId    = qi.ItemId!.Value,
                    Quantity  = qi.Quantity,
                    UnitPrice = qi.UnitPrice,
                    LineTotal = qi.Subtotal
                });
            }

            if (!saleDetails.Any())
            {
                TempData["ErrorMessage"] = "Quotation has no product-linked items. Please link items before converting.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var cashierName = (await _userManager.GetUserAsync(User))?.FullName ?? User.Identity?.Name ?? "System";
            var convTenantId = q.TenantId ?? await _tenantGuard.GetEffectiveTenantIdAsync();
            var salesNumber = await GenerateSalesNumberAsync(convTenantId);

            var salesHeader = new SalesHeader
            {
                SalesNumber    = salesNumber,
                SalesDate      = DateTime.Now,
                CustomerId     = q.CustomerId,
                CashierId      = _userManager.GetUserId(User),
                CashierName    = cashierName,
                SubTotal       = q.TotalAmount,
                DiscountAmount = 0,
                VatAmount      = 0,
                TotalAmount    = q.TotalAmount,
                AmountReceived = q.TotalAmount,
                ChangeAmount   = 0,
                PaymentMethod  = "Credit",
                ReferenceNumber = q.QuotationNo,
                Status         = "Completed",
                TenantId       = q.TenantId,
                BranchId       = q.BranchId,
                SalesDetails   = saleDetails
            };

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Deduct stock
                foreach (var detail in saleDetails)
                {
                    var item = await _context.Items.FindAsync(detail.ItemId);
                    if (item == null) continue;

                    if (q.BranchId.HasValue)
                    {
                        var bps = await _context.BranchProductStocks
                            .FirstOrDefaultAsync(s => s.BranchId == q.BranchId.Value &&
                                                      s.ProductId == detail.ItemId);
                        if (bps != null)
                            bps.Quantity = Math.Max(0, bps.Quantity - detail.Quantity);
                    }
                    else
                    {
                        item.CurrentStock = Math.Max(0, item.CurrentStock - detail.Quantity);
                    }
                }

                _context.SalesHeaders.Add(salesHeader);
                await _context.SaveChangesAsync();

                q.Status            = "Converted";
                q.ConvertedToSaleId = salesHeader.Id;
                q.UpdatedAtUtc      = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = "Conversion failed. Please try again.";
                return RedirectToAction(nameof(Details), new { id });
            }

            await _auditService.LogAsync(User, "Quotations", "ConvertToSale",
                $"Quotation {q.QuotationNo} converted to sale {salesNumber} — Total: {q.TotalAmount:N2}");

            TempData["SuccessMessage"] = $"Quotation {q.QuotationNo} converted to Sale {salesNumber}.";
            return RedirectToAction("Index", "Sales");
        }

        // ─────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────

        private async Task LoadDropdownsAsync()
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var customersQuery = _context.Customers.AsNoTracking().Where(c => c.IsActive);
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                customersQuery = customersQuery.Where(c => c.TenantId == tenantId || c.TenantId == null);

            ViewBag.Customers = await customersQuery.OrderBy(c => c.CustomerName).ToListAsync();

            var itemsQuery = _context.Items.AsNoTracking()
                .Include(i => i.Unit)
                .Where(i => i.Status == "Active");
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                itemsQuery = itemsQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);

            ViewBag.Items = await itemsQuery.OrderBy(i => i.ItemName).ToListAsync();
        }

        private async Task<string> GenerateSalesNumberAsync(int? tenantId)
        {
            var prefix = $"QCV-{DateTime.Now:yyyyMMdd}-";

            var query = _context.SalesHeaders.Where(s => s.SalesNumber.StartsWith(prefix));
            if (tenantId.HasValue)
                query = query.Where(s => s.TenantId == tenantId);

            var count = await query.CountAsync();
            return $"{prefix}{(count + 1):0000}";
        }

        private async Task<string> GenerateQuotationNoAsync(int? tenantId)
        {
            var prefix = $"QT-{DateTime.Today:yyyyMMdd}-";

            var query = _context.Quotations
                .AsNoTracking()
                .Where(q => q.QuotationNo.StartsWith(prefix));

            if (tenantId.HasValue)
                query = query.Where(q => q.TenantId == tenantId);

            var lastNo = await query
                .OrderByDescending(q => q.QuotationNo)
                .Select(q => q.QuotationNo)
                .FirstOrDefaultAsync();

            int seq = 1;
            if (lastNo != null && int.TryParse(lastNo.Replace(prefix, ""), out int parsed))
                seq = parsed + 1;

            return $"{prefix}{seq:D3}";
        }
    }
}
