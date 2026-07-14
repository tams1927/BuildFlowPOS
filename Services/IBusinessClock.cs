namespace HardwareManagementSystem.Services
{
    /// <summary>Application clock for SaaS business-date decisions.</summary>
    public interface IBusinessClock
    {
        /// <summary>Current instant in UTC.</summary>
        DateTime UtcNow { get; }

        /// <summary>Current calendar date in the configured business timezone.</summary>
        DateOnly BusinessToday { get; }

        /// <summary>Configured business timezone identifier (IANA).</summary>
        string BusinessTimeZoneId { get; }
    }
}
