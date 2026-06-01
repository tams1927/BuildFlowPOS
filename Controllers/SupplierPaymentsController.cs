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
    [PermissionAuthorize("SupplierPayments", "Create")]
    public class SupplierPaymentsController : OperationalDbController
    {
        // Shared platform context — used ONLY for platform-owned reads (e.g. Tenants).
        private readonly ApplicationDbContext _platformDb;
        private readonly AuditService _auditService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public SupplierPaymentsController(
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

        [HttpGet]
        public async Task<IActionResult> Pay(int id)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var query = _context.StockInHeaders
                .Include(s => s.Supplier)
                .AsNoTracking()
                .Where(s => s.Id == id);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(s =>
                    s.TenantId == tenantId ||
                    s.TenantId == null);
            }

            var stockIn = await query.FirstOrDefaultAsync();

            if (stockIn == null)
            {
                TempData["ErrorMessage"] = "Stock-in record not found.";
                return RedirectToAction("SupplierPayables", "Reports");
            }

            if (!await _tenantGuard.CanAccessTenantAsync(stockIn.TenantId))
            {
                return Forbid();
            }

            if (stockIn.Supplier != null &&
                !await _tenantGuard.CanAccessTenantAsync(stockIn.Supplier.TenantId))
            {
                return Forbid();
            }

            if (stockIn.BalanceDue <= 0 || stockIn.PaymentStatus == "Paid")
            {
                TempData["ErrorMessage"] = "This supplier invoice is already fully paid.";
                return RedirectToAction("SupplierPayables", "Reports");
            }

            return View(new SupplierPaymentViewModel
            {
                StockInId = stockIn.Id,
                StockInNumber = stockIn.StockInNumber,
                SupplierName = stockIn.Supplier?.SupplierName ?? "N/A",
                TotalCost = stockIn.TotalCost,
                AmountPaid = stockIn.AmountPaid,
                BalanceDue = stockIn.BalanceDue
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Pay(SupplierPaymentViewModel model)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var query = _context.StockInHeaders
                .Include(s => s.Supplier)
                .Where(s => s.Id == model.StockInId);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(s =>
                    s.TenantId == tenantId ||
                    s.TenantId == null);
            }

            var stockIn = await query.FirstOrDefaultAsync();

            if (stockIn == null)
            {
                TempData["ErrorMessage"] = "Stock-in record not found.";
                return RedirectToAction("SupplierPayables", "Reports");
            }

            if (!await _tenantGuard.CanAccessTenantAsync(stockIn.TenantId))
            {
                return Forbid();
            }

            if (stockIn.Supplier != null &&
                !await _tenantGuard.CanAccessTenantAsync(stockIn.Supplier.TenantId))
            {
                return Forbid();
            }

            if (model.PaymentAmount <= 0)
            {
                TempData["ErrorMessage"] = "Payment amount must be greater than zero.";
                return View(model);
            }

            if ((model.PaymentMethod == "GCash" ||
                 model.PaymentMethod == "Bank Transfer" ||
                 model.PaymentMethod == "Check") &&
                string.IsNullOrWhiteSpace(model.PaymentReferenceNumber))
            {
                TempData["ErrorMessage"] = "Reference number is required for this payment method.";
                return View(model);
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                stockIn = await query.FirstOrDefaultAsync();

                if (stockIn == null)
                {
                    await transaction.RollbackAsync();
                    TempData["ErrorMessage"] = "Stock-in record not found.";
                    return RedirectToAction("SupplierPayables", "Reports");
                }

                if (!stockIn.TenantId.HasValue && tenantId.HasValue)
                {
                    stockIn.TenantId = tenantId;
                }

                if (stockIn.Supplier != null &&
                    !stockIn.Supplier.TenantId.HasValue &&
                    tenantId.HasValue)
                {
                    stockIn.Supplier.TenantId = tenantId;
                }

                if (stockIn.BalanceDue <= 0 || stockIn.PaymentStatus == "Paid")
                {
                    await transaction.RollbackAsync();

                    await _auditService.LogAsync(
                        User,
                        "SupplierPayments",
                        "PAYMENT REJECTED",
                        $"Supplier payment rejected for StockIn '{stockIn.StockInNumber}': invoice is already fully paid.",
                        "StockInHeader",
                        stockIn.Id.ToString(),
                        HttpContext.Connection.RemoteIpAddress?.ToString());

                    TempData["ErrorMessage"] =
                        "This supplier invoice is already fully paid. Another payment may have been applied.";
                    return RedirectToAction("SupplierPayables", "Reports");
                }

                if (model.PaymentAmount > stockIn.BalanceDue)
                {
                    await transaction.RollbackAsync();

                    await _auditService.LogAsync(
                        User,
                        "SupplierPayments",
                        "PAYMENT REJECTED",
                        $"Supplier payment rejected for StockIn '{stockIn.StockInNumber}': amount {model.PaymentAmount:N2} exceeds latest balance due {stockIn.BalanceDue:N2}.",
                        "StockInHeader",
                        stockIn.Id.ToString(),
                        HttpContext.Connection.RemoteIpAddress?.ToString());

                    TempData["ErrorMessage"] =
                        "Payment amount cannot exceed the current balance due. Please refresh and try again.";
                    return View(model);
                }

                stockIn.AmountPaid += model.PaymentAmount;
                stockIn.BalanceDue = stockIn.TotalCost - stockIn.AmountPaid;

                if (stockIn.BalanceDue <= 0)
                {
                    stockIn.BalanceDue = 0;
                    stockIn.PaymentStatus = "Paid";
                }
                else if (stockIn.AmountPaid > 0)
                {
                    stockIn.PaymentStatus = "Partial";
                }
                else
                {
                    stockIn.PaymentStatus = "Unpaid";
                }

                stockIn.PaymentMethod = model.PaymentMethod;
                stockIn.PaymentReferenceNumber = model.PaymentReferenceNumber;

                var payment = new SupplierPayment
                {
                    StockInHeaderId = stockIn.Id,
                    SupplierId = stockIn.SupplierId,
                    PaymentDate = DateTime.Now,
                    AmountPaid = model.PaymentAmount,
                    PaymentMethod = model.PaymentMethod,
                    ReferenceNumber = model.PaymentReferenceNumber,
                    Remarks = model.Remarks,
                    CreatedBy = User.Identity?.Name ?? "Unknown",
                    CreatedAt = DateTime.Now
                };

                _context.SupplierPayments.Add(payment);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                await _auditService.LogAsync(
                    User,
                    "SupplierPayments",
                    "PAYMENT POSTED",
                    $"Supplier payment posted. StockIn: {stockIn.StockInNumber}, Supplier: {stockIn.Supplier?.SupplierName}, Amount: {model.PaymentAmount:N2}, Balance: {stockIn.BalanceDue:N2}",
                    "StockInHeader",
                    stockIn.Id.ToString(),
                    HttpContext.Connection.RemoteIpAddress?.ToString()
                );

                TempData["SuccessMessage"] = "Supplier payment posted successfully.";

                return RedirectToAction("SupplierPayables", "Reports");
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        [HttpGet]
        public async Task<IActionResult> PrintReceipt(int id)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var payment = await _context.SupplierPayments
                .AsNoTracking()
                .Include(p => p.Supplier)
                .Include(p => p.StockInHeader)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null) return NotFound();

            // Validate tenant access via the linked Supplier's TenantId
            if (!await _tenantGuard.CanAccessAsync(payment.Supplier?.TenantId))
                return Forbid();

            var settings = await _context.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId)
                ?? new SystemSetting();
            Tenant? tenant = null;
            if (tenantId.HasValue)
                tenant = await _platformDb.Tenants.FindAsync(tenantId.Value);

            ViewBag.PrintSettings = settings;
            ViewBag.PrintTenant   = tenant;

            return View(payment);
        }
    }
}