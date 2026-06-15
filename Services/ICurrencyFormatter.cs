using HardwareManagementSystem.Models;

namespace HardwareManagementSystem.Services
{
    public interface ICurrencyFormatter
    {
        string Format(decimal amount, SystemSetting? settings = null);
        string Format(decimal amount, string currencySymbol);
        Task<SystemSetting?> GetTenantSettingsAsync();
    }
}
