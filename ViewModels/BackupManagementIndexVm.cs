using HardwareManagementSystem.Models;

namespace HardwareManagementSystem.ViewModels
{
    public class BackupManagementIndexVm
    {
        public string PlatformDatabaseName { get; set; } = string.Empty;

        public BackupRecord? LatestPlatformBackup { get; set; }

        public List<TenantBackupStatusVm> Tenants { get; set; } = new();

        public List<BackupRecord> History { get; set; } = new();

        public bool AllowTenantBackupRequest { get; set; }
    }

    public class TenantBackupStatusVm
    {
        public int TenantId { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public bool IsDedicated { get; set; }

        public string? DatabaseName { get; set; }

        public BackupRecord? LastBackup { get; set; }
    }

    public class TenantDataProtectionVm
    {
        public BackupRecord? LastBackup { get; set; }

        public bool UsesDedicatedDatabase { get; set; }

        public bool AllowTenantBackupRequest { get; set; }

        public BackupRecord? LatestPlatformBackup { get; set; }
    }
}
