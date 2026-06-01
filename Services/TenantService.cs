using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Services
{
    public class TenantService
    {
        private readonly ApplicationDbContext _context;
        private readonly ITenantContext _tenantContext;

        public TenantService(ApplicationDbContext context, ITenantContext tenantContext)
        {
            _context = context;
            _tenantContext = tenantContext;
        }

        /// <summary>Returns the current user's tenant, or null if the user has no tenant assigned.</summary>
        public async Task<Tenant?> GetCurrentTenantAsync()
        {
            var tenantId = _tenantContext.CurrentTenantId;

            if (tenantId.HasValue)
                return await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId.Value);

            return null;
        }

        /// <summary>Returns all tenants. For future SuperAdmin use.</summary>
        public async Task<List<Tenant>> GetAllTenantsAsync()
        {
            return await _context.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync();
        }

        /// <summary>Returns all active tenants. For future SuperAdmin use.</summary>
        public async Task<List<Tenant>> GetAllActiveTenantsAsync()
        {
            return await _context.Tenants.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync();
        }

        /// <summary>Validates that a tenant exists and is active.</summary>
        public async Task<bool> IsTenantActiveAsync(int tenantId)
        {
            return await _context.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId && t.IsActive);
        }

        /// <summary>Resolves the effective tenant ID from the current user's assigned TenantId.</summary>
        public Task<int?> ResolveEffectiveTenantIdAsync()
        {
            return Task.FromResult(_tenantContext.CurrentTenantId);
        }
    }
}
