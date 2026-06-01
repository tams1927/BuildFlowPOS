using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HardwareManagementSystem.Controllers
{
    /// <summary>
    /// Intentionally uses only [Authorize] — NOT [PermissionAuthorize].
    ///
    /// Reason: Notifications are a user-personal UI element rendered in the shared
    /// _Layout.cshtml top bar for all authenticated users. Gating them with a
    /// module permission would require every role to explicitly hold a Notifications
    /// module entry, and a misconfiguration would silently break the top-bar
    /// notification dropdown for every user. The three actions here (MarkRead,
    /// MarkAllRead, GetUnreadCount) are non-destructive, user-scoped, and carry
    /// no cross-tenant data risk — they delegate to NotificationService which
    /// filters strictly by the current user's identity.
    /// </summary>
    [Authorize]
    public class NotificationsController : Controller
    {
        private readonly NotificationService _notificationService;

        public NotificationsController(NotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkRead(int id, string? returnUrl)
        {
            await _notificationService.MarkAsReadAsync(id);
            return Redirect(returnUrl ?? "/");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllRead(string? returnUrl)
        {
            await _notificationService.MarkAllAsReadAsync(User);
            TempData["SuccessMessage"] = "All notifications marked as read.";
            return Redirect(returnUrl ?? "/");
        }

        [HttpGet]
        public async Task<IActionResult> GetUnreadCount()
        {
            var count = await _notificationService.GetUnreadCountAsync(User);
            return Json(new { count });
        }
    }
}
