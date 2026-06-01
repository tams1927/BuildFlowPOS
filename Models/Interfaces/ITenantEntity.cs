namespace HardwareManagementSystem.Models.Interfaces
{
    /// <summary>
    /// Marker interface for entities that belong to a Tenant's isolated operational database.
    /// Tenant entities hold all business data for a single tenant: products, sales,
    /// purchasing, customers, inventory, and configuration.
    ///
    /// In the future Database-Per-Tenant architecture, each tenant will have its own
    /// SQL Server database containing exclusively ITenantEntity tables. TenantId column
    /// filters become unnecessary once each tenant has its own database.
    ///
    /// Phase 5.0A: Compilation marker only. No runtime behaviour change.
    /// </summary>
    public interface ITenantEntity { }
}
