using HardwareManagementSystem.Data;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Reports", "View")]
    public class AuditTrailController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AuditTrailController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            string? moduleFilter = null,
            string? actionFilter = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var query = _context.AuditTrails
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(a =>
                    (a.UserName != null && a.UserName.ToLower().Contains(term)) ||
                    a.ModuleName.ToLower().Contains(term) ||
                    a.ActionName.ToLower().Contains(term) ||
                    a.Description.ToLower().Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(moduleFilter) && moduleFilter != "all")
            {
                query = query.Where(a => a.ModuleName.ToLower() == moduleFilter.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(actionFilter) && actionFilter != "all")
            {
                query = query.Where(a => a.ActionName.ToLower().Contains(actionFilter.ToLower()));
            }

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var logs = await query
                .OrderByDescending(a => a.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.ModuleFilter = moduleFilter;
            ViewBag.ActionFilter = actionFilter;

            return View(new PagedResult<HardwareManagementSystem.Models.AuditTrail>
            {
                Items = logs,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords,
                SearchTerm = searchTerm
            });
        }
    }
}