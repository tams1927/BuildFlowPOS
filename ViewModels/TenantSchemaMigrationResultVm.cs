namespace HardwareManagementSystem.ViewModels
{
    /// <summary>
    /// Result returned by <see cref="Services.TenantDatabases.ITenantSchemaMigrationService"/>
    /// for a single tenant database schema upgrade or validation check.
    /// </summary>
    public class TenantSchemaMigrationResultVm
    {
        public int     TenantId     { get; set; }
        public string  TenantName   { get; set; } = string.Empty;
        public string  TenantCode   { get; set; } = string.Empty;
        public string? DatabaseName { get; set; }

        /// <summary>True when the operation completed without error.</summary>
        public bool Success { get; set; }

        /// <summary>True when no migrations were pending — database was already current.</summary>
        public bool WasUpToDate { get; set; }

        /// <summary>List of migration IDs that were applied during this run.</summary>
        public List<string> MigrationsApplied { get; set; } = new();

        /// <summary>Number of pending migrations before the upgrade ran.</summary>
        public int PendingCount { get; set; }

        /// <summary>Most recent migration ID applied to the database (after upgrade).</summary>
        public string? LatestMigration { get; set; }

        /// <summary>Error message when <see cref="Success"/> is false.</summary>
        public string? Error { get; set; }

        /// <summary>UTC timestamp when the upgrade completed.</summary>
        public DateTime UpgradedAtUtc { get; set; }
    }
}
