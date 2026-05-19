using HardwareManagementSystem.Data;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Dashboard", "View")]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
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

            ViewBag.TodayTransactions = await _context.SalesHeaders
                .CountAsync(s => s.Status == "Completed" && s.SalesDate >= today && s.SalesDate < tomorrow);

            ViewBag.InventoryValue = await _context.Items
                .Where(i => i.Status == "Active")
                .SumAsync(i => i.CurrentStock * i.CostPrice);

            ViewBag.LowStockCount = await _context.Items
                .CountAsync(i => i.Status == "Active" && i.CurrentStock > 0 && i.CurrentStock <= i.ReorderLevel);

            ViewBag.OutOfStockCount = await _context.Items
                .CountAsync(i => i.Status == "Active" && i.CurrentStock <= 0);

            ViewBag.TotalItems = await _context.Items
                .CountAsync(i => i.Status == "Active");

            ViewBag.RecentSales = await _context.SalesHeaders
                .AsNoTracking()
                .Include(s => s.Customer)
                .Where(s => s.Status == "Completed")
                .OrderByDescending(s => s.SalesDate)
                .Take(5)
                .ToListAsync();

            ViewBag.LowStockItems = await _context.Items
                .AsNoTracking()
                .Include(i => i.Category)
                .Include(i => i.Unit)
                .Where(i => i.Status == "Active" && i.CurrentStock <= i.ReorderLevel)
                .OrderBy(i => i.CurrentStock)
                .Take(5)
                .ToListAsync();

            return View();
        }
    }
}