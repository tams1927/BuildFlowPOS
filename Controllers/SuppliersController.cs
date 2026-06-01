using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.Pdf;
using HardwareManagementSystem.Services.TenantDatabases;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Suppliers", "View")]
    public class SuppliersController : OperationalDbController
    {
        private readonly AuditService _auditService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;
        private readonly DocumentPdfService _pdfService;

        public SuppliersController(
            ITenantOperationalContextProvider ctxProvider,
            AuditService auditService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard,
            DocumentPdfService pdfService)
            : base(ctxProvider)
        {
            _auditService = auditService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
            _pdfService = pdfService;
        }

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            string? statusFilter = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var query = _context.Suppliers
                .AsNoTracking()
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(s =>
                    s.TenantId == tenantId ||
                    s.TenantId == null);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(s =>
                    s.SupplierName.ToLower().Contains(term) ||
                    (s.ContactPerson != null &&
                     s.ContactPerson.ToLower().Contains(term)) ||
                    (s.ContactNumber != null &&
                     s.ContactNumber.ToLower().Contains(term)) ||
                    (s.Email != null &&
                     s.Email.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                bool isActive = statusFilter == "active";
                query = query.Where(s => s.IsActive == isActive);
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var suppliers = await query
                .OrderBy(s => s.SupplierName)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.StatusFilter = statusFilter;

            return View(new PagedResult<Supplier>
            {
                Items = suppliers,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Suppliers", "Create")]
        public async Task<IActionResult> Create(
            string supplierName,
            string? contactPerson,
            string? contactNumber,
            string? email,
            string? address,
            string? remarks)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (string.IsNullOrWhiteSpace(supplierName))
            {
                TempData["ErrorMessage"] = "Supplier name is required.";
                return RedirectToAction(nameof(Index));
            }

            var name = supplierName.Trim();

            var duplicateQuery = _context.Suppliers.AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                duplicateQuery = duplicateQuery.Where(s =>
                    s.TenantId == tenantId ||
                    s.TenantId == null);
            }

            var exists = await duplicateQuery.AnyAsync(s =>
                s.SupplierName == name);

            if (exists)
            {
                TempData["ErrorMessage"] = "Supplier already exists.";
                return RedirectToAction(nameof(Index));
            }

            var supplier = new Supplier
            {
                TenantId = tenantId,
                SupplierName = name,
                ContactPerson = contactPerson,
                ContactNumber = contactNumber,
                Email = email,
                Address = address,
                Remarks = remarks,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            _context.Suppliers.Add(supplier);

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Suppliers",
                "CREATED",
                $"Supplier created. Name: {supplier.SupplierName}",
                "Supplier",
                supplier.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Supplier added successfully.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Suppliers", "Edit")]
        public async Task<IActionResult> Edit(
            int id,
            string supplierName,
            string? contactPerson,
            string? contactNumber,
            string? email,
            string? address,
            string? remarks,
            bool isActive)
        {
            var supplier = await _context.Suppliers.FindAsync(id);

            if (supplier == null)
            {
                TempData["ErrorMessage"] = "Supplier not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(supplier.TenantId))
            {
                return Forbid();
            }

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (string.IsNullOrWhiteSpace(supplierName))
            {
                TempData["ErrorMessage"] = "Supplier name is required.";
                return RedirectToAction(nameof(Index));
            }

            var name = supplierName.Trim();

            var duplicateQuery = _context.Suppliers
                .Where(s =>
                    s.Id != id &&
                    s.SupplierName == name);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                duplicateQuery = duplicateQuery.Where(s =>
                    s.TenantId == tenantId ||
                    s.TenantId == null);
            }

            var exists = await duplicateQuery.AnyAsync();

            if (exists)
            {
                TempData["ErrorMessage"] = "Supplier name already exists.";
                return RedirectToAction(nameof(Index));
            }

            supplier.SupplierName = name;
            supplier.ContactPerson = contactPerson;
            supplier.ContactNumber = contactNumber;
            supplier.Email = email;
            supplier.Address = address;
            supplier.Remarks = remarks;
            supplier.IsActive = isActive;

            if (!supplier.TenantId.HasValue && tenantId.HasValue)
            {
                supplier.TenantId = tenantId;
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Suppliers",
                "UPDATED",
                $"Supplier updated. Name: {supplier.SupplierName}",
                "Supplier",
                supplier.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Supplier updated successfully.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Suppliers", "Delete")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);

            if (supplier == null)
            {
                TempData["ErrorMessage"] = "Supplier not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(supplier.TenantId))
            {
                return Forbid();
            }

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            supplier.IsActive = false;

            if (!supplier.TenantId.HasValue && tenantId.HasValue)
            {
                supplier.TenantId = tenantId;
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Suppliers",
                "DEACTIVATED",
                $"Supplier deactivated. Name: {supplier.SupplierName}",
                "Supplier",
                supplier.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Supplier deactivated successfully.";

            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        // SUPPLIER STATEMENT
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        [PermissionAuthorize("SupplierStatements", "View")]
        public async Task<IActionResult> Statement(
            int id,
            DateTime? dateFrom,
            DateTime? dateTo)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var supplier = await _context.Suppliers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id);

            if (supplier == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(supplier.TenantId)) return Forbid();

            var from = dateFrom?.Date ?? DateTime.Today.AddMonths(-1);
            var to   = dateTo?.Date   ?? DateTime.Today;

            var vm = await BuildSupplierStatementAsync(tenantId, supplier, from, to);

            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();

            ViewBag.Settings = settings;

            await _auditService.LogAsync(
                User, "SupplierStatements", "SUPPLIER_STATEMENT_PRINTED",
                $"Statement viewed for {supplier.SupplierName} ({from:yyyy-MM-dd} to {to:yyyy-MM-dd})",
                "Supplier", id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return View(vm);
        }

        [HttpGet]
        [PermissionAuthorize("SupplierStatements", "Print")]
        public async Task<IActionResult> StatementPdf(
            int id,
            DateTime? dateFrom,
            DateTime? dateTo)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var supplier = await _context.Suppliers.AsNoTracking()
                .Include(s => s.Tenant)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (supplier == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(supplier.TenantId)) return Forbid();

            var from = dateFrom?.Date ?? DateTime.Today.AddMonths(-1);
            var to   = dateTo?.Date   ?? DateTime.Today;

            var vm = await BuildSupplierStatementAsync(tenantId, supplier, from, to);

            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();

            var bytes = _pdfService.GenerateSupplierStatementPdf(vm, settings, supplier.Tenant, settings?.LogoPath);
            return File(bytes, "application/pdf", $"SupplierStatement-{supplier.SupplierName}-{from:yyyyMMdd}-{to:yyyyMMdd}.pdf");
        }

        private async Task<SupplierStatementViewModel> BuildSupplierStatementAsync(
            int? tenantId, Supplier supplier, DateTime from, DateTime to)
        {
            // Opening balance = all stock-in totals BEFORE from date minus all payments before from date
            var purchasesBeforeRange = await _context.StockInHeaders.AsNoTracking()
                .Where(s => s.SupplierId == supplier.Id &&
                            s.DateReceived.Date < from &&
                            (!(!_tenantContext.IsGlobalUser && tenantId.HasValue) ||
                              s.TenantId == tenantId || s.TenantId == null))
                .SumAsync(s => (decimal?)s.TotalCost) ?? 0m;

            var paymentsBeforeRange = await _context.SupplierPayments.AsNoTracking()
                .Where(p => p.SupplierId == supplier.Id &&
                            p.PaymentDate.Date < from &&
                            (!(!_tenantContext.IsGlobalUser && tenantId.HasValue) ||
                              _context.StockInHeaders.Any(s =>
                                  s.Id == p.StockInHeaderId &&
                                  (s.TenantId == tenantId || s.TenantId == null))))
                .SumAsync(p => (decimal?)p.AmountPaid) ?? 0m;

            var openingBalance = purchasesBeforeRange - paymentsBeforeRange;

            // Build lines within range: purchases + payments
            var purchases = await _context.StockInHeaders.AsNoTracking()
                .Where(s => s.SupplierId == supplier.Id &&
                            s.DateReceived.Date >= from &&
                            s.DateReceived.Date <= to &&
                            (!(!_tenantContext.IsGlobalUser && tenantId.HasValue) ||
                              s.TenantId == tenantId || s.TenantId == null))
                .OrderBy(s => s.DateReceived)
                .ToListAsync();

            var payments = await _context.SupplierPayments.AsNoTracking()
                .Where(p => p.SupplierId == supplier.Id &&
                            p.PaymentDate.Date >= from &&
                            p.PaymentDate.Date <= to)
                .OrderBy(p => p.PaymentDate)
                .ToListAsync();

            // Merge and build running balance
            var lines = new List<SupplierStatementLine>();

            foreach (var si in purchases)
            {
                lines.Add(new SupplierStatementLine
                {
                    Date        = si.DateReceived,
                    ReferenceNo = si.StockInNumber,
                    Type        = "Purchase",
                    Description = string.IsNullOrWhiteSpace(si.InvoiceNumber)
                        ? $"Stock-In #{si.StockInNumber}"
                        : $"Stock-In #{si.StockInNumber} (Inv: {si.InvoiceNumber})",
                    Debit       = si.TotalCost,
                    Credit      = 0
                });
            }

            foreach (var pmt in payments)
            {
                lines.Add(new SupplierStatementLine
                {
                    Date        = pmt.PaymentDate,
                    ReferenceNo = pmt.ReferenceNumber ?? pmt.Id.ToString(),
                    Type        = "Payment",
                    Description = $"Payment via {pmt.PaymentMethod}" +
                        (string.IsNullOrWhiteSpace(pmt.Remarks) ? "" : $" — {pmt.Remarks}"),
                    Debit       = 0,
                    Credit      = pmt.AmountPaid
                });
            }

            lines = lines.OrderBy(l => l.Date).ThenBy(l => l.Type).ToList();

            decimal runBal = openingBalance;
            foreach (var l in lines)
            {
                runBal += l.Debit - l.Credit;
                l.RunningBalance = runBal;
            }

            return new SupplierStatementViewModel
            {
                Supplier         = supplier,
                DateFrom         = from,
                DateTo           = to,
                Lines            = lines,
                OpeningBalance   = openingBalance,
                ClosingBalance   = runBal,
                TotalPurchases   = lines.Sum(l => l.Debit),
                TotalPayments    = lines.Sum(l => l.Credit)
            };
        }
    }
}