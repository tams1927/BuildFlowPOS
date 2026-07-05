namespace HardwareManagementSystem.Configuration
{
    public class BackupSettings
    {
        public string RootPath { get; set; } = string.Empty;

        public int RetentionDays { get; set; } = 14;

        public bool EnableScheduledBackups { get; set; } = false;

        public int ScheduledBackupHour { get; set; } = 2;

        public bool AllowTenantBackupRequest { get; set; } = true;

        /// <summary>
        /// Override path for restored .mdf data files when using RestoreAsNew mode.
        /// Falls back to SQL Server default InstanceDefaultDataPath when empty.
        /// </summary>
        public string? RestoreDataPath { get; set; }

        /// <summary>
        /// Override path for restored .ldf log files when using RestoreAsNew mode.
        /// Falls back to SQL Server default InstanceDefaultLogPath when empty.
        /// </summary>
        public string? RestoreLogPath { get; set; }
    }
}
