using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HardwareManagementSystem.Services
{
    public class NotificationService
    {
        private readonly ApplicationDbContext _context;

        public NotificationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task CreateAsync(
            string title,
            string message,
            string type = "Info",
            string? icon = null,
            string? targetRole = null,
            string? linkUrl = null)
        {
            var notification = new Notification
            {
                Title = title,
                Message = message,
                Type = type,
                Icon = icon ?? GetDefaultIcon(type),
                TargetRole = targetRole,
                LinkUrl = linkUrl,
                IsRead = false,
                CreatedAt = DateTime.Now
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();
        }

        public async Task<List<Notification>> GetForUserAsync(ClaimsPrincipal user, int take = 15)
        {
            var roles = user.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            return await _context.Notifications
                .AsNoTracking()
                .Where(n => n.TargetRole == null || roles.Contains(n.TargetRole))
                .OrderByDescending(n => n.CreatedAt)
                .Take(take)
                .ToListAsync();
        }

        public async Task<int> GetUnreadCountAsync(ClaimsPrincipal user)
        {
            var roles = user.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            return await _context.Notifications
                .CountAsync(n => !n.IsRead && (n.TargetRole == null || roles.Contains(n.TargetRole)));
        }

        public async Task MarkAsReadAsync(int id)
        {
            var notification = await _context.Notifications.FindAsync(id);
            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }
        }

        public async Task MarkAllAsReadAsync(ClaimsPrincipal user)
        {
            var roles = user.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();

            var unread = await _context.Notifications
                .Where(n => !n.IsRead && (n.TargetRole == null || roles.Contains(n.TargetRole)))
                .ToListAsync();

            foreach (var n in unread)
                n.IsRead = true;

            await _context.SaveChangesAsync();
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
                "Admin",
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
                "Admin",
                "/Users"
            );
        }

        public async Task CreateDiscountNotificationAsync(string receiptNumber, decimal discountAmount)
        {
            await CreateAsync(
                "Discount Applied",
                $"A discount of \u20b1{discountAmount:N2} was applied on receipt {receiptNumber}.",
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
