using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HardwareManagementSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AuditService _auditService;

        public AccountController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            AuditService auditService)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _auditService = auditService;
        }

        // ============================================
        // LOGIN
        // ============================================

        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
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
        public IActionResult ChangePassword()
        {
            return View(new ChangePasswordViewModel());
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(
            ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return RedirectToAction(nameof(Login));
            }

            var result = await _userManager.ChangePasswordAsync(
                user,
                model.CurrentPassword,
                model.NewPassword);

            if (result.Succeeded)
            {
                await _signInManager.RefreshSignInAsync(user);

                await _auditService.LogAsync(
                    User,
                    "Account",
                    "PASSWORD_CHANGED",
                    "User changed account password",
                    "User",
                    user.Id,
                    HttpContext.Connection.RemoteIpAddress?.ToString()
                );

                TempData["SuccessMessage"] =
                    "Password changed successfully.";

                return RedirectToAction(nameof(ChangePassword));
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(
                    string.Empty,
                    error.Description);
            }

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
    }
}