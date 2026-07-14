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
    [PermissionAuthorize("Reports", "View")]
    public class AuditTrailController : OperationalDbController
    {
        // AuditTrails are tenant operational data → read through routed _context:
        //   • Routing-enabled tenant user → sees their dedicated audit records.
        //   • Normal tenant / SuperAdmin   → shared ApplicationDbContext (platform-level audit).
        // The Tenants lookup is platform metadata → always read from shared _platformDb.
        private readonly ApplicationDbContext _platformDb;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        public AuditTrailController(
            ITenantOperationalContextProvider ctxProvider,
            ApplicationDbContext platformDb,
            ITenantContext tenantContext,
            TenantGuard tenantGuard)
            : base(ctxProvider)
        {
            _platformDb = platformDb;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
        }

        public async Task<IActionResult> Index(
            int pageNumber = 1,
            int pageSize = 10,
            string? searchTerm = null,
            string? moduleFilter = null,
            string? actionFilter = null,
            string? userFilter = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null,
            int? tenantFilter = null)
        {
            pageSize = PagedResult<object>.ValidatePageSize(pageSize);

            var isSuperAdmin = User.IsInRole("SuperAdmin");
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var from = dateFrom?.Date;
            var to = dateTo?.Date.AddDays(1);

            var query = _context.AuditTrails
                .AsNoTracking()
                .AsQueryable();

            // Tenant scoping: SuperAdmin/global users see all; normal users see their tenant only
            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(a => a.TenantId == tenantId);
            }
            else if (!_tenantContext.IsGlobalUser)
            {
                query = query.Where(a => false);
            }
            else if (isSuperAdmin && tenantFilter.HasValue)
            {
                // SuperAdmin applied a specific tenant filter
                query = query.Where(a => a.TenantId == tenantFilter.Value);
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                query = query.Where(a =>
                    (a.UserName != null && a.UserName.ToLower().Contains(term)) ||
                    a.ModuleName.ToLower().Contains(term) ||
                    a.ActionName.ToLower().Contains(term) ||
                    a.Description.ToLower().Contains(term) ||
                    (a.ReferenceId != null && a.ReferenceId.ToLower().Contains(term)));
            }

            if (!string.IsNullOrWhiteSpace(moduleFilter) && moduleFilter != "all")
                query = query.Where(a => a.ModuleName.ToLower() == moduleFilter.ToLower());

            if (!string.IsNullOrWhiteSpace(actionFilter) && actionFilter != "all")
                query = query.Where(a => a.ActionName.ToLower().Contains(actionFilter.ToLower()));

            if (!string.IsNullOrWhiteSpace(userFilter))
            {
                var u = userFilter.Trim().ToLower();
                query = query.Where(a => a.UserName != null && a.UserName.ToLower().Contains(u));
            }

            if (from.HasValue)
                query = query.Where(a => a.CreatedAt >= from.Value);

            if (to.HasValue)
                query = query.Where(a => a.CreatedAt < to.Value);

            var totalRecords = await query.CountAsync();

            pageNumber = PagedResult<object>.ValidatePageNumber(pageNumber,
                (int)Math.Ceiling(totalRecords / (double)pageSize));

            var logs = await query
                .OrderByDescending(a => a.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.ModuleFilter  = moduleFilter;
            ViewBag.ActionFilter  = actionFilter;
            ViewBag.UserFilter    = userFilter;
            ViewBag.DateFrom      = dateFrom;
            ViewBag.DateTo        = dateTo;
            ViewBag.TenantFilter  = tenantFilter;
            ViewBag.IsSuperAdmin  = isSuperAdmin;

            // Load tenant lookup for SuperAdmin: TenantId → Name
            if (isSuperAdmin)
            {
                var tenants = await _platformDb.Tenants
                    .AsNoTracking()
                    .OrderBy(t => t.Name)
                    .Select(t => new { t.Id, t.Name })
                    .ToListAsync();

                ViewBag.Tenants = tenants;

                // Build a quick lookup dict for displaying tenant name in table rows
                ViewBag.TenantMap = tenants.ToDictionary(t => t.Id, t => t.Name);
            }

            return View(new PagedResult<AuditTrail>
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
