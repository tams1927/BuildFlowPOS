using HardwareManagementSystem.Configuration;
using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services.Backups;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HardwareManagementSystem.Tools
{
    /// <summary>Phase 5.2 backup management QA (Development only, --qa-backup).</summary>
    public static class BackupQaRunner
    {
        public static async Task RunAsync(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[BACKUP] Development-only. Aborting.");
                return;
            }

            var pass = 0;
            var fail = 0;
            void Ok(string m) { pass++; Console.WriteLine($"[BACKUP] PASS — {m}"); }
            void No(string m) { fail++; Console.WriteLine($"[BACKUP] FAIL — {m}"); }

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var appDb = sp.GetRequiredService<ApplicationDbContext>();
            var provision = sp.GetRequiredService<ITenantDatabaseProvisioningService>();
            var configuration = sp.GetRequiredService<IConfiguration>();

            var testRoot = await ResolveQaBackupRootAsync(appDb, configuration) ?? Path.Combine(Path.GetTempPath(), $"hb_backup_qa_{stamp}");
            Directory.CreateDirectory(testRoot);

            var pending = await appDb.Database.GetPendingMigrationsAsync();
            if (!pending.Any(m => m.Contains("Phase52", StringComparison.OrdinalIgnoreCase)))
            {
                if (pending.Any())
                    No($"Pending migrations before backup QA: {string.Join(", ", pending)}");
                else
                    Ok("No pending migrations blocking backup QA");
            }
            else
                No($"Phase52 migration pending: {string.Join(", ", pending)}");

            var settings = Options.Create(new BackupSettings
            {
                RootPath = testRoot,
                RetentionDays = 14,
                EnableScheduledBackups = false,
                AllowTenantBackupRequest = true
            });

            var backupService = new BackupService(
                appDb,
                configuration,
                settings,
                NullLogger<BackupService>.Instance);

            Ok($"Test backup folder created: {testRoot}");

            // Platform backup
            BackupRecord platformRecord;
            try
            {
                platformRecord = await backupService.BackupPlatformDatabaseAsync(BackupType.Manual, notes: "QA platform backup");
                if (platformRecord.Status == BackupStatus.Success && File.Exists(platformRecord.BackupPath))
                    Ok("Platform database backup succeeded and .bak file exists");
                else
                    No($"Platform backup failed: {platformRecord.ErrorMessage ?? platformRecord.Status.ToString()}");
            }
            catch (Exception ex)
            {
                No($"Platform backup threw: {ex.Message}");
                platformRecord = new BackupRecord { Status = BackupStatus.Failed };
            }

            // Dedicated tenant backup
            int dedicatedTenantId = 0;
            var existingDedicated = await appDb.Tenants.AsNoTracking()
                .Where(t => t.DatabaseProvisionedAtUtc != null && t.ConnectionString != null)
                .OrderByDescending(t => t.Id)
                .FirstOrDefaultAsync();

            if (existingDedicated != null)
            {
                dedicatedTenantId = existingDedicated.Id;
                Ok($"Using existing dedicated tenant {existingDedicated.Code} (Id={dedicatedTenantId})");
            }
            else
            {
                var dbName = $"QA_BackupDb_{stamp}";
                var tenant = new Tenant
                {
                    Name = $"QA_Backup_{stamp}",
                    Code = $"QB{stamp}",
                    Email = $"qa_backup_{stamp}@hardbuild.local",
                    Status = TenantStatus.Trial,
                    DatabaseMode = TenantDatabaseMode.Dedicated,
                    DatabaseName = dbName,
                    MaxBranches = 2,
                    MaxUsers = 5,
                    MaxProducts = 100,
                    CreatedAtUtc = DateTime.UtcNow
                };
                appDb.Tenants.Add(tenant);
                await appDb.SaveChangesAsync();
                dedicatedTenantId = tenant.Id;

                var provisionResult = await provision.ProvisionAsync(dedicatedTenantId);
                if (provisionResult.Success)
                    Ok($"Provisioned ephemeral dedicated tenant {tenant.Code} for backup QA");
                else
                    No($"Could not provision ephemeral tenant: {provisionResult.Message}");
            }

            if (dedicatedTenantId > 0)
            {
                try
                {
                    var tenantRecord = await backupService.BackupTenantDatabaseAsync(
                        dedicatedTenantId,
                        BackupType.Manual,
                        notes: "QA tenant backup");

                    if (tenantRecord.Status == BackupStatus.Success && File.Exists(tenantRecord.BackupPath))
                        Ok("Dedicated tenant database backup succeeded and .bak file exists");
                    else
                        No($"Tenant backup failed: {tenantRecord.ErrorMessage ?? tenantRecord.Status.ToString()}");

                    var saved = await appDb.BackupRecords.AsNoTracking()
                        .AnyAsync(b => b.Id == tenantRecord.Id && b.TenantId == dedicatedTenantId);
                    if (saved)
                        Ok("BackupRecord saved for tenant backup");
                    else
                        No("BackupRecord not found after tenant backup");
                }
                catch (Exception ex)
                {
                    No($"Tenant backup threw: {ex.Message}");
                }
            }

            var history = await backupService.GetBackupHistoryAsync(20);
            if (history.Any(b => b.Status == BackupStatus.Success))
                Ok("SuperAdmin history query returns backup records");
            else
                No("SuperAdmin history query empty or no successful backups");

            if (dedicatedTenantId > 0)
            {
                var latestTenant = await backupService.GetLatestTenantBackupAsync(dedicatedTenantId);
                if (latestTenant != null)
                    Ok("Tenant backup status query returns latest tenant backup");
                else
                    No("GetLatestTenantBackupAsync returned null");
            }

            var records = await appDb.BackupRecords.AsNoTracking().ToListAsync();
            var exposed = records.Any(b =>
                (b.Notes?.Contains("Password=", StringComparison.OrdinalIgnoreCase) == true)
                || (b.ErrorMessage?.Contains("Password=", StringComparison.OrdinalIgnoreCase) == true));
            if (!exposed)
                Ok("No raw connection string markers in BackupRecord fields");
            else
                No("BackupRecord may expose connection string data");

            // Cleanup safety — file outside root must survive
            var outsideFile = Path.Combine(Path.GetTempPath(), $"hb_backup_outside_{stamp}.bak");
            await File.WriteAllTextAsync(outsideFile, "qa-outside-root");

            var rogueRecord = new BackupRecord
            {
                DatabaseName = "OutsideTest",
                DatabaseType = BackupDatabaseType.Platform,
                BackupFileName = Path.GetFileName(outsideFile),
                BackupPath = outsideFile,
                Status = BackupStatus.Success,
                StartedAtUtc = DateTime.UtcNow.AddDays(-30),
                CompletedAtUtc = DateTime.UtcNow.AddDays(-30),
                BackupType = BackupType.Manual,
                BackupSizeBytes = 16
            };
            appDb.BackupRecords.Add(rogueRecord);
            await appDb.SaveChangesAsync();

            var cleanup = await backupService.CleanupExpiredBackupsAsync();
            if (File.Exists(outsideFile))
                Ok("Retention cleanup did not delete file outside backup root");
            else
                No("Retention cleanup deleted file outside backup root");

            try { File.Delete(outsideFile); } catch { }

            Console.WriteLine($"[BACKUP] Summary: {pass} passed, {fail} failed.");
            if (fail > 0)
                Environment.ExitCode = 1;

            try
            {
                if (Directory.Exists(testRoot))
                    Directory.Delete(testRoot, recursive: true);
            }
            catch
            {
                Console.WriteLine($"[BACKUP] Note: manual cleanup of {testRoot} may be required.");
            }
        }

        private static async Task<string?> ResolveQaBackupRootAsync(ApplicationDbContext appDb, IConfiguration configuration)
        {
            var configured = configuration.GetValue<string>("BackupSettings:RootPath");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                var path = Path.GetFullPath(configured.Trim());
                Directory.CreateDirectory(path);
                return path;
            }

            try
            {
                var connectionString = appDb.Database.GetConnectionString();
                if (string.IsNullOrWhiteSpace(connectionString))
                    return null;

                await using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(
                    "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512))",
                    conn);
                var dataPath = (await cmd.ExecuteScalarAsync()) as string;
                if (string.IsNullOrWhiteSpace(dataPath))
                    return null;

                var qaRoot = Path.Combine(dataPath.TrimEnd('\\', '/'), "HardBuildBackups_QA");
                Directory.CreateDirectory(qaRoot);
                return qaRoot;
            }
            catch
            {
                return null;
            }
        }
    }
}
