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
    [PermissionAuthorize("Units", "View")]
    public class UnitsController : OperationalDbController
    {
        private readonly AuditService _auditService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public UnitsController(
            ITenantOperationalContextProvider ctxProvider,
            AuditService auditService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard)
            : base(ctxProvider)
        {
            _auditService = auditService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
        }

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            string? statusFilter = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var query = _context.Units
                .AsNoTracking()
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(u =>
                    u.TenantId == tenantId ||
                    u.TenantId == null);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(u =>
                    u.UnitName.ToLower().Contains(term) ||
                    u.ShortName.ToLower().Contains(term) ||
                    (u.UnitType != null &&
                     u.UnitType.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                bool isActive = statusFilter == "active";
                query = query.Where(u => u.IsActive == isActive);
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(
                pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var units = await query
                .OrderBy(u => u.UnitName)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.StatusFilter = statusFilter;

            return View(new PagedResult<Unit>
            {
                Items = units,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Units", "Create")]
        public async Task<IActionResult> Create(
            string unitName,
            string shortName,
            string? unitType,
            string? description,
            bool allowsDecimal)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (string.IsNullOrWhiteSpace(unitName) ||
                string.IsNullOrWhiteSpace(shortName))
            {
                TempData["ErrorMessage"] =
                    "Unit name and short name are required.";

                return RedirectToAction(nameof(Index));
            }

            var name = unitName.Trim();
            var shortCode = shortName.Trim();

            var duplicateQuery = _context.Units.AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                duplicateQuery = duplicateQuery.Where(u =>
                    u.TenantId == tenantId ||
                    u.TenantId == null);
            }

            var exists = await duplicateQuery.AnyAsync(u =>
                u.UnitName == name ||
                u.ShortName == shortCode);

            if (exists)
            {
                TempData["ErrorMessage"] =
                    "Unit name or short name already exists.";

                return RedirectToAction(nameof(Index));
            }

            var unit = new Unit
            {
                TenantId = tenantId,
                UnitName = name,
                ShortName = shortCode,
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
        [PermissionAuthorize("Units", "Edit")]
        public async Task<IActionResult> Edit(
            int id,
            string unitName,
            string shortName,
            string? unitType,
            string? description,
            bool allowsDecimal,
            bool isActive)
        {
            var unit = await _context.Units.FindAsync(id);

            if (unit == null)
            {
                TempData["ErrorMessage"] = "Unit not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(unit.TenantId))
            {
                return Forbid();
            }

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (string.IsNullOrWhiteSpace(unitName) ||
                string.IsNullOrWhiteSpace(shortName))
            {
                TempData["ErrorMessage"] =
                    "Unit name and short name are required.";

                return RedirectToAction(nameof(Index));
            }

            var name = unitName.Trim();
            var shortCode = shortName.Trim();

            var duplicateQuery = _context.Units
                .Where(u =>
                    u.Id != id &&
                    (u.UnitName == name ||
                     u.ShortName == shortCode));

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                duplicateQuery = duplicateQuery.Where(u =>
                    u.TenantId == tenantId ||
                    u.TenantId == null);
            }

            if (await duplicateQuery.AnyAsync())
            {
                TempData["ErrorMessage"] =
                    "Unit name or short name already exists.";

                return RedirectToAction(nameof(Index));
            }

            unit.UnitName = name;
            unit.ShortName = shortCode;
            unit.UnitType = unitType;
            unit.Description = description;
            unit.AllowsDecimal = allowsDecimal;
            unit.IsActive = isActive;

            if (!unit.TenantId.HasValue && tenantId.HasValue)
            {
                unit.TenantId = tenantId;
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Units",
                "UPDATED",
                $"Unit updated. Name: {unit.UnitName}",
                "Unit",
                unit.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Unit updated successfully.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Units", "Delete")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var unit = await _context.Units.FindAsync(id);

            if (unit == null)
            {
                TempData["ErrorMessage"] = "Unit not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(unit.TenantId))
            {
                return Forbid();
            }

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            unit.IsActive = false;

            if (!unit.TenantId.HasValue && tenantId.HasValue)
            {
                unit.TenantId = tenantId;
            }

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