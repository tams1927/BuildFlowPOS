using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Settings", "View")]
    public class SettingsController : OperationalDbController
    {
        private readonly AuditService _auditService;
        private readonly ITenantContext _tenantContext;
        private readonly IWebHostEnvironment _env;

        private static readonly HashSet<string> _allowedLogoTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp"
        };

        private const long MaxLogoBytes = 2 * 1024 * 1024; // 2 MB

        public SettingsController(
            ITenantOperationalContextProvider ctxProvider,
            AuditService auditService,
            ITenantContext tenantContext,
            IWebHostEnvironment env)
            : base(ctxProvider)
        {
            _auditService = auditService;
            _tenantContext = tenantContext;
            _env = env;
        }

        public async Task<IActionResult> Index()
        {
            var tenantId = _tenantContext.CurrentTenantId;

            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            if (setting == null)
            {
                setting = new SystemSetting { TenantId = tenantId };
                _context.SystemSettings.Add(setting);
                await _context.SaveChangesAsync();
            }

            return View(setting);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Settings", "Edit")]
        public async Task<IActionResult> Update(SystemSetting model)
        {
            var tenantId = _tenantContext.CurrentTenantId;

            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            if (setting == null)
            {
                TempData["ErrorMessage"] = "Settings record not found.";
                return RedirectToAction(nameof(Index));
            }

            if (setting.TenantId != tenantId)
                return Forbid();

            setting.BusinessName      = model.BusinessName;
            setting.BusinessAddress   = model.BusinessAddress;
            setting.ContactNumber     = model.ContactNumber;
            setting.Email             = model.Email;
            setting.CurrencyCode      = string.IsNullOrWhiteSpace(model.CurrencyCode) ? "PHP" : model.CurrencyCode.Trim().ToUpperInvariant();
            setting.CurrencySymbol    = string.IsNullOrWhiteSpace(model.CurrencySymbol) ? "₱" : model.CurrencySymbol.Trim();
            setting.CurrencyName      = string.IsNullOrWhiteSpace(model.CurrencyName) ? "Philippine Peso" : model.CurrencyName.Trim();
            setting.DefaultVatPercent = model.DefaultVatPercent;
            setting.TaxMode           = model.TaxMode;
            setting.ReceiptFooter     = model.ReceiptFooter;
            setting.UpdatedAt         = DateTime.Now;
            setting.ReceiptPaperSize  = model.ReceiptPaperSize;
            setting.ThemeColor        = model.ThemeColor;
            setting.TIN               = model.TIN?.Trim();
            setting.VATRegNumber      = model.VATRegNumber?.Trim();

            await _context.SaveChangesAsync();

            var currencyChanged = model.CurrencyCode != null || model.CurrencySymbol != null;
            if (currencyChanged)
            {
                await _auditService.LogAsync(
                    User, "Settings", "SETTINGS_CURRENCY_UPDATED",
                    $"Currency updated: {setting.CurrencyCode} {setting.CurrencySymbol} ({setting.CurrencyName})",
                    "SystemSetting", setting.Id.ToString(),
                    HttpContext.Connection.RemoteIpAddress?.ToString());
            }

            await _auditService.LogAsync(
                User,
                "Settings",
                "SETTINGS_UPDATED",
                $"Company profile updated. Business: {setting.BusinessName}, TIN: {setting.TIN}, VATReg: {setting.VATRegNumber}, TaxMode: {setting.TaxMode}",
                "SystemSetting",
                setting.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = "Settings updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermissionAuthorize("Settings", "Edit")]
        public async Task<IActionResult> UploadLogo(IFormFile logo)
        {
            var tenantId = _tenantContext.CurrentTenantId;

            if (!tenantId.HasValue)
            {
                TempData["ErrorMessage"] = "Tenant context not found.";
                return RedirectToAction(nameof(Index));
            }

            if (logo == null || logo.Length == 0)
            {
                TempData["ErrorMessage"] = "Please select an image file.";
                return RedirectToAction(nameof(Index));
            }

            if (!_allowedLogoTypes.Contains(logo.ContentType))
            {
                TempData["ErrorMessage"] = "Invalid file type. Allowed: JPG, PNG, GIF, WebP.";
                return RedirectToAction(nameof(Index));
            }

            if (logo.Length > MaxLogoBytes)
            {
                TempData["ErrorMessage"] = "File too large. Maximum size is 2 MB.";
                return RedirectToAction(nameof(Index));
            }

            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.TenantId == tenantId);

            if (setting == null)
            {
                TempData["ErrorMessage"] = "Settings record not found.";
                return RedirectToAction(nameof(Index));
            }

            // Delete old logo file if one exists
            if (!string.IsNullOrWhiteSpace(setting.LogoPath))
            {
                var oldAbsPath = Path.Combine(_env.WebRootPath, setting.LogoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(oldAbsPath))
                    System.IO.File.Delete(oldAbsPath);
            }

            // Determine extension from content type
            var ext = logo.ContentType.ToLower() switch
            {
                "image/jpeg" or "image/jpg" => ".jpg",
                "image/png"                 => ".png",
                "image/gif"                 => ".gif",
                "image/webp"                => ".webp",
                _                           => ".jpg"
            };

            var uploadDir = Path.Combine(_env.WebRootPath, "uploads", "logos", $"tenant-{tenantId}");
            Directory.CreateDirectory(uploadDir);

            var fileName = $"logo{ext}";
            var filePath = Path.Combine(uploadDir, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await logo.CopyToAsync(stream);
            }

            setting.LogoPath  = $"/uploads/logos/tenant-{tenantId}/{fileName}";
            setting.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                User,
                "Settings",
                "COMPANY_LOGO_UPDATED",
                $"Company logo uploaded. File: {fileName}, Size: {logo.Length / 1024:N0} KB",
                "SystemSetting",
                setting.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = "Logo uploaded successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}
