using HardwareManagementSystem.ViewModels;

namespace HardwareManagementSystem.Services.TenantDatabases
{
    /// <summary>
    /// Provisions a dedicated database for a tenant (Phase 5.0C).
    ///
    /// IMPORTANT: Provisioning CREATES and MIGRATES a dedicated database and seeds
    /// minimal reference data. It does NOT move tenant operational data and does NOT
    /// activate request routing. The tenant continues to run on the shared database
    /// (ApplicationDbContext) after provisioning.
    /// </summary>
    public interface ITenantDatabaseProvisioningService
    {
        /// <summary>
        /// Creates (if missing), migrates, and seeds the dedicated database for the
        /// given tenant, then records routing metadata on the tenant record.
        /// </summary>
        Task<TenantDatabaseProvisionResultVm> ProvisionAsync(int tenantId);
    }
}
