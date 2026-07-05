using HardwareManagementSystem.Data;
using HardwareManagementSystem.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Services.TenantDatabases
{
    /// <summary>
    /// Phase 5.3.2 — applies pending TenantDbContext EF Core migrations to provisioned
    /// dedicated tenant databases.
    ///
    /// Root cause addressed: tenant databases that were provisioned before a new
    /// TenantDbContext migration shipped (e.g. Phase53_DamagedGoodsSupplierReturns) never
    /// received the migration because TenantDatabaseProvisioningService only calls
    /// MigrateAsync() at initial provision time and there was no subsequent upgrade path.
    /// </summary>
    public class TenantSchemaMigrationService : ITenantSchemaMigrationService
    {
        private readonly ApplicationDbContext _appContext;
        private readonly ITenantDbContextFactory _contextFactory;
        private readonly ILogger<TenantSchemaMigrationService> _logger;

        public TenantSchemaMigrationService(
            ApplicationDbContext appContext,
            ITenantDbContextFactory contextFactory,
            ILogger<TenantSchemaMigrationService> logger)
        {
            _appContext      = appContext;
            _contextFactory  = contextFactory;
            _logger          = logger;
        }

        // ─────────────────────────────────────────────────────────────────
        //  UPGRADE SINGLE TENANT
        // ─────────────────────────────────────────────────────────────────

        public async Task<TenantSchemaMigrationResultVm> UpgradeSchemaAsync(int tenantId)
        {
            var tenant = await _appContext.Tenants
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null)
            {
                return new TenantSchemaMigrationResultVm
                {
                    TenantId = tenantId,
                    Success  = false,
                    Error    = "Tenant not found."
                };
            }

            var result = new TenantSchemaMigrationResultVm
            {
                TenantId     = tenantId,
                TenantName   = tenant.Name,
                TenantCode   = tenant.Code,
                DatabaseName = tenant.DatabaseName
            };

            if (!tenant.IsDatabaseProvisioned || string.IsNullOrWhiteSpace(tenant.ConnectionString))
            {
                result.Success = false;
                result.Error   = "Tenant database is not provisioned — skipping schema upgrade.";
                return result;
            }

            try
            {
                await using var ctx = _contextFactory.CreateForConnection(tenant.ConnectionString);

                var pending = (await ctx.Database.GetPendingMigrationsAsync()).ToList();
                result.PendingCount = pending.Count;

                if (pending.Count == 0)
                {
                    result.Success     = true;
                    result.WasUpToDate = true;
                    result.LatestMigration = (await ctx.Database.GetAppliedMigrationsAsync())
                        .LastOrDefault();
                    result.UpgradedAtUtc = DateTime.UtcNow;

                    _logger.LogInformation(
                        "Tenant {TenantId} ({Code}) schema is already up-to-date. Latest: {Latest}",
                        tenantId, tenant.Code, result.LatestMigration);

                    return result;
                }

                _logger.LogInformation(
                    "Upgrading schema for tenant {TenantId} ({Code}): {Count} pending migration(s): {Migrations}",
                    tenantId, tenant.Code, pending.Count, string.Join(", ", pending));

                await ctx.Database.MigrateAsync();

                result.MigrationsApplied = pending;
                result.LatestMigration   = (await ctx.Database.GetAppliedMigrationsAsync())
                    .LastOrDefault();
                result.Success        = true;
                result.UpgradedAtUtc  = DateTime.UtcNow;

                // Persist the new latest migration on the tenant record
                var tenantTracked = await _appContext.Tenants.FirstAsync(t => t.Id == tenantId);
                tenantTracked.LastDatabaseMigration = result.LatestMigration;
                tenantTracked.UpdatedAtUtc          = DateTime.UtcNow;
                await _appContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Schema upgrade complete for tenant {TenantId} ({Code}). " +
                    "Applied {Count} migration(s). Latest: {Latest}",
                    tenantId, tenant.Code, pending.Count, result.LatestMigration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Schema upgrade failed for tenant {TenantId} ({Code}).",
                    tenantId, tenant.Code);

                result.Success = false;
                result.Error   = ex.Message;
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        //  UPGRADE ALL PROVISIONED TENANTS
        // ─────────────────────────────────────────────────────────────────

        public async Task<List<TenantSchemaMigrationResultVm>> UpgradeAllSchemasAsync()
        {
            var provisionedTenants = await _appContext.Tenants
                .AsNoTracking()
                .Where(t => t.DatabaseProvisionedAtUtc != null &&
                            t.ConnectionString != null &&
                            t.ConnectionString != string.Empty)
                .OrderBy(t => t.Name)
                .ToListAsync();

            _logger.LogInformation(
                "UpgradeAllSchemas: found {Count} provisioned dedicated tenant database(s).",
                provisionedTenants.Count);

            var results = new List<TenantSchemaMigrationResultVm>();

            foreach (var tenant in provisionedTenants)
            {
                var result = await UpgradeSchemaAsync(tenant.Id);
                results.Add(result);
            }

            var upgraded    = results.Count(r => r.Success && !r.WasUpToDate);
            var upToDate    = results.Count(r => r.Success && r.WasUpToDate);
            var failed      = results.Count(r => !r.Success);

            _logger.LogInformation(
                "UpgradeAllSchemas complete. Upgraded: {Upgraded}, already up-to-date: {UpToDate}, failed: {Failed}.",
                upgraded, upToDate, failed);

            return results;
        }

        // ─────────────────────────────────────────────────────────────────
        //  PENDING COUNT (read-only check)
        // ─────────────────────────────────────────────────────────────────

        public async Task<int> GetPendingMigrationCountAsync(int tenantId)
        {
            var tenant = await _appContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant == null ||
                !tenant.IsDatabaseProvisioned ||
                string.IsNullOrWhiteSpace(tenant.ConnectionString))
                return 0;

            try
            {
                await using var ctx = _contextFactory.CreateForConnection(tenant.ConnectionString);
                var pending = await ctx.Database.GetPendingMigrationsAsync();
                return pending.Count();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not check pending migrations for tenant {TenantId} ({Code}).",
                    tenantId, tenant.Code);
                return -1; // -1 signals "unknown / connection failed"
            }
        }
    }
}
