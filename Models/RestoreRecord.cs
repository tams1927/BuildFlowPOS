using System.ComponentModel.DataAnnotations;

namespace HardwareManagementSystem.Models
{
    public enum RestoreMode
    {
        Overwrite,
        RestoreAsNew
    }

    public enum RestoreStatus
    {
        Pending,
        Running,
        Success,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Platform-owned restore audit record. Stored only in ApplicationDbContext.
    /// Tenant users never see or interact with this table.
    /// </summary>
    public class RestoreRecord
    {
        public int Id { get; set; }

        public int? BackupRecordId { get; set; }

        public int? TenantId { get; set; }

        [Required]
        [StringLength(128)]
        public string DatabaseName { get; set; } = string.Empty;

        public BackupDatabaseType DatabaseType { get; set; }

        public RestoreMode RestoreMode { get; set; }

        [StringLength(128)]
        public string? RestoreTargetDatabaseName { get; set; }

        public RestoreStatus Status { get; set; } = RestoreStatus.Pending;

        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAtUtc { get; set; }

        [StringLength(2000)]
        public string? ErrorMessage { get; set; }

        [StringLength(450)]
        public string? RequestedByUserId { get; set; }

        [Required]
        [StringLength(500)]
        public string SourceBackupPath { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Notes { get; set; }

        // Navigation properties
        public BackupRecord? BackupRecord { get; set; }
        public Tenant? Tenant { get; set; }
    }
}
