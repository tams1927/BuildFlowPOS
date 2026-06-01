namespace HardwareManagementSystem.Models.Interfaces
{
    /// <summary>
    /// Marker interface for entities that belong to the Platform Database.
    /// Platform entities govern SaaS infrastructure: tenants, subscription plans,
    /// role permissions, and ASP.NET Identity.
    ///
    /// In the future Database-Per-Tenant architecture these entities will be hosted
    /// in a shared platform database, not in any individual tenant database.
    ///
    /// Phase 5.0A: Compilation marker only. No runtime behaviour change.
    /// </summary>
    public interface IPlatformEntity { }
}
