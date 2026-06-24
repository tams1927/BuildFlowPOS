using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Tools
{
    /// <summary>Phase 5.3.1 — damaged goods migration patch QA (--qa-phase531).</summary>
    public static class Phase531QaRunner
    {
        public static async Task RunAsync(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[QA531] Development-only. Aborting.");
                return;
            }

            var pass = 0;
            var fail = 0;
            void Ok(string m) { pass++; Console.WriteLine($"[QA531] PASS — {m}"); }
            void No(string m) { fail++; Console.WriteLine($"[QA531] FAIL — {m}"); }

            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dbName = $"QA531_TenantDb_{stamp}";
            var tenantName = $"QA531_Tenant_{stamp}";
            var tenantCode = $"Q531{stamp[^6..]}";
            int tenantId = 0;
            string? dedicatedConn = null;

            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var appDb = sp.GetRequiredService<ApplicationDbContext>();
            var provision = sp.GetRequiredService<ITenantDatabaseProvisioningService>();
            var migrate = sp.GetRequiredService<ITenantDataMigrationService>();
            var factory = sp.GetRequiredService<ITenantDbContextFactory>();

            try
            {
                var tenant = new Tenant
                {
                    Name = tenantName,
                    Code = tenantCode,
                    Status = TenantStatus.Active,
                    IsActive = true,
                    MaxBranches = 3,
                    MaxUsers = 5,
                    MaxProducts = 200,
                    DatabaseMode = TenantDatabaseMode.Dedicated,
                    DatabaseName = dbName,
                    RoutingEnabled = false,
                    DataMigrated = false,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                appDb.Tenants.Add(tenant);
                await appDb.SaveChangesAsync();
                tenantId = tenant.Id;
                Ok($"Created dedicated tenant {tenantId}");

                var pr = await provision.ProvisionAsync(tenantId);
                if (pr.Success) Ok("Provisioned dedicated database");
                else
                {
                    No($"Provision failed: {pr.Message}");
                    Console.WriteLine($"[QA531] Summary: {pass} passed, {fail} failed.");
                    Environment.ExitCode = 1;
                    return;
                }

                tenant = await appDb.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId);
                dedicatedConn = tenant.ConnectionString;

                var branch = new Branch
                {
                    TenantId = tenantId,
                    Name = $"B_{stamp}",
                    Code = "Q531",
                    IsMainBranch = true,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                var category = new Category
                {
                    TenantId = tenantId,
                    CategoryName = $"C_{stamp}",
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };
                var unit = new Unit
                {
                    TenantId = tenantId,
                    UnitName = "kg",
                    ShortName = "kg",
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };
                var supplier = new Supplier
                {
                    TenantId = tenantId,
                    SupplierName = $"S_{stamp}",
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };
                appDb.Branches.Add(branch);
                appDb.Categories.Add(category);
                appDb.Units.Add(unit);
                appDb.Suppliers.Add(supplier);
                await appDb.SaveChangesAsync();

                var item = new Item
                {
                    TenantId = tenantId,
                    ItemCode = $"QA531_{stamp}",
                    ItemName = $"QA531 Item {stamp}",
                    CategoryId = category.Id,
                    UnitId = unit.Id,
                    BaseUnitId = unit.Id,
                    CurrentStock = 100,
                    DamagedStock = 10,
                    CostPrice = 10,
                    SellingPrice = 15,
                    Status = "Active",
                    CreatedAt = DateTime.Now
                };
                appDb.Items.Add(item);
                await appDb.SaveChangesAsync();

                var damageHeader = new DamagedGoodsHeader
                {
                    TenantId = tenantId,
                    BranchId = branch.Id,
                    DamageNumber = $"DMG-{stamp}",
                    DamageDate = DateTime.Now,
                    Status = "ReturnedToSupplier",
                    Remarks = "QA531 damage",
                    CreatedAtUtc = DateTime.UtcNow
                };
                damageHeader.Details.Add(new DamagedGoodsDetail
                {
                    ItemId = item.Id,
                    Quantity = 10,
                    UnitId = unit.Id,
                    ConversionQuantity = 1,
                    BaseQuantity = 10,
                    Reason = "Damaged",
                    Notes = "QA531"
                });
                appDb.DamagedGoodsHeaders.Add(damageHeader);
                await appDb.SaveChangesAsync();

                var returnHeader = new SupplierReturnHeader
                {
                    TenantId = tenantId,
                    BranchId = branch.Id,
                    SupplierId = supplier.Id,
                    ReturnNumber = $"SR-{stamp}",
                    ReturnDate = DateTime.Now,
                    Status = "Pending",
                    LinkedDamagedGoodsId = damageHeader.Id,
                    Remarks = "QA531 return",
                    CreatedAtUtc = DateTime.UtcNow
                };
                returnHeader.Details.Add(new SupplierReturnDetail
                {
                    ItemId = item.Id,
                    Quantity = 10,
                    UnitId = unit.Id,
                    ConversionQuantity = 1,
                    BaseQuantity = 10,
                    Reason = "Damaged",
                    Notes = "QA531"
                });
                appDb.SupplierReturnHeaders.Add(returnHeader);
                await appDb.SaveChangesAsync();

                Ok("Created damaged goods + supplier return in shared DB");

                var sharedCounts = new Dictionary<string, int>
                {
                    ["DamagedGoodsHeaders"] = await appDb.DamagedGoodsHeaders.CountAsync(h => h.TenantId == tenantId),
                    ["DamagedGoodsDetails"] = await appDb.DamagedGoodsDetails.CountAsync(d =>
                        appDb.DamagedGoodsHeaders.Any(h => h.TenantId == tenantId && h.Id == d.DamagedGoodsHeaderId)),
                    ["SupplierReturnHeaders"] = await appDb.SupplierReturnHeaders.CountAsync(h => h.TenantId == tenantId),
                    ["SupplierReturnDetails"] = await appDb.SupplierReturnDetails.CountAsync(d =>
                        appDb.SupplierReturnHeaders.Any(h => h.TenantId == tenantId && h.Id == d.SupplierReturnHeaderId))
                };

                foreach (var (table, count) in sharedCounts)
                {
                    if (count == 1) Ok($"Shared {table} count = 1");
                    else No($"Shared {table} expected 1, got {count}");
                }

                var mr = await migrate.MigrateAsync(tenantId);
                if (mr.Success) Ok($"Migration succeeded ({mr.TotalRowsCopied} rows)");
                else
                {
                    No($"Migration failed: {mr.Message}");
                    Console.WriteLine($"[QA531] Summary: {pass} passed, {fail} failed.");
                    Environment.ExitCode = 1;
                    return;
                }

                if (mr.AllMatched) Ok("All table count comparisons matched");
                else No("Count mismatch in migration result");

                foreach (var table in sharedCounts.Keys)
                {
                    var row = mr.Tables.FirstOrDefault(t => t.TableName == table);
                    if (row != null && row.Match)
                        Ok($"{table}: shared={row.SharedCount} dedicated={row.DedicatedCount}");
                    else
                        No($"{table}: mismatch or missing in result");
                }

                await using var ded = await factory.CreateAsync(tenantId);
                var dedDamage = await ded.DamagedGoodsHeaders.AsNoTracking()
                    .FirstOrDefaultAsync(h => h.TenantId == tenantId && h.DamageNumber == $"DMG-{stamp}");
                var dedReturn = await ded.SupplierReturnHeaders.AsNoTracking()
                    .FirstOrDefaultAsync(h => h.TenantId == tenantId && h.ReturnNumber == $"SR-{stamp}");

                if (dedDamage != null && dedDamage.Id == damageHeader.Id)
                    Ok($"DamagedGoodsHeader PK preserved (Id={dedDamage.Id})");
                else
                    No($"DamagedGoodsHeader PK mismatch (expected {damageHeader.Id})");

                if (dedReturn != null && dedReturn.LinkedDamagedGoodsId == damageHeader.Id)
                    Ok($"LinkedDamagedGoodsId preserved ({dedReturn.LinkedDamagedGoodsId})");
                else
                    No($"LinkedDamagedGoodsId not preserved (got {dedReturn?.LinkedDamagedGoodsId})");

                if (dedReturn != null && dedReturn.Status == "Pending" && dedDamage?.Status == "ReturnedToSupplier")
                    Ok("Status and document numbers preserved");
                else
                    No("Status or document fields not preserved");

                var dedDetail = await ded.DamagedGoodsDetails.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.DamagedGoodsHeaderId == damageHeader.Id);
                if (dedDetail != null && dedDetail.BaseQuantity == 10 && dedDetail.ConversionQuantity == 1)
                    Ok("Damaged detail quantities and conversion preserved");
                else
                    No("Damaged detail fields not preserved");

                tenant = await appDb.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId);
                if (tenant.DataMigrated) Ok("Tenant marked DataMigrated");
                else No("Tenant not marked DataMigrated");
            }
            finally
            {
                if (app.Configuration.GetValue<bool>("QA:EnableCleanup") && tenantId > 0)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(dedicatedConn))
                        {
                            var builder = new SqlConnectionStringBuilder(dedicatedConn);
                            var master = new SqlConnectionStringBuilder(dedicatedConn) { InitialCatalog = "master" };
                            await using var conn = new SqlConnection(master.ConnectionString);
                            await conn.OpenAsync();
                            await using var cmd = conn.CreateCommand();
                            cmd.CommandText =
                                $"ALTER DATABASE [{builder.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                                $"DROP DATABASE [{builder.InitialCatalog}]";
                            await cmd.ExecuteNonQueryAsync();
                        }

                        var t = await appDb.Tenants.FirstOrDefaultAsync(x => x.Id == tenantId);
                        if (t != null)
                        {
                            appDb.Tenants.Remove(t);
                            await appDb.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[QA531] Cleanup warning: {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"[QA531] Summary: {pass} passed, {fail} failed.");
            if (fail > 0) Environment.ExitCode = 1;
        }
    }
}
