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
    /// Global action filter enforcing tenant subscription access on every protected request.
    /// SuperAdmin and Account controller are exempt.
    /// </summary>
    public class TenantStatusFilter : IAsyncActionFilter
    {
        private readonly ApplicationDbContext _db;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IMemoryCache _cache;
        private readonly ITenantSubscriptionAccessService _subscriptionAccess;

        public TenantStatusFilter(
            ApplicationDbContext db,
            SignInManager<ApplicationUser> signInManager,
            IMemoryCache cache,
            ITenantSubscriptionAccessService subscriptionAccess)
        {
            _db = db;
            _signInManager = signInManager;
            _cache = cache;
            _subscriptionAccess = subscriptionAccess;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var user = context.HttpContext.User;

            if (user.Identity?.IsAuthenticated != true)
            {
                await next();
                return;
            }

            if (user.IsInRole("SuperAdmin"))
            {
                await next();
                return;
            }

            if (context.ActionDescriptor is ControllerActionDescriptor cadAccount &&
                cadAccount.ControllerName.Equals("Account", StringComparison.OrdinalIgnoreCase))
            {
                var publicActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Login", "Logout", "AccessDenied", "SubscriptionExpired", "Suspended"
                };
                if (publicActions.Contains(cadAccount.ActionName))
                {
                    await next();
                    return;
                }
            }

            if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            {
                await next();
                return;
            }

            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                await next();
                return;
            }

            var tenantIdKey = TenantSubscriptionCacheKeys.UserTenant(userId);
            if (!_cache.TryGetValue(tenantIdKey, out int? tenantId))
            {
                tenantId = await _db.Users
                    .AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => (int?)u.TenantId)
                    .FirstOrDefaultAsync();

                _cache.Set(tenantIdKey, tenantId, TimeSpan.FromMinutes(15));
            }

            if (!tenantId.HasValue)
            {
                await next();
                return;
            }

            var accessKey = TenantSubscriptionCacheKeys.Access(tenantId.Value);
            if (!_cache.TryGetValue(accessKey, out TenantSubscriptionAccessResult? access))
            {
                var tenant = await _db.Tenants
                    .AsNoTracking()
                    .Where(t => t.Id == tenantId.Value)
                    .Select(t => new Tenant
                    {
                        Id = t.Id,
                        Name = t.Name,
                        IsActive = t.IsActive,
                        Status = t.Status,
                        ExpirationDate = t.ExpirationDate,
                        SubscriptionPlanId = t.SubscriptionPlanId
                    })
                    .FirstOrDefaultAsync();

                access = tenant == null
                    ? new TenantSubscriptionAccessResult
                    {
                        IsAllowed = true,
                        ReasonCode = SubscriptionAccessReasonCode.Allowed
                    }
                    : _subscriptionAccess.Evaluate(tenant);

                _cache.Set(accessKey, access, TimeSpan.FromMinutes(2));
            }

            if (access!.IsAllowed)
            {
                await next();
                return;
            }

            var tenantName = await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId.Value)
                .Select(t => t.Name)
                .FirstOrDefaultAsync() ?? "Your organization";

            await DenyAccessAsync(context, access, tenantName);
        }

        private async Task DenyAccessAsync(
            ActionExecutingContext context,
            TenantSubscriptionAccessResult access,
            string tenantName)
        {
            await _signInManager.SignOutAsync();

            context.HttpContext.Session.SetString("SubAccess_TenantName", tenantName);
            context.HttpContext.Session.SetString("SubAccess_Status", access.EffectiveStatus.ToString());
            context.HttpContext.Session.SetString("SubAccess_Message", access.Message);
            if (access.ExpirationDate.HasValue)
                context.HttpContext.Session.SetString("SubAccess_Expiration",
                    access.ExpirationDate.Value.ToString("yyyy-MM-dd"));

            if (IsApiOrAjaxRequest(context.HttpContext.Request))
            {
                context.Result = new JsonResult(new
                {
                    error = access.Message,
                    code = access.ReasonCode.ToString(),
                    status = access.EffectiveStatus.ToString(),
                    requiresRenewal = access.RequiresRenewal
                })
                { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }

            context.Result = new RedirectToActionResult("SubscriptionExpired", "Account", null);
        }

        private static bool IsApiOrAjaxRequest(HttpRequest request) =>
            request.Headers.XRequestedWith == "XMLHttpRequest"
            || (request.Headers.Accept.ToString()?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
