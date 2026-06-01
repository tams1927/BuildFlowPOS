using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;

namespace HardwareManagementSystem.Services
{
    /// <summary>
    /// Global action filter that signs out and redirects already-logged-in tenant users
    /// if their tenant becomes Suspended or Expired mid-session.
    ///
    /// SuperAdmin (TenantId == null) is fully exempt.
    /// Static files, /health, and the Account controller are exempt automatically
    /// (static files never reach MVC action filters; /health is a minimal-API endpoint).
    /// </summary>
    public class TenantStatusFilter : IAsyncActionFilter
    {
        private readonly ApplicationDbContext _db;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IMemoryCache _cache;

        public TenantStatusFilter(
            ApplicationDbContext db,
            SignInManager<ApplicationUser> signInManager,
            IMemoryCache cache)
        {
            _db = db;
            _signInManager = signInManager;
            _cache = cache;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var user = context.HttpContext.User;

            // Pass through: unauthenticated users
            if (user.Identity?.IsAuthenticated != true)
            {
                await next();
                return;
            }

            // Pass through: SuperAdmin is a global platform user — never scoped to a tenant
            if (user.IsInRole("SuperAdmin"))
            {
                await next();
                return;
            }

            // Pass through: Account controller (Login, Logout, Suspended, AccessDenied, etc.)
            if (context.ActionDescriptor is ControllerActionDescriptor cad &&
                cad.ControllerName.Equals("Account", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            // Pass through: actions decorated with [AllowAnonymous]
            if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            {
                await next();
                return;
            }

            // ── Resolve user's tenant ────────────────────────────────────────────
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                await next();
                return;
            }

            // Cache the tenantId per userId so we avoid a DB hit on every request.
            var tenantIdKey = $"tid_uid_{userId}";
            if (!_cache.TryGetValue(tenantIdKey, out int? tenantId))
            {
                tenantId = await _db.Users
                    .AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => (int?)u.TenantId)
                    .FirstOrDefaultAsync();

                _cache.Set(tenantIdKey, tenantId, TimeSpan.FromMinutes(15));
            }

            // No tenant → global user (Admin), pass through
            if (!tenantId.HasValue)
            {
                await next();
                return;
            }

            // ── Check tenant status ───────────────────────────────────────────────
            // Short 5-minute cache so suspension takes effect within a reasonable window
            // without hammering the DB on every page load.
            var blockedKey = $"tenant_blocked_{tenantId.Value}";
            if (!_cache.TryGetValue(blockedKey, out bool isBlocked))
            {
                var tenant = await _db.Tenants
                    .AsNoTracking()
                    .Where(t => t.Id == tenantId.Value)
                    .Select(t => new { t.Status, t.ExpirationDate })
                    .FirstOrDefaultAsync();

                if (tenant == null)
                {
                    isBlocked = false;
                }
                else
                {
                    bool dateExpired = tenant.ExpirationDate.HasValue
                        && tenant.ExpirationDate.Value.Date < DateTime.UtcNow.Date;

                    isBlocked = tenant.Status == TenantStatus.Suspended
                             || tenant.Status == TenantStatus.Expired
                             || dateExpired;
                }

                _cache.Set(blockedKey, isBlocked, TimeSpan.FromMinutes(5));
            }

            if (isBlocked)
            {
                await _signInManager.SignOutAsync();
                context.Result = new RedirectToActionResult("Suspended", "Account", null);
                return;
            }

            await next();
        }
    }
}
