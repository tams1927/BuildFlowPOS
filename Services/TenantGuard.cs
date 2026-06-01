namespace HardwareManagementSystem.Services
{
    /// <summary>
    /// Reusable service for tenant-aware writes and cross-tenant access prevention.
    /// Admin/SuperAdmin (IsGlobalUser) are always unrestricted.
    /// Tenant users are scoped strictly to their assigned TenantId.
    /// </summary>
    public class TenantGuard
    {
        private readonly ITenantContext _tenantContext;

        private int? _cachedEffectiveTenantId;
        private bool _resolved = false;

        public TenantGuard(ITenantContext tenantContext)
        {
            _tenantContext = tenantContext;
        }

        // ============================================
        // EFFECTIVE TENANT ID
        // Returns current user's TenantId.
        // Returns null for Admin/SuperAdmin (global users).
        // ============================================

        public Task<int?> GetEffectiveTenantIdAsync()
        {
            if (_resolved)
                return Task.FromResult(_cachedEffectiveTenantId);

            _resolved = true;

            _cachedEffectiveTenantId = _tenantContext.IsGlobalUser
                ? null
                : _tenantContext.CurrentTenantId;

            return Task.FromResult(_cachedEffectiveTenantId);
        }

        // ============================================
        // VALIDATE TENANT ACCESS
        // Returns true if the current user may access this entity.
        // Admin/SuperAdmin: always allowed.
        // Tenant users: entity must belong to same tenant; null TenantId is denied.
        // ============================================

        public async Task<bool> CanAccessAsync(int? entityTenantId)
        {
            if (_tenantContext.IsGlobalUser)
                return true;

            // Null entityTenantId means the row is unscoped/platform-level.
            // Tenant users must not access or mutate unscoped rows.
            if (entityTenantId == null)
                return false;

            var effectiveTenantId = await GetEffectiveTenantIdAsync();

            if (effectiveTenantId == null)
                return false;

            return entityTenantId == effectiveTenantId;
        }

        // ============================================
        // VALIDATE TENANT ACCESS
        // Compatibility alias
        // ============================================

        public Task<bool> CanAccessTenantAsync(int? entityTenantId)
        {
            return CanAccessAsync(entityTenantId);
        }

        // ============================================
        // VALIDATE TENANT ACCESS (throws)
        // ============================================

        public async Task AssertAccessAsync(int? entityTenantId)
        {
            if (!await CanAccessAsync(entityTenantId))
            {
                throw new UnauthorizedAccessException("Cross-tenant access denied.");
            }
        }

        // ============================================
        // IS SAME TENANT
        // Safe during null/legacy transition
        // ============================================

        public Task<bool> IsSameTenantAsync(int? a, int? b)
        {
            if (_tenantContext.IsGlobalUser)
                return Task.FromResult(true);

            // Both sides must be non-null and equal.
            if (a == null || b == null)
                return Task.FromResult(false);

            return Task.FromResult(a == b);
        }
    }
}