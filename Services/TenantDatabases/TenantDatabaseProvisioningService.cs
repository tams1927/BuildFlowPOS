using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.ViewModels;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Services.TenantDatabases
{
    /// <summary>
    /// Phase 5.0C — creates, migrates, and seeds a dedicated database for a tenant.
    ///
    /// Safety contract:
    ///   • Does NOT move tenant operational data.
    ///   • Does NOT change request routing — the tenant keeps using ApplicationDbContext.
    ///   • Refuses to provision unless the tenant is in Dedicated mode with a name set.
    ///   • Refuses to re-provision an already provisioned tenant (no Force in 5.0C).
    /// </summary>
    public class TenantDatabaseProvisioningService : ITenantDatabaseProvisioningService
    {
        private readonly ApplicationDbContext _appContext;
        private readonly IConfiguration _configuration;
        private readonly ITenantDbContextFactory _contextFactory;
        private readonly ILogger<TenantDatabaseProvisioningService> _logger;

        public TenantDatabaseProvisioningService(
            ApplicationDbContext appContext,
            IConfiguration configuration,
            ITenantDbContextFactory contextFactory,
            ILogger<TenantDatabaseProvisioningService> logger)
        {
            _appContext = appContext;
            _configuration = configuration;
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<TenantDatabaseProvisionResultVm> ProvisionAsync(int tenantId)
        {
            var result = new TenantDatabaseProvisionResultVm();

            // ── Load + validate tenant ───────────────────────────────────────
            var tenant = await _appContext.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant == null)
            {
                result.Message = "Tenant not found.";
                return result;
            }

            result.DatabaseName = tenant.DatabaseName ?? string.Empty;

            if (tenant.DatabaseMode != TenantDatabaseMode.Dedicated)
            {
                result.Message = "Tenant must be switched to Dedicated mode first.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(tenant.DatabaseName))
            {
                result.Message = "Database name is required before provisioning.";
                return result;
            }

            if (tenant.DatabaseProvisionedAtUtc != null)
            {
                result.Message = "Database already provisioned.";
                return result;
            }

            var databaseName = tenant.DatabaseName.Trim();
            if (!IsValidDatabaseName(databaseName))
            {
                result.Message = "Database name contains invalid characters.";
                return result;
            }

            // ── Build connection strings from the current SQL Server instance ─
            string dedicatedConnection;
            string masterConnection;
            string serverName;
            try
            {
                var defaultConnection = _configuration.GetConnectionString("DefaultConnection")
                    ?? throw new InvalidOperationException("DefaultConnection is not configured.");

                var dedicatedBuilder = new SqlConnectionStringBuilder(defaultConnection)
                {
                    InitialCatalog = databaseName,
                    TrustServerCertificate = true,
                    MultipleActiveResultSets = true
                };
                dedicatedConnection = dedicatedBuilder.ConnectionString;
                serverName = dedicatedBuilder.DataSource;

                var masterBuilder = new SqlConnectionStringBuilder(dedicatedConnection)
                {
                    InitialCatalog = "master"
                };
                masterConnection = masterBuilder.ConnectionString;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to build connection string for tenant {TenantId}.", tenantId);
                result.Message = $"Could not build connection string: {ex.Message}";
                return result;
            }

            try
            {
                // ── Create database if it does not already exist ─────────────
                result.DatabaseCreated = await CreateDatabaseIfMissingAsync(masterConnection, databaseName);

                // ── Apply TenantDbContext migrations ─────────────────────────
                string? lastMigration;
                await using (var tenantContext = _contextFactory.CreateForConnection(dedicatedConnection))
                {
                    await tenantContext.Database.MigrateAsync();
                    result.MigrationsApplied = true;

                    lastMigration = (await tenantContext.Database.GetAppliedMigrationsAsync())
                        .LastOrDefault();

                    // ── Seed minimal reference data ──────────────────────────
                    await SeedMinimumDataAsync(tenantContext, tenant);
                    result.SeedCompleted = true;
                }

                // ── Persist routing metadata on the tenant record ────────────
                var provisionedAt = DateTime.UtcNow;
                tenant.ConnectionString = dedicatedConnection;
                tenant.DatabaseServer = serverName;
                tenant.DatabaseProvisionedAtUtc = provisionedAt;
                tenant.LastDatabaseMigration = lastMigration;
                await _appContext.SaveChangesAsync();

                result.Success = true;
                result.ProvisionedAtUtc = provisionedAt;
                result.Message =
                    $"Dedicated database '{databaseName}' provisioned successfully. " +
                    "The tenant continues to use the shared database (routing inactive).";

                _logger.LogInformation(
                    "Provisioned dedicated database for tenant {TenantId} ({DatabaseName}).",
                    tenantId, databaseName);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Provisioning failed for tenant {TenantId} ({DatabaseName}).",
                    tenantId, databaseName);
                result.Success = false;
                result.Message = $"Provisioning failed: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Connects to the master database and creates the target database if it
        /// does not exist. Returns true if a new database was created.
        /// </summary>
        private static async Task<bool> CreateDatabaseIfMissingAsync(string masterConnection, string databaseName)
        {
            await using var connection = new SqlConnection(masterConnection);
            await connection.OpenAsync();

            await using (var existsCommand = connection.CreateCommand())
            {
                existsCommand.CommandText = "SELECT DB_ID(@name)";
                var param = existsCommand.CreateParameter();
                param.ParameterName = "@name";
                param.Value = databaseName;
                existsCommand.Parameters.Add(param);

                var dbId = await existsCommand.ExecuteScalarAsync();
                if (dbId != null && dbId != DBNull.Value)
                    return false; // already exists
            }

            await using (var createCommand = connection.CreateCommand())
            {
                // databaseName is validated against an identifier allow-list before
                // this point; brackets are additionally escaped to be safe.
                var safeName = databaseName.Replace("]", "]]");
                createCommand.CommandText = $"CREATE DATABASE [{safeName}]";
                await createCommand.ExecuteNonQueryAsync();
            }

            return true;
        }

        /// <summary>
        /// Seeds ONLY the minimal reference data required for a dedicated database:
        /// one tenant-owned SystemSetting row, the RolePermissions matrix, and a
        /// read-only copy of the SubscriptionPlans catalog. No business data is seeded.
        /// Idempotent: skips any set that already has rows.
        /// </summary>
        private async Task SeedMinimumDataAsync(TenantDbContext tenantContext, Tenant tenant)
        {
            // ── SystemSetting (tenant-owned row) ─────────────────────────────
            if (!await tenantContext.SystemSettings.AnyAsync())
            {
                tenantContext.SystemSettings.Add(new SystemSetting
                {
                    TenantId = tenant.Id,
                    BusinessName = tenant.Name,
                    CurrencySymbol = "₱",
                    DefaultVatPercent = 12m,
                    TaxMode = "VAT",
                    UpdatedAt = DateTime.Now
                });
            }

            // ── RolePermissions (replicated permission matrix) ───────────────
            if (!await tenantContext.RolePermissions.AnyAsync())
            {
                var permissions = await _appContext.RolePermissions
                    .AsNoTracking()
                    .ToListAsync();

                foreach (var p in permissions)
                {
                    tenantContext.RolePermissions.Add(new RolePermission
                    {
                        RoleName = p.RoleName,
                        ModuleName = p.ModuleName,
                        CanView = p.CanView,
                        CanCreate = p.CanCreate,
                        CanEdit = p.CanEdit,
                        CanDelete = p.CanDelete,
                        CanPrint = p.CanPrint,
                        CanExport = p.CanExport
                    });
                }
            }

            // ── SubscriptionPlans (read-only reference copy) ─────────────────
            if (!await tenantContext.SubscriptionPlans.AnyAsync())
            {
                var plans = await _appContext.SubscriptionPlans
                    .AsNoTracking()
                    .ToListAsync();

                foreach (var plan in plans)
                {
                    tenantContext.SubscriptionPlans.Add(new SubscriptionPlan
                    {
                        Name = plan.Name,
                        Description = plan.Description,
                        MonthlyPrice = plan.MonthlyPrice,
                        MaxBranches = plan.MaxBranches,
                        MaxUsers = plan.MaxUsers,
                        MaxProducts = plan.MaxProducts,
                        IsActive = plan.IsActive,
                        SortOrder = plan.SortOrder,
                        CreatedAtUtc = plan.CreatedAtUtc
                    });
                }
            }

            await tenantContext.SaveChangesAsync();
        }

        /// <summary>
        /// Allow-list validation: letters, digits, underscore, and hyphen only.
        /// Prevents SQL injection through the database name.
        /// </summary>
        private static bool IsValidDatabaseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
                return false;

            foreach (var c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                    return false;
            }

            return char.IsLetter(name[0]) || name[0] == '_';
        }
    }
}
