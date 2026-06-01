namespace HardwareManagementSystem.ViewModels
{
    /// <summary>Step 2: Column mapping submitted by the user.</summary>
    public class ImportMappingViewModel
    {
        public int BatchId { get; set; }

        public string ImportType { get; set; } = string.Empty;

        /// <summary>Excel header names read from the uploaded file.</summary>
        public List<string> ExcelHeaders { get; set; } = new();

        /// <summary>
        /// Key = system field name, Value = selected Excel column header (or empty string = skip).
        /// </summary>
        public Dictionary<string, string> Mapping { get; set; } = new();
    }

    /// <summary>Step 3: One preview row (key = system field, value = parsed cell value).</summary>
    public class ImportPreviewRow
    {
        public int RowNumber { get; set; }
        public Dictionary<string, string> Fields { get; set; } = new();
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// Status stored in ImportBatchRow: Pending | Skipped | Imported | Failed
        /// Populated when rebuilding rows from the database (Preview / Details views).
        /// </summary>
        public string RowDbStatus { get; set; } = "Pending";

        /// <summary>True when validation produced errors (pre-import check).</summary>
        public bool HasErrors => Errors.Count > 0;
    }

    /// <summary>Step 3: Full preview result passed to the Preview view.</summary>
    public class ImportPreviewViewModel
    {
        public int BatchId { get; set; }
        public string ImportType { get; set; } = string.Empty;
        public List<ImportPreviewRow> Rows { get; set; } = new();

        /// <summary>Rows with no validation errors (Pending = ready to import).</summary>
        public int ValidCount => Rows.Count(r => !r.HasErrors && r.RowDbStatus == "Pending");

        /// <summary>Rows that failed pre-import validation (Skipped).</summary>
        public int SkippedCount => Rows.Count(r => r.RowDbStatus == "Skipped");

        /// <summary>Rows successfully imported.</summary>
        public int ImportedCount => Rows.Count(r => r.RowDbStatus == "Imported");

        /// <summary>Rows that failed during the actual import run.</summary>
        public int FailedCount => Rows.Count(r => r.RowDbStatus == "Failed");

        /// <summary>Total rows with any problem (skipped + failed).</summary>
        public int InvalidCount => Rows.Count(r => r.HasErrors);

        public List<string> SystemFields { get; set; } = new();
    }

    /// <summary>Step 5: Import result shown after confirmed import.</summary>
    public class ImportResultViewModel
    {
        public int BatchId { get; set; }
        public string ImportType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int TotalRows { get; set; }
        public int SuccessRows { get; set; }
        public int FailedRows { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? CompletedAtUtc { get; set; }
    }
}
