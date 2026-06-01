using System.ComponentModel.DataAnnotations;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class ImportBatch : ITenantEntity
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ImportType { get; set; } = string.Empty;
        // Products, OpeningStock, Customers, Suppliers

        [Required]
        [StringLength(255)]
        public string FileName { get; set; } = string.Empty;

        public int TotalRows { get; set; }

        public int SuccessRows { get; set; }

        public int FailedRows { get; set; }

        [StringLength(30)]
        public string Status { get; set; } = "Pending";
        // Pending, Processing, Completed, Failed

        public string? CreatedByUserId { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAtUtc { get; set; }

        /// <summary>Tenant this import batch belongs to. Nullable for backward compatibility.</summary>
        public int? TenantId { get; set; }

        public Tenant? Tenant { get; set; }

        public ICollection<ImportBatchRow> ImportBatchRows { get; set; } = new List<ImportBatchRow>();
    }
}
