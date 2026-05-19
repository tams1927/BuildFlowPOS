using HardwareManagementSystem.Data;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Categories", "View")]
    public class CategoriesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;
        public CategoriesController(ApplicationDbContext context, AuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        public async Task<IActionResult> Index()
        {
            var categories = await _context.Categories
                .OrderBy(c => c.CategoryName)
                .ToListAsync();

            return View(categories);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string categoryName, string? description)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                TempData["ErrorMessage"] = "Category name is required.";
                return RedirectToAction(nameof(Index));
            }

            var category = new Models.Category
            {
                CategoryName = categoryName.Trim(),
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
        public async Task<IActionResult> Edit(int id, string categoryName, string? description, bool isActive)
        {
            var category = await _context.Categories.FindAsync(id);

            if (category == null)
            {
                TempData["ErrorMessage"] = "Category not found.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(categoryName))
            {
                TempData["ErrorMessage"] = "Category name is required.";
                return RedirectToAction(nameof(Index));
            }

            category.CategoryName = categoryName.Trim();
            category.Description = description;
            category.IsActive = isActive;

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
        public async Task<IActionResult> Deactivate(int id)
        {
            var category = await _context.Categories.FindAsync(id);

            if (category == null)
            {
                TempData["ErrorMessage"] = "Category not found.";
                return RedirectToAction(nameof(Index));
            }

            category.IsActive = false;

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