namespace HardwareManagementSystem.Configuration
{
    public class BackupSettings
    {
        public string RootPath { get; set; } = string.Empty;

        public int RetentionDays { get; set; } = 14;

        public bool EnableScheduledBackups { get; set; } = false;

        public int ScheduledBackupHour { get; set; } = 2;

        public bool AllowTenantBackupRequest { get; set; } = true;
    }
}
