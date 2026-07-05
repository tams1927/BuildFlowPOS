using HardwareManagementSystem.Configuration;
using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.Backups;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HardwareManagementSystem.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class BackupManagementController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IBackupService _backupService;
        private readonly IRestoreService _restoreService;
        private readonly AuditService _auditService;
        private readonly IConfiguration _configuration;
        private readonly BackupSettings _backupSettings;
        private readonly ILogger<BackupManagementController> _logger;

        public BackupManagementController(
            ApplicationDbContext context,
            IBackupService backupService,
            IRestoreService restoreService,
            AuditService auditService,
            IConfiguration configuration,
            IOptions<BackupSettings> backupSettings,
            ILogger<BackupManagementController> logger)
        {
            _context = context;
            _backupService = backupService;
            _restoreService = restoreService;
            _auditService = auditService;
            _configuration = configuration;
            _backupSettings = backupSettings.Value;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────
        //  INDEX
        // ─────────────────────────────────────────────────────────────

        public async Task<IActionResult> Index(
            int tenantPage = 1, int tenantPageSize = 10, string? tenantSearch = null,
            int backupPage = 1, int backupPageSize = 10, string? backupSearch = null,
            int restorePage = 1, int restorePageSize = 10, string? restoreSearch = null)
        {
            ViewData["Title"] = "Backup & Restore Management";

            var vm = new BackupManagementIndexVm
            {
                PlatformDatabaseName = GetPlatformDatabaseName(),
                AllowTenantBackupRequest = _backupSettings.AllowTenantBackupRequest
            };

            // ── Config / folder warnings ──────────────────────────────────
            if (string.IsNullOrWhiteSpace(_backupSettings.RootPath))
            {
                vm.Warnings.Add("Backup path is not configured. Set BackupSettings:RootPath in appsettings.");
            }
            else
            {
                var rootPath = _backupSettings.RootPath.Trim();
                if (!Directory.Exists(rootPath))
                    vm.Warnings.Add($"Backup folder does not exist: {rootPath} — create it and grant SQL Server write access.");
            }

            if (string.IsNullOrWhiteSpace(_backupSettings.RestoreDataPath) ||
                string.IsNullOrWhiteSpace(_backupSettings.RestoreLogPath))
            {
                vm.Warnings.Add(
                    "RestoreDataPath / RestoreLogPath are not configured. " +
                    "RestoreService will fall back to SQL Server default data/log paths.");
            }

            // ── Tenant list — paginated + searched (resilient) ────────────
            try
            {
                tenantPageSize = PagedResult<TenantBackupStatusVm>.ValidatePageSize(tenantPageSize);

                var tenantQuery = _context.Tenants.AsNoTracking().AsQueryable();

                if (!string.IsNullOrWhiteSpace(tenantSearch))
                {
                    var s = tenantSearch.Trim();
                    tenantQuery = tenantQuery.Where(t =>
                        t.Code.Contains(s) ||
                        t.Name.Contains(s) ||
                        (t.DatabaseName != null && t.DatabaseName.Contains(s)));
                }

                tenantQuery = tenantQuery.OrderBy(t => t.Name);

                var totalTenants = await tenantQuery.CountAsync();
                tenantPage = PagedResult<TenantBackupStatusVm>.ValidatePageNumber(
                    tenantPage,
                    (int)Math.Ceiling(totalTenants / (double)tenantPageSize));

                var pagedTenants = await tenantQuery
                    .Skip((tenantPage - 1) * tenantPageSize)
                    .Take(tenantPageSize)
                    .ToListAsync();

                var tenantItems = new List<TenantBackupStatusVm>();
                foreach (var tenant in pagedTenants)
                {
                    BackupRecord? lastBackup = null;
                    try { lastBackup = await _backupService.GetLatestTenantBackupAsync(tenant.Id); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Could not load latest backup for tenant {TenantId}.", tenant.Id);
                    }

                    tenantItems.Add(new TenantBackupStatusVm
                    {
                        TenantId = tenant.Id,
                        Code = tenant.Code,
                        Name = tenant.Name,
                        IsDedicated = tenant.IsDatabaseProvisioned,
                        DatabaseName = tenant.DatabaseName,
                        LastBackup = lastBackup
                    });
                }

                vm.Tenants = new PagedResult<TenantBackupStatusVm>
                {
                    Items = tenantItems,
                    PageNumber = tenantPage,
                    PageSize = tenantPageSize,
                    TotalRecords = totalTenants,
                    SearchTerm = tenantSearch
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BackupManagement: failed to load tenant list.");
                vm.Warnings.Add(
                    "Could not load tenant list — database may have pending migrations or be unreachable. " +
                    "Run: dotnet ef database update --context ApplicationDbContext");
            }

            // ── Platform latest backup (resilient) ───────────────────────
            try
            {
                vm.LatestPlatformBackup = await _backupService.GetLatestPlatformBackupAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BackupManagement: failed to load latest platform backup.");
                vm.Warnings.Add(
                    "Could not query backup history — BackupRecords table may be missing. " +
                    "Ensure Phase52_BackupManagement migration is applied.");
            }

            // ── Backup history — paginated + searched (resilient) ─────────
            try
            {
                backupPageSize = PagedResult<BackupRecord>.ValidatePageSize(backupPageSize);

                var backupQuery = _context.BackupRecords
                    .AsNoTracking()
                    .Include(b => b.Tenant)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(backupSearch))
                {
                    var s = backupSearch.Trim();
                    var matchedBackupTypes = Enum.GetValues<BackupType>()
                        .Where(e => e.ToString().Contains(s, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var matchedBackupStatuses = Enum.GetValues<BackupStatus>()
                        .Where(e => e.ToString().Contains(s, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    backupQuery = backupQuery.Where(b =>
                        b.DatabaseName.Contains(s) ||
                        b.BackupFileName.Contains(s) ||
                        (b.Tenant != null && (b.Tenant.Code.Contains(s) || b.Tenant.Name.Contains(s))) ||
                        matchedBackupTypes.Contains(b.BackupType) ||
                        matchedBackupStatuses.Contains(b.Status));
                }

                backupQuery = backupQuery.OrderByDescending(b => b.StartedAtUtc);

                var totalBackups = await backupQuery.CountAsync();
                backupPage = PagedResult<BackupRecord>.ValidatePageNumber(
                    backupPage,
                    (int)Math.Ceiling(totalBackups / (double)backupPageSize));

                var backupItems = await backupQuery
                    .Skip((backupPage - 1) * backupPageSize)
                    .Take(backupPageSize)
                    .ToListAsync();

                vm.History = new PagedResult<BackupRecord>
                {
                    Items = backupItems,
                    PageNumber = backupPage,
                    PageSize = backupPageSize,
                    TotalRecords = totalBackups,
                    SearchTerm = backupSearch
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BackupManagement: failed to load backup history.");
                if (!vm.Warnings.Any(w => w.Contains("BackupRecords")))
                    vm.Warnings.Add(
                        "Could not load backup history — BackupRecords table may be missing.");
            }

            // ── Restore history — paginated + searched (resilient) ────────
            try
            {
                restorePageSize = PagedResult<RestoreRecord>.ValidatePageSize(restorePageSize);

                var restoreQuery = _context.RestoreRecords
                    .AsNoTracking()
                    .Include(r => r.BackupRecord)
                    .Include(r => r.Tenant)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(restoreSearch))
                {
                    var s = restoreSearch.Trim();
                    var matchedRestoreModes = Enum.GetValues<RestoreMode>()
                        .Where(e => e.ToString().Contains(s, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    var matchedRestoreStatuses = Enum.GetValues<RestoreStatus>()
                        .Where(e => e.ToString().Contains(s, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    restoreQuery = restoreQuery.Where(r =>
                        r.DatabaseName.Contains(s) ||
                        (r.RestoreTargetDatabaseName != null && r.RestoreTargetDatabaseName.Contains(s)) ||
                        (r.RequestedByUserId != null && r.RequestedByUserId.Contains(s)) ||
                        (r.Tenant != null && (r.Tenant.Code.Contains(s) || r.Tenant.Name.Contains(s))) ||
                        matchedRestoreModes.Contains(r.RestoreMode) ||
                        matchedRestoreStatuses.Contains(r.Status));
                }

                restoreQuery = restoreQuery.OrderByDescending(r => r.StartedAtUtc);

                var totalRestores = await restoreQuery.CountAsync();
                restorePage = PagedResult<RestoreRecord>.ValidatePageNumber(
                    restorePage,
                    (int)Math.Ceiling(totalRestores / (double)restorePageSize));

                var restoreItems = await restoreQuery
                    .Skip((restorePage - 1) * restorePageSize)
                    .Take(restorePageSize)
                    .ToListAsync();

                vm.RestoreHistory = new PagedResult<RestoreRecord>
                {
                    Items = restoreItems,
                    PageNumber = restorePage,
                    PageSize = restorePageSize,
                    TotalRecords = totalRestores,
                    SearchTerm = restoreSearch
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BackupManagement: failed to load restore history.");
                vm.Warnings.Add(
                    "Could not load restore history — RestoreRecords table may be missing. " +
                    "Ensure Phase521_RestoreManagement migration is applied.");
            }

            return View(vm);
        }

        // ─────────────────────────────────────────────────────────────
        //  BACKUP ACTIONS
        // ─────────────────────────────────────────────────────────────

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
                    User, "BackupManagement", "BACKUP_PLATFORM",
                    $"Platform backup {record.Status}: {record.BackupFileName}",
                    "BackupRecord", record.Id.ToString());

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
                    tenantId, BackupType.Manual,
                    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);

                await _auditService.LogAsync(
                    User, "BackupManagement", "BACKUP_TENANT",
                    $"Tenant {tenant.Code} backup {record.Status}: {record.BackupFileName}",
                    "BackupRecord", record.Id.ToString());

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
                    User, "BackupManagement", "BACKUP_ALL",
                    $"Backup All completed: {success} success, {failed} failed.",
                    "BackupRecord", null);

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
                User, "BackupManagement", "BACKUP_RETENTION_CLEANUP",
                $"Retention cleanup: {result.FilesDeleted} files deleted, {result.RecordsRemoved} records removed.",
                "BackupRecord", null);

            TempData["SuccessMessage"] =
                $"Retention cleanup completed: {result.FilesDeleted} file(s) deleted, {result.RecordsRemoved} record(s) removed.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        //  RESTORE ACTIONS  —  SuperAdmin only (enforced by class attr)
        // ─────────────────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreAsNew(int backupRecordId, string? targetDatabaseName)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Pre-validate before touching SQL
            var validation = await _restoreService.ValidateAsync(backupRecordId);
            if (!validation.IsValid)
            {
                TempData["ErrorMessage"] = $"Restore validation failed: {validation.Error}";
                return RedirectToAction(nameof(Index));
            }

            var backup = validation.BackupRecord!;
            RestoreRecord record;

            try
            {
                if (backup.DatabaseType == BackupDatabaseType.Platform)
                    record = await _restoreService.RestorePlatformAsNewAsync(
                        backupRecordId, targetDatabaseName, userId,
                        "SuperAdmin restore-as-new from UI");
                else
                    record = await _restoreService.RestoreTenantAsNewAsync(
                        backupRecordId, targetDatabaseName, userId,
                        "SuperAdmin restore-as-new from UI");

                var auditEvent = record.Status == RestoreStatus.Success
                    ? "DATABASE_RESTORE_TEST_CREATED"
                    : "DATABASE_RESTORE_FAILED";

                await _auditService.LogAsync(
                    User, "BackupManagement", auditEvent,
                    $"Restore As New: backup #{backupRecordId} ({backup.DatabaseName}) → " +
                    $"{record.RestoreTargetDatabaseName} — {record.Status}",
                    "RestoreRecord", record.Id.ToString(), ip);

                if (record.Status == RestoreStatus.Success)
                    TempData["SuccessMessage"] =
                        $"Test database '{record.RestoreTargetDatabaseName}' created successfully from backup.";
                else
                    TempData["ErrorMessage"] =
                        $"Restore As New failed: {record.ErrorMessage}";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Restore As New failed: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreOverwrite(int backupRecordId)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

            var validation = await _restoreService.ValidateAsync(backupRecordId);
            if (!validation.IsValid)
            {
                TempData["ErrorMessage"] = $"Restore validation failed: {validation.Error}";
                return RedirectToAction(nameof(Index));
            }

            var backup = validation.BackupRecord!;
            if (backup.DatabaseType == BackupDatabaseType.Platform)
            {
                TempData["ErrorMessage"] =
                    "Platform database overwrite restore must be performed manually by a DBA using the documented recovery procedure.";
                return RedirectToAction(nameof(Index));
            }

            await _auditService.LogAsync(
                User, "BackupManagement", "DATABASE_RESTORE_STARTED",
                $"Restore Overwrite started: backup #{backupRecordId} ({backup.DatabaseName}), tenant #{backup.TenantId}",
                "BackupRecord", backupRecordId.ToString(), ip);

            RestoreRecord record;
            try
            {
                record = await _restoreService.RestoreTenantOverwriteAsync(
                    backupRecordId, userId,
                    "SuperAdmin overwrite restore from UI");

                var auditEvent = record.Status == RestoreStatus.Success
                    ? "DATABASE_RESTORE_COMPLETED"
                    : "DATABASE_RESTORE_FAILED";

                await _auditService.LogAsync(
                    User, "BackupManagement", auditEvent,
                    $"Restore Overwrite: backup #{backupRecordId} ({backup.DatabaseName}) → " +
                    $"{record.RestoreTargetDatabaseName} — {record.Status}",
                    "RestoreRecord", record.Id.ToString(), ip);

                if (record.Status == RestoreStatus.Success)
                    TempData["SuccessMessage"] =
                        $"Tenant database '{record.RestoreTargetDatabaseName}' has been restored from backup.";
                else
                    TempData["ErrorMessage"] =
                        $"Restore Overwrite failed: {record.ErrorMessage}";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Restore Overwrite failed: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────────────────────

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
