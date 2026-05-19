using HardwareManagementSystem.Data;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Reports", "View")]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            var monthStart = new DateTime(today.Year, today.Month, 1);

            ViewBag.TodaySales = await _context.SalesHeaders
                .Where(s => s.Status == "Completed" && s.SalesDate >= today && s.SalesDate < tomorrow)
                .SumAsync(s => s.TotalAmount);

            ViewBag.MonthlySales = await _context.SalesHeaders
                .Where(s => s.Status == "Completed" && s.SalesDate >= monthStart)
                .SumAsync(s => s.TotalAmount);

            ViewBag.InventoryValue = await _context.Items
                .Where(i => i.Status == "Active")
                .SumAsync(i => i.CurrentStock * i.CostPrice);

            ViewBag.LowStockCount = await _context.Items
                .CountAsync(i => i.Status == "Active" && i.CurrentStock > 0 && i.CurrentStock <= i.ReorderLevel);

            ViewBag.OutOfStockCount = await _context.Items
                .CountAsync(i => i.Status == "Active" && i.CurrentStock <= 0);

            ViewBag.StockInValue = await _context.StockInHeaders
                .Where(s => s.DateReceived >= monthStart)
                .SumAsync(s => s.TotalCost);

            var cashierReports = await _context.SalesHeaders
                .AsNoTracking()
                .Where(s => s.Status == "Completed" && s.SalesDate >= monthStart)
                .GroupBy(s => s.CashierName ?? "Unknown")
                .Select(g => new CashierSalesReportVm
                {
                    CashierName = g.Key,
                    TransactionCount = g.Count(),
                    TotalSales = g.Sum(x => x.TotalAmount),
                    CashSales = g.Where(x => x.PaymentMethod == "Cash").Sum(x => x.TotalAmount),
                    DigitalSales = g.Where(x => x.PaymentMethod != "Cash").Sum(x => x.TotalAmount)
                })
                .OrderByDescending(x => x.TotalSales)
                .ToListAsync();

            return View(cashierReports);
        }
    }

    public class CashierSalesReportVm
    {
        public string CashierName { get; set; } = string.Empty;

        public int TransactionCount { get; set; }

        public decimal TotalSales { get; set; }

        public decimal CashSales { get; set; }

        public decimal DigitalSales { get; set; }
    }
}