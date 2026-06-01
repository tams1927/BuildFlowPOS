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
    [PermissionAuthorize("Categories", "View")]
    public class CategoriesController : OperationalDbController
    {
        private readonly AuditService _auditService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public CategoriesController(
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

            var query = _context.Categories
                .AsNoTracking()
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(c =>
                    c.TenantId == tenantId ||
                    c.TenantId == null);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();

                query = query.Where(c =>
                    c.CategoryName.ToLower().Contains(term) ||
                    (c.Description != null &&
                     c.Description.ToLower().Contains(term)));
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

            var categories = await query
                .OrderBy(c => c.CategoryName)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.StatusFilter = statusFilter;

            return View(new PagedResult<Category>
            {
                Items = categories,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Categories", "Create")]
        public async Task<IActionResult> Create(
            string categoryName,
            string? description)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (string.IsNullOrWhiteSpace(categoryName))
            {
                TempData["ErrorMessage"] = "Category name is required.";
                return RedirectToAction(nameof(Index));
            }

            var name = categoryName.Trim();

            var duplicateQuery = _context.Categories.AsQueryable();

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                duplicateQuery = duplicateQuery.Where(c =>
                    c.TenantId == tenantId ||
                    c.TenantId == null);
            }

            if (await duplicateQuery.AnyAsync(c => c.CategoryName == name))
            {
                TempData["ErrorMessage"] = $"Category '{name}' already exists.";
                return RedirectToAction(nameof(Index));
            }

            var category = new Category
            {
                TenantId = tenantId,
                CategoryName = name,
                Description = description,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            _context.Categories.Add(category);

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Categories",
                "CREATED",
                $"Category created. Name: {category.CategoryName}",
                "Category",
                category.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Category added successfully.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Categories", "Edit")]
        public async Task<IActionResult> Edit(
            int id,
            string categoryName,
            string? description,
            bool isActive)
        {
            var category = await _context.Categories.FindAsync(id);

            if (category == null)
            {
                TempData["ErrorMessage"] = "Category not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(category.TenantId))
            {
                return Forbid();
            }

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            if (string.IsNullOrWhiteSpace(categoryName))
            {
                TempData["ErrorMessage"] = "Category name is required.";
                return RedirectToAction(nameof(Index));
            }

            var name = categoryName.Trim();

            var duplicateQuery = _context.Categories
                .Where(c => c.Id != id && c.CategoryName == name);

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                duplicateQuery = duplicateQuery.Where(c =>
                    c.TenantId == tenantId ||
                    c.TenantId == null);
            }

            if (await duplicateQuery.AnyAsync())
            {
                TempData["ErrorMessage"] = $"Category '{name}' already exists.";
                return RedirectToAction(nameof(Index));
            }

            category.CategoryName = name;
            category.Description = description;
            category.IsActive = isActive;

            if (!category.TenantId.HasValue && tenantId.HasValue)
            {
                category.TenantId = tenantId;
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Categories",
                "UPDATED",
                $"Category updated. Name: {category.CategoryName}",
                "Category",
                category.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Category updated successfully.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Categories", "Delete")]
        public async Task<IActionResult> Deactivate(int id)
        {
            var category = await _context.Categories.FindAsync(id);

            if (category == null)
            {
                TempData["ErrorMessage"] = "Category not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!await _tenantGuard.CanAccessTenantAsync(category.TenantId))
            {
                return Forbid();
            }

            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            category.IsActive = false;

            if (!category.TenantId.HasValue && tenantId.HasValue)
            {
                category.TenantId = tenantId;
            }

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Categories",
                "DEACTIVATED",
                $"Category deactivated. Name: {category.CategoryName}",
                "Category",
                category.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "Category deactivated successfully.";

            return RedirectToAction(nameof(Index));
        }
    }
}