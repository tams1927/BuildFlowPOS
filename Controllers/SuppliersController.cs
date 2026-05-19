using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Suppliers", "View")]
    public class SuppliersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;

        public SuppliersController(ApplicationDbContext context, AuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        public async Task<IActionResult> Index()
        {
            var suppliers = await _context.Suppliers
                .OrderBy(s => s.SupplierName)
                .ToListAsync();

            return View(suppliers);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string supplierName,
            string? contactPerson,
            string? contactNumber,
            string? email,
            string? address,
            string? remarks)
        {
            if (string.IsNullOrWhiteSpace(supplierName))
            {
                TempData["ErrorMessage"] = "Supplier name is required.";
                return RedirectToAction(nameof(Index));
            }

            var exists = await _context.Suppliers
                .AnyAsync(s => s.SupplierName == supplierName.Trim());

            if (exists)
            {
                TempData["ErrorMessage"] = "Supplier already exists.";
                return RedirectToAction(nameof(Index));
            }

            var supplier = new Supplier
            {
                SupplierName = supplierName.Trim(),
                ContactPerson = contactPerson,
                ContactNumber = contactNumber,
                Email = email,
                Address = address,
                Remarks = remarks,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            _context.Suppliers.Add(supplier);
            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
            User,
            "Suppliers",
            "CREATED",
            $"Supplier created. Name: {supplier.SupplierName}",
            "Supplier",
            supplier.Id.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );
            TempData["SuccessMessage"] = "Supplier added successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            string supplierName,
            string? contactPerson,
            string? contactNumber,
            string? email,
            string? address,
            string? remarks,
            bool isActive)
        {
            var supplier = await _context.Suppliers.FindAsync(id);

            if (supplier == null)
            {
                TempData["ErrorMessage"] = "Supplier not found.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(supplierName))
            {
                TempData["ErrorMessage"] = "Supplier name is required.";
                return RedirectToAction(nameof(Index));
            }

            var exists = await _context.Suppliers.AnyAsync(s =>
                s.Id != id &&
                s.SupplierName == supplierName.Trim());

            if (exists)
            {
                TempData["ErrorMessage"] = "Supplier name already exists.";
                return RedirectToAction(nameof(Index));
            }

            supplier.SupplierName = supplierName.Trim();
            supplier.ContactPerson = contactPerson;
            supplier.ContactNumber = contactNumber;
            supplier.Email = email;
            supplier.Address = address;
            supplier.Remarks = remarks;
            supplier.IsActive = isActive;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
                User,
                "Suppliers",
                "UPDATED",
                $"Supplier deactivated. Name: {supplier.SupplierName}",
                "Supplier",
                supplier.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );
            TempData["SuccessMessage"] = "Supplier updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);

            if (supplier == null)
            {
                TempData["ErrorMessage"] = "Supplier not found.";
                return RedirectToAction(nameof(Index));
            }

            supplier.IsActive = false;
            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
                User,
                "Suppliers",
                "DEACTIVATED",
                $"Supplier deactivated. Name: {supplier.SupplierName}",
                "Supplier",
                supplier.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );
            TempData["SuccessMessage"] = "Supplier deactivated successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}