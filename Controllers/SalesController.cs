using HardwareManagementSystem.Data;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.ViewModels;
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

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            string? paymentFilter = null,
            string? statusFilter = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var query = _context.SalesHeaders
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.SalesDetails)
                    .ThenInclude(d => d.Item)
                        .ThenInclude(i => i!.Unit)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(s =>
                    s.SalesNumber.ToLower().Contains(term) ||
                    (s.Customer != null && s.Customer.CustomerName.ToLower().Contains(term)) ||
                    (s.CashierName != null && s.CashierName.ToLower().Contains(term)) ||
                    (s.ReferenceNumber != null && s.ReferenceNumber.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(paymentFilter) && paymentFilter != "all")
            {
                query = query.Where(s => s.PaymentMethod.ToLower() == paymentFilter.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "all")
            {
                query = query.Where(s => s.Status.ToLower() == statusFilter.ToLower());
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var items = await query
                .OrderByDescending(s => s.SalesDate)
                .ThenByDescending(s => s.Id)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var settings = await _context.SystemSettings.FirstOrDefaultAsync();
            ViewBag.SystemSetting = settings;

            // Today's summary stats (separate lightweight query for KPI cards)
            var today = DateTime.Today;
            var todayStats = await _context.SalesHeaders
                .AsNoTracking()
                .Where(s => s.SalesDate.Date == today)
                .Select(s => new { s.TotalAmount, s.PaymentMethod })
                .ToListAsync();

            ViewBag.TodayTotalSales = todayStats.Sum(s => s.TotalAmount);
            ViewBag.TodayTransactions = todayStats.Count;
            ViewBag.TodayCashPayments = todayStats.Where(s => s.PaymentMethod == "Cash").Sum(s => s.TotalAmount);
            ViewBag.TodayDigitalPayments = todayStats.Where(s => s.PaymentMethod != "Cash").Sum(s => s.TotalAmount);

            var result = new PagedResult<HardwareManagementSystem.Models.SalesHeader>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            };

            ViewBag.PaymentFilter = paymentFilter;
            ViewBag.StatusFilter = statusFilter;

            return View(result);
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

        public async Task<IActionResult> Receipt(int id)
        {
            var sale = await _context.SalesHeaders
                .AsNoTracking()
                .Include(s => s.Customer)
                .Include(s => s.SalesDetails)
                    .ThenInclude(d => d.Item)
                        .ThenInclude(i => i!.Unit)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sale == null)
            {
                TempData["ErrorMessage"] = "Receipt not found.";
                return RedirectToAction(nameof(Index));
            }

            var settings = await _context.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync();

            ViewBag.SystemSetting = settings;

            return View(sale);
        }
    }
}