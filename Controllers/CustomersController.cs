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
    [PermissionAuthorize("Customers", "View")]
    public class CustomersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;

        public CustomersController(ApplicationDbContext context, AuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        public async Task<IActionResult> Index(
    int pageNumber = 1,
    int pageSize = 10,
    string? searchTerm = null,
    string? statusFilter = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var query = _context.Customers
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(c =>
                    c.CustomerName.ToLower().Contains(term) ||
                    (c.ContactNumber != null && c.ContactNumber.ToLower().Contains(term)) ||
                    (c.Email != null && c.Email.ToLower().Contains(term)));
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
        public async Task<IActionResult> Create(
            string customerName,
            string? contactNumber,
            string? email,
            string? address,
            string customerType)
        {
            if (string.IsNullOrWhiteSpace(customerName))
            {
                TempData["ErrorMessage"] = "Customer name is required.";
                return RedirectToAction(nameof(Index));
            }

            var customer = new Customer
            {
                CustomerName = customerName.Trim(),
                ContactNumber = contactNumber,
                Email = email,
                Address = address,
                CustomerType = string.IsNullOrWhiteSpace(customerType) ? "Walk-in" : customerType,
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

            if (string.IsNullOrWhiteSpace(customerName))
            {
                TempData["ErrorMessage"] = "Customer name is required.";
                return RedirectToAction(nameof(Index));
            }

            customer.CustomerName = customerName.Trim();
            customer.ContactNumber = contactNumber;
            customer.Email = email;
            customer.Address = address;
            customer.CustomerType = string.IsNullOrWhiteSpace(customerType) ? "Walk-in" : customerType;
            customer.IsActive = isActive;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
                User,
                "Customers",
                "UPDATED",
                $"Customer deactivated. Name: {customer.CustomerName}",
                "Customer",
                customer.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Customer updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(int id)
        {
            var customer = await _context.Customers.FindAsync(id);

            if (customer == null)
            {
                TempData["ErrorMessage"] = "Customer not found.";
                return RedirectToAction(nameof(Index));
            }

            customer.IsActive = false;
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
    }
}