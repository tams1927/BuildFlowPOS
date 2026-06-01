using HardwareManagementSystem.Data;

namespace HardwareManagementSystem.Services.TenantDatabases
{
    /// <summary>
    /// Phase 5.0D — resolves the correct operational database context for the
    /// current request/tenant.
    ///
    /// Returns a dedicated <see cref="TenantDbContext"/> only when the current tenant
    /// has routing fully active (Dedicated + provisioned + data migrated + routing
    /// enabled). Otherwise returns the shared <see cref="ApplicationDbContext"/>.
    ///
    /// Lifetime: scoped. When a dedicated context is created it is owned by this
    /// provider and disposed with the request scope. The shared context is owned by DI
    /// and is never disposed here.
    /// </summary>
    public interface ITenantOperationalContextProvider
    {
        /// <summary>Gets the operational context for the current tenant.</summary>
        Task<ITenantOperationalDbContext> GetContextAsync();

        /// <summary>Gets the operational context for an explicit tenant id.</summary>
        Task<ITenantOperationalDbContext> GetContextAsync(int tenantId);

        /// <summary>"Dedicated" or "Shared" — the runtime database for the current tenant.</summary>
        Task<string> GetRuntimeDatabaseAsync();
    }
}
