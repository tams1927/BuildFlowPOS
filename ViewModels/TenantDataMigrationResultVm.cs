namespace HardwareManagementSystem.ViewModels
{
    /// <summary>
    /// Outcome of a Phase 5.0D tenant data migration (shared → dedicated copy).
    /// </summary>
    public class TenantDataMigrationResultVm
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

        public string DatabaseName { get; set; } = string.Empty;
        public DateTime? MigratedAtUtc { get; set; }

        /// <summary>Per-table row-count comparison used for validation.</summary>
        public List<TableCountComparison> Tables { get; set; } = new();

        public int TotalRowsCopied => Tables.Sum(t => t.DedicatedCount);
        public bool AllMatched => Tables.All(t => t.Match);
    }

    public class TableCountComparison
    {
        public string TableName { get; set; } = string.Empty;
        public int SharedCount { get; set; }
        public int DedicatedCount { get; set; }
        public bool Match => SharedCount == DedicatedCount;
    }
}
