using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("SupplierPayments", "Create")]
    public class SupplierPaymentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;

        public SupplierPaymentsController(
            ApplicationDbContext context,
            AuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        [HttpGet]
        public async Task<IActionResult> Pay(int id)
        {
            var stockIn = await _context.StockInHeaders
                .Include(s => s.Supplier)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id);

            if (stockIn == null)
            {
                TempData["ErrorMessage"] = "Stock-in record not found.";
                return RedirectToAction("SupplierPayables", "Reports");
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
            if (!ModelState.IsValid)
                return View(model);

            var stockIn = await _context.StockInHeaders
                .Include(s => s.Supplier)
                .FirstOrDefaultAsync(s => s.Id == model.StockInId);

            if (stockIn == null)
            {
                TempData["ErrorMessage"] = "Stock-in record not found.";
                return RedirectToAction("SupplierPayables", "Reports");
            }

            if (model.PaymentAmount <= 0)
            {
                TempData["ErrorMessage"] = "Payment amount must be greater than zero.";
                return View(model);
            }

            if (model.PaymentAmount > stockIn.BalanceDue)
            {
                TempData["ErrorMessage"] = "Payment amount cannot exceed balance due.";
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

            // =========================================
            // CREATE PAYMENT LEDGER
            // =========================================

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
    }
}