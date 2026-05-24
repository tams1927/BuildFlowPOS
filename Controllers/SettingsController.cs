using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Settings", "View")]
    public class SettingsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;

        public SettingsController(ApplicationDbContext context, AuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        public async Task<IActionResult> Index()
        {
            var setting = await _context.SystemSettings.FirstOrDefaultAsync();

            if (setting == null)
            {
                setting = new SystemSetting();
                _context.SystemSettings.Add(setting);
                await _context.SaveChangesAsync();
            }

            return View(setting);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(SystemSetting model)
        {
            var setting = await _context.SystemSettings.FirstOrDefaultAsync();

            if (setting == null)
            {
                TempData["ErrorMessage"] = "System setting not found.";
                return RedirectToAction(nameof(Index));
            }

            setting.BusinessName = model.BusinessName;
            setting.BusinessAddress = model.BusinessAddress;
            setting.ContactNumber = model.ContactNumber;
            setting.Email = model.Email;
            setting.CurrencySymbol = model.CurrencySymbol;
            setting.DefaultVatPercent = model.DefaultVatPercent;
            setting.TaxMode = model.TaxMode;
            setting.ReceiptFooter = model.ReceiptFooter;
            setting.UpdatedAt = DateTime.Now;
            setting.ReceiptPaperSize = model.ReceiptPaperSize;
            setting.ThemeColor = model.ThemeColor;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
                User,
                "Settings",
                "UPDATED",
                $"System settings updated. Business Name: {setting.BusinessName}, TaxMode: {setting.TaxMode}, VAT: {setting.DefaultVatPercent:0.##}%, Paper Size: {setting.ReceiptPaperSize}",
                "SystemSetting",
                setting.Id.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );

            TempData["SuccessMessage"] = "System settings updated successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}