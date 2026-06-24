using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.Backups;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class BackupManagementController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IBackupService _backupService;
        private readonly AuditService _auditService;
        private readonly IConfiguration _configuration;

        public BackupManagementController(
            ApplicationDbContext context,
            IBackupService backupService,
            AuditService auditService,
            IConfiguration configuration)
        {
            _context = context;
            _backupService = backupService;
            _auditService = auditService;
            _configuration = configuration;
        }

        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Backup Management";

            var platformDbName = GetPlatformDatabaseName();
            var tenants = await _context.Tenants.AsNoTracking()
                .OrderBy(t => t.Name)
                .ToListAsync();

            var tenantStatuses = new List<TenantBackupStatusVm>();
            foreach (var tenant in tenants)
            {
                tenantStatuses.Add(new TenantBackupStatusVm
                {
                    TenantId = tenant.Id,
                    Code = tenant.Code,
                    Name = tenant.Name,
                    IsDedicated = tenant.IsDatabaseProvisioned,
                    DatabaseName = tenant.DatabaseName,
                    LastBackup = await _backupService.GetLatestTenantBackupAsync(tenant.Id)
                });
            }

            var vm = new BackupManagementIndexVm
            {
                PlatformDatabaseName = platformDbName,
                LatestPlatformBackup = await _backupService.GetLatestPlatformBackupAsync(),
                Tenants = tenantStatuses,
                History = await _backupService.GetBackupHistoryAsync(50),
                AllowTenantBackupRequest = _configuration.GetValue<bool>("BackupSettings:AllowTenantBackupRequest", true)
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BackupPlatform()
        {
            try
            {
                var record = await _backupService.BackupPlatformDatabaseAsync(
                    BackupType.Manual,
                    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);

                await _auditService.LogAsync(
                    User,
                    "BackupManagement",
                    "BACKUP_PLATFORM",
                    $"Platform backup {record.Status}: {record.BackupFileName}",
                    "BackupRecord",
                    record.Id.ToString());

                if (record.Status == BackupStatus.Success)
                    TempData["SuccessMessage"] = $"Platform database backup completed ({FormatFileSize(record.BackupSizeBytes)}).";
                else
                    TempData["ErrorMessage"] = $"Platform backup failed: {record.ErrorMessage}";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Platform backup failed: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BackupTenant(int tenantId)
        {
            try
            {
                var tenant = await _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId);
                if (tenant == null)
                {
                    TempData["ErrorMessage"] = "Tenant not found.";
                    return RedirectToAction(nameof(Index));
                }

                var record = await _backupService.BackupTenantDatabaseAsync(
                    tenantId,
                    BackupType.Manual,
                    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);

                await _auditService.LogAsync(
                    User,
                    "BackupManagement",
                    "BACKUP_TENANT",
                    $"Tenant {tenant.Code} backup {record.Status}: {record.BackupFileName}",
                    "BackupRecord",
                    record.Id.ToString());

                if (record.Status == BackupStatus.Success)
                    TempData["SuccessMessage"] = $"Tenant '{tenant.Code}' backup completed ({FormatFileSize(record.BackupSizeBytes)}).";
                else
                    TempData["ErrorMessage"] = $"Tenant backup failed: {record.ErrorMessage}";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Tenant backup failed: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BackupAll()
        {
            try
            {
                var results = await _backupService.BackupAllDatabasesAsync(
                    BackupType.Manual,
                    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);

                var success = results.Count(r => r.Status == BackupStatus.Success);
                var failed = results.Count(r => r.Status == BackupStatus.Failed);

                await _auditService.LogAsync(
                    User,
                    "BackupManagement",
                    "BACKUP_ALL",
                    $"Backup All completed: {success} success, {failed} failed.",
                    "BackupRecord",
                    null);

                if (failed == 0)
                    TempData["SuccessMessage"] = $"Backup All completed: {success} database(s) backed up successfully.";
                else
                    TempData["ErrorMessage"] = $"Backup All finished with {failed} failure(s). Check backup history for details.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Backup All failed: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunRetentionCleanup()
        {
            var result = await _backupService.CleanupExpiredBackupsAsync();

            await _auditService.LogAsync(
                User,
                "BackupManagement",
                "BACKUP_RETENTION_CLEANUP",
                $"Retention cleanup: {result.FilesDeleted} files deleted, {result.RecordsRemoved} records removed.",
                "BackupRecord",
                null);

            TempData["SuccessMessage"] =
                $"Retention cleanup completed: {result.FilesDeleted} file(s) deleted, {result.RecordsRemoved} record(s) removed.";
            return RedirectToAction(nameof(Index));
        }

        private string GetPlatformDatabaseName()
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
                return "—";

            try
            {
                return new SqlConnectionStringBuilder(connectionString).InitialCatalog ?? "—";
            }
            catch
            {
                return "—";
            }
        }

        private static string FormatFileSize(long? bytes)
        {
            if (bytes == null || bytes <= 0)
                return "0 B";

            var size = (double)bytes.Value;
            if (size < 1024) return $"{size:N0} B";
            if (size < 1024 * 1024) return $"{size / 1024:N1} KB";
            if (size < 1024 * 1024 * 1024) return $"{size / (1024 * 1024):N1} MB";
            return $"{size / (1024 * 1024 * 1024):N2} GB";
        }
    }
}
