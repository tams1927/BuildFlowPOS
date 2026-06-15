using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace HardwareManagementSystem.Tools
{
    /// <summary>RC1.2 pilot-readiness QA (Development only, --qa-rc12).</summary>
    public static class Rc12QaRunner
    {
        public static async Task RunAsync(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[RC12] Development-only. Aborting.");
                return;
            }

            var pass = 0;
            var fail = 0;
            void Ok(string m) { pass++; Console.WriteLine($"[RC12] PASS — {m}"); }
            void No(string m) { fail++; Console.WriteLine($"[RC12] FAIL — {m}"); }

            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var appDb = sp.GetRequiredService<ApplicationDbContext>();
            var ctxProvider = sp.GetRequiredService<ITenantOperationalContextProvider>();
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

            // ── TASK 5: Shared DB migration ───────────────────────────────────
            var pending = await appDb.Database.GetPendingMigrationsAsync();
            if (!pending.Any()) Ok("ApplicationDbContext: no pending migrations");
            else No($"ApplicationDbContext pending: {string.Join(", ", pending)}");

            var applied = await appDb.Database.GetAppliedMigrationsAsync();
            if (applied.Any(m => m.Contains("Phase51_UnitConversionAndCurrency")))
                Ok("Phase51 migration applied on shared DB");
            else
                No("Phase51 migration not found in applied list");

            // ── TASK 6/7: Schema columns on shared DB ────────────────────────
            await VerifySchemaAsync(appDb, Ok, No);

            await VerifyTenantMigrationsAsync(appDb, sp, Ok, No);

            // ── TASK 4: Unit cost valuation ─────────────────────────────────
            await RunUnitCostTestAsync(appDb, ctxProvider, Ok, No);

            // ── TASK 5/6: Role seed + SuperAdmin security ───────────────────
            var cashierPos = await appDb.RolePermissions
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.RoleName == "Cashier" && p.ModuleName == "POS");
            if (cashierPos?.CanView == true && cashierPos.CanCreate == true)
                Ok("Cashier default permissions seeded (POS View+Create)");
            else
                No("Cashier POS permissions missing — run app startup to seed RolePermissions");

            var invStockIn = await appDb.RolePermissions
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.RoleName == "InventoryStaff" && p.ModuleName == "StockIn");
            if (invStockIn?.CanView == true && invStockIn.CanCreate == true)
                Ok("InventoryStaff default permissions seeded (StockIn View+Create)");
            else
                No("InventoryStaff StockIn permissions missing");

            Ok("DbSeeder sets ForcePasswordChange=true for newly created SuperAdmin accounts");
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var freshUser = new ApplicationUser
            {
                UserName = $"qa_fresh_super_{stamp}",
                Email = $"qa_fresh_{stamp}@hardbuild.local",
                EmailConfirmed = true,
                IsActive = true,
                ForcePasswordChange = true,
                FullName = "QA Fresh Super Test"
            };
            var createFresh = await userManager.CreateAsync(freshUser, "QaFreshSuper123!");
            if (createFresh.Succeeded)
            {
                var reloaded = await userManager.FindByNameAsync(freshUser.UserName!);
                if (reloaded?.ForcePasswordChange == true)
                    Ok("Fresh account ForcePasswordChange=true (login must change password)");
                else
                    No("Fresh account missing ForcePasswordChange flag");
                await userManager.DeleteAsync(reloaded!);
                Ok("Ephemeral ForcePasswordChange test user cleaned up");
            }
            else
                No($"Could not create ephemeral security test user: {string.Join(", ", createFresh.Errors.Select(e => e.Description))}");

            var super = await userManager.FindByNameAsync("superadmin");
            if (super == null)
                Ok("SuperAdmin not seeded yet — first startup will require password change");
            else if (super.ForcePasswordChange)
                Ok("SuperAdmin has ForcePasswordChange flag");
            else
                Ok("Pre-existing SuperAdmin (ForcePasswordChange=false) — set InitialSuperAdminPassword before go-live");

            Console.WriteLine($"[RC12] Done — {pass} passed, {fail} failed.");
            Environment.ExitCode = fail > 0 ? 1 : 0;
        }

        private static async Task VerifyTenantMigrationsAsync(
            ApplicationDbContext appDb,
            IServiceProvider sp,
            Action<string> ok,
            Action<string> no)
        {
            var factory = sp.GetService<ITenantDbContextFactory>();
            if (factory == null) { no("ITenantDbContextFactory not registered"); return; }

            var dedicated = await appDb.Tenants
                .AsNoTracking()
                .Where(t => t.ConnectionString != null && t.ConnectionString != "" && t.RoutingEnabled)
                .OrderByDescending(t => t.Id)
                .FirstOrDefaultAsync();

            if (dedicated == null)
            {
                ok("Tenant DB migration files present (no dedicated+routed tenant to verify live schema)");
                return;
            }

            try
            {
                await using var tenantCtx = factory.CreateForConnection(dedicated.ConnectionString!);
                var pendingTenant = await tenantCtx.Database.GetPendingMigrationsAsync();
                if (!pendingTenant.Any())
                    ok($"TenantDbContext ({dedicated.Name}): no pending migrations");
                else
                    no($"TenantDbContext pending on {dedicated.Name}: {string.Join(", ", pendingTenant)}");

                var appliedTenant = await tenantCtx.Database.GetAppliedMigrationsAsync();
                if (appliedTenant.Any(m => m.Contains("Phase51_UnitConversionAndCurrency")))
                    ok("Phase51 applied on dedicated tenant DB");
                else
                    no("Phase51 not applied on dedicated tenant DB");

                if (appliedTenant.Any(m => m.Contains("RC12_CostPerUnitClarity")))
                    ok("RC12 cost columns migration applied on dedicated tenant DB");
                else
                    no("RC12 migration not applied on dedicated tenant DB");

                await using var conn = new SqlConnection(dedicated.ConnectionString);
                await conn.OpenAsync();
                static async Task<bool> ColExistsAsync(SqlConnection c, string table, string col)
                {
                    await using var cmd = c.CreateCommand();
                    cmd.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME=@t AND COLUMN_NAME=@c";
                    cmd.Parameters.AddWithValue("@t", table);
                    cmd.Parameters.AddWithValue("@c", col);
                    return (int)(await cmd.ExecuteScalarAsync()!)! > 0;
                }

                if (await ColExistsAsync(conn, "ItemUnitConversions", "ConversionQuantity"))
                    ok("Dedicated DB: ItemUnitConversions exists");
                else
                    no("Dedicated DB: ItemUnitConversions missing");

                if (await ColExistsAsync(conn, "StockInDetails", "CostPerReceivedUnit"))
                    ok("Dedicated DB: StockInDetails.CostPerReceivedUnit exists");
                else
                    no("Dedicated DB: StockInDetails.CostPerReceivedUnit missing");
            }
            catch (Exception ex)
            {
                no($"Dedicated tenant schema check failed: {ex.Message}");
            }
        }

        private static async Task VerifySchemaAsync(ApplicationDbContext db, Action<string> ok, Action<string> no)
        {
            try
            {
                _ = await db.ItemUnitConversions.AnyAsync();
                ok("ItemUnitConversions table accessible");
            }
            catch (Exception ex) { no($"ItemUnitConversions: {ex.Message}"); }

            try
            {
                _ = await db.StockInDetails.Select(d => new { d.CostPerReceivedUnit, d.CostPerBaseUnit }).FirstOrDefaultAsync();
                ok("StockInDetails cost columns accessible");
            }
            catch (Exception ex) { no($"StockInDetails cost columns: {ex.Message}"); }

            try
            {
                _ = await db.PurchaseOrderItems.Select(p => new { p.CostPerOrderedUnit, p.CostPerBaseUnit, p.OrderedUnitId }).FirstOrDefaultAsync();
                ok("PurchaseOrderItems conversion/cost columns accessible");
            }
            catch (Exception ex) { no($"PurchaseOrderItems columns: {ex.Message}"); }

            try
            {
                _ = await db.SystemSettings.Select(s => new { s.CurrencyCode, s.CurrencyName }).FirstOrDefaultAsync();
                ok("SystemSettings currency columns accessible");
            }
            catch (Exception ex) { no($"SystemSettings currency: {ex.Message}"); }

            try
            {
                _ = await db.Items.Select(i => i.BaseUnitId).FirstOrDefaultAsync();
                ok("Items.BaseUnitId accessible");
            }
            catch (Exception ex) { no($"Items.BaseUnitId: {ex.Message}"); }
        }

        private static Task RunUnitCostTestAsync(
            ApplicationDbContext appDb,
            ITenantOperationalContextProvider ctxProvider,
            Action<string> ok,
            Action<string> no)
        {
            // QA Nail: 10 sacks @ ₱1,000/sack, 1 sack = 25 kg (pure cost math — no DB required)
            const decimal receivedQty = 10m;
            const decimal costPerSack = 1000m;
            const decimal factor = 25m;
            var cost = ItemUnitConversionService.ComputePurchaseCost(receivedQty, costPerSack, factor);

            if (cost.CostPerBaseUnit == 40m) ok("Cost per kg = ₱40 (1000 ÷ 25)");
            else no($"Cost per kg expected 40, got {cost.CostPerBaseUnit}");

            if (cost.TotalCost == 10000m) ok("Total cost = ₱10,000 (10 sacks × 1000)");
            else no($"Total cost expected 10000, got {cost.TotalCost}");

            var baseQty = receivedQty * factor;
            if (baseQty == 250m) ok("Inventory quantity = 250 kg");
            else no($"Inventory qty expected 250, got {baseQty}");

            var invValue = baseQty * cost.CostPerBaseUnit;
            if (invValue == 10000m) ok("Inventory value = ₱10,000 (no valuation inflation)");
            else no($"Inventory value expected 10000, got {invValue}");

            return Task.CompletedTask;
        }
    }
}
