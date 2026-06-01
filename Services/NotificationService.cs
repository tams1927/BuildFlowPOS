using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HardwareManagementSystem.Services
{
    public class NotificationService
    {
        // Phase 5.0D.2 — notifications are tenant-owned operational data and route to the
        // tenant's dedicated database when routing is active (shared otherwise).
        private readonly ITenantOperationalContextProvider _operationalContextProvider;
        private readonly TenantGuard _tenantGuard;
        private readonly ITenantContext _tenantContext;

        public NotificationService(
            ITenantOperationalContextProvider operationalContextProvider,
            TenantGuard tenantGuard,
            ITenantContext tenantContext)
        {
            _operationalContextProvider = operationalContextProvider;
            _tenantGuard = tenantGuard;
            _tenantContext = tenantContext;
        }

        public async Task CreateAsync(
            string title,
            string message,
            string type = "Info",
            string? icon = null,
            string? targetRole = null,
            string? linkUrl = null)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var notification = new Notification
            {
                TenantId = tenantId,
                Title = title,
                Message = message,
                Type = type,
                Icon = icon ?? GetDefaultIcon(type),
                TargetRole = targetRole,
                LinkUrl = linkUrl,
                IsRead = false,
                CreatedAt = DateTime.Now
            };

            var db = await _operationalContextProvider.GetContextAsync();
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();
        }

        public async Task<List<Notification>> GetForUserAsync(ClaimsPrincipal user, int take = 15)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var roles = user.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            var db = await _operationalContextProvider.GetContextAsync();
            var query = db.Notifications
                .AsNoTracking()
                .Where(n => n.TargetRole == null || roles.Contains(n.TargetRole));

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(n =>
                    n.TenantId == tenantId ||
                    n.TenantId == null);
            }

            return await query
                .OrderByDescending(n => n.CreatedAt)
                .Take(take)
                .ToListAsync();
        }

        public async Task<int> GetUnreadCountAsync(ClaimsPrincipal user)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var roles = user.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            var db = await _operationalContextProvider.GetContextAsync();
            var query = db.Notifications
                .AsNoTracking()
                .Where(n => !n.IsRead && (n.TargetRole == null || roles.Contains(n.TargetRole)));

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(n =>
                    n.TenantId == tenantId ||
                    n.TenantId == null);
            }

            return await query.CountAsync();
        }

        public async Task MarkAsReadAsync(int id)
        {
            var db = await _operationalContextProvider.GetContextAsync();
            var notification = await db.Notifications.FindAsync(id);

            if (notification == null)
                return;

            if (!await _tenantGuard.CanAccessTenantAsync(notification.TenantId))
                return;

            notification.IsRead = true;

            await db.SaveChangesAsync();
        }

        public async Task MarkAllAsReadAsync(ClaimsPrincipal user)
        {
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var roles = user.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            var db = await _operationalContextProvider.GetContextAsync();
            var query = db.Notifications
                .Where(n => !n.IsRead && (n.TargetRole == null || roles.Contains(n.TargetRole)));

            if (!_tenantContext.IsGlobalUser && tenantId.HasValue)
            {
                query = query.Where(n =>
                    n.TenantId == tenantId ||
                    n.TenantId == null);
            }

            await query.ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        }

        public async Task CreateLowStockNotificationAsync(string itemName)
        {
            await CreateAsync(
                "Low Stock Alert",
                $"Item \"{itemName}\" is running low on stock.",
                "Warning",
                "bi bi-exclamation-triangle",
                null,
                "/Inventory"
            );
        }

        public async Task CreateOutOfStockNotificationAsync(string itemName)
        {
            await CreateAsync(
                "Out of Stock",
                $"Item \"{itemName}\" is out of stock.",
                "Danger",
                "bi bi-x-circle",
                null,
                "/Inventory"
            );
        }

        public async Task CreateStockAdjustmentNotificationAsync(string adjustmentNumber, string itemName, string adjustmentType, decimal quantity)
        {
            await CreateAsync(
                "Stock Adjustment Created",
                $"Adjustment {adjustmentNumber}: {adjustmentType} {quantity:0.###} units of \"{itemName}\".",
                "Info",
                "bi bi-sliders",
                null,
                "/StockAdjustment"
            );
        }

        public async Task CreateStockInNotificationAsync(string stockInNumber, string supplierName, string itemName, decimal quantity)
        {
            await CreateAsync(
                "Stock Received",
                $"Stock In {stockInNumber}: Received {quantity:0.###} units of \"{itemName}\" from {supplierName}.",
                "Success",
                "bi bi-box-arrow-in-down",
                null,
                "/StockIn"
            );
        }

        public async Task CreateItemDeactivatedNotificationAsync(string itemName)
        {
            await CreateAsync(
                "Item Deactivated",
                $"Inventory item \"{itemName}\" has been deactivated.",
                "Warning",
                "bi bi-slash-circle",
                "TenantAdmin",
                "/Inventory"
            );
        }

        public async Task CreateNewUserNotificationAsync(string userName, string role)
        {
            await CreateAsync(
                "New User Created",
                $"User \"{userName}\" was created with role: {role}.",
                "Info",
                "bi bi-person-plus",
                "TenantAdmin",
                "/Users"
            );
        }

        public async Task CreateDiscountNotificationAsync(string receiptNumber, decimal discountAmount)
        {
            await CreateAsync(
                "Discount Applied",
                $"A discount of ₱{discountAmount:N2} was applied on receipt {receiptNumber}.",
                "Warning",
                "bi bi-tag",
                null,
                "/Sales"
            );
        }

        private static string GetDefaultIcon(string type) => type switch
        {
            "Warning" => "bi bi-exclamation-triangle",
            "Danger" => "bi bi-x-circle",
            "Success" => "bi bi-check-circle",
            _ => "bi bi-info-circle"
        };
    }
}