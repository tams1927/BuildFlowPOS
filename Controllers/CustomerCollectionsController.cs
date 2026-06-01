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
    [PermissionAuthorize("CustomerCollections", "View")]
    public class CustomerCollectionsController : OperationalDbController
    {
        // Shared platform context — used ONLY for platform-owned reads (e.g. Tenants).
        private readonly ApplicationDbContext _platformDb;
        private readonly AuditService _auditService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public CustomerCollectionsController(
            ITenantOperationalContextProvider ctxProvider,
            ApplicationDbContext platformDb,
            AuditService auditService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard)
            : base(ctxProvider)
        {
            _platformDb = platformDb;
            _auditService = auditService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
        }

        public async Task<IActionResult> Index(
            int? customerId,
            int pageNumber = 1,
            int pageSize = 10)
        {
            pageSize = PagedResult<CustomerLedger>.ValidatePageSize(pageSize);

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var customersQuery = _context.Customers
                .AsNoTracking()
                .Where(c => c.IsActive);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                customersQuery = customersQuery.Where(c =>
                    c.TenantId == tenantId ||
                    c.TenantId == null);
            }

            ViewBag.Customers = await customersQuery
                .OrderBy(c => c.CustomerName)
                .ToListAsync();

            ViewBag.SelectedCustomerId = customerId;

            if (customerId == null)
            {
                return View(new PagedResult<CustomerLedger>
                {
                    Items = new List<CustomerLedger>(),
                    PageNumber = 1,
                    PageSize = pageSize,
                    TotalRecords = 0
                });
            }

            var customer = await _context.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == customerId.Value);

            if (customer == null)
            {
                TempData["ErrorMessage"] = "Customer not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(customer.TenantId))
            {
                return Forbid();
            }

            var query = _context.CustomerLedgers
                .AsNoTracking()
                .Include(l => l.Customer)
                .Where(l => l.CustomerId == customerId.Value);

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<CustomerLedger>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var items = await query
                .OrderByDescending(l => l.TransactionDate)
                .ThenByDescending(l => l.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var allLedger = await query
                .Select(l => new
                {
                    l.DebitAmount,
                    l.CreditAmount,
                    l.RunningBalance,
                    l.Id
                })
                .ToListAsync();

            ViewBag.TotalCharges = allLedger.Sum(l => l.DebitAmount);
            ViewBag.TotalPayments = allLedger.Sum(l => l.CreditAmount);
            ViewBag.CurrentBalance = allLedger.Any()
                ? allLedger.OrderByDescending(l => l.Id).First().RunningBalance
                : 0;

            return View(new PagedResult<CustomerLedger>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("CustomerCollections", "Create")]
        public async Task<IActionResult> CollectPayment(
            int customerId,
            decimal paymentAmount,
            string paymentMethod,
            string? referenceNumber,
            string? remarks)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (customerId <= 0)
            {
                TempData["ErrorMessage"] = "Customer is required.";
                return RedirectToAction(nameof(Index));
            }

            if (paymentAmount <= 0)
            {
                TempData["ErrorMessage"] = "Payment amount must be greater than zero.";
                return RedirectToAction(nameof(Index), new { customerId });
            }

            if (string.IsNullOrWhiteSpace(paymentMethod))
            {
                TempData["ErrorMessage"] = "Payment method is required.";
                return RedirectToAction(nameof(Index), new { customerId });
            }

            if ((paymentMethod == "GCash" || paymentMethod == "Bank Transfer") &&
                string.IsNullOrWhiteSpace(referenceNumber))
            {
                TempData["ErrorMessage"] = "Reference number is required for this payment method.";
                return RedirectToAction(nameof(Index), new { customerId });
            }

            var customerQuery = _context.Customers
                .Where(c => c.Id == customerId && c.IsActive);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                customerQuery = customerQuery.Where(c =>
                    c.TenantId == tenantId ||
                    c.TenantId == null);
            }

            var customer = await customerQuery.FirstOrDefaultAsync();

            if (customer == null)
            {
                TempData["ErrorMessage"] = "Customer not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(customer.TenantId))
            {
                return Forbid();
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                customer = await customerQuery.FirstOrDefaultAsync();

                if (customer == null)
                {
                    await transaction.RollbackAsync();
                    TempData["ErrorMessage"] = "Customer not found.";
                    return RedirectToAction(nameof(Index));
                }

                if (!customer.TenantId.HasValue && tenantId.HasValue)
                {
                    customer.TenantId = tenantId;
                }

                var previousBalance = await _context.CustomerLedgers
                    .Where(l => l.CustomerId == customerId)
                    .OrderByDescending(l => l.Id)
                    .Select(l => l.RunningBalance)
                    .FirstOrDefaultAsync();

                if (previousBalance <= 0)
                {
                    await transaction.RollbackAsync();

                    await _auditService.LogAsync(
                        User,
                        "CustomerCollections",
                        "PAYMENT REJECTED",
                        $"Payment rejected for customer '{customer.CustomerName}': no outstanding balance (concurrent payment may have cleared it).",
                        "Customer",
                        customerId.ToString(),
                        HttpContext.Connection.RemoteIpAddress?.ToString());

                    TempData["ErrorMessage"] =
                        "Customer has no outstanding balance. Another payment may have already been applied.";
                    return RedirectToAction(nameof(Index), new { customerId });
                }

                if (paymentAmount > previousBalance)
                {
                    await transaction.RollbackAsync();

                    await _auditService.LogAsync(
                        User,
                        "CustomerCollections",
                        "PAYMENT REJECTED",
                        $"Payment rejected for customer '{customer.CustomerName}': amount {paymentAmount:N2} exceeds latest balance {previousBalance:N2}.",
                        "Customer",
                        customerId.ToString(),
                        HttpContext.Connection.RemoteIpAddress?.ToString());

                    TempData["ErrorMessage"] =
                        "Payment cannot exceed the current outstanding balance. Please refresh and try again.";
                    return RedirectToAction(nameof(Index), new { customerId });
                }

                var collectionNumber = await GenerateCollectionNumberAsync();
                var newBalance = previousBalance - paymentAmount;

                var ledger = new CustomerLedger
                {
                    CustomerId = customerId,
                    TransactionType = "PAYMENT",
                    ReferenceNumber = collectionNumber,
                    DebitAmount = 0,
                    CreditAmount = paymentAmount,
                    BalanceBefore = previousBalance,
                    RunningBalance = newBalance,
                    PaymentMethod = paymentMethod,
                    PaymentReferenceNumber = referenceNumber,
                    Remarks = string.IsNullOrWhiteSpace(remarks)
                        ? $"Collection payment via {paymentMethod}"
                        : remarks,
                    TransactionDate = DateTime.Now,
                    CreatedBy = User.Identity?.Name ?? "Unknown",
                    CreatedAt = DateTime.Now
                };

                _context.CustomerLedgers.Add(ledger);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                await _auditService.LogAsync(
                    User,
                    "CustomerCollections",
                    "PAYMENT COLLECTED",
                    $"Payment collected. Customer: {customer.CustomerName}, Amount: {paymentAmount:N2}, Previous Balance: {previousBalance:N2}, Remaining Balance: {newBalance:N2}, Method: {paymentMethod}",
                    "CustomerLedger",
                    ledger.Id.ToString(),
                    HttpContext.Connection.RemoteIpAddress?.ToString()
                );

                TempData["SuccessMessage"] =
                    $"Payment collected successfully. Ref: {collectionNumber}";

                return RedirectToAction(nameof(Receipt), new { id = ledger.Id });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<IActionResult> Receipt(int id)
        {
            var ledger = await _context.CustomerLedgers
                .Include(l => l.Customer)
                .AsNoTracking()
                .FirstOrDefaultAsync(l =>
                    l.Id == id &&
                    l.TransactionType == "PAYMENT");

            if (ledger == null)
            {
                TempData["ErrorMessage"] = "Collection receipt not found.";
                return RedirectToAction(nameof(Index));
            }

            if (ledger.Customer != null &&
                !await _tenantGuard.CanAccessTenantAsync(ledger.Customer.TenantId))
            {
                return Forbid();
            }

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();
            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();
            Tenant? tenant = tenantId.HasValue
                ? await _platformDb.Tenants.FindAsync(tenantId.Value)
                : null;

            ViewBag.PrintSettings = settings;
            ViewBag.PrintTenant   = tenant;

            return View(ledger);
        }

        private async Task<string> GenerateCollectionNumberAsync()
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            var prefix = $"COL-{today}-";

            var countToday = await _context.CustomerLedgers
                .CountAsync(l =>
                    l.TransactionType == "PAYMENT" &&
                    l.ReferenceNumber.StartsWith(prefix));

            return $"{prefix}{(countToday + 1):D4}";
        }
    }
}