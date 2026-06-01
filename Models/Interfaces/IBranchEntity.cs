namespace HardwareManagementSystem.Models.Interfaces
{
    /// <summary>
    /// Marker interface for entities that are scoped to a specific Branch within a tenant.
    /// Branch entities represent data that is further sub-divided by physical store location
    /// within a tenant's operational database.
    ///
    /// IBranchEntity implies ITenantEntity — all branch-scoped data lives inside the
    /// tenant's own database. The BranchId FK links to the Branches table in the same
    /// tenant database.
    ///
    /// Phase 5.0A: Compilation marker only. No runtime behaviour change.
    /// </summary>
    public interface IBranchEntity : ITenantEntity { }
}
