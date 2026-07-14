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
    [PermissionAuthorize("Customers", "View")]
    public class CustomersController : OperationalDbController
    {
        private readonly ApplicationDbContext _platformDb;
        private readonly AuditService _auditService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;
        private readonly DocumentPdfService _pdfService;

        public CustomersController(
            ITenantOperationalContextProvider ctxProvider,
            ApplicationDbContext platformDb,
            AuditService auditService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard,
            DocumentPdfService pdfService)
            : base(ctxProvider)
        {
            _platformDb = platformDb;
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

            var query = _context.Customers
                .AsNoTracking()
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(c =>
                    c.TenantId == tenantId ||
                    c.TenantId == null);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(c =>
                    c.CustomerName.ToLower().Contains(term) ||
                    (c.ContactNumber != null &&
                     c.ContactNumber.ToLower().Contains(term)) ||
                    (c.Email != null &&
                     c.Email.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                bool isActive = statusFilter == "active";
                query = query.Where(c => c.IsActive == isActive);
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var customers = await query
                .OrderBy(c => c.CustomerName)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var customerIds = customers
                .Select(c => c.Id)
                .ToList();

            var balances = await _context.CustomerLedgers
                .AsNoTracking()
                .Where(l => customerIds.Contains(l.CustomerId))
                .GroupBy(l => l.CustomerId)
                .Select(g => new
                {
                    CustomerId = g.Key,
                    Balance = g.OrderByDescending(x => x.Id)
                        .Select(x => x.RunningBalance)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.CustomerId, x => x.Balance);

            ViewBag.StatusFilter = statusFilter;
            ViewBag.CustomerBalances = balances;

            return View(new PagedResult<Customer>
            {
                Items = customers,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Customers", "Create")]
        public async Task<IActionResult> Create(
            string customerName,
            string? contactNumber,
            string? email,
            string? address,
            string customerType)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (string.IsNullOrWhiteSpace(customerName))
            {
                TempData["ErrorMessage"] = "Customer name is required.";
                return RedirectToAction(nameof(Index));
            }

            var name = customerName.Trim();

            var duplicateQuery = _context.Customers.AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                duplicateQuery = duplicateQuery.Where(c =>
                    c.TenantId == tenantId ||
                    c.TenantId == null);
            }

            var exists = await duplicateQuery.AnyAsync(c =>
                c.CustomerName == name);

            if (exists)
            {
                TempData["ErrorMessage"] = "Customer already exists.";
                return RedirectToAction(nameof(Index));
            }

            var customer = new Customer
            {
                TenantId = tenantId,
                CustomerName = name,
                ContactNumber = contactNumber,
                Email = email,
                Address = address,
                CustomerType = string.IsNullOrWhiteSpace(customerType)
                    ? "Walk-in"
                    : customerType,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            _context.Customers.Add(customer);

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Customers",
                "CREATED",
                $"Customer created. Name: {customer.CustomerName}",
                "Customer",
                customer.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Customer added successfully.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Customers", "Edit")]
        public async Task<IActionResult> Edit(
            int id,
            string customerName,
            string? contactNumber,
            string? email,
            string? address,
            string customerType,
            bool isActive)
        {
            var customer = await _context.Customers.FindAsync(id);

            if (customer == null)
            {
                TempData["ErrorMessage"] = "Customer not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(customer.TenantId))
            {
                return Forbid();
            }

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (string.IsNullOrWhiteSpace(customerName))
            {
                TempData["ErrorMessage"] = "Customer name is required.";
                return RedirectToAction(nameof(Index));
            }

            var name = customerName.Trim();

            var duplicateQuery = _context.Customers
                .Where(c =>
                    c.Id != id &&
                    c.CustomerName == name);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                duplicateQuery = duplicateQuery.Where(c =>
                    c.TenantId == tenantId ||
                    c.TenantId == null);
            }

            var exists = await duplicateQuery.AnyAsync();

            if (exists)
            {
                TempData["ErrorMessage"] = "Customer already exists.";
                return RedirectToAction(nameof(Index));
            }

            customer.CustomerName = name;
            customer.ContactNumber = contactNumber;
            customer.Email = email;
            customer.Address = address;
            customer.CustomerType = string.IsNullOrWhiteSpace(customerType)
                ? "Walk-in"
                : customerType;
            customer.IsActive = isActive;

            if (!customer.TenantId.HasValue && tenantId.HasValue)
            {
                customer.TenantId = tenantId;
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Customers",
                "UPDATED",
                $"Customer updated. Name: {customer.CustomerName}",
                "Customer",
                customer.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Customer updated successfully.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Customers", "Delete")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var customer = await _context.Customers.FindAsync(id);

            if (customer == null)
            {
                TempData["ErrorMessage"] = "Customer not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(customer.TenantId))
            {
                return Forbid();
            }

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            customer.IsActive = false;

            if (!customer.TenantId.HasValue && tenantId.HasValue)
            {
                customer.TenantId = tenantId;
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Customers",
                "DEACTIVATED",
                $"Customer deactivated. Name: {customer.CustomerName}",
                "Customer",
                customer.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Customer deactivated successfully.";

            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        // UPDATE CREDIT LIMIT
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Customers", "Edit")]
        public async Task<IActionResult> UpdateCreditLimit(int id, decimal creditLimit)
        {
            var customer = await _context.Customers.FindAsync(id);

            if (customer == null)
            {
                TempData["ErrorMessage"] = "Customer not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(customer.TenantId))
                return Forbid();

            if (creditLimit < 0)
            {
                TempData["ErrorMessage"] = "Credit limit cannot be negative.";
                return RedirectToAction(nameof(Index));
            }

            var oldLimit = customer.CreditLimit;
            customer.CreditLimit = creditLimit;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User, "Customers", "CUSTOMER_CREDIT_LIMIT_CHANGED",
                $"Credit limit changed for {customer.CustomerName}: {oldLimit:N2} → {creditLimit:N2}",
                "Customer", id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Credit limit updated to ₱{creditLimit:N2} for {customer.CustomerName}.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        // STATEMENT OF ACCOUNT
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        [PermissionAuthorize("CustomerStatements", "View")]
        public async Task<IActionResult> Statement(
            int id,
            DateTime? dateFrom,
            DateTime? dateTo)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var customer = await _context.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);

            if (customer == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(customer.TenantId)) return Forbid();

            var from = dateFrom?.Date ?? DateTime.Today.AddMonths(-1);
            var to   = dateTo?.Date   ?? DateTime.Today;

            // Beginning balance = RunningBalance of the last entry strictly before the date range
            var beginningEntry = await _context.CustomerLedgers.AsNoTracking()
                .Where(l => l.CustomerId == id && l.TransactionDate.Date < from)
                .OrderByDescending(l => l.Id)
                .Select(l => l.RunningBalance)
                .FirstOrDefaultAsync();

            // Transactions within range
            var transactions = await _context.CustomerLedgers.AsNoTracking()
                .Where(l => l.CustomerId == id &&
                            l.TransactionDate.Date >= from &&
                            l.TransactionDate.Date <= to)
                .OrderBy(l => l.TransactionDate)
                .ThenBy(l => l.Id)
                .ToListAsync();

            var closingBalance = transactions.Any()
                ? transactions.Last().RunningBalance
                : beginningEntry;

            var vm = new CustomerSOAViewModel
            {
                Customer         = customer,
                DateFrom         = from,
                DateTo           = to,
                BeginningBalance = beginningEntry,
                Transactions     = transactions,
                ClosingBalance   = closingBalance,
                TotalDebits      = transactions.Sum(t => t.DebitAmount),
                TotalCredits     = transactions.Sum(t => t.CreditAmount)
            };

            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();

            ViewBag.Settings = settings;

            await _auditService.LogAsync(
                User, "CustomerStatements", "CUSTOMER_SOA_PRINTED",
                $"SOA viewed for {customer.CustomerName} ({from:yyyy-MM-dd} to {to:yyyy-MM-dd})",
                "Customer", id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            return View(vm);
        }

        // ─────────────────────────────────────────────────────────────
        // STATEMENT PDF DOWNLOAD
        // ─────────────────────────────────────────────────────────────

        [HttpGet]
        [PermissionAuthorize("CustomerStatements", "Print")]
        public async Task<IActionResult> StatementPdf(
            int id,
            DateTime? dateFrom,
            DateTime? dateTo)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var customer = await _context.Customers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);

            if (customer == null) return NotFound();
            if (!await _tenantGuard.CanAccessTenantAsync(customer.TenantId)) return Forbid();

            var from = dateFrom?.Date ?? DateTime.Today.AddMonths(-1);
            var to   = dateTo?.Date   ?? DateTime.Today;

            var beginningEntry = await _context.CustomerLedgers.AsNoTracking()
                .Where(l => l.CustomerId == id && l.TransactionDate.Date < from)
                .OrderByDescending(l => l.Id)
                .Select(l => l.RunningBalance)
                .FirstOrDefaultAsync();

            var transactions = await _context.CustomerLedgers.AsNoTracking()
                .Where(l => l.CustomerId == id &&
                            l.TransactionDate.Date >= from &&
                            l.TransactionDate.Date <= to)
                .OrderBy(l => l.TransactionDate)
                .ThenBy(l => l.Id)
                .ToListAsync();

            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();

            var vm = new CustomerSOAViewModel
            {
                Customer         = customer,
                DateFrom         = from,
                DateTo           = to,
                BeginningBalance = beginningEntry,
                Transactions     = transactions,
                ClosingBalance   = transactions.Any() ? transactions.Last().RunningBalance : beginningEntry,
                TotalDebits      = transactions.Sum(t => t.DebitAmount),
                TotalCredits     = transactions.Sum(t => t.CreditAmount)
            };

            var printTenant = await ResolvePlatformTenantAsync(customer.TenantId);
            var bytes = _pdfService.GenerateCustomerSOAPdf(vm, settings, printTenant, settings?.LogoPath);
            return File(bytes, "application/pdf", $"SOA-{customer.CustomerName}-{from:yyyyMMdd}-{to:yyyyMMdd}.pdf");
        }

        private async Task<Tenant?> ResolvePlatformTenantAsync(int? tenantId)
        {
            if (!tenantId.HasValue) return null;
            return await _platformDb.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId.Value);
        }
    }
}