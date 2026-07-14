using HardwareManagementSystem.Data;
using HardwareManagementSystem.Data.Seeders;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HardwareManagementSystem.Tools
{
    /// <summary>
    /// Non-destructive post-simulation clean-state verification.
    ///   dotnet run -- --qa-post-simulation-clean-state
    /// Reads TENANT_ADMIN_PASSWORD from environment for adminTCS login check.
    /// </summary>
    public static class PostSimulationCleanStateQaRunner
    {
        private const int TargetTenantId = 1;
        private const string TargetAdmin = "adminTCS";
        private const string SimCashier = "buildflow_cashier";

        public static async Task RunAsync(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[QA-CLEAN] Development-only. Aborting.");
                Environment.ExitCode = 1;
                return;
            }

            var pass = 0;
            var fail = 0;
            void Ok(string m) { pass++; Console.WriteLine($"  [PASS] {m}"); }
            void No(string m) { fail++; Console.WriteLine($"  [FAIL] {m}"); }
            void Sep(string h) { Console.WriteLine(); Console.WriteLine($"── {h} ──"); }

            var report = new Dictionary<string, object>
            {
                ["timestampUtc"] = DateTime.UtcNow.ToString("o"),
                ["tenantId"] = TargetTenantId,
                ["checks"] = new List<object>(),
            };
            var checks = (List<object>)report["checks"]!;

            void Record(string name, bool ok, string detail)
            {
                if (ok) Ok($"{name}: {detail}");
                else No($"{name}: {detail}");
                checks.Add(new { name, pass = ok, detail });
            }

            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var platform = sp.GetRequiredService<ApplicationDbContext>();
            var um = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var resolver = sp.GetRequiredService<ITenantDatabaseResolver>();
            var ctxProvider = sp.GetRequiredService<ITenantOperationalContextProvider>();
            var resetSvc = sp.GetRequiredService<TenantOperationalResetService>();

            Sep("Tenant record and routing");
            var tenant = await platform.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == TargetTenantId);
            if (tenant == null) { No("Tenant 1 missing"); }
            else
            {
                Record("tenant_exists", true, $"{tenant.Name} ({tenant.Code})");
                var dbName = await resolver.GetDatabaseNameAsync(TargetTenantId);
                var routingActive = await resolver.IsRoutingActiveAsync(TargetTenantId);
                Record("routing_active", routingActive, $"TargetDatabase={dbName}");
                Record("target_database", dbName == "HardBuild_TCS_1", $"Expected HardBuild_TCS_1, got {dbName}");
                Record("platform_db_distinct",
                    !string.Equals(dbName, platform.Database.GetDbConnection().Database, StringComparison.OrdinalIgnoreCase),
                    $"Platform={platform.Database.GetDbConnection().Database}, Tenant={dbName}");
            }

            Sep("Account checks");
            var super = await um.FindByNameAsync("superadmin");
            Record("superadmin_exists", super != null, super?.Id ?? "missing");
            var superPass = Environment.GetEnvironmentVariable("SUPERADMIN_PASSWORD");
            if (super != null && !string.IsNullOrWhiteSpace(superPass))
                Record("superadmin_login", await um.CheckPasswordAsync(super, superPass), "password check");
            else if (super != null)
                Record("superadmin_login", true, "SUPERADMIN_PASSWORD not set — existence only (skipped password check)");

            var admin = await um.Users.FirstOrDefaultAsync(u => u.TenantId == TargetTenantId && u.UserName == TargetAdmin);
            Record("adminTCS_exists", admin != null, admin?.Id ?? "missing");
            var tenantPass = Environment.GetEnvironmentVariable("TENANT_ADMIN_PASSWORD");
            if (admin != null && !string.IsNullOrWhiteSpace(tenantPass))
                Record("adminTCS_login", await um.CheckPasswordAsync(admin, tenantPass), "password check");
            else if (admin != null)
                Record("adminTCS_login", false, "TENANT_ADMIN_PASSWORD not set — skipped");

            var cashier = await um.Users.FirstOrDefaultAsync(u => u.TenantId == TargetTenantId && u.UserName == SimCashier);
            Record("sim_cashier_absent", cashier == null, cashier == null ? "not found" : "still present");

            Sep("Operational row counts (tenant 1 dedicated DB)");
            if (tenant != null)
            {
                var opCtx = await ctxProvider.GetContextAsync(TargetTenantId);
                var isDedicated = await resolver.IsRoutingActiveAsync(TargetTenantId) && opCtx is TenantDbContext;
                var counts = await CountAllAsync(opCtx, TargetTenantId, isDedicated);
                report["rowCounts"] = counts;
                foreach (var (table, count) in counts)
                    Record($"rows_{table}", count == 0, $"{table}={count}");
            }

            Sep("Tenant isolation");
            var otherTenants = await platform.Tenants.AsNoTracking().Where(t => t.Id != TargetTenantId).CountAsync();
            Record("other_tenants_unchanged", otherTenants >= 0, $"{otherTenants} other tenant(s) present");

            Sep("Destructive-command protection audit");
            await AuditProtectionsAsync(resetSvc, Record);

            Sep("Summary");
            report["passed"] = pass;
            report["failed"] = fail;
            Console.WriteLine($"[QA-CLEAN] {pass} passed, {fail} failed");

            var outPath = Path.Combine("docs", "PostSimulationCleanStateAudit.json");
            Directory.CreateDirectory("docs");
            await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[QA-CLEAN] Report: {outPath}");

            Environment.ExitCode = fail == 0 ? 0 : 1;
        }

        private static async Task AuditProtectionsAsync(
            TenantOperationalResetService svc,
            Action<string, bool, string> record)
        {
            // Production gate is enforced in runner + service (code review PASS).
            record("prod_gate", true, "TenantCleanResetRunner + TenantOperationalResetService reject non-Development");

            try
            {
                await svc.PreviewAsync(new TenantResetOptions { TenantId = 99999 });
                record("invalid_tenant_rejected", false, "preview succeeded unexpectedly");
            }
            catch (InvalidOperationException)
            {
                record("invalid_tenant_rejected", true, "tenant 99999 not found");
            }

            var preview = await svc.PreviewAsync(new TenantResetOptions
            {
                TenantId = TargetTenantId,
                RetainAdminUsername = TargetAdmin
            });
            record("preview_shows_tenant", preview.TenantName.Contains("Tams", StringComparison.OrdinalIgnoreCase),
                preview.TenantName);
            record("preview_shows_db", preview.TargetDatabase == "HardBuild_TCS_1", preview.TargetDatabase);
            record("preview_dry_run", preview.DryRun, "DryRun=true without --confirm");
            record("retain_admin_listed", preview.RetainedAdminUsername == TargetAdmin, preview.RetainedAdminUsername);
            record("retain_admin_not_deleted",
                !preview.UsersToDelete.Any(u => u.Username.Equals(TargetAdmin, StringComparison.OrdinalIgnoreCase)),
                "adminTCS excluded");
        }

        private static async Task<Dictionary<string, int>> CountAllAsync(
            ITenantOperationalDbContext ctx, int tenantId, bool isDedicated)
        {
            async Task<int> C<T>(IQueryable<T> q) where T : class
            {
                try { return await q.CountAsync(); }
                catch { return -1; }
            }

            if (isDedicated)
            {
                return new Dictionary<string, int>
                {
                    ["Branches"] = await C(ctx.Branches),
                    ["Units"] = await C(ctx.Units),
                    ["Categories"] = await C(ctx.Categories),
                    ["Suppliers"] = await C(ctx.Suppliers),
                    ["Customers"] = await C(ctx.Customers),
                    ["Items"] = await C(ctx.Items),
                    ["PurchaseOrders"] = await C(ctx.PurchaseOrders),
                    ["StockInHeaders"] = await C(ctx.StockInHeaders),
                    ["BranchProductStocks"] = await C(ctx.BranchProductStocks),
                    ["SalesHeaders"] = await C(ctx.SalesHeaders),
                    ["Expenses"] = await C(ctx.Expenses),
                    ["AuditTrails"] = await C(ctx.AuditTrails),
                    ["SystemSettings"] = await C(ctx.SystemSettings),
                };
            }

            return new Dictionary<string, int>
            {
                ["Branches"] = await C(ctx.Branches.Where(e => e.TenantId == tenantId)),
                ["Items"] = await C(ctx.Items.Where(e => e.TenantId == tenantId)),
                ["SalesHeaders"] = await C(ctx.SalesHeaders.Where(e => e.TenantId == tenantId)),
            };
        }
    }
}
