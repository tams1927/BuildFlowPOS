using HardwareManagementSystem.Data;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Tools
{
    /// <summary>
    /// Phase 5.3.2 — Tenant Schema Validation &amp; Upgrade QA Runner (--qa-tenant-schema).
    ///
    /// What it does:
    ///   1. Enumerates all provisioned dedicated tenant databases.
    ///   2. Checks pending EF Core migrations per tenant.
    ///   3. Applies any pending migrations (UpgradeSchemaAsync).
    ///   4. Verifies critical Phase 5.3 columns exist in each tenant database.
    ///   5. Prints a per-tenant schema verification report and overall summary.
    ///
    /// Run with:  dotnet run -- --qa-tenant-schema
    /// </summary>
    public static class TenantSchemaQaRunner
    {
        // ── Critical columns expected in every tenant database after Phase 5.3 ──
        private static readonly (string Table, string Column)[] RequiredColumns =
        [
            ("Items",                 "DamagedStock"),
            ("BranchProductStocks",   "DamagedStock"),
            ("DamagedGoodsHeaders",   "Id"),
            ("DamagedGoodsDetails",   "Id"),
            ("SupplierReturnHeaders", "Id"),
            ("SupplierReturnDetails", "Id"),
        ];

        // ── Phase 5.1 columns ──────────────────────────────────────────────────
        private static readonly (string Table, string Column)[] Phase51Columns =
        [
            ("ItemUnitConversions", "Id"),
            ("Items",              "ReorderLevel"),
        ];

        public static async Task RunAsync(WebApplication app)
        {
            Console.WriteLine();
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  Phase 5.3.2 — Tenant Schema Validation & Upgrade Runner     ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            var pass = 0;
            var fail = 0;

            void Ok(string msg) { pass++; Console.WriteLine($"  ✓  {msg}"); }
            void No(string msg) { fail++; Console.WriteLine($"  ✗  {msg}"); }
            void Info(string msg) { Console.WriteLine($"  ·  {msg}"); }
            void Head(string msg) { Console.WriteLine(); Console.WriteLine($"── {msg}"); }

            await using var scope = app.Services.CreateAsyncScope();
            var sp    = scope.ServiceProvider;
            var appDb = sp.GetRequiredService<ApplicationDbContext>();
            var ctxFac = sp.GetRequiredService<ITenantDbContextFactory>();
            var schemaSvc = sp.GetRequiredService<ITenantSchemaMigrationService>();

            // ── 1. Load all provisioned tenants ─────────────────────────────
            var tenants = await appDb.Tenants
                .AsNoTracking()
                .Where(t => t.DatabaseProvisionedAtUtc != null &&
                            t.ConnectionString != null &&
                            t.ConnectionString != string.Empty)
                .OrderBy(t => t.Name)
                .ToListAsync();

            Head($"Found {tenants.Count} provisioned dedicated tenant database(s).");

            if (tenants.Count == 0)
            {
                Console.WriteLine("  No provisioned tenant databases found. Nothing to validate.");
                Console.WriteLine();
                Console.WriteLine("  DONE — no tenants to upgrade.");
                return;
            }

            // ── 2. Upgrade all schemas ────────────────────────────────────────
            Head("STEP 1 — Apply pending EF Core migrations to all tenant databases");
            var upgradeResults = await schemaSvc.UpgradeAllSchemasAsync();

            foreach (var r in upgradeResults)
            {
                if (!r.Success)
                {
                    No($"[{r.TenantCode}] ({r.DatabaseName}) — UPGRADE FAILED: {r.Error}");
                }
                else if (r.WasUpToDate)
                {
                    Ok($"[{r.TenantCode}] ({r.DatabaseName}) — already up-to-date. Latest: {r.LatestMigration ?? "—"}");
                }
                else
                {
                    Ok($"[{r.TenantCode}] ({r.DatabaseName}) — applied {r.MigrationsApplied.Count} migration(s): " +
                       string.Join(", ", r.MigrationsApplied));
                }
            }

            // ── 3. Verify critical columns per tenant ─────────────────────────
            Head("STEP 2 — Verify critical Phase 5.3 columns in each tenant database");

            foreach (var tenant in tenants)
            {
                Console.WriteLine();
                Console.WriteLine($"  Tenant: [{tenant.Code}] {tenant.Name} → {tenant.DatabaseName}");

                try
                {
                    // Check Phase 5.1 columns
                    foreach (var (table, column) in Phase51Columns)
                    {
                        var exists = await ColumnExistsAsync(tenant.ConnectionString!, table, column);
                        if (exists) Ok($"  Phase51  {table}.{column}");
                        else        No($"  Phase51  {table}.{column} — MISSING");
                    }

                    // Check Phase 5.3 columns (DamagedStock + new tables)
                    foreach (var (table, column) in RequiredColumns)
                    {
                        var exists = await ColumnExistsAsync(tenant.ConnectionString!, table, column);
                        if (exists) Ok($"  Phase53  {table}.{column}");
                        else        No($"  Phase53  {table}.{column} — MISSING");
                    }

                    // Check __EFMigrationsHistory
                    var migrations = await GetAppliedMigrationsAsync(tenant.ConnectionString!);
                    Info($"  Applied migrations ({migrations.Count}): " +
                         (migrations.Count > 0 ? string.Join(", ", migrations) : "(none)"));
                }
                catch (Exception ex)
                {
                    No($"  [{tenant.Code}] Could not connect to dedicated database: {ex.Message}");
                }
            }

            // ── 4. Summary ────────────────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine($"  SCHEMA VALIDATION REPORT");
            Console.WriteLine($"  Tenants checked:  {tenants.Count}");
            Console.WriteLine($"  Checks passed:    {pass}");
            Console.WriteLine($"  Checks failed:    {fail}");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");

            if (fail == 0)
            {
                Console.WriteLine();
                Console.WriteLine("  ALL TENANT SCHEMAS ARE VALID — Phase 5.3 columns present.");
                Console.WriteLine("  'DamagedStock' column exists on all tenant Items and BranchProductStocks tables.");
                Console.WriteLine("  DamagedGoodsHeaders/Details and SupplierReturnHeaders/Details tables exist.");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine($"  WARNING: {fail} check(s) failed.");
                Console.WriteLine("  Re-run this runner after investigating connection/migration issues.");
            }

            Console.WriteLine();
            Environment.Exit(fail > 0 ? 1 : 0);
        }

        private static async Task<bool> ColumnExistsAsync(string connectionString, string tableName, string columnName)
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(1)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME  = @table
                  AND COLUMN_NAME = @col";
            cmd.Parameters.AddWithValue("@table", tableName);
            cmd.Parameters.AddWithValue("@col",   columnName);

            var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);
            return count > 0;
        }

        private static async Task<List<string>> GetAppliedMigrationsAsync(string connectionString)
        {
            var result = new List<string>();

            try
            {
                await using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    result.Add(reader.GetString(0));
            }
            catch
            {
                // Table may not exist on very old databases
            }

            return result;
        }
    }
}
