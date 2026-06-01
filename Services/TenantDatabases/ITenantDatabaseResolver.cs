namespace HardwareManagementSystem.Services.TenantDatabases
{
    /// <summary>
    /// Resolves the database connection metadata for a given tenant.
    ///
    /// Phase 5.0B: This resolver is available for dependency injection but is NOT
    /// wired into any runtime controller. All tenants are in Shared mode, so the
    /// resolver returns the default shared connection string. Dedicated-database
    /// routing becomes active in Phase 5.0C.
    /// </summary>
    public interface ITenantDatabaseResolver
    {
        /// <summary>
        /// Returns the connection string for the tenant's operational data.
        /// Shared-mode tenants receive the configured DefaultConnection.
        /// Dedicated-mode tenants receive their stored ConnectionString.
        /// </summary>
        Task<string> GetConnectionStringAsync(int tenantId);

        /// <summary>
        /// Returns the logical database name for the tenant.
        /// Shared-mode tenants return the shared database name (or "Shared" when
        /// it cannot be parsed). Dedicated-mode tenants return their DatabaseName.
        /// </summary>
        Task<string> GetDatabaseNameAsync(int tenantId);

        /// <summary>
        /// True when the tenant is configured to use a dedicated database
        /// (Dedicated mode + a stored connection string). This reflects
        /// CONFIGURATION, not whether routing is live.
        /// </summary>
        Task<bool> UsesDedicatedDatabaseAsync(int tenantId);

        /// <summary>
        /// Phase 5.0D — true only when live routing should send this tenant's
        /// runtime operations to its dedicated database: Dedicated mode +
        /// provisioned + data migrated + routing switch enabled.
        /// </summary>
        Task<bool> IsRoutingActiveAsync(int tenantId);

        /// <summary>
        /// Returns "Dedicated" when <see cref="IsRoutingActiveAsync"/> is true for
        /// the tenant, otherwise "Shared". This is the actual runtime database.
        /// </summary>
        Task<string> GetRuntimeDatabaseAsync(int tenantId);

        /// <summary>
        /// Evicts cached metadata for a tenant. Call after changing routing flags
        /// (provision / migrate / enable / disable) so the next resolution is fresh.
        /// </summary>
        void Invalidate(int tenantId);
    }
}
