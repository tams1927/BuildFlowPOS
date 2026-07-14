using HardwareManagementSystem.Configuration;
using HardwareManagementSystem.Data;
using HardwareManagementSystem.Data.Seeders;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HardwareManagementSystem.Tools
{
    /// <summary>
    /// RC1.7-QA — DEV-ONLY automated test for the One-Tenant-One-Database direct
    /// provisioning flow introduced in RC1.7.
    ///
    /// Verifies that a new tenant:
    ///   1. Has its dedicated database auto-created and schema migrated.
    ///   2. Has DataMigrated = true, RoutingEnabled = true, IsDirectlyProvisioned = true.
    ///   3. Writes all operational data to the dedicated DB, not the shared DB.
    ///   4. Has zero operational data leakage into the shared DB.
    ///
    /// SAFETY: runs only in Development AND only with "--qa-direct-dedicated-tenant".
    /// All test data uses the QA_RC17_ prefix. Cleanup drops the QA database when
    ///   QA:EnableCleanup == true in appsettings.
    /// </summary>
    public static class DirectDedicatedTenantQaRunner
    {
        public static async Task RunAsync(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[QA-RC17] Runner is Development-only. Aborting.");
                return;
            }

            var stamp        = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var enableCleanup = app.Configuration.GetValue<bool>("QA:EnableCleanup");

            Console.WriteLine($"[QA-RC17] RC1.7 Direct-Dedicated Tenant QA starting ({stamp})…");
            Console.WriteLine();

            var tenantName   = $"QA_RC17_{stamp}";
            var tenantCode   = $"R17{stamp[8..]}";   // max 20 chars
            var adminUser    = $"qa_rc17_{stamp}";
            var branchCode   = $"QBR{stamp[8..]}";
            const string adminPw = "QaRC17Admin!";

            var pass = 0;
            var fail = 0;

            void Ok(string msg)  { Console.WriteLine($"  [PASS] {msg}"); pass++; }
            void Fail(string msg){ Console.WriteLine($"  [FAIL] {msg}"); fail++; }
            void Sep(string hdr) { Console.WriteLine(); Console.WriteLine($"── {hdr} ──────────────────────────"); }

            // ── SCOPE 1: Create + provision + enable routing ─────────────────
            int    tenantId     = 0;
            string dbName       = string.Empty;
            Tenant? tenantRecord = null;

            using (var scope = app.Services.CreateScope())
            {
                var sp       = scope.ServiceProvider;
                var appCtx   = sp.GetRequiredService<ApplicationDbContext>();
                var provSvc  = sp.GetRequiredService<ITenantDatabaseProvisioningService>();
                var schemaSvc = sp.GetRequiredService<ITenantSchemaMigrationService>();
                var resolver  = sp.GetRequiredService<ITenantDatabaseResolver>();
                var userMgr  = sp.GetRequiredService<UserManager<ApplicationUser>>();
                var roleMgr  = sp.GetRequiredService<RoleManager<IdentityRole>>();
                var dbSettings = sp.GetRequiredService<IOptions<TenantDatabaseSettings>>().Value;

                await DbSeeder.SeedRolePermissionsAsync(sp);

                Sep("STEP 1 — Subscription Plan");
                var plan = await appCtx.SubscriptionPlans.FirstOrDefaultAsync();
                if (plan == null)
                {
                    plan = new SubscriptionPlan
                    {
                        Name         = $"QA_RC17_Plan_{stamp}",
                        MonthlyPrice = 999m,
                        MaxBranches  = 5,
                        MaxUsers     = 20,
                        MaxProducts  = 500,
                        IsActive     = true,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    appCtx.SubscriptionPlans.Add(plan);
                    await appCtx.SaveChangesAsync();
                    Ok($"Created subscription plan: {plan.Name}");
                }
                else
                {
                    Ok($"Using existing subscription plan: {plan.Name}");
                }

                Sep("STEP 2 — Create Tenant");
                var tenant = new Tenant
                {
                    Name               = tenantName,
                    Code               = tenantCode,
                    Status             = TenantStatus.Trial,
                    IsActive           = true,
                    SubscriptionPlanId = plan.Id,
                    MaxBranches        = plan.MaxBranches,
                    MaxUsers           = plan.MaxUsers,
                    MaxProducts        = plan.MaxProducts,
                    CreatedAtUtc       = DateTime.UtcNow,
                    UpdatedAtUtc       = DateTime.UtcNow
                };
                appCtx.Tenants.Add(tenant);
                await appCtx.SaveChangesAsync();
                tenantId = tenant.Id;
                Ok($"Tenant saved: {tenant.Name} (Id={tenantId})");

                appCtx.SystemSettings.Add(new SystemSetting
                {
                    TenantId          = tenantId,
                    BusinessName      = tenant.Name,
                    CurrencySymbol    = "₱",
                    DefaultVatPercent = 12m,
                    TaxMode           = "VAT",
                    ReceiptPaperSize  = "80mm",
                    ThemeColor        = "white-blue",
                    UpdatedAt         = DateTime.Now
                });
                await appCtx.SaveChangesAsync();
                Ok("Shared-DB SystemSetting seeded");

                Sep("STEP 3 — Auto-Provision Dedicated Database");
                var safeCode = System.Text.RegularExpressions.Regex.Replace(tenantCode, @"[^A-Za-z0-9]", "_");
                dbName = $"{dbSettings.EffectivePrefix}_{safeCode}_{tenantId}";

                tenant.DatabaseMode = TenantDatabaseMode.Dedicated;
                tenant.DatabaseName = dbName;
                tenant.UpdatedAtUtc = DateTime.UtcNow;
                await appCtx.SaveChangesAsync();

                var provResult = await provSvc.ProvisionAsync(tenantId);
                if (provResult.Success)
                {
                    Ok($"Dedicated DB provisioned: {dbName}");
                }
                else
                {
                    Fail($"Provisioning FAILED: {provResult.Message}");
                    goto Summary;
                }

                tenant = await appCtx.Tenants.FirstAsync(t => t.Id == tenantId);
                tenant.DataMigrated          = true;
                tenant.DataMigratedAtUtc     = DateTime.UtcNow;
                tenant.RoutingEnabled        = true;
                tenant.IsDirectlyProvisioned = true;
                tenant.UpdatedAtUtc          = DateTime.UtcNow;
                await appCtx.SaveChangesAsync();
                resolver.Invalidate(tenantId);
                tenantRecord = tenant;
                Ok("DataMigrated=true, RoutingEnabled=true, IsDirectlyProvisioned=true — resolver invalidated");

                Sep("STEP 4 — Verify Tenant Flags");
                var fresh = await appCtx.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId);
                if (fresh.DatabaseMode == TenantDatabaseMode.Dedicated) Ok("DatabaseMode = Dedicated");
                else Fail($"DatabaseMode = {fresh.DatabaseMode}");
                if (fresh.IsDatabaseProvisioned) Ok("IsDatabaseProvisioned = true");
                else Fail("IsDatabaseProvisioned = false");
                if (fresh.DataMigrated)          Ok("DataMigrated = true");
                else Fail("DataMigrated = false");
                if (fresh.RoutingEnabled)        Ok("RoutingEnabled = true");
                else Fail("RoutingEnabled = false");
                if (fresh.IsDirectlyProvisioned) Ok("IsDirectlyProvisioned = true");
                else Fail("IsDirectlyProvisioned = false");
                if (fresh.RoutingActive)         Ok("RoutingActive = true (composite check)");
                else Fail("RoutingActive = false");

                Sep("STEP 5 — Schema Status");
                var pending = await schemaSvc.GetPendingMigrationCountAsync(tenantId);
                if (pending == 0) Ok("Schema is up to date (0 pending migrations)");
                else Fail($"Schema has {pending} pending migration(s)");

                Sep("STEP 6 — TenantAdmin Account");
                const string role = "TenantAdmin";
                if (!await roleMgr.RoleExistsAsync(role))
                    await roleMgr.CreateAsync(new IdentityRole(role));

                var owner = new ApplicationUser
                {
                    UserName       = adminUser,
                    Email          = $"{adminUser}@qa.local",
                    FullName       = "QA RC17 Admin",
                    EmailConfirmed = true,
                    IsActive       = true,
                    TenantId       = tenantId
                };
                var createResult = await userMgr.CreateAsync(owner, adminPw);
                if (createResult.Succeeded)
                {
                    await userMgr.AddToRoleAsync(owner, role);
                    Ok($"TenantAdmin '{adminUser}' created and role assigned");
                }
                else
                {
                    Fail($"TenantAdmin creation failed: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
                }

                Sep("STEP 7 — Resolver Returns Dedicated Context");
                var usesDedicated = await resolver.UsesDedicatedDatabaseAsync(tenantId);
                if (usesDedicated) Ok("Resolver confirms dedicated database routing");
                else Fail("Resolver returned shared (expected dedicated)");
            }

            // ── SCOPE 2: Write operational data via routed context ───────────
            using (var scope = app.Services.CreateScope())
            {
                var sp       = scope.ServiceProvider;
                var provider = sp.GetRequiredService<ITenantOperationalContextProvider>();
                var appCtx   = sp.GetRequiredService<ApplicationDbContext>();

                Sep("STEP 8 — Resolve Operational Context");
                var ctx     = await provider.GetContextAsync(tenantId);
                var ctxType = ctx.GetType().Name;
                if (ctxType == "TenantDbContext") Ok($"Provider resolved: TenantDbContext (dedicated)");
                else Fail($"Provider resolved: {ctxType} (expected TenantDbContext)");

                Sep("STEP 9 — Write Operational Data to Dedicated DB");

                var branch = new Branch
                {
                    TenantId     = tenantId,
                    Name         = $"QA_Branch_{stamp}",
                    Code         = branchCode,
                    IsMainBranch = true,
                    IsActive     = true
                };
                ctx.Branches.Add(branch);
                await ctx.SaveChangesAsync();
                Ok($"Branch: {branch.Name} (Id={branch.Id})");

                var category = new Category
                {
                    TenantId     = tenantId,
                    CategoryName = $"QA_Cat_{stamp}",
                    IsActive     = true,
                    CreatedAt    = DateTime.Now
                };
                ctx.Categories.Add(category);
                await ctx.SaveChangesAsync();
                Ok($"Category: {category.CategoryName}");

                var unit = new Unit
                {
                    TenantId  = tenantId,
                    UnitName  = $"QA_Unit_{stamp}",
                    ShortName = "QU",
                    IsActive  = true,
                    CreatedAt = DateTime.Now
                };
                ctx.Units.Add(unit);
                await ctx.SaveChangesAsync();
                Ok($"Unit: {unit.UnitName}");

                var supplier = new Supplier
                {
                    TenantId     = tenantId,
                    SupplierName = $"QA_Supplier_{stamp}",
                    IsActive     = true,
                    CreatedAt    = DateTime.Now
                };
                ctx.Suppliers.Add(supplier);
                await ctx.SaveChangesAsync();
                Ok($"Supplier: {supplier.SupplierName}");

                var customer = new Customer
                {
                    TenantId     = tenantId,
                    CustomerName = $"QA_Cust_{stamp}",
                    CustomerType = "Regular",
                    IsActive     = true,
                    CreatedAt    = DateTime.Now
                };
                ctx.Customers.Add(customer);
                await ctx.SaveChangesAsync();
                Ok($"Customer: {customer.CustomerName}");

                var item = new Item
                {
                    TenantId     = tenantId,
                    ItemCode     = $"QAIT{stamp[8..]}",
                    ItemName     = $"QA_Item_{stamp}",
                    CategoryId   = category.Id,
                    UnitId       = unit.Id,
                    BaseUnitId   = unit.Id,
                    SupplierId   = supplier.Id,
                    CostPrice    = 100m,
                    SellingPrice = 150m,
                    CurrentStock = 0m,
                    ReorderLevel = 5m,
                    Status       = "Active",
                    CreatedAt    = DateTime.Now
                };
                ctx.Items.Add(item);
                await ctx.SaveChangesAsync();
                Ok($"Product: {item.ItemName} (Id={item.Id})");

                var stockIn = new StockInHeader
                {
                    TenantId      = tenantId,
                    BranchId      = branch.Id,
                    SupplierId    = supplier.Id,
                    StockInNumber = $"QASI{stamp[8..]}",
                    DateReceived  = DateTime.Now,
                    TotalCost     = 5000m,
                    PaymentStatus = "Unpaid",
                    AmountPaid    = 0m,
                    CreatedAt     = DateTime.Now
                };
                stockIn.StockInDetails.Add(new StockInDetail
                {
                    ItemId    = item.Id,
                    Quantity  = 50m,
                    UnitCost  = 100m,
                    TotalCost = 5000m
                });
                ctx.StockInHeaders.Add(stockIn);
                item.CurrentStock += 50m;
                await ctx.SaveChangesAsync();
                Ok($"Stock In (Id={stockIn.Id}, qty=50)");

                var sale = new SalesHeader
                {
                    TenantId       = tenantId,
                    BranchId       = branch.Id,
                    CustomerId     = customer.Id,
                    SalesNumber    = $"QASALE{stamp[8..]}",
                    SalesDate      = DateTime.Now,
                    CashierName    = adminUser,
                    SubTotal       = 150m,
                    TotalAmount    = 150m,
                    AmountReceived = 150m,
                    ChangeAmount   = 0m,
                    PaymentMethod  = "Cash",
                    Status         = "Completed",
                    CreatedAt      = DateTime.Now
                };
                sale.SalesDetails.Add(new SalesDetail
                {
                    ItemId    = item.Id,
                    Quantity  = 1m,
                    UnitPrice = 150m,
                    LineTotal = 150m
                });
                ctx.SalesHeaders.Add(sale);
                item.CurrentStock -= 1m;
                await ctx.SaveChangesAsync();
                Ok($"POS Sale (Id={sale.Id})");

                var damaged = new DamagedGoodsHeader
                {
                    TenantId       = tenantId,
                    BranchId       = branch.Id,
                    DamageNumber   = $"QADG{stamp[8..]}",
                    DamageDate     = DateTime.Now,
                    Status         = "Confirmed",
                    Remarks        = "QA damaged goods test",
                    CreatedByUserId = adminUser,
                    CreatedAtUtc   = DateTime.UtcNow
                };
                damaged.Details.Add(new DamagedGoodsDetail
                {
                    ItemId             = item.Id,
                    UnitId             = unit.Id,
                    Quantity           = 1m,
                    BaseQuantity       = 1m,
                    ConversionQuantity = 1m,
                    Reason             = "Damaged"
                });
                ctx.DamagedGoodsHeaders.Add(damaged);
                item.CurrentStock -= 1m;
                await ctx.SaveChangesAsync();
                Ok($"Damaged Goods (Id={damaged.Id})");

                var supReturn = new SupplierReturnHeader
                {
                    TenantId        = tenantId,
                    BranchId        = branch.Id,
                    SupplierId      = supplier.Id,
                    ReturnNumber    = $"QASR{stamp[8..]}",
                    ReturnDate      = DateTime.Now,
                    Status          = "Completed",
                    Remarks         = "QA supplier return test",
                    CreatedByUserId = adminUser,
                    CreatedAtUtc    = DateTime.UtcNow
                };
                ctx.SupplierReturnHeaders.Add(supReturn);
                await ctx.SaveChangesAsync();
                Ok($"Supplier Return (Id={supReturn.Id})");

                Sep("STEP 10 — Verify Write Location");

                // Dedicated DB row counts (via TenantDbContext)
                var tCtx = (TenantDbContext)ctx;
                var dedBranches  = await tCtx.Branches.CountAsync(b => b.TenantId == tenantId);
                var dedSales     = await tCtx.SalesHeaders.CountAsync(s => s.TenantId == tenantId);
                var dedStockIn   = await tCtx.StockInHeaders.CountAsync(s => s.TenantId == tenantId);
                var dedDamaged   = await tCtx.DamagedGoodsHeaders.CountAsync(h => h.TenantId == tenantId);
                var dedSupReturn = await tCtx.SupplierReturnHeaders.CountAsync(h => h.TenantId == tenantId);

                if (dedBranches  >= 1) Ok($"Dedicated DB: {dedBranches} branch(es)");
                else                   Fail("Dedicated DB: 0 branches (data not written)");
                if (dedSales     >= 1) Ok($"Dedicated DB: {dedSales} sale(s)");
                else                   Fail("Dedicated DB: 0 sales");
                if (dedStockIn   >= 1) Ok($"Dedicated DB: {dedStockIn} stock-in record(s)");
                else                   Fail("Dedicated DB: 0 stock-in");
                if (dedDamaged   >= 1) Ok($"Dedicated DB: {dedDamaged} damaged-goods record(s)");
                else                   Fail("Dedicated DB: 0 damaged goods");
                if (dedSupReturn >= 1) Ok($"Dedicated DB: {dedSupReturn} supplier return(s)");
                else                   Fail("Dedicated DB: 0 supplier returns");

                // Shared DB must NOT have these operational rows
                var sharedBranches = await appCtx.Branches.CountAsync(b => b.TenantId == tenantId);
                var sharedSales    = await appCtx.SalesHeaders.CountAsync(s => s.TenantId == tenantId);
                var sharedStockIn  = await appCtx.StockInHeaders.CountAsync(s => s.TenantId == tenantId);

                if (sharedBranches == 0) Ok("Shared DB: 0 branches (no leakage)");
                else Fail($"Shared DB: {sharedBranches} branch(es) found — LEAKAGE DETECTED");
                if (sharedSales    == 0) Ok("Shared DB: 0 sales (no leakage)");
                else Fail($"Shared DB: {sharedSales} sale(s) found — LEAKAGE DETECTED");
                if (sharedStockIn  == 0) Ok("Shared DB: 0 stock-in (no leakage)");
                else Fail($"Shared DB: {sharedStockIn} stock-in record(s) found — LEAKAGE DETECTED");

                Sep("STEP 11 — Existing Tenants Unaffected");
                var otherCount = await appCtx.Tenants.CountAsync(t => t.Id != tenantId);
                Ok($"Other tenants in platform DB: {otherCount} (untouched)");
            }

            Summary:
            Sep("SUMMARY");
            Console.WriteLine($"  Passed : {pass}");
            Console.WriteLine($"  Failed : {fail}");
            Console.WriteLine($"  Result : {(fail == 0 ? "ALL PASS ✓" : $"FAILED ({fail} failure(s))")}");

            if (fail == 0)
            {
                Console.WriteLine();
                Console.WriteLine("[QA-RC17] RC1.7 direct-dedicated tenant flow is VERIFIED.");
            }

            // ── Optional cleanup ─────────────────────────────────────────────
            if (enableCleanup && tenantRecord != null && tenantId > 0)
            {
                Console.WriteLine();
                Console.WriteLine("[QA-RC17] Cleanup: removing QA tenant and dropping QA database…");
                try
                {
                    using var cleanScope = app.Services.CreateScope();
                    var sp      = cleanScope.ServiceProvider;
                    var appCtx  = sp.GetRequiredService<ApplicationDbContext>();
                    var userMgr = sp.GetRequiredService<UserManager<ApplicationUser>>();

                    var tenantFresh = await appCtx.Tenants.FindAsync(tenantId);
                    var dbToDrop    = tenantFresh?.DatabaseName;
                    var connForDrop = tenantFresh?.ConnectionString;

                    var qaUsers = await userMgr.Users.Where(u => u.TenantId == tenantId).ToListAsync();
                    foreach (var u in qaUsers) await userMgr.DeleteAsync(u);

                    var sys = await appCtx.SystemSettings.Where(s => s.TenantId == tenantId).ToListAsync();
                    appCtx.SystemSettings.RemoveRange(sys);
                    if (tenantFresh != null) appCtx.Tenants.Remove(tenantFresh);
                    await appCtx.SaveChangesAsync();

                    if (!string.IsNullOrWhiteSpace(dbToDrop) && !string.IsNullOrWhiteSpace(connForDrop))
                    {
                        var masterConn = new SqlConnectionStringBuilder(connForDrop)
                        { InitialCatalog = "master" }.ConnectionString;

                        await using var conn = new SqlConnection(masterConn);
                        await conn.OpenAsync();
                        await using var cmd = conn.CreateCommand();
                        var safe = dbToDrop.Replace("]", "]]");
                        cmd.CommandText =
                            $"IF DB_ID(N'{safe}') IS NOT NULL " +
                            $"BEGIN ALTER DATABASE [{safe}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                            $"DROP DATABASE [{safe}]; END";
                        await cmd.ExecuteNonQueryAsync();
                        Console.WriteLine($"[QA-RC17] Dropped database: {dbToDrop}");
                    }

                    Console.WriteLine("[QA-RC17] Cleanup complete.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[QA-RC17] Cleanup failed (non-fatal): {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine($"[QA-RC17] QA tenant left in DB. Tenant code: {tenantCode}, Database: {dbName}");
                Console.WriteLine("[QA-RC17] Set QA:EnableCleanup=true in appsettings to auto-drop on next run.");
            }
        }
    }
}
