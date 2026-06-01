using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Services
{
    /// <summary>
    /// Checks per-tenant usage limits (branches, users, products).
    /// All checks are soft-checks — they return a friendly result; enforcement is
    /// done by the calling controller which shows a warning and blocks creation.
    ///
    /// Phase 5.0D.2 — this is a MIXED service:
    ///   * Tenant limit/plan metadata + user counts are PLATFORM data → shared ApplicationDbContext / UserManager.
    ///   * Branch and product counts are TENANT OPERATIONAL data → resolved via the operational
    ///     provider for the SPECIFIC tenant being inspected (GetContextAsync(tenantId)), so the
    ///     counts are correct even when a routing-enabled tenant lives in a dedicated database
    ///     and even when SuperAdmin is the caller.
    /// </summary>
    public class TenantLimitGuard
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ITenantOperationalContextProvider _operationalContextProvider;

        public TenantLimitGuard(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ITenantOperationalContextProvider operationalContextProvider)
        {
            _context = context;
            _userManager = userManager;
            _operationalContextProvider = operationalContextProvider;
        }

        public record LimitCheckResult(bool Allowed, string Message, int Current, int Max);

        public async Task<LimitCheckResult> CanAddBranchAsync(int tenantId)
        {
            var tenant = await _context.Tenants
                .AsNoTracking()
                .Include(t => t.SubscriptionPlan)
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
                return new LimitCheckResult(true, string.Empty, 0, int.MaxValue);

            var opDb     = await _operationalContextProvider.GetContextAsync(tenantId);
            var max     = tenant.EffectiveMaxBranches;
            var current = await opDb.Branches.CountAsync(b => b.TenantId == tenantId && b.IsActive);

            if (current >= max)
                return new LimitCheckResult(false,
                    $"Branch limit reached ({current}/{max}). Upgrade the subscription plan or increase the limit.",
                    current, max);

            return new LimitCheckResult(true, string.Empty, current, max);
        }

        public async Task<LimitCheckResult> CanAddUserAsync(int tenantId)
        {
            var tenant = await _context.Tenants
                .AsNoTracking()
                .Include(t => t.SubscriptionPlan)
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
                return new LimitCheckResult(true, string.Empty, 0, int.MaxValue);

            var max     = tenant.EffectiveMaxUsers;
            var current = await _userManager.Users.CountAsync(u => u.TenantId == tenantId && u.IsActive);

            if (current >= max)
                return new LimitCheckResult(false,
                    $"User limit reached ({current}/{max}). Upgrade the subscription plan or increase the limit.",
                    current, max);

            return new LimitCheckResult(true, string.Empty, current, max);
        }

        public async Task<LimitCheckResult> CanAddProductAsync(int tenantId)
        {
            var tenant = await _context.Tenants
                .AsNoTracking()
                .Include(t => t.SubscriptionPlan)
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
                return new LimitCheckResult(true, string.Empty, 0, int.MaxValue);

            var opDb     = await _operationalContextProvider.GetContextAsync(tenantId);
            var max     = tenant.EffectiveMaxProducts;
            var current = await opDb.Items.CountAsync(i => i.TenantId == tenantId);

            if (current >= max)
                return new LimitCheckResult(false,
                    $"Product limit reached ({current}/{max}). Upgrade the subscription plan or increase the limit.",
                    current, max);

            return new LimitCheckResult(true, string.Empty, current, max);
        }

        public async Task<TenantUsageSummary> GetUsageAsync(int tenantId)
        {
            var tenant = await _context.Tenants
                .AsNoTracking()
                .Include(t => t.SubscriptionPlan)
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
                return new TenantUsageSummary();

            var opDb = await _operationalContextProvider.GetContextAsync(tenantId);

            return new TenantUsageSummary
            {
                TenantId     = tenantId,
                TenantName   = tenant.Name,
                PlanName     = tenant.SubscriptionPlan?.Name ?? "Custom",
                Status       = tenant.Status,
                ExpirationDate = tenant.ExpirationDate,
                Branches     = await opDb.Branches.CountAsync(b => b.TenantId == tenantId && b.IsActive),
                MaxBranches  = tenant.EffectiveMaxBranches,
                Users        = await _userManager.Users.CountAsync(u => u.TenantId == tenantId && u.IsActive),
                MaxUsers     = tenant.EffectiveMaxUsers,
                Products     = await opDb.Items.CountAsync(i => i.TenantId == tenantId),
                MaxProducts  = tenant.EffectiveMaxProducts,
            };
        }
    }

    public class TenantUsageSummary
    {
        public int TenantId { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public string PlanName { get; set; } = string.Empty;
        public TenantStatus Status { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public int Branches  { get; set; }
        public int MaxBranches { get; set; }
        public int Users     { get; set; }
        public int MaxUsers  { get; set; }
        public int Products  { get; set; }
        public int MaxProducts { get; set; }
        public int BranchPct  => MaxBranches  > 0 ? Math.Min(100, Branches  * 100 / MaxBranches)  : 0;
        public int UserPct    => MaxUsers     > 0 ? Math.Min(100, Users     * 100 / MaxUsers)     : 0;
        public int ProductPct => MaxProducts  > 0 ? Math.Min(100, Products  * 100 / MaxProducts)  : 0;
    }
}
