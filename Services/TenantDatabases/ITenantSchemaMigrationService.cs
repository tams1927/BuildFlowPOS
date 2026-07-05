using HardwareManagementSystem.ViewModels;

namespace HardwareManagementSystem.Services.TenantDatabases
{
    /// <summary>
    /// Phase 5.3.2 — applies pending TenantDbContext EF Core migrations to one or all
    /// provisioned dedicated tenant databases.
    ///
    /// Safety contract:
    ///   • Only applies schema changes — never touches business data.
    ///   • Idempotent: running against an already-current DB is a no-op.
    ///   • Each tenant is upgraded independently; a failure for one does not block others.
    ///   • Updates Tenant.LastDatabaseMigration on success.
    ///   • Only operates on tenants with DatabaseProvisionedAtUtc set and a non-null
    ///     ConnectionString — shared-mode tenants are skipped.
    /// </summary>
    public interface ITenantSchemaMigrationService
    {
        /// <summary>
        /// Apply any pending TenantDbContext migrations to a single tenant's dedicated database.
        /// </summary>
        Task<TenantSchemaMigrationResultVm> UpgradeSchemaAsync(int tenantId);

        /// <summary>
        /// Apply any pending TenantDbContext migrations to ALL provisioned dedicated tenant
        /// databases. Returns one result entry per provisioned tenant.
        /// </summary>
        Task<List<TenantSchemaMigrationResultVm>> UpgradeAllSchemasAsync();

        /// <summary>
        /// Check (without modifying) how many migrations are pending for a tenant's database.
        /// Returns 0 if the tenant is not provisioned or the database is already current.
        /// </summary>
        Task<int> GetPendingMigrationCountAsync(int tenantId);
    }
}
