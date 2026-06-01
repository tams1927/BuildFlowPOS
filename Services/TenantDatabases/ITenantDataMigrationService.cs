using HardwareManagementSystem.ViewModels;

namespace HardwareManagementSystem.Services.TenantDatabases
{
    /// <summary>
    /// Phase 5.0D — copies a tenant's operational data from the shared database into
    /// its provisioned dedicated database, then validates row counts.
    ///
    /// SAFETY: This is a COPY. No data is removed from the shared database. The shared
    /// database remains the live source of truth until routing is explicitly enabled.
    /// Tenant.DataMigrated is set to true only when every table's counts match.
    /// </summary>
    public interface ITenantDataMigrationService
    {
        Task<TenantDataMigrationResultVm> MigrateAsync(int tenantId);
    }
}
