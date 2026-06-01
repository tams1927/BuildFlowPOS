using HardwareManagementSystem.Data;

namespace HardwareManagementSystem.Services.TenantDatabases
{
    /// <summary>
    /// Default <see cref="ITenantOperationalContextProvider"/>.
    ///
    /// Decides per request whether to hand back the shared context or a freshly
    /// created dedicated context, based on <see cref="ITenantDatabaseResolver.IsRoutingActiveAsync"/>.
    /// </summary>
    public sealed class TenantOperationalContextProvider
        : ITenantOperationalContextProvider, IAsyncDisposable, IDisposable
    {
        private readonly ApplicationDbContext _shared;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantDatabaseResolver _resolver;
        private readonly ITenantDbContextFactory _factory;
        private readonly ILogger<TenantOperationalContextProvider> _logger;

        // Cache dedicated contexts per tenant for the lifetime of this (scoped) provider.
        private readonly Dictionary<int, TenantDbContext> _dedicated = new();

        public TenantOperationalContextProvider(
            ApplicationDbContext shared,
            ITenantContext tenantContext,
            ITenantDatabaseResolver resolver,
            ITenantDbContextFactory factory,
            ILogger<TenantOperationalContextProvider> logger)
        {
            _shared        = shared;
            _tenantContext = tenantContext;
            _resolver      = resolver;
            _factory       = factory;
            _logger        = logger;
        }

        public async Task<ITenantOperationalDbContext> GetContextAsync()
        {
            var tenantId = _tenantContext.CurrentTenantId
                           ?? await _tenantContext.GetCurrentTenantIdAsync();

            return tenantId.HasValue
                ? await GetContextAsync(tenantId.Value)
                : _shared;
        }

        public async Task<ITenantOperationalDbContext> GetContextAsync(int tenantId)
        {
            if (!await _resolver.IsRoutingActiveAsync(tenantId))
                return _shared;

            if (_dedicated.TryGetValue(tenantId, out var existing))
                return existing;

            var ctx = await _factory.CreateAsync(tenantId);
            _dedicated[tenantId] = ctx;

            _logger.LogInformation(
                "Tenant {TenantId} routed to its DEDICATED operational database.", tenantId);

            return ctx;
        }

        public async Task<string> GetRuntimeDatabaseAsync()
        {
            var tenantId = _tenantContext.CurrentTenantId
                           ?? await _tenantContext.GetCurrentTenantIdAsync();

            if (!tenantId.HasValue) return "Shared";
            return await _resolver.GetRuntimeDatabaseAsync(tenantId.Value);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var ctx in _dedicated.Values)
                await ctx.DisposeAsync();
            _dedicated.Clear();
        }

        public void Dispose()
        {
            foreach (var ctx in _dedicated.Values)
                ctx.Dispose();
            _dedicated.Clear();
        }
    }
}
