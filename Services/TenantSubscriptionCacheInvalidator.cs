using Microsoft.Extensions.Caching.Memory;

namespace HardwareManagementSystem.Services
{
    public interface ITenantSubscriptionCacheInvalidator
    {
        void InvalidateTenant(int tenantId);
    }

    public sealed class TenantSubscriptionCacheInvalidator : ITenantSubscriptionCacheInvalidator
    {
        private readonly IMemoryCache _cache;

        public TenantSubscriptionCacheInvalidator(IMemoryCache cache) => _cache = cache;

        public void InvalidateTenant(int tenantId)
        {
            _cache.Remove(TenantSubscriptionCacheKeys.Blocked(tenantId));
            _cache.Remove(TenantSubscriptionCacheKeys.Access(tenantId));
        }
    }

    public static class TenantSubscriptionCacheKeys
    {
        public static string Blocked(int tenantId) => $"tenant_blocked_{tenantId}";
        public static string Access(int tenantId) => $"tenant_access_{tenantId}";
        public static string UserTenant(string userId) => $"tid_uid_{userId}";
    }
}
