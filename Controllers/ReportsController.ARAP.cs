using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    public partial class ReportsController
    {
        // ── Customer Aging ─────────────────────────────────────────────

        [PermissionAuthorize("CustomerAging", "View")]
        public async Task<IActionResult> CustomerAging(string? searchTerm = null)
        {
            var tenantId = await GetReportTenantIdAsync();
            var today    = DateTime.Today;

            var customersQuery = ApplyCustomerTenantScope(
                    _context.Customers.AsNoTracking(),
                    tenantId)
                .Where(c => c.IsActive);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                customersQuery = customersQuery.Where(c =>
                    c.CustomerName.ToLower().Contains(term) ||
                    (c.ContactNumber != null && c.ContactNumber.ToLower().Contains(term)));
            }

            var customers = await customersQuery
                .OrderBy(c => c.CustomerName)
                .ToListAsync();

            var customerIds = customers.Select(c => c.Id).ToList();

            // Load all relevant ledger entries once
            var ledgersRaw = await ApplyTenantScope(
                    _context.CustomerLedgers.AsNoTracking()
                        .Where(l => customerIds.Contains(l.CustomerId)),
                    tenantId)
                .ToListAsync();

            var ledgerByCustomer = ledgersRaw.ToLookup(l => l.CustomerId);

            var rows = new List<CustomerAgingRow>();

            foreach (var c in customers)
            {
                var entries      = ledgerByCustomer[c.Id].OrderBy(l => l.Id).ToList();
                var totalBalance = entries.Any() ? entries.Last().RunningBalance : 0m;

                if (totalBalance <= 0) continue;  // no outstanding — skip

                // Charge entries — used to age the balance (gross, then bucket)
                var charges = entries
                    .Where(l => l.TransactionType == "CHARGE")
                    .ToList();

                decimal current   = 0, days1_30 = 0, days31_60 = 0, days61_90 = 0, over90 = 0;

                foreach (var charge in charges)
                {
                    var age = (int)(today - charge.TransactionDate.Date).TotalDays;

                    if      (age <= 0)  current   += charge.DebitAmount;
                    else if (age <= 30) days1_30  += charge.DebitAmount;
                    else if (age <= 60) days31_60 += charge.DebitAmount;
                    else if (age <= 90) days61_90 += charge.DebitAmount;
                    else                over90    += charge.DebitAmount;
                }

                // Pro-rate: if payments brought balance below gross charges, reduce buckets (FIFO)
                var totalCharges = current + days1_30 + days31_60 + days61_90 + over90;
                if (totalCharges > totalBalance)
                {
                    var reduction = totalCharges - totalBalance;
                    // Apply reduction starting from the newest (current) bucket
                    var applied = ApplyReduction(ref current, ref days1_30, ref days31_60, ref days61_90, ref over90, reduction);
                }

                rows.Add(new CustomerAgingRow
                {
                    CustomerId        = c.Id,
                    CustomerName      = c.CustomerName,
                    ContactNumber     = c.ContactNumber,
                    CreditLimit       = c.CreditLimit,
                    Current           = Math.Max(0, current),
                    Days1_30          = Math.Max(0, days1_30),
                    Days31_60         = Math.Max(0, days31_60),
                    Days61_90         = Math.Max(0, days61_90),
                    Over90            = Math.Max(0, over90),
                    TotalOutstanding  = totalBalance
                });
            }

            var vm = new CustomerAgingViewModel { AsOfDate = today, Rows = rows };

            await _auditService.LogAsync(
                User, "CustomerAging", "CUSTOMER_AGING_VIEWED",
                $"Customer Aging report viewed as of {today:yyyy-MM-dd}",
                "Report", null,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            ViewBag.SearchTerm = searchTerm;
            return View(vm);
        }

        [PermissionAuthorize("CustomerAging", "Print")]
        public async Task<IActionResult> CustomerAgingPdf(string? searchTerm = null)
        {
            var tenantId = await GetReportTenantIdAsync();
            var today    = DateTime.Today;

            var customersQuery = ApplyCustomerTenantScope(
                    _context.Customers.AsNoTracking(), tenantId)
                .Where(c => c.IsActive);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                customersQuery = customersQuery.Where(c =>
                    c.CustomerName.ToLower().Contains(term));
            }

            var customers  = await customersQuery.OrderBy(c => c.CustomerName).ToListAsync();
            var customerIds = customers.Select(c => c.Id).ToList();

            var ledgersRaw = await ApplyTenantScope(
                    _context.CustomerLedgers.AsNoTracking()
                        .Where(l => customerIds.Contains(l.CustomerId)),
                    tenantId)
                .ToListAsync();

            var ledgerByCustomer = ledgersRaw.ToLookup(l => l.CustomerId);
            var rows = BuildCustomerAgingRows(customers, ledgerByCustomer, today);

            var vm = new CustomerAgingViewModel { AsOfDate = today, Rows = rows };

            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();

            var tenant = tenantId.HasValue
                ? await _platformDb.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId)
                : null;

            var bytes = _reportPdfService.GenerateCustomerAgingPdf(vm, settings, tenant, settings?.LogoPath);
            return File(bytes, "application/pdf", $"CustomerAging-{today:yyyyMMdd}.pdf");
        }

        // ── Supplier Aging ─────────────────────────────────────────────

        [PermissionAuthorize("SupplierAging", "View")]
        public async Task<IActionResult> SupplierAging(string? searchTerm = null)
        {
            var tenantId = await GetReportTenantIdAsync();
            var today    = DateTime.Today;

            var suppliersQuery = _context.Suppliers.AsNoTracking()
                .Where(s => s.IsActive);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                suppliersQuery = suppliersQuery.Where(s => s.TenantId == tenantId || s.TenantId == null);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                suppliersQuery = suppliersQuery.Where(s =>
                    s.SupplierName.ToLower().Contains(term));
            }

            var suppliers = await suppliersQuery.OrderBy(s => s.SupplierName).ToListAsync();
            var supplierIds = suppliers.Select(s => s.Id).ToList();

            // Load all unpaid stock-ins for these suppliers
            var stockInsRaw = await ApplyTenantScope(
                    _context.StockInHeaders.AsNoTracking()
                        .Where(h => supplierIds.Contains(h.SupplierId) && h.PaymentStatus != "Paid"),
                    tenantId)
                .ToListAsync();

            var stockInsBySupplier = stockInsRaw.ToLookup(h => h.SupplierId);

            var rows = new List<SupplierAgingRow>();

            foreach (var s in suppliers)
            {
                var unpaid = stockInsBySupplier[s.Id].ToList();
                var totalOutstanding = unpaid.Sum(h => h.BalanceDue);
                if (totalOutstanding <= 0) continue;

                decimal current = 0, days1_30 = 0, days31_60 = 0, days61_90 = 0, over90 = 0;

                foreach (var si in unpaid)
                {
                    var age = (int)(today - si.DateReceived.Date).TotalDays;

                    if      (age <= 0)  current   += si.BalanceDue;
                    else if (age <= 30) days1_30  += si.BalanceDue;
                    else if (age <= 60) days31_60 += si.BalanceDue;
                    else if (age <= 90) days61_90 += si.BalanceDue;
                    else                over90    += si.BalanceDue;
                }

                rows.Add(new SupplierAgingRow
                {
                    SupplierId        = s.Id,
                    SupplierName      = s.SupplierName,
                    ContactNumber     = s.ContactNumber,
                    Current           = current,
                    Days1_30          = days1_30,
                    Days31_60         = days31_60,
                    Days61_90         = days61_90,
                    Over90            = over90,
                    TotalOutstanding  = totalOutstanding
                });
            }

            var vm = new SupplierAgingViewModel { AsOfDate = today, Rows = rows };

            await _auditService.LogAsync(
                User, "SupplierAging", "SUPPLIER_AGING_VIEWED",
                $"Supplier Aging report viewed as of {today:yyyy-MM-dd}",
                "Report", null,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            ViewBag.SearchTerm = searchTerm;
            return View(vm);
        }

        [PermissionAuthorize("SupplierAging", "Print")]
        public async Task<IActionResult> SupplierAgingPdf(string? searchTerm = null)
        {
            var tenantId = await GetReportTenantIdAsync();
            var today    = DateTime.Today;

            var suppliersQuery = _context.Suppliers.AsNoTracking().Where(s => s.IsActive);
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                suppliersQuery = suppliersQuery.Where(s => s.TenantId == tenantId || s.TenantId == null);

            var suppliers   = await suppliersQuery.OrderBy(s => s.SupplierName).ToListAsync();
            var supplierIds = suppliers.Select(s => s.Id).ToList();

            var stockInsRaw = await ApplyTenantScope(
                    _context.StockInHeaders.AsNoTracking()
                        .Where(h => supplierIds.Contains(h.SupplierId) && h.PaymentStatus != "Paid"),
                    tenantId)
                .ToListAsync();

            var stockInsBySupplier = stockInsRaw.ToLookup(h => h.SupplierId);
            var rows = BuildSupplierAgingRows(suppliers, stockInsBySupplier, today);

            var vm = new SupplierAgingViewModel { AsOfDate = today, Rows = rows };

            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();

            var tenant = tenantId.HasValue
                ? await _platformDb.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId)
                : null;

            var bytes = _reportPdfService.GenerateSupplierAgingPdf(vm, settings, tenant, settings?.LogoPath);
            return File(bytes, "application/pdf", $"SupplierAging-{today:yyyyMMdd}.pdf");
        }

        // ── Helpers ────────────────────────────────────────────────────

        private static List<CustomerAgingRow> BuildCustomerAgingRows(
            List<Customer> customers,
            ILookup<int, CustomerLedger> ledgerByCustomer,
            DateTime today)
        {
            var rows = new List<CustomerAgingRow>();

            foreach (var c in customers)
            {
                var entries      = ledgerByCustomer[c.Id].OrderBy(l => l.Id).ToList();
                var totalBalance = entries.Any() ? entries.Last().RunningBalance : 0m;
                if (totalBalance <= 0) continue;

                var charges = entries.Where(l => l.TransactionType == "CHARGE").ToList();

                decimal current = 0, days1_30 = 0, days31_60 = 0, days61_90 = 0, over90 = 0;

                foreach (var charge in charges)
                {
                    var age = (int)(today - charge.TransactionDate.Date).TotalDays;

                    if      (age <= 0)  current   += charge.DebitAmount;
                    else if (age <= 30) days1_30  += charge.DebitAmount;
                    else if (age <= 60) days31_60 += charge.DebitAmount;
                    else if (age <= 90) days61_90 += charge.DebitAmount;
                    else                over90    += charge.DebitAmount;
                }

                var totalCharges = current + days1_30 + days31_60 + days61_90 + over90;
                if (totalCharges > totalBalance)
                {
                    var reduction = totalCharges - totalBalance;
                    ApplyReduction(ref current, ref days1_30, ref days31_60, ref days61_90, ref over90, reduction);
                }

                rows.Add(new CustomerAgingRow
                {
                    CustomerId       = c.Id,
                    CustomerName     = c.CustomerName,
                    ContactNumber    = c.ContactNumber,
                    CreditLimit      = c.CreditLimit,
                    Current          = Math.Max(0, current),
                    Days1_30         = Math.Max(0, days1_30),
                    Days31_60        = Math.Max(0, days31_60),
                    Days61_90        = Math.Max(0, days61_90),
                    Over90           = Math.Max(0, over90),
                    TotalOutstanding = totalBalance
                });
            }

            return rows;
        }

        private static List<SupplierAgingRow> BuildSupplierAgingRows(
            List<Supplier> suppliers,
            ILookup<int, StockInHeader> stockInsBySupplier,
            DateTime today)
        {
            var rows = new List<SupplierAgingRow>();

            foreach (var s in suppliers)
            {
                var unpaid           = stockInsBySupplier[s.Id].ToList();
                var totalOutstanding = unpaid.Sum(h => h.BalanceDue);
                if (totalOutstanding <= 0) continue;

                decimal current = 0, days1_30 = 0, days31_60 = 0, days61_90 = 0, over90 = 0;

                foreach (var si in unpaid)
                {
                    var age = (int)(today - si.DateReceived.Date).TotalDays;

                    if      (age <= 0)  current   += si.BalanceDue;
                    else if (age <= 30) days1_30  += si.BalanceDue;
                    else if (age <= 60) days31_60 += si.BalanceDue;
                    else if (age <= 90) days61_90 += si.BalanceDue;
                    else                over90    += si.BalanceDue;
                }

                rows.Add(new SupplierAgingRow
                {
                    SupplierId        = s.Id,
                    SupplierName      = s.SupplierName,
                    ContactNumber     = s.ContactNumber,
                    Current           = current,
                    Days1_30          = days1_30,
                    Days31_60         = days31_60,
                    Days61_90         = days61_90,
                    Over90            = over90,
                    TotalOutstanding  = totalOutstanding
                });
            }

            return rows;
        }

        /// <summary>
        /// Reduces aging buckets oldest-first (over90 → 61-90 → 31-60 → 1-30 → current)
        /// to account for partial payments already applied (FIFO).
        /// </summary>
        private static decimal ApplyReduction(
            ref decimal current, ref decimal days1_30, ref decimal days31_60,
            ref decimal days61_90, ref decimal over90, decimal reduction)
        {
            void Reduce(ref decimal bucket)
            {
                var r = Math.Min(reduction, bucket);
                bucket    -= r;
                reduction -= r;
            }

            Reduce(ref over90);
            Reduce(ref days61_90);
            Reduce(ref days31_60);
            Reduce(ref days1_30);
            Reduce(ref current);

            return reduction;
        }

        private IQueryable<AuditTrail> ApplyTenantScope(IQueryable<AuditTrail> query, int? tenantId)
        {
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
                query = query.Where(a => a.TenantId == tenantId || a.TenantId == null);
            return query;
        }
    }
}
