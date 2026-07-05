using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class SuperAdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly TenantLimitGuard _limitGuard;
        private readonly ILogger<SuperAdminController> _logger;

        public SuperAdminController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            TenantLimitGuard limitGuard,
            ILogger<SuperAdminController> logger)
        {
            _context     = context;
            _userManager = userManager;
            _limitGuard  = limitGuard;
            _logger      = logger;
        }

        public async Task<IActionResult> Dashboard(
            int     tenantPage     = 1,  int tenantPageSize  = 10, string? tenantSearch = null,
            int     planPage       = 1,  int planPageSize    = 10, string? planSearch   = null)
        {
            ViewData["Title"] = "SuperAdmin Dashboard";

            // ── KPI summary counts (use EF COUNT — no full load) ────────────
            ViewBag.TotalTenants     = await _context.Tenants.CountAsync();
            ViewBag.ActiveTenants    = await _context.Tenants.CountAsync(t => t.Status == TenantStatus.Active);
            ViewBag.TrialTenants     = await _context.Tenants.CountAsync(t => t.Status == TenantStatus.Trial);
            ViewBag.SuspendedTenants = await _context.Tenants.CountAsync(t => t.Status == TenantStatus.Suspended);

            try { ViewBag.TotalBranches = await _context.Branches.CountAsync(b => b.IsActive); }
            catch { ViewBag.TotalBranches = 0; }

            try { ViewBag.TotalUsers = await _userManager.Users.CountAsync(u => u.IsActive); }
            catch { ViewBag.TotalUsers = 0; }

            try { ViewBag.TotalProducts = await _context.Items.CountAsync(); }
            catch { ViewBag.TotalProducts = 0; }

            // ── Tenant table ─────────────────────────────────────────────────
            tenantPageSize = PagedResult<Tenant>.ValidatePageSize(tenantPageSize);

            var tenantQuery = _context.Tenants
                .AsNoTracking()
                .Include(t => t.SubscriptionPlan)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(tenantSearch))
            {
                var term = tenantSearch.Trim();

                // Resolve enum values whose names contain the search term so the
                // status column can be searched without calling .ToString() in SQL.
                var matchingStatuses = Enum.GetValues<TenantStatus>()
                    .Where(s => s.ToString().Contains(term, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                tenantQuery = tenantQuery.Where(t =>
                    t.Name.Contains(term) ||
                    t.Code.Contains(term) ||
                    (t.SubscriptionPlan != null && t.SubscriptionPlan.Name.Contains(term)) ||
                    matchingStatuses.Contains(t.Status));
            }

            var tenantTotal = await tenantQuery.CountAsync();
            tenantPage = PagedResult<Tenant>.ValidatePageNumber(
                tenantPage, (int)Math.Ceiling(tenantTotal / (double)tenantPageSize));

            var pagedTenants = await tenantQuery
                .OrderByDescending(t => t.CreatedAtUtc)
                .Skip((tenantPage - 1) * tenantPageSize)
                .Take(tenantPageSize)
                .ToListAsync();

            // Load per-tenant usage only for the visible page rows so one
            // unreachable dedicated database cannot crash the dashboard.
            var usages = new List<TenantUsageSummary>();
            foreach (var t in pagedTenants)
            {
                try
                {
                    usages.Add(await _limitGuard.GetUsageAsync(t.Id));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Dashboard: could not load usage for tenant {TenantId} ({Code}).", t.Id, t.Code);

                    usages.Add(new TenantUsageSummary
                    {
                        TenantId    = t.Id,
                        MaxBranches = t.EffectiveMaxBranches,
                        MaxUsers    = t.EffectiveMaxUsers,
                        MaxProducts = t.EffectiveMaxProducts
                    });
                }
            }

            var tenantResult = new PagedResult<Tenant>
            {
                Items        = pagedTenants,
                PageNumber   = tenantPage,
                PageSize     = tenantPageSize,
                TotalRecords = tenantTotal,
                SearchTerm   = tenantSearch
            };

            // ── Subscription Plans table ─────────────────────────────────────
            planPageSize = PagedResult<SubscriptionPlan>.ValidatePageSize(planPageSize);

            var planQuery = _context.SubscriptionPlans
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(planSearch))
            {
                var term = planSearch.Trim();
                planQuery = planQuery.Where(p =>
                    p.Name.Contains(term) ||
                    (p.Description != null && p.Description.Contains(term)));
            }

            var planTotal = await planQuery.CountAsync();
            planPage = PagedResult<SubscriptionPlan>.ValidatePageNumber(
                planPage, (int)Math.Ceiling(planTotal / (double)planPageSize));

            var pagedPlans = await planQuery
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.MonthlyPrice)
                .Skip((planPage - 1) * planPageSize)
                .Take(planPageSize)
                .ToListAsync();

            var planResult = new PagedResult<SubscriptionPlan>
            {
                Items        = pagedPlans,
                PageNumber   = planPage,
                PageSize     = planPageSize,
                TotalRecords = planTotal,
                SearchTerm   = planSearch
            };

            // ── Build PaginationMeta (independent state per table) ───────────
            // Each table's ExtraParams carries the OTHER table's current state so
            // navigating one table never resets the other.
            var tenantPaginationMeta = PaginationMeta.ForSection(
                tenantResult, "SuperAdmin", "Dashboard",
                pageNumberParam: "tenantPage",
                pageSizeParam:   "tenantPageSize",
                extraParams: new Dictionary<string, string?>
                {
                    ["tenantSearch"] = tenantSearch,
                    ["planPage"]     = planPage.ToString(),
                    ["planPageSize"] = planPageSize.ToString(),
                    ["planSearch"]   = planSearch
                });

            var planPaginationMeta = PaginationMeta.ForSection(
                planResult, "SuperAdmin", "Dashboard",
                pageNumberParam: "planPage",
                pageSizeParam:   "planPageSize",
                extraParams: new Dictionary<string, string?>
                {
                    ["planSearch"]      = planSearch,
                    ["tenantPage"]      = tenantPage.ToString(),
                    ["tenantPageSize"]  = tenantPageSize.ToString(),
                    ["tenantSearch"]    = tenantSearch
                });

            ViewBag.Tenants              = pagedTenants;
            ViewBag.Usages               = usages;
            ViewBag.TenantResult         = tenantResult;
            ViewBag.TenantPaginationMeta = tenantPaginationMeta;
            ViewBag.PlanResult           = planResult;
            ViewBag.PlanPaginationMeta   = planPaginationMeta;

            return View();
        }
    }
}
