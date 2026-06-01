using System.Text;
using HardwareManagementSystem.Data;
using HardwareManagementSystem.Data.Seeders;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Tools
{
    /// <summary>
    /// Phase 5.0D.1-QA — DEV-ONLY automated full-cycle pilot test.
    ///
    /// Runs the complete SaaS + dedicated-database routing lifecycle against the real
    /// SQL Server instance configured in appsettings.Development.json:
    ///
    ///   SuperAdmin → Create Tenant → Create TenantAdmin → Dedicated mode → Provision DB
    ///   → Migrate Data → Enable Routing → write operational records through the live
    ///   routing path → verify rows land in the DEDICATED db and NOT in the shared db
    ///   → read reports from dedicated → Disable Routing → verify fallback.
    ///
    /// SAFETY:
    ///   • Only runs when ASPNETCORE_ENVIRONMENT == Development AND launched with the
    ///     explicit "--qa-phase50d1" argument. It never runs during normal startup and
    ///     does NOT start the web server.
    ///   • All test data uses the obvious QA_ prefix.
    ///   • Cleanup (including DROP DATABASE) only runs when QA:EnableCleanup == true.
    ///
    /// This is test tooling. It deliberately reproduces the small amount of metadata
    /// flipping that lives in TenantsController (EnableRouting/DisableRouting) because
    /// those are HTTP actions; everything else uses the real services and the real
    /// ITenantOperationalContextProvider routing decision.
    /// </summary>
    public static class Phase50D1QaRunner
    {
        public static async Task RunAsync(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[QA] Phase 5.0D.1 runner is Development-only. Aborting.");
                return;
            }

            var stamp     = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var report    = new Report(stamp);
            var enableCleanup = app.Configuration.GetValue<bool>("QA:EnableCleanup");

            Console.WriteLine($"[QA] Phase 5.0D.1 full-cycle test starting ({stamp})…");

            // Names (TASK 2)
            var tenantName   = $"QA_Tenant_{stamp}";
            var tenantCode   = $"QA{stamp}";                  // <= 20 chars
            var adminUser    = $"qa_admin_{stamp}";
            var dbName       = $"QA_TenantDb_{stamp}";
            var branchName   = $"QA_MainBranch_{stamp}";
            var categoryName = $"QA_Category_{stamp}";
            var unitName     = $"QA_Unit_{stamp}";
            var itemName     = $"QA_Item_{stamp}";
            var itemCode     = $"QAITM{stamp}";
            var supplierName = $"QA_Supplier_{stamp}";
            var customerName = $"QA_Customer_{stamp}";
            const string adminPassword = "QaAdmin123!";

            report.TenantName   = tenantName;
            report.DatabaseName = dbName;

            int tenantId = 0;
            string? dedicatedConn = null;

            try
            {
                // ─────────────────────────────────────────────────────────────
                // SCOPE 1 — SuperAdmin flow (TASK 3)
                // ─────────────────────────────────────────────────────────────
                using (var scope = app.Services.CreateScope())
                {
                    var sp        = scope.ServiceProvider;
                    var db        = sp.GetRequiredService<ApplicationDbContext>();
                    var users     = sp.GetRequiredService<UserManager<ApplicationUser>>();
                    var roles     = sp.GetRequiredService<RoleManager<IdentityRole>>();
                    var resolver  = sp.GetRequiredService<ITenantDatabaseResolver>();
                    var provision = sp.GetRequiredService<ITenantDatabaseProvisioningService>();
                    var migrate   = sp.GetRequiredService<ITenantDataMigrationService>();

                    // 1) Ensure SuperAdmin exists
                    await DbSeeder.SeedAdminAsync(sp);
                    await DbSeeder.SeedRolePermissionsAsync(sp);
                    var superAdmin = await users.FindByNameAsync("superadmin");
                    report.Check("Ensure SuperAdmin exists", superAdmin != null,
                        superAdmin != null ? "superadmin present" : "superadmin NOT found");

                    // 2) Create Tenant (Dedicated mode, QA db name)
                    var tenant = new Tenant
                    {
                        Name         = tenantName,
                        Code         = tenantCode,
                        Status       = TenantStatus.Active,
                        IsActive     = true,
                        MaxBranches  = 5,
                        MaxUsers     = 10,
                        MaxProducts  = 500,
                        DatabaseMode = TenantDatabaseMode.Dedicated,
                        DatabaseName = dbName,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    };
                    db.Tenants.Add(tenant);
                    await db.SaveChangesAsync();
                    tenantId = tenant.Id;
                    report.TenantId = tenantId;
                    report.Check("Create Tenant", tenantId > 0, $"TenantId={tenantId}, Mode=Dedicated, Db={dbName}");

                    // tenant-owned SystemSetting (shared) — mirrors TenantsController.Create
                    db.SystemSettings.Add(new SystemSetting
                    {
                        TenantId          = tenantId,
                        BusinessName      = tenantName,
                        CurrencySymbol    = "₱",
                        DefaultVatPercent = 12m,
                        TaxMode           = "VAT",
                        ReceiptPaperSize  = "80mm",
                        ThemeColor        = "dark-blue",
                        UpdatedAt         = DateTime.Now
                    });
                    await db.SaveChangesAsync();

                    // 3) Create first TenantAdmin
                    if (!await roles.RoleExistsAsync("TenantAdmin"))
                        await roles.CreateAsync(new IdentityRole("TenantAdmin"));

                    var admin = new ApplicationUser
                    {
                        UserName       = adminUser,
                        Email          = $"{adminUser}@qa.local",
                        FullName       = "QA Tenant Admin",
                        EmailConfirmed = true,
                        IsActive       = true,
                        TenantId       = tenantId
                    };
                    var createRes = await users.CreateAsync(admin, adminPassword);
                    if (createRes.Succeeded)
                        await users.AddToRoleAsync(admin, "TenantAdmin");
                    report.Check("Create TenantAdmin", createRes.Succeeded,
                        createRes.Succeeded ? $"{adminUser} (TenantAdmin)" :
                        string.Join("; ", createRes.Errors.Select(e => e.Description)));

                    // 5) Provision dedicated database (real service)
                    var pr = await provision.ProvisionAsync(tenantId);
                    report.Check("Provision dedicated DB", pr.Success,
                        $"{pr.Message} (created={pr.DatabaseCreated}, migrated={pr.MigrationsApplied}, seeded={pr.SeedCompleted})");
                    if (!pr.Success) throw new QaStop("Provisioning failed.");

                    tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId);
                    dedicatedConn = tenant.ConnectionString;

                    // 6) Test connection
                    var (connOk, connMsg) = await TryOpenAsync(dedicatedConn!);
                    report.Check("Test connection (dedicated)", connOk, connMsg);
                    if (!connOk) throw new QaStop("Dedicated connection failed.");

                    // 7) Migrate tenant data (real service)
                    var mr = await migrate.MigrateAsync(tenantId);
                    report.Check("Migrate tenant data", mr.Success,
                        $"{mr.Message} (rows={mr.TotalRowsCopied}, tables={mr.Tables.Count})");
                    if (!mr.Success) throw new QaStop("Migration failed.");

                    // 8) Enable routing (mirrors TenantsController.EnableRouting)
                    var t2 = await db.Tenants.FirstAsync(t => t.Id == tenantId);
                    if (!t2.IsDatabaseProvisioned) throw new QaStop("Tenant not provisioned at enable-routing.");
                    if (!t2.DataMigrated)          throw new QaStop("Tenant not migrated at enable-routing.");
                    t2.RoutingEnabled = true;
                    t2.UpdatedAtUtc   = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    resolver.Invalidate(tenantId);
                    report.Check("Enable routing", true, "RoutingEnabled=true; resolver cache invalidated");

                    // 9) Diagnostics
                    var usesDedicated = await resolver.UsesDedicatedDatabaseAsync(tenantId);
                    var routingActive = await resolver.IsRoutingActiveAsync(tenantId);
                    var runtimeDb     = await resolver.GetRuntimeDatabaseAsync(tenantId);
                    var diagOk = t2.DatabaseProvisionedAtUtc != null && t2.DataMigrated && t2.RoutingEnabled
                                 && usesDedicated && routingActive && runtimeDb == "Dedicated";
                    report.Check("Diagnostics (Provisioned/Migrated/Routing/Runtime=Dedicated)", diagOk,
                        $"Provisioned={t2.DatabaseProvisionedAtUtc != null}, DataMigrated={t2.DataMigrated}, " +
                        $"RoutingEnabled={t2.RoutingEnabled}, RuntimeDatabase={runtimeDb}");
                    if (!diagOk) throw new QaStop("Diagnostics did not confirm dedicated routing.");
                }

                // ─────────────────────────────────────────────────────────────
                // SCOPE 2 — TenantAdmin operational flow via the live routing path (TASK 4)
                // ─────────────────────────────────────────────────────────────
                using (var scope = app.Services.CreateScope())
                {
                    var sp       = scope.ServiceProvider;
                    var provider = sp.GetRequiredService<ITenantOperationalContextProvider>();

                    // Resolve the operational context exactly like OperationalDbController does.
                    var ctx = await provider.GetContextAsync(tenantId);
                    var ctxType = ctx.GetType().Name;
                    report.Check("Operational context routes to TenantDbContext", ctxType == "TenantDbContext",
                        $"Resolved context = {ctxType}");
                    if (ctxType != "TenantDbContext") throw new QaStop("Provider did not route to dedicated context.");

                    // 1) Branch
                    var branch = new Branch { TenantId = tenantId, Name = branchName, Code = $"QABR{stamp}", IsMainBranch = true, IsActive = true };
                    ctx.Branches.Add(branch);
                    // 2) Category, 3) Unit, 5) Supplier, 7) Customer
                    var category = new Category { TenantId = tenantId, CategoryName = categoryName, IsActive = true, CreatedAt = DateTime.Now };
                    var unit     = new Unit { TenantId = tenantId, UnitName = unitName, ShortName = $"Q{stamp.Substring(stamp.Length - 3)}", IsActive = true, CreatedAt = DateTime.Now };
                    var supplier = new Supplier { TenantId = tenantId, SupplierName = supplierName, IsActive = true, CreatedAt = DateTime.Now };
                    var customer = new Customer { TenantId = tenantId, CustomerName = customerName, CustomerType = "Walk-in", IsActive = true, CreatedAt = DateTime.Now };
                    ctx.Categories.Add(category);
                    ctx.Units.Add(unit);
                    ctx.Suppliers.Add(supplier);
                    ctx.Customers.Add(customer);
                    await ctx.SaveChangesAsync();
                    report.Check("Create Branch/Category/Unit/Supplier/Customer (dedicated)", true,
                        $"BranchId={branch.Id}, CategoryId={category.Id}, UnitId={unit.Id}, SupplierId={supplier.Id}, CustomerId={customer.Id}");

                    // 4) Item
                    var item = new Item
                    {
                        TenantId     = tenantId,
                        ItemCode     = itemCode,
                        ItemName     = itemName,
                        CategoryId   = category.Id,
                        UnitId       = unit.Id,
                        SupplierId   = supplier.Id,
                        CostPrice    = 50m,
                        SellingPrice = 80m,
                        CurrentStock = 0m,
                        ReorderLevel = 5m,
                        Status       = "Active",
                        CreatedAt    = DateTime.Now
                    };
                    ctx.Items.Add(item);
                    await ctx.SaveChangesAsync();
                    report.Check("Create Item (dedicated)", item.Id > 0, $"ItemId={item.Id}, Code={itemCode}");

                    // 6) Stock-In (header + detail), bump stock
                    var stockIn = new StockInHeader
                    {
                        TenantId      = tenantId,
                        StockInNumber = $"QASI{stamp}",
                        DateReceived  = DateTime.Now,
                        SupplierId    = supplier.Id,
                        BranchId      = branch.Id,
                        TotalCost     = 500m,
                        PaymentStatus = "Paid",
                        AmountPaid    = 500m,
                        CreatedAt     = DateTime.Now
                    };
                    stockIn.StockInDetails.Add(new StockInDetail { ItemId = item.Id, Quantity = 10m, UnitCost = 50m, TotalCost = 500m });
                    ctx.StockInHeaders.Add(stockIn);
                    item.CurrentStock += 10m;
                    await ctx.SaveChangesAsync();
                    report.Check("Create Stock-In header + detail (dedicated)", stockIn.Id > 0,
                        $"StockInId={stockIn.Id}, Details={stockIn.StockInDetails.Count}, Item.CurrentStock={item.CurrentStock}");

                    // 8) POS Sale (header + detail)
                    var sale = new SalesHeader
                    {
                        TenantId       = tenantId,
                        SalesNumber    = $"QASALE{stamp}",
                        SalesDate      = DateTime.Now,
                        CustomerId     = customer.Id,
                        BranchId       = branch.Id,
                        CashierName    = "QA",
                        SubTotal       = 80m,
                        TotalAmount    = 80m,
                        AmountReceived = 100m,
                        ChangeAmount   = 20m,
                        PaymentMethod  = "Cash",
                        Status         = "Completed",
                        CreatedAt      = DateTime.Now
                    };
                    sale.SalesDetails.Add(new SalesDetail { ItemId = item.Id, Quantity = 1m, UnitPrice = 80m, LineTotal = 80m });
                    ctx.SalesHeaders.Add(sale);
                    item.CurrentStock -= 1m;
                    await ctx.SaveChangesAsync();
                    report.Check("Create POS Sale header + detail (dedicated)", sale.Id > 0,
                        $"SaleId={sale.Id}, Details={sale.SalesDetails.Count}");
                }

                // ─────────────────────────────────────────────────────────────
                // SCOPE 3 — Write-location verification (TASK 5) + report reads (TASK 6)
                // ─────────────────────────────────────────────────────────────
                using (var scope = app.Services.CreateScope())
                {
                    var sp      = scope.ServiceProvider;
                    var shared  = sp.GetRequiredService<ApplicationDbContext>();
                    var factory = sp.GetRequiredService<ITenantDbContextFactory>();

                    await using var ded = factory.CreateForConnection(dedicatedConn!);

                    // Dedicated counts (expect > 0)
                    report.Dedicated["Branches"]       = await ded.Branches.CountAsync(x => x.Name == branchName);
                    report.Dedicated["Categories"]     = await ded.Categories.CountAsync(x => x.CategoryName == categoryName);
                    report.Dedicated["Units"]          = await ded.Units.CountAsync(x => x.UnitName == unitName);
                    report.Dedicated["Items"]          = await ded.Items.CountAsync(x => x.ItemName == itemName);
                    report.Dedicated["Suppliers"]      = await ded.Suppliers.CountAsync(x => x.SupplierName == supplierName);
                    report.Dedicated["Customers"]      = await ded.Customers.CountAsync(x => x.CustomerName == customerName);
                    report.Dedicated["StockInHeaders"] = await ded.StockInHeaders.CountAsync(x => x.StockInNumber == $"QASI{stamp}");
                    report.Dedicated["StockInDetails"] = await ded.StockInDetails.CountAsync(x => x.StockInHeader!.StockInNumber == $"QASI{stamp}");
                    report.Dedicated["SalesHeaders"]   = await ded.SalesHeaders.CountAsync(x => x.SalesNumber == $"QASALE{stamp}");
                    report.Dedicated["SalesDetails"]   = await ded.SalesDetails.CountAsync(x => x.SalesHeader!.SalesNumber == $"QASALE{stamp}");

                    // Shared counts (expect 0 — these QA operational rows were never written to shared)
                    report.Shared["Branches"]       = await shared.Branches.CountAsync(x => x.Name == branchName);
                    report.Shared["Categories"]     = await shared.Categories.CountAsync(x => x.CategoryName == categoryName);
                    report.Shared["Units"]          = await shared.Units.CountAsync(x => x.UnitName == unitName);
                    report.Shared["Items"]          = await shared.Items.CountAsync(x => x.ItemName == itemName);
                    report.Shared["Suppliers"]      = await shared.Suppliers.CountAsync(x => x.SupplierName == supplierName);
                    report.Shared["Customers"]      = await shared.Customers.CountAsync(x => x.CustomerName == customerName);
                    report.Shared["StockInHeaders"] = await shared.StockInHeaders.CountAsync(x => x.StockInNumber == $"QASI{stamp}");
                    report.Shared["SalesHeaders"]   = await shared.SalesHeaders.CountAsync(x => x.SalesNumber == $"QASALE{stamp}");

                    var dedAllPresent = report.Dedicated.Values.All(v => v > 0);
                    var sharedAllZero = report.Shared.Values.All(v => v == 0);
                    report.Check("Dedicated DB contains all QA operational rows", dedAllPresent, report.DedicatedSummary());
                    report.Check("Shared DB contains NONE of the QA operational rows", sharedAllZero, report.SharedSummary());
                    if (!dedAllPresent) throw new QaStop("Some QA rows missing from dedicated DB.");
                    if (!sharedAllZero) throw new QaStop("QA operational rows leaked into shared DB.");

                    // TASK 6 — report reads against dedicated DB
                    var itemsCount   = await ded.Items.CountAsync(i => i.Status == "Active" && i.ItemName == itemName);
                    var inventoryQty = await ded.Items.Where(i => i.ItemName == itemName).Select(i => i.CurrentStock).FirstOrDefaultAsync();
                    var salesHistory = await ded.SalesHeaders.CountAsync(s => s.SalesNumber == $"QASALE{stamp}");
                    var salesTotal   = await ded.SalesHeaders.Where(s => s.SalesNumber == $"QASALE{stamp}").SumAsync(s => s.TotalAmount);
                    var reportsOk = itemsCount == 1 && salesHistory == 1 && salesTotal == 80m;
                    report.Check("Report reads see QA item/sale from dedicated DB", reportsOk,
                        $"Dashboard/Inventory items={itemsCount}, Inventory qty={inventoryQty}, SalesHistory={salesHistory}, SalesReport total={salesTotal}");
                }

                // ─────────────────────────────────────────────────────────────
                // SCOPE 4 — Rollback test (TASK 7)
                // ─────────────────────────────────────────────────────────────
                using (var scope = app.Services.CreateScope())
                {
                    var sp       = scope.ServiceProvider;
                    var db       = sp.GetRequiredService<ApplicationDbContext>();
                    var resolver = sp.GetRequiredService<ITenantDatabaseResolver>();
                    var users    = sp.GetRequiredService<UserManager<ApplicationUser>>();

                    var t = await db.Tenants.FirstAsync(x => x.Id == tenantId);
                    t.RoutingEnabled = false;
                    t.UpdatedAtUtc   = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    resolver.Invalidate(tenantId);

                    var runtimeDb = await resolver.GetRuntimeDatabaseAsync(tenantId);
                    report.Check("Rollback: runtime database returns Shared", runtimeDb == "Shared", $"RuntimeDatabase={runtimeDb}");

                    var t3 = await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == tenantId);
                    report.Check("Rollback: diagnostics show RoutingEnabled=false", !t3.RoutingEnabled, $"RoutingEnabled={t3.RoutingEnabled}");

                    // Tenant can still login (credential check; full cookie login is HTTP-level)
                    var admin = await users.FindByNameAsync(adminUser);
                    var pwOk  = admin != null && admin.IsActive && await users.CheckPasswordAsync(admin, adminPassword);
                    report.Check("Rollback: TenantAdmin can still authenticate", pwOk,
                        pwOk ? "password check passed, user active" : "authentication check failed");
                    report.RollbackOk = runtimeDb == "Shared" && !t3.RoutingEnabled && pwOk;
                    report.Note("Dedicated database was NOT deleted (per spec).");
                }

                // Re-route provider check post-rollback (fresh scope)
                using (var scope = app.Services.CreateScope())
                {
                    var provider = scope.ServiceProvider.GetRequiredService<ITenantOperationalContextProvider>();
                    var ctx = await provider.GetContextAsync(tenantId);
                    report.Check("Rollback: provider returns ApplicationDbContext", ctx.GetType().Name == "ApplicationDbContext",
                        $"Resolved context = {ctx.GetType().Name}");
                }

                report.Success = report.Failures == 0;
            }
            catch (QaStop stop)
            {
                report.Note($"STOPPED: {stop.Message}");
                report.Success = false;
            }
            catch (Exception ex)
            {
                report.Note($"UNEXPECTED EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                report.LastException = ex.ToString();
                report.Success = false;
            }

            // ── Optional cleanup (TASK 8) ────────────────────────────────────
            if (enableCleanup && tenantId > 0)
            {
                try
                {
                    await CleanupAsync(app, tenantId, adminUser, dedicatedConn, dbName, report);
                }
                catch (Exception ex)
                {
                    report.Note($"Cleanup error: {ex.Message}");
                }
            }
            else
            {
                report.Note("Cleanup skipped (QA:EnableCleanup not true). Test data retained with QA_ prefix.");
            }

            // ── Write the report (TASK 9) + console summary ──────────────────
            await report.WriteFileAsync(app);
            Console.WriteLine(report.ConsoleSummary());
        }

        // ──────────────────────────────────────────────────────────────────
        private static async Task<(bool ok, string message)> TryOpenAsync(string connectionString)
        {
            try
            {
                await using var c = new SqlConnection(connectionString);
                await c.OpenAsync();
                return (true, "Connection successful.");
            }
            catch (Exception ex) { return (false, $"Connection failed: {ex.Message}"); }
        }

        private static async Task CleanupAsync(
            WebApplication app, int tenantId, string adminUser, string? dedicatedConn, string dbName, Report report)
        {
            using var scope = app.Services.CreateScope();
            var sp    = scope.ServiceProvider;
            var db    = sp.GetRequiredService<ApplicationDbContext>();
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

            var admin = await users.FindByNameAsync(adminUser);
            if (admin != null) await users.DeleteAsync(admin);

            var settings = await db.SystemSettings.Where(s => s.TenantId == tenantId).ToListAsync();
            db.SystemSettings.RemoveRange(settings);
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant != null) db.Tenants.Remove(tenant);
            await db.SaveChangesAsync();

            // DROP the dedicated database via master
            if (!string.IsNullOrWhiteSpace(dedicatedConn))
            {
                var master = new SqlConnectionStringBuilder(dedicatedConn) { InitialCatalog = "master" }.ConnectionString;
                await using var c = new SqlConnection(master);
                await c.OpenAsync();
                await using var cmd = c.CreateCommand();
                var safe = dbName.Replace("]", "]]");
                cmd.CommandText =
                    $"IF DB_ID(N'{dbName.Replace("'", "''")}') IS NOT NULL BEGIN " +
                    $"ALTER DATABASE [{safe}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{safe}]; END";
                await cmd.ExecuteNonQueryAsync();
            }
            report.Note($"Cleanup completed: removed tenant {tenantId}, user {adminUser}, and dropped DB {dbName}.");
        }

        // ──────────────────────────────────────────────────────────────────
        private sealed class QaStop : Exception
        {
            public QaStop(string message) : base(message) { }
        }

        private sealed class Report
        {
            private readonly string _stamp;
            public Report(string stamp) { _stamp = stamp; }

            public string  TenantName   = "";
            public int     TenantId;
            public string  DatabaseName = "";
            public bool    Success;
            public bool    RollbackOk;
            public string? LastException;

            public readonly List<(string step, bool ok, string detail)> Steps = new();
            public readonly List<string> Notes = new();
            public readonly Dictionary<string, int> Dedicated = new();
            public readonly Dictionary<string, int> Shared = new();

            public int Failures => Steps.Count(s => !s.ok);

            public void Check(string step, bool ok, string detail)
            {
                Steps.Add((step, ok, detail));
                Console.WriteLine($"[QA] {(ok ? "PASS" : "FAIL")} — {step} :: {detail}");
            }

            public void Note(string n) { Notes.Add(n); Console.WriteLine($"[QA] NOTE — {n}"); }

            public string DedicatedSummary() => string.Join(", ", Dedicated.Select(kv => $"{kv.Key}={kv.Value}"));
            public string SharedSummary()    => string.Join(", ", Shared.Select(kv => $"{kv.Key}={kv.Value}"));

            public string ConsoleSummary()
            {
                var passed = Steps.Count(s => s.ok);
                return $"\n[QA] ==== Phase 5.0D.1 Full-Cycle ==== " +
                       $"{(Success ? "OVERALL PASS" : "OVERALL FAIL")} — {passed}/{Steps.Count} steps passed. " +
                       $"Report: docs/Phase50D1_FullCycleQAReport.md";
            }

            public async Task WriteFileAsync(WebApplication app)
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Phase 5.0D.1 — Full-Cycle Pilot QA Report");
                sb.AppendLine();
                sb.AppendLine($"- **Test run:** {DateTime.Now:yyyy-MM-dd HH:mm:ss} (local), id `{_stamp}`");
                sb.AppendLine($"- **Runner:** dev-only `Tools/Phase50D1QaRunner.cs` via `dotnet run -- --qa-phase50d1` (web server NOT started)");
                sb.AppendLine($"- **Environment:** {app.Environment.EnvironmentName}");
                sb.AppendLine($"- **Tenant created:** `{TenantName}` (TenantId = {TenantId})");
                sb.AppendLine($"- **Dedicated database:** `{DatabaseName}`");
                sb.AppendLine($"- **Overall result:** {(Success ? "✅ PASS" : "❌ FAIL")} — {Steps.Count(s => s.ok)}/{Steps.Count} steps passed");
                sb.AppendLine();

                sb.AppendLine("## Steps");
                sb.AppendLine();
                sb.AppendLine("| # | Step | Result | Detail |");
                sb.AppendLine("|---|------|--------|--------|");
                for (var i = 0; i < Steps.Count; i++)
                {
                    var s = Steps[i];
                    sb.AppendLine($"| {i + 1} | {s.step} | {(s.ok ? "PASS" : "FAIL")} | {s.detail.Replace("|", "\\|")} |");
                }
                sb.AppendLine();

                sb.AppendLine("## Dedicated DB row verification (expected > 0)");
                sb.AppendLine();
                sb.AppendLine("| Table | Rows in Dedicated |");
                sb.AppendLine("|-------|-------------------|");
                foreach (var kv in Dedicated) sb.AppendLine($"| {kv.Key} | {kv.Value} |");
                sb.AppendLine();

                sb.AppendLine("## Shared DB row verification (expected 0 for QA operational rows)");
                sb.AppendLine();
                sb.AppendLine("| Table | Rows in Shared |");
                sb.AppendLine("|-------|----------------|");
                foreach (var kv in Shared) sb.AppendLine($"| {kv.Key} | {kv.Value} |");
                sb.AppendLine();
                sb.AppendLine("> Platform rows (AspNetUsers, Tenants, SubscriptionPlans, RolePermissions) intentionally remain in the shared DB.");
                sb.AppendLine();

                sb.AppendLine("## Routing & rollback");
                sb.AppendLine();
                sb.AppendLine($"- Routing verification: {(Steps.Any(s => s.step.Contains("Runtime=Dedicated") && s.ok) ? "Dedicated confirmed" : "see steps")}");
                sb.AppendLine($"- Rollback result: {(RollbackOk ? "✅ fell back to Shared, no crash, tenant can authenticate, RoutingEnabled=false" : "❌ see steps")}");
                sb.AppendLine();

                sb.AppendLine("## Notes");
                sb.AppendLine();
                foreach (var n in Notes) sb.AppendLine($"- {n}");
                if (LastException != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("## Exception");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(LastException);
                    sb.AppendLine("```");
                }
                sb.AppendLine();
                sb.AppendLine("## Cleanup");
                sb.AppendLine();
                sb.AppendLine("Cleanup is OFF by default. To remove QA data and DROP the dedicated database, set");
                sb.AppendLine("`QA:EnableCleanup = true` (e.g. in appsettings.Development.json) and re-run the runner,");
                sb.AppendLine("or drop manually: `DROP DATABASE [<QA_TenantDb_...>]` and delete the QA tenant/user rows.");

                var path = Path.Combine(app.Environment.ContentRootPath, "docs", "Phase50D1_FullCycleQAReport.md");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, sb.ToString());
            }
        }
    }
}
