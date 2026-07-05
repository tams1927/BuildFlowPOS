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
    /// <summary>
    /// Phase 5.2.1 — Restore Management QA runner (Development only, --qa-restore).
    /// Tests RestoreAsNew and validates safety guards.
    /// Does NOT perform destructive overwrite unless the environment is Development
    /// AND an explicit QA tenant is being used.
    /// </summary>
    public static class RestoreQaRunner
    {
        public static async Task RunAsync(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[RESTORE] Development-only. Aborting.");
                return;
            }

            var pass = 0;
            var fail = 0;
            void Ok(string m) { pass++; Console.WriteLine($"[RESTORE] PASS — {m}"); }
            void No(string m) { fail++; Console.WriteLine($"[RESTORE] FAIL — {m}"); }

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var appDb = sp.GetRequiredService<ApplicationDbContext>();
            var configuration = sp.GetRequiredService<IConfiguration>();
            var provision = sp.GetRequiredService<ITenantDatabaseProvisioningService>();
            var resolver = sp.GetRequiredService<ITenantDatabaseResolver>();

            // ── 1. Check pending migrations ─────────────────────────
            var pending = await appDb.Database.GetPendingMigrationsAsync();
            if (pending.Any(m => m.Contains("Phase521", StringComparison.OrdinalIgnoreCase)))
                No($"Phase521 migration pending — run 'dotnet ef database update' first. Pending: {string.Join(", ", pending)}");
            else
                Ok("No Phase521 migration pending");

            // ── 2. Build services with a fresh testRoot ──────────────
            var testRoot = await ResolveQaBackupRootAsync(appDb, configuration)
                ?? Path.Combine(Path.GetTempPath(), $"hb_restore_qa_{stamp}");
            Directory.CreateDirectory(testRoot);
            Ok($"QA backup root: {testRoot}");

            var settings = Options.Create(new BackupSettings
            {
                RootPath = testRoot,
                RetentionDays = 14,
                EnableScheduledBackups = false
            });

            var backupService = new BackupService(
                appDb, configuration, settings,
                NullLogger<BackupService>.Instance);

            var restoreService = new RestoreService(
                appDb, configuration, settings,
                resolver, NullLogger<RestoreService>.Instance);

            // ── 3. Find or create a dedicated tenant ────────────────
            int dedicatedTenantId = 0;
            string? dedicatedTenantCode = null;

            var existing = await appDb.Tenants.AsNoTracking()
                .Where(t => t.DatabaseProvisionedAtUtc != null && t.ConnectionString != null)
                .OrderByDescending(t => t.Id)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                dedicatedTenantId = existing.Id;
                dedicatedTenantCode = existing.Code;
                Ok($"Using existing dedicated tenant {existing.Code} (Id={dedicatedTenantId})");
            }
            else
            {
                var dbName = $"QA_RestoreDb_{stamp}";
                var tenant = new Tenant
                {
                    Name = $"QA_Restore_{stamp}",
                    Code = $"QR{stamp[^6..]}",
                    Email = $"qa_restore_{stamp}@hardbuild.local",
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
                dedicatedTenantCode = tenant.Code;

                var provResult = await provision.ProvisionAsync(dedicatedTenantId);
                if (provResult.Success)
                    Ok($"Provisioned ephemeral dedicated tenant {tenant.Code} for restore QA");
                else
                    No($"Could not provision tenant: {provResult.Message}");
            }

            if (dedicatedTenantId == 0)
            {
                No("No dedicated tenant available — aborting restore tests.");
                goto Summary;
            }

            // ── 4. Create a tenant backup ────────────────────────────
            BackupRecord? tenantBackup = null;
            try
            {
                tenantBackup = await backupService.BackupTenantDatabaseAsync(
                    dedicatedTenantId, BackupType.Manual,
                    notes: "QA restore test backup");

                if (tenantBackup.Status == BackupStatus.Success && File.Exists(tenantBackup.BackupPath))
                    Ok($"Tenant backup created: {tenantBackup.BackupFileName}");
                else
                    No($"Tenant backup failed: {tenantBackup.ErrorMessage}");
            }
            catch (Exception ex)
            {
                No($"Tenant backup threw: {ex.Message}");
            }

            if (tenantBackup == null || tenantBackup.Status != BackupStatus.Success)
            {
                No("Cannot proceed with restore tests — no successful tenant backup.");
                goto Summary;
            }

            // ── 5. Validate backup eligibility ───────────────────────
            var validation = await restoreService.ValidateAsync(tenantBackup.Id);
            if (validation.IsValid)
                Ok($"ValidateAsync returned IsValid=true for backup #{tenantBackup.Id}");
            else
                No($"ValidateAsync rejected valid backup: {validation.Error}");

            // ── 6. Reject invalid backup id ──────────────────────────
            var badValidation = await restoreService.ValidateAsync(-999);
            if (!badValidation.IsValid)
                Ok("ValidateAsync correctly rejected non-existent backup id");
            else
                No("ValidateAsync accepted non-existent backup id");

            // ── 7. Reject backup with path outside root ──────────────
            var rogueRecord = new BackupRecord
            {
                TenantId = dedicatedTenantId,
                DatabaseName = "RogueTest",
                DatabaseType = BackupDatabaseType.Tenant,
                BackupFileName = "rogue.bak",
                BackupPath = Path.Combine(Path.GetTempPath(), $"rogue_{stamp}.bak"),
                Status = BackupStatus.Success,
                StartedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow,
                BackupType = BackupType.Manual,
                BackupSizeBytes = 100
            };
            // Write a fake file outside root so "file exists" check passes
            await File.WriteAllTextAsync(rogueRecord.BackupPath, "rogue");
            appDb.BackupRecords.Add(rogueRecord);
            await appDb.SaveChangesAsync();

            var rogueValidation = await restoreService.ValidateAsync(rogueRecord.Id);
            if (!rogueValidation.IsValid)
                Ok("ValidateAsync correctly rejected backup path outside root");
            else
                No("ValidateAsync accepted backup path outside root — path traversal guard FAILED");

            try { File.Delete(rogueRecord.BackupPath); } catch { }

            // ── 8. RestoreAsNew ──────────────────────────────────────
            var testTargetName = $"QA_RestoreTest_{stamp}";
            RestoreRecord? restoreRecord = null;
            try
            {
                restoreRecord = await restoreService.RestoreTenantAsNewAsync(
                    tenantBackup.Id,
                    testTargetName,
                    requestedByUserId: "qa-runner",
                    notes: "QA RestoreAsNew test");

                if (restoreRecord.Status == RestoreStatus.Success)
                    Ok($"RestoreAsNew succeeded — target: {restoreRecord.RestoreTargetDatabaseName}");
                else
                    No($"RestoreAsNew failed: {restoreRecord.ErrorMessage}");
            }
            catch (Exception ex)
            {
                No($"RestoreAsNew threw: {ex.Message}");
            }

            // ── 9. Verify RestoreRecord saved ───────────────────────
            if (restoreRecord != null)
            {
                var saved = await appDb.RestoreRecords.AsNoTracking()
                    .AnyAsync(r => r.Id == restoreRecord.Id);
                if (saved)
                    Ok("RestoreRecord saved in ApplicationDbContext");
                else
                    No("RestoreRecord NOT found in ApplicationDbContext after restore");
            }

            // ── 10. Verify restored DB exists and has tables ─────────
            if (restoreRecord?.Status == RestoreStatus.Success)
            {
                try
                {
                    var tenantDb = await appDb.Tenants.AsNoTracking()
                        .FirstOrDefaultAsync(t => t.Id == dedicatedTenantId);

                    if (tenantDb?.ConnectionString != null)
                    {
                        var csb = new SqlConnectionStringBuilder(tenantDb.ConnectionString)
                        {
                            InitialCatalog = "master"
                        };
                        await using var conn = new SqlConnection(csb.ConnectionString);
                        await conn.OpenAsync();

                        var escaped = testTargetName.Replace("'", "''");
                        await using var existCmd = new SqlCommand(
                            $"SELECT COUNT(*) FROM sys.databases WHERE name = N'{escaped}'", conn);
                        var dbCount = (int)(await existCmd.ExecuteScalarAsync())!;

                        if (dbCount > 0)
                            Ok($"Restored database '{testTargetName}' exists in SQL Server");
                        else
                            No($"Restored database '{testTargetName}' NOT found in SQL Server");

                        // Check basic tables
                        var restoredCs = new SqlConnectionStringBuilder(tenantDb.ConnectionString)
                        {
                            InitialCatalog = testTargetName
                        };
                        await using var restoredConn = new SqlConnection(restoredCs.ConnectionString);
                        await restoredConn.OpenAsync();
                        await using var tableCmd = new SqlCommand(
                            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE'",
                            restoredConn);
                        var tableCount = (int)(await tableCmd.ExecuteScalarAsync())!;
                        if (tableCount > 5)
                            Ok($"Restored database has {tableCount} tables — schema intact");
                        else
                            No($"Restored database has only {tableCount} tables — schema may be incomplete");
                    }
                    else
                    {
                        No("Cannot verify restored DB — tenant has no ConnectionString");
                    }
                }
                catch (Exception ex)
                {
                    No($"Restored DB verification threw: {ex.Message}");
                }
            }

            // ── 11. Verify live tenant routing unchanged ─────────────
            {
                var tenantAfter = await appDb.Tenants.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == dedicatedTenantId);
                var tenantBefore = existing ?? tenantAfter;
                if (tenantAfter?.DatabaseName == tenantBefore?.DatabaseName)
                    Ok("Live tenant DatabaseName unchanged after RestoreAsNew");
                else
                    No("Live tenant DatabaseName changed after RestoreAsNew — routing safety FAILED");
            }

            // ── 12. GetHistoryAsync returns records ──────────────────
            var history = await restoreService.GetHistoryAsync(20);
            if (history.Any(r => r.Status == RestoreStatus.Success || r.Status == RestoreStatus.Failed))
                Ok("RestoreService.GetHistoryAsync returns restore history records");
            else
                No("RestoreService.GetHistoryAsync returned no records");

            // ── 13. Cleanup restored test DB ─────────────────────────
            if (restoreRecord?.Status == RestoreStatus.Success)
            {
                try
                {
                    var tenantDb = await appDb.Tenants.AsNoTracking()
                        .FirstOrDefaultAsync(t => t.Id == dedicatedTenantId);

                    if (tenantDb?.ConnectionString != null)
                    {
                        var csb = new SqlConnectionStringBuilder(tenantDb.ConnectionString)
                        {
                            InitialCatalog = "master"
                        };
                        await using var conn = new SqlConnection(csb.ConnectionString);
                        await conn.OpenAsync();
                        var escaped = testTargetName.Replace("]", "]]");
                        await using var dropCmd = new SqlCommand(
                            $"IF DB_ID(N'{testTargetName.Replace("'", "''")}') IS NOT NULL " +
                            $"BEGIN ALTER DATABASE [{escaped}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                            $"DROP DATABASE [{escaped}]; END",
                            conn) { CommandTimeout = 60 };
                        await dropCmd.ExecuteNonQueryAsync();
                        Ok($"Restored test database '{testTargetName}' cleaned up");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[RESTORE] Note: manual cleanup of '{testTargetName}' may be required. ({ex.Message})");
                }
            }

        Summary:
            Console.WriteLine($"[RESTORE] Summary: {pass} passed, {fail} failed.");
            if (fail > 0)
                Environment.ExitCode = 1;

            // Cleanup test backup root
            try
            {
                if (Directory.Exists(testRoot))
                    Directory.Delete(testRoot, recursive: true);
            }
            catch
            {
                Console.WriteLine($"[RESTORE] Note: manual cleanup of {testRoot} may be required.");
            }
        }

        private static async Task<string?> ResolveQaBackupRootAsync(
            ApplicationDbContext appDb, IConfiguration configuration)
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
                    "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(512))", conn);
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
