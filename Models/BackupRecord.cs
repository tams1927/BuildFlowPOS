using System.ComponentModel.DataAnnotations;

namespace HardwareManagementSystem.Models
{
    public enum BackupDatabaseType
    {
        Platform,
        Tenant
    }

    public enum BackupStatus
    {
        Pending,
        Success,
        Failed
    }

    public enum BackupType
    {
        Manual,
        Scheduled,
        Requested
    }

    /// <summary>Platform-owned backup audit record. Stored only in ApplicationDbContext.</summary>
    public class BackupRecord
    {
        public int Id { get; set; }

        public int? TenantId { get; set; }

        [Required]
        [StringLength(128)]
        public string DatabaseName { get; set; } = string.Empty;

        public BackupDatabaseType DatabaseType { get; set; }

        [Required]
        [StringLength(260)]
        public string BackupFileName { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string BackupPath { get; set; } = string.Empty;

        public long? BackupSizeBytes { get; set; }

        public BackupStatus Status { get; set; } = BackupStatus.Pending;

        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAtUtc { get; set; }

        [StringLength(2000)]
        public string? ErrorMessage { get; set; }

        [StringLength(450)]
        public string? CreatedByUserId { get; set; }

        public BackupType BackupType { get; set; } = BackupType.Manual;

        [StringLength(500)]
        public string? Notes { get; set; }

        public Tenant? Tenant { get; set; }
    }
}
