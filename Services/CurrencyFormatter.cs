using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Services
{
    public sealed class CurrencyFormatter : ICurrencyFormatter
    {
        private readonly ITenantOperationalContextProvider _ctxProvider;
        private readonly ITenantContext _tenantContext;
        private SystemSetting? _cached;

        public CurrencyFormatter(
            ITenantOperationalContextProvider ctxProvider,
            ITenantContext tenantContext)
        {
            _ctxProvider = ctxProvider;
            _tenantContext = tenantContext;
        }

        public async Task<SystemSetting?> GetTenantSettingsAsync()
        {
            if (_cached != null)
                return _cached;

            var tenantId = _tenantContext.CurrentTenantId;
            if (!tenantId.HasValue)
                return null;

            var db = await _ctxProvider.GetContextAsync();
            _cached = await db.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            return _cached;
        }

        public string Format(decimal amount, SystemSetting? settings = null)
        {
            var symbol = settings?.CurrencySymbol;
            if (string.IsNullOrWhiteSpace(symbol))
                symbol = "₱";
            return $"{symbol}{amount:N2}";
        }

        public string Format(decimal amount, string currencySymbol) =>
            $"{currencySymbol}{amount:N2}";
    }
}
