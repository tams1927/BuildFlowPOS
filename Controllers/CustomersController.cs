using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
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

        public async Task<IActionResult> Index()
        {
            var customers = await _context.Customers
                .OrderBy(c => c.CustomerName)
                .ToListAsync();

            return View(customers);
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