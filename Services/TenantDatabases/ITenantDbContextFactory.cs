using HardwareManagementSystem.Data;

namespace HardwareManagementSystem.Services.TenantDatabases
{
    /// <summary>
    /// Creates <see cref="TenantDbContext"/> instances pointed at the correct
    /// database for a given tenant, using <see cref="ITenantDatabaseResolver"/>.
    ///
    /// Phase 5.0B: Available for DI and future use (Phase 5.0C). It is NOT injected
    /// into any runtime controller, and <see cref="TenantDbContext"/> is NOT the
    /// runtime DbContext. <see cref="ApplicationDbContext"/> remains authoritative.
    /// </summary>
    public interface ITenantDbContextFactory
    {
        /// <summary>
        /// Builds a <see cref="TenantDbContext"/> connected to the resolved database
        /// for the specified tenant. Caller owns the returned context and must dispose it.
        /// </summary>
        Task<TenantDbContext> CreateAsync(int tenantId);

        /// <summary>
        /// Builds a <see cref="TenantDbContext"/> bound to an explicit connection string.
        /// Used during provisioning (Phase 5.0C) when the connection string has been
        /// built but not yet persisted to the tenant record. Caller owns the context.
        /// </summary>
        TenantDbContext CreateForConnection(string connectionString);
    }
}
