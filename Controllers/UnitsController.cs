using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Units", "View")]
    public class UnitsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;

        public UnitsController(ApplicationDbContext context, AuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        public async Task<IActionResult> Index()
        {
            var units = await _context.Units
                .OrderBy(u => u.UnitName)
                .ToListAsync();

            return View(units);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string unitName, string shortName, string? unitType, string? description, bool allowsDecimal)
        {
            if (string.IsNullOrWhiteSpace(unitName) || string.IsNullOrWhiteSpace(shortName))
            {
                TempData["ErrorMessage"] = "Unit name and short name are required.";
                return RedirectToAction(nameof(Index));
            }

            var exists = await _context.Units.AnyAsync(u =>
                u.UnitName == unitName.Trim() || u.ShortName == shortName.Trim());

            if (exists)
            {
                TempData["ErrorMessage"] = "Unit name or short name already exists.";
                return RedirectToAction(nameof(Index));
            }

            var unit = new Unit
            {
                UnitName = unitName.Trim(),
                ShortName = shortName.Trim(),
                UnitType = unitType,
                Description = description,
                AllowsDecimal = allowsDecimal,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            _context.Units.Add(unit);
            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
                User,
                "Units",
                "CREATED",
                $"Unit created. Name: {unit.UnitName}, Short: {unit.ShortName}",
                "Unit",
                unit.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Unit added successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, string unitName, string shortName, string? unitType, string? description, bool allowsDecimal, bool isActive)
        {
            var unit = await _context.Units.FindAsync(id);

            if (unit == null)
            {
                TempData["ErrorMessage"] = "Unit not found.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(unitName) || string.IsNullOrWhiteSpace(shortName))
            {
                TempData["ErrorMessage"] = "Unit name and short name are required.";
                return RedirectToAction(nameof(Index));
            }

            var exists = await _context.Units.AnyAsync(u =>
                u.Id != id &&
                (u.UnitName == unitName.Trim() || u.ShortName == shortName.Trim()));

            if (exists)
            {
                TempData["ErrorMessage"] = "Unit name or short name already exists.";
                return RedirectToAction(nameof(Index));
            }

            unit.UnitName = unitName.Trim();
            unit.ShortName = shortName.Trim();
            unit.UnitType = unitType;
            unit.Description = description;
            unit.AllowsDecimal = allowsDecimal;
            unit.IsActive = isActive;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
                User,
                "Units",
                "UPDATED",
                $"Unit deactivated. Name: {unit.UnitName}",
                "Unit",
                unit.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );
            TempData["SuccessMessage"] = "Unit updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(int id)
        {
            var unit = await _context.Units.FindAsync(id);

            if (unit == null)
            {
                TempData["ErrorMessage"] = "Unit not found.";
                return RedirectToAction(nameof(Index));
            }

            unit.IsActive = false;
            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
                User,
                "Units",
                "DEACTIVATED",
                $"Unit deactivated. Name: {unit.UnitName}",
                "Unit",
                unit.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Unit deactivated successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}