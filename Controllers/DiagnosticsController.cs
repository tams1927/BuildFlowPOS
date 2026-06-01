using HardwareManagementSystem.Data;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    /// <summary>
    /// Phase 5.0D.1 — temporary runtime-routing verification tooling (SuperAdmin only).
    ///
    /// Confirms, without opening a business screen, which physical database a tenant's
    /// operational requests resolve to at runtime:
    ///   • Dedicated → <c>TenantDbContext</c>
    ///   • Shared    → <c>ApplicationDbContext</c>
    ///
    /// This controller is read-only. It never moves data and never enables/disables routing.
    /// </summary>
    [Authorize(Roles = "SuperAdmin")]
    public class DiagnosticsController : Controller
    {
        private readonly ApplicationDbContext _platformDb;
        private readonly ITenantDatabaseResolver _resolver;
        private readonly AuditService _auditService;

        public DiagnosticsController(
            ApplicationDbContext platformDb,
            ITenantDatabaseResolver resolver,
            AuditService auditService)
        {
            _platformDb = platformDb;
            _resolver = resolver;
            _auditService = auditService;
        }

        /// <summary>
        /// GET /Diagnostics/RuntimeDatabase[?tenantId=4]
        /// Returns the resolved runtime database for a single tenant (when tenantId is
        /// supplied) or for every tenant. Logs TENANT_RUNTIME_ROUTING_VERIFIED on success
        /// and TENANT_RUNTIME_ROUTING_FAILED if resolution throws.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> RuntimeDatabase(int? tenantId = null)
        {
            var ip = HttpContext?.Connection?.RemoteIpAddress?.ToString();

            try
            {
                var tenantsQuery = _platformDb.Tenants.AsNoTracking().AsQueryable();
                if (tenantId.HasValue)
                    tenantsQuery = tenantsQuery.Where(t => t.Id == tenantId.Value);

                var tenants = await tenantsQuery
                    .OrderBy(t => t.Id)
                    .Select(t => new { t.Id, t.Name, t.Code })
                    .ToListAsync();

                if (tenantId.HasValue && tenants.Count == 0)
                    return NotFound(new { message = $"Tenant {tenantId} not found." });

                var results = new List<RuntimeDatabaseVm>();
                foreach (var t in tenants)
                {
                    var runtime = await _resolver.GetRuntimeDatabaseAsync(t.Id); // "Dedicated" | "Shared"
                    var dbName  = await _resolver.GetDatabaseNameAsync(t.Id);
                    var dedicated = string.Equals(runtime, "Dedicated", StringComparison.OrdinalIgnoreCase);

                    results.Add(new RuntimeDatabaseVm
                    {
                        TenantId        = t.Id,
                        TenantName      = t.Name,
                        TenantCode      = t.Code,
                        CurrentContext  = dedicated ? "TenantDbContext" : "ApplicationDbContext",
                        ConnectionType  = dedicated ? "Dedicated" : "Shared",
                        DatabaseName    = dbName,
                        RoutingActive   = dedicated
                    });
                }

                await _auditService.LogAsync(User, "Diagnostics", "TENANT_RUNTIME_ROUTING_VERIFIED",
                    $"Runtime routing verified for {(tenantId.HasValue ? $"tenant {tenantId}" : "all tenants")}. " +
                    string.Join("; ", results.Select(r => $"{r.TenantName}=>{r.ConnectionType}")),
                    "Tenant", tenantId?.ToString(), ip);

                return Json(new
                {
                    verifiedAtUtc = DateTime.UtcNow,
                    count = results.Count,
                    tenants = results
                });
            }
            catch (Exception ex)
            {
                await _auditService.LogAsync(User, "Diagnostics", "TENANT_RUNTIME_ROUTING_FAILED",
                    $"Runtime routing verification FAILED for {(tenantId.HasValue ? $"tenant {tenantId}" : "all tenants")}. " +
                    $"Reason: {ex.Message}",
                    "Tenant", tenantId?.ToString(), ip);

                return StatusCode(500, new { error = "Runtime routing verification failed.", detail = ex.Message });
            }
        }

        public sealed class RuntimeDatabaseVm
        {
            public int    TenantId       { get; set; }
            public string TenantName     { get; set; } = string.Empty;
            public string TenantCode     { get; set; } = string.Empty;
            public string CurrentContext { get; set; } = "ApplicationDbContext";
            public string ConnectionType { get; set; } = "Shared";
            public string DatabaseName   { get; set; } = string.Empty;
            public bool   RoutingActive  { get; set; }
        }
    }
}
