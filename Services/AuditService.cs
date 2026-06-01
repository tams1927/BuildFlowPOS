using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace HardwareManagementSystem.Services
{
    public class AuditService
    {
        // Phase 5.0D.2 — audit rows are tenant-owned operational data. They route to the
        // tenant's dedicated database when routing is active, and to the shared database
        // otherwise (including all SuperAdmin/platform actions where CurrentTenantId is null).
        private readonly ITenantOperationalContextProvider _operationalContextProvider;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ITenantContext _tenantContext;

        public AuditService(
            ITenantOperationalContextProvider operationalContextProvider,
            IHttpContextAccessor httpContextAccessor,
            ITenantContext tenantContext)
        {
            _operationalContextProvider = operationalContextProvider;
            _httpContextAccessor = httpContextAccessor;
            _tenantContext = tenantContext;
        }

        public async Task LogAsync(
            ClaimsPrincipal user,
            string moduleName,
            string actionName,
            string description,
            string? referenceType = null,
            string? referenceId = null,
            string? ipAddress = null)
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var userName = user.Identity?.Name ?? "Unknown";

            // ============================================
            // DEVICE / BROWSER DETECTION
            // ============================================

            var userAgent = _httpContextAccessor.HttpContext?
                .Request
                .Headers["User-Agent"]
                .ToString();

            var browser = "Unknown";
            var operatingSystem = "Unknown";
            var deviceType = "Desktop";

            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                // Browser
                if (userAgent.Contains("Edg"))
                    browser = "Microsoft Edge";

                else if (userAgent.Contains("Chrome"))
                    browser = "Chrome";

                else if (userAgent.Contains("Firefox"))
                    browser = "Firefox";

                else if (userAgent.Contains("Safari"))
                    browser = "Safari";

                // Operating System
                if (userAgent.Contains("Windows"))
                    operatingSystem = "Windows";

                else if (userAgent.Contains("Android"))
                    operatingSystem = "Android";

                else if (userAgent.Contains("iPhone"))
                    operatingSystem = "iPhone";

                else if (userAgent.Contains("Mac"))
                    operatingSystem = "MacOS";

                else if (userAgent.Contains("Linux"))
                    operatingSystem = "Linux";

                // Device Type
                if (userAgent.Contains("Mobile") ||
                    userAgent.Contains("Android") ||
                    userAgent.Contains("iPhone"))
                {
                    deviceType = "Mobile";
                }
            }

            // ============================================
            // SAVE AUDIT
            // ============================================

            var audit = new AuditTrail
            {
                UserId = userId,
                UserName = userName,

                ModuleName = moduleName,
                ActionName = actionName,

                Description = description,

                ReferenceType = referenceType,
                ReferenceId = referenceId,

                IpAddress = ipAddress,

                Browser = browser,
                OperatingSystem = operatingSystem,
                DeviceType = deviceType,
                UserAgent = userAgent,

                TenantId = _tenantContext.CurrentTenantId,

                CreatedAt = DateTime.Now
            };

            var db = await _operationalContextProvider.GetContextAsync();

            db.AuditTrails.Add(audit);

            await db.SaveChangesAsync();
        }
    }
}