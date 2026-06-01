namespace HardwareManagementSystem.ViewModels
{
    /// <summary>
    /// Result of a Phase 5.0C dedicated-database provisioning attempt.
    /// Provisioning creates and migrates a dedicated database but does NOT move
    /// tenant data or activate routing — the tenant keeps using the shared database.
    /// </summary>
    public class TenantDatabaseProvisionResultVm
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

        public bool DatabaseCreated { get; set; }
        public bool MigrationsApplied { get; set; }
        public bool SeedCompleted { get; set; }

        public string DatabaseName { get; set; } = string.Empty;
        public DateTime? ProvisionedAtUtc { get; set; }
    }
}
