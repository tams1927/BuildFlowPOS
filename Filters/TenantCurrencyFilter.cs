using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HardwareManagementSystem.Filters
{
    /// <summary>Exposes tenant currency fields on ViewBag for every MVC action.</summary>
    public sealed class TenantCurrencyFilter : IAsyncActionFilter
    {
        private readonly ICurrencyFormatter _currency;

        public TenantCurrencyFilter(ICurrencyFormatter currency)
        {
            _currency = currency;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (context.Controller is Controller controller)
            {
                var settings = await _currency.GetTenantSettingsAsync();
                controller.ViewBag.CurrencySymbol = settings?.CurrencySymbol ?? "₱";
                controller.ViewBag.CurrencyCode = settings?.CurrencyCode ?? "PHP";
                controller.ViewBag.CurrencyName = settings?.CurrencyName ?? "Philippine Peso";
                controller.ViewBag.TenantSettings = settings;
            }

            await next();
        }
    }
}
