using HardwareManagementSystem.Data;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HardwareManagementSystem.Controllers
{
    /// <summary>
    /// Phase 5.0D.1 — base controller for routed operational modules
    /// (Items/Products, Categories, Units, Inventory, Stock-In, POS, Sales,
    /// Reports, Dashboard KPIs).
    ///
    /// Before every action executes, <see cref="_context"/> is resolved from
    /// <see cref="ITenantOperationalContextProvider"/>:
    ///   • Routing-enabled tenant (Dedicated + Provisioned + DataMigrated + RoutingEnabled)
    ///     → its dedicated <c>TenantDbContext</c>.
    ///   • Every other tenant / SuperAdmin → the shared <c>ApplicationDbContext</c>
    ///     (exactly the same instance DI would have injected — behavior unchanged).
    ///
    /// Because <see cref="_context"/> is exposed as <see cref="ITenantOperationalDbContext"/>,
    /// the existing query/command code in derived controllers continues to compile and run
    /// unchanged. Platform-only data (Tenants/SubscriptionPlans/Identity) must NOT be read
    /// through this context — controllers that need it inject <c>ApplicationDbContext</c>
    /// separately for those reads.
    /// </summary>
    public abstract class OperationalDbController : Controller
    {
        private readonly ITenantOperationalContextProvider _operationalContextProvider;

        /// <summary>
        /// Operational database context for the current tenant. Populated per request in
        /// <see cref="OnActionExecutionAsync"/>; never null while an action is executing.
        /// </summary>
        protected ITenantOperationalDbContext _context = null!;

        protected OperationalDbController(ITenantOperationalContextProvider operationalContextProvider)
        {
            _operationalContextProvider = operationalContextProvider;
        }

        public override async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            _context = await _operationalContextProvider.GetContextAsync();
            await base.OnActionExecutionAsync(context, next);
        }
    }
}
