using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
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

        public SuperAdminController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            TenantLimitGuard limitGuard)
        {
            _context = context;
            _userManager = userManager;
            _limitGuard = limitGuard;
        }

        public async Task<IActionResult> Dashboard()
        {
            ViewData["Title"] = "SuperAdmin Dashboard";

            var tenants = await _context.Tenants
                .AsNoTracking()
                .Include(t => t.SubscriptionPlan)
                .OrderBy(t => t.Name)
                .ToListAsync();

            // Usage per tenant (lightweight: just counts from DB)
            var usages = new List<TenantUsageSummary>();
            foreach (var t in tenants)
            {
                var u = await _limitGuard.GetUsageAsync(t.Id);
                usages.Add(u);
            }

            ViewBag.Tenants    = tenants;
            ViewBag.Usages     = usages;
            ViewBag.TotalTenants  = tenants.Count;
            ViewBag.ActiveTenants = tenants.Count(t => t.Status == TenantStatus.Active);
            ViewBag.TrialTenants  = tenants.Count(t => t.Status == TenantStatus.Trial);
            ViewBag.SuspendedTenants = tenants.Count(t => t.Status == TenantStatus.Suspended);

            ViewBag.TotalBranches = await _context.Branches.CountAsync(b => b.IsActive);
            ViewBag.TotalUsers    = await _userManager.Users.CountAsync(u => u.IsActive);
            ViewBag.TotalProducts = await _context.Items.CountAsync();

            var plans = await _context.SubscriptionPlans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync();
            ViewBag.Plans = plans;

            return View();
        }
    }
}
