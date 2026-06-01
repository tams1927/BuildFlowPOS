using HardwareManagementSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Services.TenantDatabases
{
    /// <summary>
    /// Default <see cref="ITenantDbContextFactory"/> implementation.
    ///
    /// Resolves the tenant connection string via <see cref="ITenantDatabaseResolver"/>
    /// and constructs a <see cref="TenantDbContext"/> bound to it.
    ///
    /// Phase 5.0B: Wired into DI but not used by runtime controllers. Because every
    /// tenant is in Shared mode, the resolver returns the DefaultConnection — so a
    /// context created here would point at the same shared database. No controller
    /// uses this yet, so runtime behaviour is unchanged.
    /// </summary>
    public class TenantDbContextFactory : ITenantDbContextFactory
    {
        private readonly ITenantDatabaseResolver _resolver;
        private readonly ILogger<TenantDbContextFactory> _logger;

        public TenantDbContextFactory(
            ITenantDatabaseResolver resolver,
            ILogger<TenantDbContextFactory> logger)
        {
            _resolver = resolver;
            _logger   = logger;
        }

        public async Task<TenantDbContext> CreateAsync(int tenantId)
        {
            var connectionString = await _resolver.GetConnectionStringAsync(tenantId);

            _logger.LogInformation(
                "Created TenantDbContext for tenant {TenantId} ({Masked}).",
                tenantId, TenantDatabaseResolver.Mask(connectionString));

            return CreateForConnection(connectionString);
        }

        public TenantDbContext CreateForConnection(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string is required.", nameof(connectionString));

            var options = new DbContextOptionsBuilder<TenantDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            return new TenantDbContext(options);
        }
    }
}
