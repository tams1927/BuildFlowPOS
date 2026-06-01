namespace HardwareManagementSystem.Constants
{
    /// <summary>
    /// Centralised string constants for entity status values.
    /// Use these instead of inline magic strings where straightforward.
    /// </summary>
    public static class AppStatuses
    {
        // ─── Sales / POS ───────────────────────────────────────────────
        public const string Completed  = "Completed";
        public const string Voided     = "Voided";

        // ─── Transfers ─────────────────────────────────────────────────
        public const string Pending    = "Pending";
        public const string Approved   = "Approved";
        public const string Cancelled  = "Cancelled";

        // ─── Import batches ────────────────────────────────────────────
        public const string Processing = "Processing";

        // ─── Supplier payments ─────────────────────────────────────────
        public const string Paid       = "Paid";
        public const string Partial    = "Partial";
        public const string Unpaid     = "Unpaid";

        // ─── Inventory items ───────────────────────────────────────────
        public const string Active     = "Active";
        public const string Inactive   = "Inactive";
    }
}
