using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AuditService _auditService;
        private readonly ApplicationDbContext _db;
        private readonly ITenantSubscriptionAccessService _subscriptionAccess;

        public AccountController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            AuditService auditService,
            ApplicationDbContext db,
            ITenantSubscriptionAccessService subscriptionAccess)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _auditService = auditService;
            _db = db;
            _subscriptionAccess = subscriptionAccess;
        }

        // ============================================
        // LOGIN
        // ============================================

        public IActionResult Login(string? returnUrl = null)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.FindByNameAsync(model.Username);

            if (user == null)
            {
                await _auditService.LogAsync(
                    User,
                    "Authentication",
                    "LOGIN_FAILED",
                    $"Failed login attempt for unknown username '{model.Username}'.",
                    "Login",
                    null,
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["ErrorMessage"] = "Invalid username or password.";
                return View(model);
            }

            if (!user.IsActive)
            {
                await _auditService.LogAsync(
                    User,
                    "Authentication",
                    "LOGIN_DISABLED_ACCOUNT",
                    $"Login attempt on disabled account '{user.UserName}'.",
                    "Login",
                    user.Id,
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["ErrorMessage"] =
                    "Your account is disabled. Please contact the administrator.";

                return View(model);
            }

            // ── Tenant subscription access guard ────────────────────────────────
            // SuperAdmin (TenantId == null) bypasses this check entirely.
            if (user.TenantId.HasValue)
            {
                var tenant = await _db.Tenants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == user.TenantId.Value);

                if (tenant != null)
                {
                    var access = _subscriptionAccess.Evaluate(tenant);

                    if (!access.IsAllowed)
                    {
                        await _auditService.LogAsync(
                            User,
                            "Authentication",
                            "LOGIN_TENANT_BLOCKED",
                            $"Login blocked for '{user.UserName}' — {access.ReasonCode}.",
                            "Login",
                            user.Id,
                            HttpContext.Connection.RemoteIpAddress?.ToString());

                        HttpContext.Session.SetString("SubAccess_TenantName", tenant.Name);
                        HttpContext.Session.SetString("SubAccess_Status", access.EffectiveStatus.ToString());
                        HttpContext.Session.SetString("SubAccess_Message", access.Message);
                        if (access.ExpirationDate.HasValue)
                            HttpContext.Session.SetString("SubAccess_Expiration",
                                access.ExpirationDate.Value.ToString("yyyy-MM-dd"));

                        return RedirectToAction(nameof(SubscriptionExpired));
                    }
                }
            }

            var result = await _signInManager.PasswordSignInAsync(
                user,
                model.Password,
                false,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                await _auditService.LogAsync(
                    User,
                    "Authentication",
                    "LOGIN_SUCCESS",
                    $"User '{user.UserName}' logged in successfully.",
                    "Login",
                    user.Id,
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                // If a SuperAdmin-forced password reset is pending, redirect immediately.
                if (user.ForcePasswordChange)
                    return RedirectToAction(nameof(ChangePassword), "Account");

                // Explicit returnUrl always takes priority (e.g. from [Authorize] redirect)
                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);

                // SuperAdmin goes directly to the platform dashboard, not the store dashboard
                var roles = await _userManager.GetRolesAsync(user);
                if (roles.Contains("SuperAdmin"))
                    return RedirectToAction("Dashboard", "SuperAdmin");

                return RedirectToAction("Index", "Home");
            }

            if (result.IsLockedOut)
            {
                var lockoutEnd = await _userManager.GetLockoutEndDateAsync(user);
                var remaining = lockoutEnd.HasValue
                    ? (int)Math.Ceiling((lockoutEnd.Value - DateTimeOffset.UtcNow).TotalMinutes)
                    : 15;

                await _auditService.LogAsync(
                    User,
                    "Authentication",
                    "LOGIN_LOCKOUT",
                    $"Account '{user.UserName}' is locked out for {remaining} minute(s).",
                    "Login",
                    user.Id,
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["ErrorMessage"] =
                    $"Account locked due to multiple failed attempts. Try again in {remaining} minute(s).";

                return View(model);
            }

            // Failed login — log and return error
            await _auditService.LogAsync(
                User,
                "Authentication",
                "LOGIN_FAILED",
                $"Failed login attempt for username '{model.Username}'.",
                "Login",
                user.Id,
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["ErrorMessage"] = "Invalid username or password.";

            return View(model);
        }

        // ============================================
        // PROFILE
        // ============================================

        [Authorize]
        public IActionResult Profile()
        {
            return View();
        }

        // ============================================
        // CHANGE PASSWORD
        // ============================================

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ChangePassword()
        {
            var user = await _userManager.GetUserAsync(User);
            ViewBag.ForcePasswordChange = user?.ForcePasswordChange ?? false;
            return View(new ChangePasswordViewModel());
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(
            ChangePasswordViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
                return RedirectToAction(nameof(Login));

            if (!ModelState.IsValid)
            {
                ViewBag.ForcePasswordChange = user.ForcePasswordChange;
                return View(model);
            }

            var result = await _userManager.ChangePasswordAsync(
                user,
                model.CurrentPassword,
                model.NewPassword);

            if (result.Succeeded)
            {
                bool wasForcedChange = user.ForcePasswordChange;

                if (wasForcedChange)
                {
                    user.ForcePasswordChange = false;
                    await _userManager.UpdateAsync(user);
                }

                await _signInManager.RefreshSignInAsync(user);

                await _auditService.LogAsync(
                    User,
                    "Account",
                    "PASSWORD_CHANGED",
                    "User changed account password.",
                    "User",
                    user.Id,
                    HttpContext.Connection.RemoteIpAddress?.ToString()
                );

                TempData["SuccessMessage"] = "Password changed successfully.";

                // After a forced password change redirect to the appropriate dashboard
                // so the user can continue working without looping back to this page.
                if (wasForcedChange)
                {
                    var roles = await _userManager.GetRolesAsync(user);
                    if (roles.Contains("SuperAdmin"))
                        return RedirectToAction("Dashboard", "SuperAdmin");

                    return RedirectToAction("Index", "Home");
                }

                return RedirectToAction(nameof(ChangePassword));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            // Preserve the forced-change banner when returning the form with errors.
            ViewBag.ForcePasswordChange = user.ForcePasswordChange;
            return View(model);
        }

        // ============================================
        // LOGOUT
        // ============================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();

            return RedirectToAction("Login", "Account");
        }

        // ============================================
        // ACCESS DENIED
        // ============================================

        public IActionResult AccessDenied()
        {
            return View();
        }

        // ============================================
        // SUSPENDED / EXPIRED TENANT
        // ============================================

        [AllowAnonymous]
        public IActionResult Suspended() => RedirectToAction(nameof(SubscriptionExpired));

        [AllowAnonymous]
        public IActionResult SubscriptionExpired()
        {
            ViewBag.TenantName = HttpContext.Session.GetString("SubAccess_TenantName") ?? "Your organization";
            ViewBag.Status = HttpContext.Session.GetString("SubAccess_Status") ?? "Expired";
            ViewBag.Message = HttpContext.Session.GetString("SubAccess_Message")
                ?? "Your subscription is no longer active. Please contact your administrator.";
            var exp = HttpContext.Session.GetString("SubAccess_Expiration");
            ViewBag.ExpirationDisplay = exp != null && DateTime.TryParse(exp, out var d)
                ? d.ToString("MMMM d, yyyy")
                : null;
            return View();
        }
    }
}