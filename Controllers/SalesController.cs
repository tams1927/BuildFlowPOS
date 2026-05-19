using HardwareManagementSystem.Data;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Sales", "View")]
    public class SalesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;
        private readonly NotificationService _notificationService;

        public SalesController(ApplicationDbContext context, AuditService auditService, NotificationService notificationService)
        {
            _context = context;
            _auditService = auditService;
            _notificationService = notificationService;
        }

        public async Task<IActionResult> Index()
        {
            var sales = await _context.SalesHeaders
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.SalesDetails)
                    .ThenInclude(d => d.Item)
                        .ThenInclude(i => i!.Unit)
                .OrderByDescending(s => s.SalesDate)
                .ThenByDescending(s => s.Id)
                .ToListAsync();

            var settings = await _context.SystemSettings.FirstOrDefaultAsync();

            ViewBag.SystemSetting = settings;

            return View(sales);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VoidSale(int id, string voidReason)
        {
            if (string.IsNullOrWhiteSpace(voidReason))
            {
                TempData["ErrorMessage"] = "Void reason is required.";
                return RedirectToAction(nameof(Index));
            }

            var sale = await _context.SalesHeaders
                .Include(s => s.SalesDetails)
                    .ThenInclude(d => d.Item)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null)
            {
                TempData["ErrorMessage"] = "Sale not found.";
                return RedirectToAction(nameof(Index));
            }

            if (sale.Status == "Voided")
            {
                TempData["ErrorMessage"] = "This sale is already voided.";
                return RedirectToAction(nameof(Index));
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                foreach (var detail in sale.SalesDetails)
                {
                    if (detail.Item != null)
                    {
                        detail.Item.CurrentStock += detail.Quantity;
                    }
                }

                sale.Status = "Voided";

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    User,
                    "Sales",
                    "SALE VOIDED",
                    $"Sale voided. Receipt: {sale.SalesNumber}, Total: {sale.TotalAmount:N2}, Reason: {voidReason}",
                    "SalesHeader",
                    sale.Id.ToString(),
                    HttpContext.Connection.RemoteIpAddress?.ToString()
                );

                await _notificationService.CreateAsync(
                    "Sale Voided",
                    $"Receipt {sale.SalesNumber} was voided. Total: ₱{sale.TotalAmount:N2}.",
                    "Warning",
                    "bi bi-x-circle",
                    null,
                    "/Sales"
                );

                await transaction.CommitAsync();

                TempData["SuccessMessage"] = $"Sale {sale.SalesNumber} voided successfully.";
            }
            catch
            {
                await transaction.RollbackAsync();

                TempData["ErrorMessage"] = "Unable to void sale.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}