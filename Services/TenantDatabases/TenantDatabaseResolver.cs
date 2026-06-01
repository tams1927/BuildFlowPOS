using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HardwareManagementSystem.Services.TenantDatabases
{
    /// <summary>
    /// Default <see cref="ITenantDatabaseResolver"/> implementation.
    ///
    /// Reads tenant database metadata from the shared <see cref="ApplicationDbContext"/>
    /// and falls back to the configured DefaultConnection for Shared-mode tenants.
    /// Metadata is cached in <see cref="IMemoryCache"/> for 5 minutes to avoid a
    /// database round-trip on every resolution.
    ///
    /// SECURITY: Full connection strings are NEVER logged. Use <see cref="Mask"/>
    /// whenever a connection string needs to appear in diagnostics or logs.
    ///
    /// Phase 5.0B: Available for DI; not yet used by runtime controllers.
    /// </summary>
    public class TenantDatabaseResolver : ITenantDatabaseResolver
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TenantDatabaseResolver> _logger;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
        private const string CacheKeyPrefix = "tenant_db_meta_";

        public TenantDatabaseResolver(
            ApplicationDbContext context,
            IConfiguration configuration,
            IMemoryCache cache,
            ILogger<TenantDatabaseResolver> logger)
        {
            _context       = context;
            _configuration = configuration;
            _cache         = cache;
            _logger        = logger;
        }

        /// <summary>Lightweight cached projection of tenant database metadata.</summary>
        private sealed record TenantDbMetadata(
            int Id,
            TenantDatabaseMode Mode,
            string? DatabaseName,
            string? ConnectionString,
            string? DatabaseServer,
            bool Provisioned,
            bool DataMigrated,
            bool RoutingEnabled)
        {
            /// <summary>Phase 5.0D — all conditions required for live dedicated routing.</summary>
            public bool RoutingActive =>
                Mode == TenantDatabaseMode.Dedicated &&
                Provisioned &&
                DataMigrated &&
                RoutingEnabled &&
                !string.IsNullOrWhiteSpace(ConnectionString);
        }

        public async Task<string> GetConnectionStringAsync(int tenantId)
        {
            var meta = await GetMetadataAsync(tenantId);

            if (meta.Mode == TenantDatabaseMode.Dedicated &&
                !string.IsNullOrWhiteSpace(meta.ConnectionString))
            {
                _logger.LogInformation(
                    "Tenant {TenantId} resolved to a dedicated database connection.",
                    tenantId);
                return meta.ConnectionString;
            }

            // Shared mode (the only active mode in Phase 5.0B).
            return GetDefaultConnectionString();
        }

        public async Task<string> GetDatabaseNameAsync(int tenantId)
        {
            var meta = await GetMetadataAsync(tenantId);

            if (meta.Mode == TenantDatabaseMode.Dedicated &&
                !string.IsNullOrWhiteSpace(meta.DatabaseName))
            {
                return meta.DatabaseName;
            }

            // Shared mode: attempt to surface the shared DB name for diagnostics.
            return GetSharedDatabaseName();
        }

        public async Task<bool> UsesDedicatedDatabaseAsync(int tenantId)
        {
            var meta = await GetMetadataAsync(tenantId);
            return meta.Mode == TenantDatabaseMode.Dedicated &&
                   !string.IsNullOrWhiteSpace(meta.ConnectionString);
        }

        public async Task<bool> IsRoutingActiveAsync(int tenantId)
        {
            var meta = await GetMetadataAsync(tenantId);
            return meta.RoutingActive;
        }

        public async Task<string> GetRuntimeDatabaseAsync(int tenantId)
        {
            var meta = await GetMetadataAsync(tenantId);
            return meta.RoutingActive ? "Dedicated" : "Shared";
        }

        public void Invalidate(int tenantId)
        {
            _cache.Remove(CacheKeyPrefix + tenantId);
            _logger.LogInformation("Invalidated cached database metadata for tenant {TenantId}.", tenantId);
        }

        // ──────────────────────────────────────────────────────────────────
        // Internals
        // ──────────────────────────────────────────────────────────────────

        private async Task<TenantDbMetadata> GetMetadataAsync(int tenantId)
        {
            var cacheKey = CacheKeyPrefix + tenantId;

            if (_cache.TryGetValue(cacheKey, out TenantDbMetadata? cached) && cached != null)
                return cached;

            var meta = await _context.Tenants
                .AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => new TenantDbMetadata(
                    t.Id,
                    t.DatabaseMode,
                    t.DatabaseName,
                    t.ConnectionString,
                    t.DatabaseServer,
                    t.DatabaseProvisionedAtUtc != null,
                    t.DataMigrated,
                    t.RoutingEnabled))
                .FirstOrDefaultAsync();

            if (meta == null)
            {
                // Unknown tenant: do not invent a dedicated connection. We log the
                // missing tenant (id only — never a connection string) and surface a
                // safe Shared placeholder so callers fall back to the default DB.
                _logger.LogWarning(
                    "Tenant {TenantId} not found while resolving database metadata. " +
                    "Falling back to shared database.", tenantId);

                meta = new TenantDbMetadata(
                    tenantId, TenantDatabaseMode.Shared, null, null, null, false, false, false);
            }

            _cache.Set(cacheKey, meta, CacheDuration);
            return meta;
        }

        private string GetDefaultConnectionString()
        {
            var cs = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs))
            {
                _logger.LogError("DefaultConnection is not configured.");
                throw new InvalidOperationException(
                    "DefaultConnection connection string is not configured.");
            }
            return cs;
        }

        private string GetSharedDatabaseName()
        {
            var cs = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(cs)) return "Shared";

            // Parse the Database/Initial Catalog token without exposing the full string.
            foreach (var part in cs.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;
                var key = kv[0].Trim();
                if (key.Equals("Database", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase))
                {
                    return kv[1].Trim();
                }
            }
            return "Shared";
        }

        /// <summary>
        /// Masks a connection string for safe display in diagnostics/logs.
        /// Returns only non-sensitive tokens (Server, Database) and redacts the rest.
        /// </summary>
        public static string Mask(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return "(none)";

            var server   = "(hidden)";
            var database = "(hidden)";

            foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;
                var key = kv[0].Trim();
                var val = kv[1].Trim();

                if (key.Equals("Server", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Data Source", StringComparison.OrdinalIgnoreCase))
                    server = val;
                else if (key.Equals("Database", StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase))
                    database = val;
            }

            return $"Server={server}; Database={database}; (credentials hidden)";
        }
    }
}
