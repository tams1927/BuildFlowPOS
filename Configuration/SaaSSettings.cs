namespace HardwareManagementSystem.Configuration
{
    /// <summary>SaaS platform settings for subscription date semantics.</summary>
    public class SaaSSettings
    {
        /// <summary>
        /// IANA timezone used to interpret date-only subscription expiration
        /// (e.g. "Asia/Manila"). Expiration is inclusive through this calendar day.
        /// </summary>
        public string BusinessTimeZoneId { get; set; } = "Asia/Manila";
    }
}
