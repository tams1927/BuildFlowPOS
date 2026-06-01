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
    [PermissionAuthorize("DeliveryReceipts", "View")]
    public class DeliveryReceiptsController : OperationalDbController
    {
        // Shared platform context — used ONLY for platform-owned reads (e.g. Tenants).
        private readonly ApplicationDbContext _platformDb;
        private readonly BranchService _branchService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;
        private readonly AuditService _auditService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly DocumentPdfService _pdfService;

        public DeliveryReceiptsController(
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
            ViewData["Title"] = "Delivery Receipts";

            var tenantId      = await _tenantGuard.GetEffectiveTenantIdAsync();
            var currentBranch = await _branchService.GetCurrentBranchAsync(User);

            var query = _context.DeliveryReceipts
                .AsNoTracking()
                .Include(dr => dr.Customer)
                .Include(dr => dr.Branch)
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                query = query.Where(dr => dr.TenantId == tenantId || dr.TenantId == null);

            if (currentBranch != null)
                query = query.Where(dr => dr.BranchId == currentBranch.Id || dr.BranchId == null);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(dr =>
                    dr.DRNumber.ToLower().Contains(term) ||
                    (dr.Customer != null && dr.Customer.CustomerName.ToLower().Contains(term)) ||
                    (dr.DriverName != null && dr.DriverName.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(statusFilter))
                query = query.Where(dr => dr.Status == statusFilter);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(dr => dr.DeliveryDate)
                .ThenByDescending(dr => dr.Id)
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
            ViewData["Title"] = "New Delivery Receipt";
            await LoadDropdownsAsync();
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();
            return View(new DeliveryReceipt
            {
                DeliveryDate = DateTime.Today,
                Status       = "Pending",
                DRNumber     = await GenerateDrNumberAsync(tenantId)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("DeliveryReceipts", "Create")]
        public async Task<IActionResult> Create(
            DeliveryReceipt dr,
            int[] itemIds,
            string[] itemDescs,
            decimal[] quantities,
            string[] units)
        {
            ModelState.Remove(nameof(DeliveryReceipt.Tenant));
            ModelState.Remove(nameof(DeliveryReceipt.Branch));
            ModelState.Remove(nameof(DeliveryReceipt.Customer));
            ModelState.Remove(nameof(DeliveryReceipt.SalesHeader));
            ModelState.Remove(nameof(DeliveryReceipt.Items));

            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "New Delivery Receipt";
                await LoadDropdownsAsync();
                return View(dr);
            }

            var tenantId      = await _tenantGuard.GetEffectiveTenantIdAsync();
            var currentBranch = await _branchService.GetCurrentBranchAsync(User);

            dr.TenantId        = tenantId;
            dr.BranchId        = currentBranch?.Id;
            dr.CreatedByUserId = _userManager.GetUserId(User);
            dr.CreatedAtUtc    = DateTime.UtcNow;

            dr.Items.Clear();
            for (int i = 0; i < itemDescs.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(itemDescs[i])) continue;
                dr.Items.Add(new DeliveryReceiptItem
                {
                    ItemId      = i < itemIds.Length && itemIds[i] > 0 ? itemIds[i] : null,
                    Description = itemDescs[i].Trim(),
                    Quantity    = i < quantities.Length ? quantities[i] : 1m,
                    Unit        = i < units.Length ? units[i] : null,
                    SortOrder   = i
                });
            }

            _context.DeliveryReceipts.Add(dr);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(User, "DeliveryReceipts", "Create",
                $"DR {dr.DRNumber} created for {(dr.CustomerId.HasValue ? $"customer #{dr.CustomerId}" : "walk-in")}");

            TempData["SuccessMessage"] = $"Delivery Receipt {dr.DRNumber} created.";
            return RedirectToAction(nameof(Details), new { id = dr.Id });
        }

        // ─────────────────────────────────────────────────────────────
        // DETAILS
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            ViewData["Title"] = "Delivery Receipt";

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var dr = await _context.DeliveryReceipts
                .AsNoTracking()
                .Include(x => x.Items).ThenInclude(i => i.Item).ThenInclude(i => i!.Unit)
                .Include(x => x.Customer)
                .Include(x => x.Branch)
                .Include(x => x.SalesHeader)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (dr == null) return NotFound();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue &&
                dr.TenantId.HasValue && dr.TenantId != tenantId)
                return Forbid();

            return View(dr);
        }

        // ─────────────────────────────────────────────────────────────
        // PRINT / DOWNLOAD PDF
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        public async Task<IActionResult> Print(int id)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var dr = await _context.DeliveryReceipts
                .AsNoTracking()
                .Include(x => x.Items).ThenInclude(i => i.Item).ThenInclude(i => i!.Unit)
                .Include(x => x.Customer)
                .Include(x => x.Branch)
                .Include(x => x.SalesHeader)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (dr == null) return NotFound();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue &&
                dr.TenantId.HasValue && dr.TenantId != tenantId)
                return Forbid();

            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();
            var tenant   = tenantId.HasValue
                ? await _platformDb.Tenants.FindAsync(tenantId.Value)
                : null;

            ViewBag.PrintSettings = settings;
            ViewBag.PrintTenant   = tenant;
            ViewBag.PrintBranch   = dr.Branch;

            return View(dr);
        }

        [HttpGet]
        public async Task<IActionResult> DownloadPdf(int id)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var dr = await _context.DeliveryReceipts
                .AsNoTracking()
                .Include(x => x.Items).ThenInclude(i => i.Item).ThenInclude(i => i!.Unit)
                .Include(x => x.Customer)
                .Include(x => x.Branch)
                .Include(x => x.SalesHeader)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (dr == null) return NotFound();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue &&
                dr.TenantId.HasValue && dr.TenantId != tenantId)
                return Forbid();

            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();
            var tenant   = tenantId.HasValue
                ? await _platformDb.Tenants.FindAsync(tenantId.Value)
                : null;

            var bytes = _pdfService.GenerateDeliveryReceiptPdf(dr, settings, tenant, settings?.LogoPath);
            return File(bytes, "application/pdf", $"DR-{dr.DRNumber}.pdf");
        }

        // ─────────────────────────────────────────────────────────────
        // MARK DELIVERED
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("DeliveryReceipts", "Edit")]
        public async Task<IActionResult> MarkDelivered(int id)
        {
            var dr = await _context.DeliveryReceipts.FindAsync(id);
            if (dr == null) return NotFound();
            if (!await _tenantGuard.CanAccessAsync(dr.TenantId)) return Forbid();

            dr.Status          = "Delivered";
            dr.DeliveredAtUtc  = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(User, "DeliveryReceipts", "MarkDelivered",
                $"DR {dr.DRNumber} marked as Delivered");

            TempData["SuccessMessage"] = $"DR {dr.DRNumber} marked as Delivered.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ─────────────────────────────────────────────────────────────
        // CANCEL
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("DeliveryReceipts", "Delete")]
        public async Task<IActionResult> Cancel(int id)
        {
            var dr = await _context.DeliveryReceipts.FindAsync(id);
            if (dr == null) return NotFound();
            if (!await _tenantGuard.CanAccessAsync(dr.TenantId)) return Forbid();

            dr.Status = "Cancelled";
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(User, "DeliveryReceipts", "Cancel",
                $"DR {dr.DRNumber} cancelled");

            TempData["SuccessMessage"] = $"DR {dr.DRNumber} cancelled.";
            return RedirectToAction(nameof(Details), new { id });
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

            var itemsQuery = _context.Items.AsNoTracking().Include(i => i.Unit)
                .Where(i => i.Status == "Active");
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                itemsQuery = itemsQuery.Where(i => i.TenantId == tenantId || i.TenantId == null);
            ViewBag.Items = await itemsQuery.OrderBy(i => i.ItemName).ToListAsync();

            var salesBaseQuery = _context.SalesHeaders.AsNoTracking()
                .Where(s => s.Status == "Completed");
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                salesBaseQuery = salesBaseQuery.Where(s => s.TenantId == tenantId || s.TenantId == null);
            ViewBag.RecentSales = await salesBaseQuery
                .OrderByDescending(s => s.SalesDate)
                .Take(50)
                .ToListAsync();
        }

        private async Task<string> GenerateDrNumberAsync(int? tenantId)
        {
            var prefix = $"DR-{DateTime.Today:yyyyMMdd}-";

            var query = _context.DeliveryReceipts.Where(dr => dr.DRNumber.StartsWith(prefix));
            if (tenantId.HasValue)
                query = query.Where(dr => dr.TenantId == tenantId);

            var count = await query.CountAsync();
            return $"{prefix}{(count + 1):000}";
        }
    }
}
