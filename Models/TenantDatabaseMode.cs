namespace HardwareManagementSystem.Models
{
    /// <summary>
    /// Describes how a tenant's operational data is physically stored.
    ///
    /// Phase 5.0B: All tenants default to <see cref="Shared"/>. The
    /// <see cref="Dedicated"/> mode is reserved for Phase 5.0C onward and is
    /// not yet activated at runtime.
    /// </summary>
    public enum TenantDatabaseMode
    {
        /// <summary>
        /// Tenant data lives in the shared application database alongside other
        /// tenants, isolated by the TenantId column. This is the only active mode
        /// in Phase 5.0B and the default for every existing and new tenant.
        /// </summary>
        Shared = 0,

        /// <summary>
        /// Tenant data lives in its own dedicated database resolved through the
        /// tenant's <c>ConnectionString</c>. Reserved for Phase 5.0C+. Not yet
        /// activated at runtime.
        /// </summary>
        Dedicated = 1
    }
}
