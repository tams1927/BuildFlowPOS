using System.ComponentModel.DataAnnotations;
using HardwareManagementSystem.Models.Interfaces;

namespace HardwareManagementSystem.Models
{
    public class ImportBatchRow : ITenantEntity
    {
        public int Id { get; set; }

        public int ImportBatchId { get; set; }

        public int RowNumber { get; set; }

        public string RawJson { get; set; } = string.Empty;

        [StringLength(30)]
        public string Status { get; set; } = "Pending";
        // Pending, Success, Failed

        [StringLength(1000)]
        public string? ErrorMessage { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public ImportBatch? ImportBatch { get; set; }
    }
}
