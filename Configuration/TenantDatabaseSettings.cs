namespace HardwareManagementSystem.Configuration
{
    /// <summary>
    /// Strongly-typed options for tenant database naming.
    /// Bound to the "TenantDatabaseSettings" section in appsettings.json.
    /// </summary>
    public sealed class TenantDatabaseSettings
    {
        /// <summary>
        /// Prefix prepended to every auto-generated tenant database name.
        /// Example: "BuildFlowPOS" produces "BuildFlowPOS_TCS_1".
        /// Falls back to "BuildFlowPOS" when null, empty, or whitespace.
        /// </summary>
        public string DatabasePrefix { get; set; } = "BuildFlowPOS";

        /// <summary>
        /// Returns the effective prefix: trims whitespace and falls back to
        /// "BuildFlowPOS" so database creation never fails due to a blank value.
        /// </summary>
        public string EffectivePrefix =>
            string.IsNullOrWhiteSpace(DatabasePrefix) ? "BuildFlowPOS" : DatabasePrefix.Trim();
    }
}
