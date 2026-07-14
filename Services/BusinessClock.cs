using HardwareManagementSystem.Configuration;
using Microsoft.Extensions.Options;

namespace HardwareManagementSystem.Services
{
    public sealed class BusinessClock : IBusinessClock
    {
        private readonly TimeProvider _time;
        private readonly TimeZoneInfo _tz;

        public string BusinessTimeZoneId { get; }

        public BusinessClock(TimeProvider time, IOptions<SaaSSettings> settings)
        {
            _time = time;
            var tzId = settings.Value.BusinessTimeZoneId;
            _tz = TimeZoneInfo.TryFindSystemTimeZoneById(tzId, out var found)
                ? found
                : TimeZoneInfo.Utc;
            BusinessTimeZoneId = _tz.Id;
        }

        /// <summary>Test/QA override constructor.</summary>
        public BusinessClock(DateTime utcNow, string timeZoneId)
        {
            _time = TimeProvider.System;
            _tz = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var found)
                ? found
                : TimeZoneInfo.Utc;
            BusinessTimeZoneId = _tz.Id;
            _fixedUtc = utcNow;
        }

        private readonly DateTime? _fixedUtc;

        public DateTime UtcNow => _fixedUtc ?? _time.GetUtcNow().UtcDateTime;

        public DateOnly BusinessToday
        {
            get
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(UtcNow, DateTimeKind.Utc), _tz);
                return DateOnly.FromDateTime(local.Date);
            }
        }
    }
}
