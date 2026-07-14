using HardwareManagementSystem.Models;

namespace HardwareManagementSystem.Services
{
    public enum SubscriptionAccessReasonCode
    {
        Allowed,
        TenantNotFound,
        TenantDisabled,
        TenantSuspended,
        StatusExpired,
        DateExpired,
        NoExpiration
    }

    public sealed class TenantSubscriptionAccessResult
    {
        public bool IsAllowed { get; init; }
        public TenantStatus EffectiveStatus { get; init; }
        public TenantStatus StoredStatus { get; init; }
        public SubscriptionAccessReasonCode ReasonCode { get; init; }
        public string Message { get; init; } = string.Empty;
        public DateTime? ExpirationDate { get; init; }
        public DateOnly? ExpirationBusinessDate { get; init; }
        public DateOnly BusinessToday { get; init; }
        public DateTime? GracePeriodEnd { get; init; }
        public bool RequiresRenewal { get; init; }

        public static TenantSubscriptionAccessResult Allowed(Tenant tenant, DateOnly businessToday) =>
            new()
            {
                IsAllowed = true,
                EffectiveStatus = tenant.Status,
                StoredStatus = tenant.Status,
                ReasonCode = SubscriptionAccessReasonCode.Allowed,
                Message = string.Empty,
                ExpirationDate = tenant.ExpirationDate,
                ExpirationBusinessDate = TenantSubscriptionAccessService.ToBusinessDate(tenant.ExpirationDate),
                BusinessToday = businessToday,
                RequiresRenewal = false
            };
    }

    public interface ITenantSubscriptionAccessService
    {
        TenantSubscriptionAccessResult Evaluate(Tenant tenant);
        TenantSubscriptionAccessResult Evaluate(
            bool isActive,
            TenantStatus status,
            DateTime? expirationDate,
            int? subscriptionPlanId = null);
        bool IsExpirationDatePassed(DateTime? expirationDate);
        DateOnly GetBusinessToday();
    }

    /// <summary>
    /// Centralized subscription eligibility for tenant operational access.
    /// Payment gateway billing is not modeled — access follows SuperAdmin-managed
    /// status and expiration dates only.
    /// </summary>
    public sealed class TenantSubscriptionAccessService : ITenantSubscriptionAccessService
    {
        private readonly IBusinessClock _clock;

        public TenantSubscriptionAccessService(IBusinessClock clock) => _clock = clock;

        public DateOnly GetBusinessToday() => _clock.BusinessToday;

        public TenantSubscriptionAccessResult Evaluate(Tenant tenant) =>
            Evaluate(tenant.IsActive, tenant.Status, tenant.ExpirationDate, tenant.SubscriptionPlanId);

        public TenantSubscriptionAccessResult Evaluate(
            bool isActive,
            TenantStatus status,
            DateTime? expirationDate,
            int? subscriptionPlanId = null)
        {
            var today = _clock.BusinessToday;
            var expBiz = ToBusinessDate(expirationDate);

            if (!isActive)
            {
                return Deny(status, status, SubscriptionAccessReasonCode.TenantDisabled,
                    "Your tenant account is disabled. Please contact support.", expirationDate, expBiz, today);
            }

            if (status == TenantStatus.Suspended)
            {
                return Deny(status, status, SubscriptionAccessReasonCode.TenantSuspended,
                    "Your tenant account is currently suspended. Please contact support.",
                    expirationDate, expBiz, today);
            }

            if (status == TenantStatus.Expired)
            {
                return Deny(TenantStatus.Expired, status, SubscriptionAccessReasonCode.StatusExpired,
                    BuildExpiredMessage(expirationDate, expBiz),
                    expirationDate, expBiz, today, requiresRenewal: true);
            }

            if (expirationDate.HasValue && today > expBiz)
            {
                return Deny(TenantStatus.Expired, status, SubscriptionAccessReasonCode.DateExpired,
                    BuildExpiredMessage(expirationDate, expBiz),
                    expirationDate, expBiz, today, requiresRenewal: true);
            }

            if (!subscriptionPlanId.HasValue && !expirationDate.HasValue
                && status is TenantStatus.Trial or TenantStatus.Active)
            {
                return Deny(status, status, SubscriptionAccessReasonCode.NoExpiration,
                    "No active subscription is assigned to this tenant.",
                    null, null, today, requiresRenewal: true);
            }

            return TenantSubscriptionAccessResult.Allowed(
                new Tenant { IsActive = isActive, Status = status, ExpirationDate = expirationDate },
                today);
        }

        public bool IsExpirationDatePassed(DateTime? expirationDate)
        {
            var exp = ToBusinessDate(expirationDate);
            return expirationDate.HasValue && _clock.BusinessToday > exp;
        }

        internal static DateOnly ToBusinessDate(DateTime? expirationDate) =>
            expirationDate.HasValue
                ? DateOnly.FromDateTime(expirationDate.Value.Date)
                : default;

        private static string BuildExpiredMessage(DateTime? expirationDate, DateOnly? expBiz)
        {
            if (expirationDate.HasValue)
                return $"Your subscription expired on {expirationDate.Value:MMMM d, yyyy}. " +
                       "Please contact your administrator or renew your subscription to continue.";
            return "Your subscription has expired. Please contact your administrator or renew your subscription to continue.";
        }

        private static TenantSubscriptionAccessResult Deny(
            TenantStatus effective,
            TenantStatus stored,
            SubscriptionAccessReasonCode code,
            string message,
            DateTime? expirationDate,
            DateOnly? expBiz,
            DateOnly today,
            bool requiresRenewal = false) =>
            new()
            {
                IsAllowed = false,
                EffectiveStatus = effective,
                StoredStatus = stored,
                ReasonCode = code,
                Message = message,
                ExpirationDate = expirationDate,
                ExpirationBusinessDate = expBiz,
                BusinessToday = today,
                GracePeriodEnd = null,
                RequiresRenewal = requiresRenewal
            };
    }
}
